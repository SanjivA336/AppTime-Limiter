using Curfew.Core.Models;
using Curfew.Core.Processes;
using Curfew.Core.Usage;

namespace Curfew.Core.Enforcement;

public enum CloseReason
{
    Blocked,
    BudgetUsedUp,
}

/// <summary>An order to close the running apps of one group.</summary>
public sealed record CloseRequest(Guid GroupId, string GroupName, IReadOnlyList<string> Processes, CloseReason Reason);

/// <summary>A message to show the user.</summary>
public sealed record Alert(string Title, string Message);

/// <summary>What one group looks like right now. The window uses this for its "running" labels.</summary>
/// <param name="IsBudgetDraining">True when this tick actually charged time to the group's budget (a Limited cell, not paused, not used up, and the apps counted under the group's count mode).</param>
public sealed record GroupStatus(Guid GroupId, bool IsRunning, CellState State, bool IsBudgetDraining = false);

/// <summary>An order to freeze some newly started apps of one group behind a countdown (launch friction).</summary>
/// <param name="Processes">The apps that just started. If more start while the group is already held, they come in another request.</param>
/// <param name="Seconds">How long the countdown lasts.</param>
public sealed record HoldRequest(Guid GroupId, string GroupName, IReadOnlyList<string> Processes, int Seconds);

/// <param name="NewHolds">Apps to freeze now. Empty (or null) when nothing just started.</param>
/// <param name="EndedHolds">Groups whose hold has to end now, because it no longer applies (a pause began, the apps closed, ...).</param>
public sealed record TickResult(
    IReadOnlyList<CloseRequest> Closes,
    IReadOnlyList<Alert> Alerts,
    IReadOnlyList<GroupStatus> Statuses,
    IReadOnlyList<HoldRequest>? NewHolds = null,
    IReadOnlyList<Guid>? EndedHolds = null);

/// <summary>
/// The part that makes the calls. A few seconds apart, the app hands it the current time and the list of
/// running processes (that's one "tick"). For each tick it:
///   1. adds the time that has passed to the usage record,
///   2. decides which apps have to be closed, and
///   3. decides whether the user should get a warning.
/// It never closes anything or shows anything itself. It only returns what should happen, which is what
/// lets the tests run any scenario with a made-up clock.
/// </summary>
public sealed class EnforcementEngine
{
    /// <summary>
    /// The most time a single tick may add to the usage record. If the computer was asleep for three hours,
    /// we don't want to charge those three hours to the budget.
    /// </summary>
    public static readonly TimeSpan MaxCreditedGap = TimeSpan.FromSeconds(30);

    /// <summary>Don't repeat the "closing Games" notification more often than this for the same group.</summary>
    public static readonly TimeSpan CloseNoticeCooldown = TimeSpan.FromSeconds(30);

    private readonly UsageLedger _ledger;

    // For each group, which warnings (as "minutes before") we've already shown, so each is shown only once.
    private readonly Dictionary<Guid, HashSet<int>> _warningsAlreadySent = new();

    // For each group, when we last told the user it was being closed.
    private readonly Dictionary<Guid, DateTime> _lastCloseNoticeAt = new();

    // For each group whose apps are inside their grace period: when the grace period ends and the apps are closed.
    private readonly Dictionary<Guid, DateTime> _graceEndsAt = new();

    // Groups that were running and allowed on the last tick. Only these get a grace period when a limit starts: an app
    // opened while a block is already on is closed straight away, or "grace" would be a free few minutes on every launch.
    private readonly HashSet<Guid> _wasRunningAndAllowed = new();

    // Launch friction. Groups whose newly started apps are frozen behind a countdown right now: nothing in them is counted, closed
    // or warned about until the hold ends.
    private readonly HashSet<Guid> _holds = new();

    // For each group, which of its apps were running on the last check, to tell which ones just started.
    private readonly Dictionary<Guid, HashSet<string>> _knownApps = new();

    private DateTime? _lastTickAt;
    private bool _hasTicked;

    public EnforcementEngine(UsageLedger ledger)
    {
        _ledger = ledger;
    }

    /// <summary>
    /// Says a group's hold is over (its countdown ended, or it was cancelled), so it is counted and enforced again. The apps that
    /// were frozen are not held a second time: they were already running on the check that started the hold.
    /// </summary>
    public void ReleaseHold(Guid groupId) => _holds.Remove(groupId);

    public TickResult Tick(
        AppSettings settings, DateTime now, IReadOnlyCollection<string> runningProcessNames, string? foregroundProcessName = null,
        TimeSpan? idleFor = null)
    {
        // While the computer is idle (and the setting is on) nothing is counted: not toward the budgets, and not in the usage record.
        // Limits are still enforced as usual. The time before the idle limit was reached has already been counted; it is not taken back.
        var isIdle = settings.IdleDetectionEnabled && idleFor is TimeSpan quiet && quiet >= TimeSpan.FromMinutes(settings.IdleMinutes);
        var secondsToCredit = isIdle ? 0 : SecondsToCreditSinceLastTick(now);

        // Usage and budgets are kept per "budget day", which starts at the reset time (midnight unless the user changed it).
        var today = BudgetDay.Of(now, settings.ResetMinutes);
        var budgetDayOfWeek = today.DayOfWeek;
        var runningNames = new HashSet<string>(runningProcessNames, ProcessNames.Comparer);

        var closes = new List<CloseRequest>();
        var alerts = new List<Alert>();
        var statuses = new List<GroupStatus>();
        var newHolds = new List<HoldRequest>();
        var endedHolds = new List<Guid>();

        foreach (var group in settings.Groups)
        {
            // A paused group is treated as Free: its time is still tracked, but nothing is limited or closed.
            var isPaused = group.IsPausedAt(now);
            var cellState = isPaused ? CellState.Free : group.Schedule.GetState(now);
            var runningFromThisGroup = group.Processes.Where(runningNames.Contains).ToList();
            var statusIndex = statuses.Count;
            statuses.Add(new GroupStatus(group.Id, runningFromThisGroup.Count > 0, cellState));

            // Which of the group's apps just started? Not on the very first check: apps already open when Curfew starts aren't held.
            _knownApps.TryGetValue(group.Id, out var appsBefore);
            var newApps = _hasTicked
                ? runningFromThisGroup.Where(app => appsBefore is null || !appsBefore.Contains(app)).ToList()
                : new List<string>();
            _knownApps[group.Id] = new HashSet<string>(runningFromThisGroup, ProcessNames.Comparer);

            // Launch friction. A hold ends when it no longer makes sense: the apps closed, or the group was paused, entered a Blocked
            // time (which closes its apps instead), or had the friction switched off.
            var isHeld = _holds.Contains(group.Id);
            if (isHeld && (runningFromThisGroup.Count == 0 || isPaused || cellState == CellState.Blocked || group.LaunchFrictionSeconds <= 0))
            {
                _holds.Remove(group.Id);
                endedHolds.Add(group.Id);
                isHeld = false;
            }

            // Newly started apps are held, unless a pause or a block applies, or the budget is gone (they'd be closed anyway).
            var budgetIsGone = cellState == CellState.Limited
                && _ledger.GetBudgetSecondsUsed(today, group.Id, group.CountMode) >= group.BudgetMinutesFor(budgetDayOfWeek) * 60.0;
            if (newApps.Count > 0 && group.LaunchFrictionSeconds > 0 && !isPaused && cellState != CellState.Blocked && !budgetIsGone)
            {
                _holds.Add(group.Id);
                newHolds.Add(new HoldRequest(group.Id, group.Name, newApps, group.LaunchFrictionSeconds));
                isHeld = true;
            }

            if (isHeld)
            {
                continue; // frozen behind a countdown: nothing is counted, closed or warned about until it ends
            }

            if (runningFromThisGroup.Count == 0)
            {
                // Nothing running, so start fresh next time: the warnings can be shown again, and no grace period is pending.
                _warningsAlreadySent.Remove(group.Id);
                _graceEndsAt.Remove(group.Id);
                _wasRunningAndAllowed.Remove(group.Id);
                continue;
            }

            // Each group has its own budget for each day (and each day can have a different one).
            var dailyBudgetSeconds = group.BudgetMinutesFor(budgetDayOfWeek) * 60.0;
            // The group is "in front" when the window being used belongs to one of its running apps.
            var inFrontApp = foregroundProcessName is null
                ? null
                : runningFromThisGroup.FirstOrDefault(app => ProcessNames.Comparer.Equals(app, ProcessNames.Normalize(foregroundProcessName)));
            var closeReason = RecordUsageAndCheckLimits(group, cellState, secondsToCredit, today, dailyBudgetSeconds, runningFromThisGroup, inFrontApp, now);

            // The card's bar only animates while the budget is really going down: a Limited cell (a pause makes it Free), some
            // budget left, and time that counts under the group's mode (an app in the background doesn't, in "in front" mode).
            var isSpendingBudget = closeReason is null
                && cellState == CellState.Limited
                && !isIdle
                && (group.CountMode == CountMode.WhileRunning || inFrontApp is not null);
            statuses[statusIndex] = statuses[statusIndex] with { IsBudgetDraining = isSpendingBudget };
            if (closeReason is CloseReason reason)
            {
                if (ShouldCloseNow(group, reason, runningFromThisGroup, now, alerts))
                {
                    closes.Add(new CloseRequest(group.Id, group.Name, runningFromThisGroup, reason));
                    AddCloseNotice(alerts, group, reason, runningFromThisGroup, now);
                }
            }
            else
            {
                _graceEndsAt.Remove(group.Id);
                _wasRunningAndAllowed.Add(group.Id);
                if (!isPaused)
                {
                    AddWarningIfDue(alerts, group, runningFromThisGroup, now, today, settings.ResetMinutes);
                }
            }
        }

        _lastTickAt = now;
        _hasTicked = true;
        return new TickResult(closes, alerts, statuses, newHolds, endedHolds);
    }

    /// <summary>
    /// Decides whether a group that has hit a limit is closed on this tick, or is still inside its grace period.
    ///   No grace period set: close now.
    ///   Grace already running: close once it has run out.
    ///   The limit has only just started and the apps were running and allowed a moment ago: start the grace period
    ///   and say so.
    ///   Otherwise (the apps were opened during the block): close now.
    /// </summary>
    private bool ShouldCloseNow(AppGroup group, CloseReason reason, IReadOnlyList<string> runningApps, DateTime now, List<Alert> alerts)
    {
        var graceSeconds = Math.Clamp(group.GraceSeconds, 0, AppGroup.MaxGraceSeconds);
        if (graceSeconds == 0)
        {
            return true;
        }

        if (_graceEndsAt.TryGetValue(group.Id, out var graceEnds))
        {
            return now >= graceEnds;
        }

        if (_wasRunningAndAllowed.Remove(group.Id))
        {
            _graceEndsAt[group.Id] = now.AddSeconds(graceSeconds);
            var why = reason == CloseReason.Blocked ? "are blocked" : "have used up their daily budget";
            alerts.Add(new Alert("Curfew", $"Apps in {group.Name} {why}, so {ProcessNames.Join(runningApps)} will close in {LockTime.Describe(graceSeconds)}; save your work."));
            return false;
        }

        return true;
    }

    /// <summary>How many seconds of "this app was running" to add for this tick.</summary>
    private double SecondsToCreditSinceLastTick(DateTime now)
    {
        if (_lastTickAt is not DateTime lastTick)
        {
            return 0; // The very first tick has nothing to compare against.
        }

        var secondsPassed = (now - lastTick).TotalSeconds;
        if (secondsPassed <= 0)
        {
            return 0; // The clock went backwards. Add nothing rather than negative time.
        }

        return Math.Min(secondsPassed, MaxCreditedGap.TotalSeconds);
    }

    /// <summary>
    /// Adds this tick's time (for the group, and for each of its apps that is running) to the usage record, if the cell
    /// type tracks it, then says whether the group's apps have to be closed. Returns null when they're allowed to keep
    /// running.
    /// </summary>
    private CloseReason? RecordUsageAndCheckLimits(
        AppGroup group, CellState cellState, double secondsToCredit, DateOnly today, double dailyBudgetSeconds,
        IReadOnlyList<string> runningApps,
        string? inFrontApp,
        DateTime now)
    {
        // Both ways of counting are always recorded; the group's own setting only picks which one the budget reads.
        switch (cellState)
        {
            case CellState.Blocked:
                return CloseReason.Blocked;

            case CellState.Limited:
                if (_ledger.GetBudgetSecondsUsed(today, group.Id, group.CountMode) >= dailyBudgetSeconds)
                {
                    return CloseReason.BudgetUsedUp; // Already out of budget before this tick.
                }

                _ledger.Add(today, group.Id, secondsToCredit, countsTowardBudget: true, runningApps, endedAt: now, trackInFront: true, inFrontProcess: inFrontApp);

                // This tick's time may have just used up the last of the budget.
                var budgetNowEmpty = _ledger.GetBudgetSecondsUsed(today, group.Id, group.CountMode) >= dailyBudgetSeconds;
                return budgetNowEmpty ? CloseReason.BudgetUsedUp : null;

            default: // Free: record it for stats, but it doesn't touch the budget.
                _ledger.Add(today, group.Id, secondsToCredit, countsTowardBudget: false, runningApps, endedAt: now, trackInFront: true, inFrontProcess: inFrontApp);
                return null;
        }
    }

    private void AddCloseNotice(List<Alert> alerts, AppGroup group, CloseReason reason, IReadOnlyList<string> runningApps, DateTime now)
    {
        var toldRecently = _lastCloseNoticeAt.TryGetValue(group.Id, out var lastNotice)
            && now - lastNotice < CloseNoticeCooldown;
        if (toldRecently)
        {
            return;
        }

        _lastCloseNoticeAt[group.Id] = now;
        var apps = ProcessNames.Join(runningApps);
        var message = reason == CloseReason.Blocked
            ? $"Closing {apps}: apps in {group.Name} are blocked right now."
            : $"Closing {apps}: apps in {group.Name} have used up their daily budget.";
        alerts.Add(new Alert("Curfew", message));
    }

    /// <summary>
    /// Shows a warning if the group is about to be cut off. Each warning time (say "5 minutes before")
    /// is shown once, and becomes available again if the cutoff moves further away.
    /// If several warning times are passed at once (the app was started with 2 minutes left and warnings are
    /// set for 5 and 2), the user gets one message, not two.
    /// </summary>
    private void AddWarningIfDue(List<Alert> alerts, AppGroup group, IReadOnlyList<string> runningApps, DateTime now, DateOnly today, int resetMinutes)
    {
        if (group.WarningMinutes.Count == 0)
        {
            return;
        }

        if (!_warningsAlreadySent.TryGetValue(group.Id, out var alreadySent))
        {
            _warningsAlreadySent[group.Id] = alreadySent = new HashSet<int>();
        }

        // No need to look further ahead than the earliest warning.
        var lookAhead = TimeSpan.FromMinutes(group.WarningMinutes.Max() + 1);
        var usedToday = _ledger.GetBudgetSecondsUsed(today, group.Id, group.CountMode);
        var cutoff = LimitForecast.Next(group.Schedule, now, usedToday, group.BudgetMinutesFor, lookAhead, resetMinutes);

        var newlyDue = new List<int>();
        foreach (var minutesBefore in group.WarningMinutes)
        {
            var isDue = cutoff is not null && cutoff.In <= TimeSpan.FromMinutes(minutesBefore);
            if (!isDue)
            {
                alreadySent.Remove(minutesBefore); // Not due (any more), so it may fire again later.
            }
            else if (alreadySent.Add(minutesBefore))
            {
                newlyDue.Add(minutesBefore);
            }
        }

        if (newlyDue.Count == 0 || cutoff is null)
        {
            return;
        }

        alerts.Add(new Alert("Curfew", WarningText(group, runningApps, cutoff)));
    }

    private static string WarningText(AppGroup group, IReadOnlyList<string> runningApps, LimitEnd cutoff)
    {
        var minutesLeft = Math.Max(1, (int)Math.Ceiling(cutoff.In.TotalMinutes));
        var minutesWord = minutesLeft == 1 ? "minute" : "minutes";
        var apps = ProcessNames.Join(runningApps);

        return cutoff.Reason == LimitReason.Budget
            ? $"{apps} will close in {minutesLeft} {minutesWord}: apps in {group.Name} are almost out of budget."
            : $"{apps} will close in {minutesLeft} {minutesWord}: apps in {group.Name} are about to be blocked.";
    }
}
