using Curfew.Core.Models;
using Curfew.Core.Processes;
using Curfew.Core.Usage;

namespace Curfew.Core.Enforcement;

/// <summary>
/// Glue between the engine and the outside world. A timer calls <see cref="RunOnceAsync"/> every few seconds:
/// it looks at running processes, asks the engine what to do, then closes processes and sends notifications.
/// It also carries out launch friction: it freezes newly started apps while a countdown is shown, and unfreezes them.
/// </summary>
public sealed class EnforcementRunner
{
    /// <summary>How often usage is flushed to disk. A crash loses at most this much tracked time.</summary>
    public static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The longest a hold can last. The countdown only runs while its window is in front, so a hold can go on while the window isn't;
    /// after this long the person is taken to have walked away, and the apps are unfrozen and closed (as if "don't open" was chosen).
    /// It also means a stuck countdown window can never leave an app frozen for good.
    /// </summary>
    public static readonly TimeSpan MaxHold = TimeSpan.FromMinutes(5);

    private readonly Func<AppSettings> _getSettings;
    private readonly IProcessController _processes;
    private readonly INotifier _notifier;
    private readonly UsageLedger _ledger;
    private readonly IIdleProbe? _idle;
    private readonly EnforcementEngine _engine;
    private DateTime _lastSave = DateTime.MinValue;
    private bool _busy;

    // The groups whose newly started apps are frozen right now, and which apps are frozen.
    private readonly Dictionary<Guid, ActiveHold> _holds = new();

    // Groups we have already told "couldn't freeze it", so the message appears once, not on every launch.
    private readonly HashSet<Guid> _toldCannotFreeze = new();

    private sealed class ActiveHold
    {
        public ActiveHold(HoldRequest request, List<string> frozen, DateTime startedAt)
        {
            Request = request;
            Frozen = frozen;
            StartedAt = startedAt;
        }

        public HoldRequest Request { get; }

        public List<string> Frozen { get; }

        public DateTime StartedAt { get; }
    }

    public EnforcementRunner(
        Func<AppSettings> getSettings, IProcessController processes, INotifier notifier, UsageLedger ledger, IIdleProbe? idle = null)
    {
        _getSettings = getSettings;
        _processes = processes;
        _notifier = notifier;
        _ledger = ledger;
        _idle = idle;
        _engine = new EnforcementEngine(ledger);
    }

    /// <summary>Raised after every pass so the UI can refresh its "running" dots and usage numbers.</summary>
    public event Action<TickResult>? Ticked;

    /// <summary>
    /// Raised once when apps have been frozen and a countdown should be shown for them. The request lists the apps that really were
    /// frozen. The countdown ends with <see cref="CompleteHold"/> (it ran out) or <see cref="CancelHoldAsync"/> (the person chose not to open).
    /// </summary>
    public event Action<HoldRequest>? HoldStarted;

    /// <summary>Raised when a hold is over, however it ended, so the countdown can be taken down.</summary>
    public event Action<Guid>? HoldEnded;

    public async Task RunOnceAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        // Closing a stubborn app can take a few seconds; never let two passes overlap.
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            var settings = _getSettings();
            // The idle check is only asked for when the setting is on: with it off, the computer is never asked.
            var idleFor = settings.IdleDetectionEnabled ? _idle?.GetIdleTime() : null;
            var result = _engine.Tick(settings, now, _processes.GetRunningProcessNames(), _processes.GetForegroundProcessName(), idleFor);

            foreach (var groupId in result.EndedHolds ?? Array.Empty<Guid>())
            {
                EndHold(groupId);
            }

            foreach (var request in result.NewHolds ?? Array.Empty<HoldRequest>())
            {
                StartHold(request, now);
            }

            await ExpireOldHoldsAsync(now, cancellationToken);

            foreach (var alert in result.Alerts)
            {
                _notifier.Notify(alert.Title, alert.Message);
            }

            foreach (var close in result.Closes)
            {
                var failed = await _processes.CloseAsync(close.Processes, cancellationToken);
                if (failed.Count > 0)
                {
                    NotifyCouldNotClose(failed);
                }
            }

            if (now - _lastSave >= SaveInterval)
            {
                _lastSave = now;
                // Usage older than the retention period is dropped as part of the regular save.
                _ledger.PruneOlderThan(BudgetDay.Of(now, settings.ResetMinutes).AddMonths(-UsageLedger.RetentionMonths));
                _ledger.SaveIfDirty();
            }

            Ticked?.Invoke(result);
        }
        finally
        {
            _busy = false;
        }
    }

    // ---- Launch friction ----

    /// <summary>
    /// Freezes the newly started apps and, if any were frozen, announces the hold. An app that can't be frozen (Windows or an
    /// anti-cheat refuses) is left running and the person is told once. It is never closed instead, and nothing is ever relaunched.
    /// </summary>
    private void StartHold(HoldRequest request, DateTime now)
    {
        var cannot = _processes.Suspend(request.Processes);
        var frozen = request.Processes.Where(app => !cannot.Contains(app, ProcessNames.Comparer)).ToList();

        if (cannot.Count > 0 && _toldCannotFreeze.Add(request.GroupId))
        {
            _notifier.Notify("Curfew", $"Couldn't hold {ProcessNames.Join(cannot)} (it can't be frozen), so it opens without a wait.");
        }

        if (frozen.Count == 0)
        {
            if (!_holds.ContainsKey(request.GroupId))
            {
                _engine.ReleaseHold(request.GroupId);
            }

            return;
        }

        if (_holds.TryGetValue(request.GroupId, out var existing))
        {
            existing.Frozen.AddRange(frozen); // more apps of a group that is already held join the same countdown
            return;
        }

        _holds[request.GroupId] = new ActiveHold(request, frozen, now);
        HoldStarted?.Invoke(request with { Processes = frozen });
    }

    /// <summary>The countdown ran out: unfreeze the apps and let them carry on.</summary>
    public void CompleteHold(Guid groupId) => EndHold(groupId);

    /// <summary>
    /// The person chose not to open: unfreeze the apps (a frozen app can't close politely) and close them. This never
    /// relaunches anything.
    /// </summary>
    public async Task CancelHoldAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        var apps = _holds.TryGetValue(groupId, out var hold) ? hold.Frozen.ToList() : new List<string>();
        EndHold(groupId);

        if (apps.Count > 0)
        {
            var failed = await _processes.CloseAsync(apps, cancellationToken);
            if (failed.Count > 0)
            {
                NotifyCouldNotClose(failed);
            }
        }
    }

    /// <summary>Unfreezes everything frozen, for when Curfew quits, so nothing is left frozen behind it.</summary>
    public void ReleaseAllHolds()
    {
        foreach (var groupId in _holds.Keys.ToList())
        {
            EndHold(groupId);
        }

        _processes.ResumeAll();
    }

    private void EndHold(Guid groupId)
    {
        var existed = _holds.Remove(groupId, out var hold);
        if (hold is not null)
        {
            _processes.Resume(hold.Frozen);
        }

        _engine.ReleaseHold(groupId);
        if (existed)
        {
            HoldEnded?.Invoke(groupId);
        }
    }

    /// <summary>
    /// The safety net: a hold that has gone on for <see cref="MaxHold"/> ends by unfreezing and closing the apps, as if the person had
    /// chosen "don't open". It is not a way to get the app for free by waiting, and nothing stays frozen for good.
    /// </summary>
    private async Task ExpireOldHoldsAsync(DateTime now, CancellationToken cancellationToken)
    {
        foreach (var (groupId, hold) in _holds.ToList())
        {
            if (now - hold.StartedAt > MaxHold)
            {
                await CancelHoldAsync(groupId, cancellationToken);
            }
        }
    }

    private void NotifyCouldNotClose(IReadOnlyList<string> failed) => _notifier.Notify(
        "Curfew",
        $"Couldn't close {ProcessNames.Join(failed)}; if it's running as administrator, run Curfew as administrator too.");

    /// <summary>Flushes usage to disk right away (on app exit).</summary>
    public void Flush() => _ledger.SaveIfDirty();
}
