using AppTimeLimiter.Core.Models;
using AppTimeLimiter.Core.Processes;
using AppTimeLimiter.Core.Usage;

namespace AppTimeLimiter.Core.Enforcement;

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
public sealed record GroupStatus(Guid GroupId, bool IsRunning, CellState State);

public sealed record TickResult(
    IReadOnlyList<CloseRequest> Closes,
    IReadOnlyList<Alert> Alerts,
    IReadOnlyList<GroupStatus> Statuses);

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

    private DateTime? _lastTickAt;

    public EnforcementEngine(UsageLedger ledger)
    {
        _ledger = ledger;
    }

    public TickResult Tick(AppSettings settings, DateTime now, IReadOnlyCollection<string> runningProcessNames)
    {
        var secondsToCredit = SecondsToCreditSinceLastTick(now);
        var today = DateOnly.FromDateTime(now);
        var runningNames = new HashSet<string>(runningProcessNames, ProcessNames.Comparer);

        var closes = new List<CloseRequest>();
        var alerts = new List<Alert>();
        var statuses = new List<GroupStatus>();

        foreach (var group in settings.Groups)
        {
            var cellState = group.Schedule.GetState(now);
            var runningFromThisGroup = group.Processes.Where(runningNames.Contains).ToList();
            statuses.Add(new GroupStatus(group.Id, runningFromThisGroup.Count > 0, cellState));

            if (runningFromThisGroup.Count == 0)
            {
                // Nothing running, so start fresh next time: the warnings can be shown again.
                _warningsAlreadySent.Remove(group.Id);
                continue;
            }

            // Each group has its own budget for each day (and each day can have a different one).
            var dailyBudgetSeconds = group.BudgetMinutesFor(now.DayOfWeek) * 60.0;
            var closeReason = RecordUsageAndCheckLimits(group, cellState, secondsToCredit, today, dailyBudgetSeconds);
            if (closeReason is CloseReason reason)
            {
                closes.Add(new CloseRequest(group.Id, group.Name, runningFromThisGroup, reason));
                AddCloseNotice(alerts, group, reason, now);
            }
            else
            {
                AddWarningIfDue(alerts, group, now, today);
            }
        }

        _lastTickAt = now;
        return new TickResult(closes, alerts, statuses);
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
    /// Adds this tick's time to the usage record (if the cell type tracks it), then says whether the
    /// group's apps have to be closed. Returns null when they're allowed to keep running.
    /// </summary>
    private CloseReason? RecordUsageAndCheckLimits(
        AppGroup group, CellState cellState, double secondsToCredit, DateOnly today, double dailyBudgetSeconds)
    {
        switch (cellState)
        {
            case CellState.Blocked:
                return CloseReason.Blocked;

            case CellState.Limited:
                if (_ledger.GetBudgetSecondsUsed(today, group.Id) >= dailyBudgetSeconds)
                {
                    return CloseReason.BudgetUsedUp; // Already out of budget before this tick.
                }

                _ledger.Add(today, group.Id, secondsToCredit, countsTowardBudget: true);

                // This tick's time may have just used up the last of the budget.
                var budgetNowEmpty = _ledger.GetBudgetSecondsUsed(today, group.Id) >= dailyBudgetSeconds;
                return budgetNowEmpty ? CloseReason.BudgetUsedUp : null;

            default: // Free: record it for stats, but it doesn't touch the budget.
                _ledger.Add(today, group.Id, secondsToCredit, countsTowardBudget: false);
                return null;
        }
    }

    private void AddCloseNotice(List<Alert> alerts, AppGroup group, CloseReason reason, DateTime now)
    {
        var toldRecently = _lastCloseNoticeAt.TryGetValue(group.Id, out var lastNotice)
            && now - lastNotice < CloseNoticeCooldown;
        if (toldRecently)
        {
            return;
        }

        _lastCloseNoticeAt[group.Id] = now;
        var message = reason == CloseReason.Blocked
            ? $"{group.Name} is blocked right now, so it's being closed."
            : $"Your daily budget is used up, so {group.Name} is being closed.";
        alerts.Add(new Alert("AppTime Limiter", message));
    }

    /// <summary>
    /// Shows a warning if the group is about to be cut off. Each warning time (say "5 minutes before")
    /// is shown once, and becomes available again if the cutoff moves further away.
    /// If several warning times are passed at once (the app was started with 2 minutes left and warnings are
    /// set for 5 and 2), the user gets one message, not two.
    /// </summary>
    private void AddWarningIfDue(List<Alert> alerts, AppGroup group, DateTime now, DateOnly today)
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
        var usedToday = _ledger.GetBudgetSecondsUsed(today, group.Id);
        var cutoff = LimitForecast.Next(group.Schedule, now, usedToday, group.BudgetMinutesFor, lookAhead);

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

        alerts.Add(new Alert("AppTime Limiter", WarningText(group, cutoff, now)));
    }

    private static string WarningText(AppGroup group, LimitEnd cutoff, DateTime now)
    {
        var minutesLeft = Math.Max(1, (int)Math.Ceiling(cutoff.In.TotalMinutes));
        var minutesWord = minutesLeft == 1 ? "minute" : "minutes";

        return cutoff.Reason == LimitReason.Budget
            ? $"You have {minutesLeft} {minutesWord} left of {group.Name}."
            : $"It's {now:h:mm tt}. {group.Name} access ends in {minutesLeft} {minutesWord}.";
    }
}
