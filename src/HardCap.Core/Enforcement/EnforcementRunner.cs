using HardCap.Core.Models;
using HardCap.Core.Usage;

namespace HardCap.Core.Enforcement;

/// <summary>
/// Glue between the engine and the outside world. A timer calls <see cref="RunOnceAsync"/> every few seconds:
/// it looks at running processes, asks the engine what to do, then closes processes and sends notifications.
/// </summary>
public sealed class EnforcementRunner
{
    /// <summary>How often usage is flushed to disk. A crash loses at most this much tracked time.</summary>
    public static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(15);

    private readonly Func<AppSettings> _getSettings;
    private readonly IProcessController _processes;
    private readonly INotifier _notifier;
    private readonly UsageLedger _ledger;
    private readonly EnforcementEngine _engine;
    private DateTime _lastSave = DateTime.MinValue;
    private bool _busy;

    public EnforcementRunner(
        Func<AppSettings> getSettings, IProcessController processes, INotifier notifier, UsageLedger ledger)
    {
        _getSettings = getSettings;
        _processes = processes;
        _notifier = notifier;
        _ledger = ledger;
        _engine = new EnforcementEngine(ledger);
    }

    /// <summary>Raised after every pass so the UI can refresh its "running" dots and usage numbers.</summary>
    public event Action<TickResult>? Ticked;

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
            var result = _engine.Tick(settings, now, _processes.GetRunningProcessNames());

            foreach (var alert in result.Alerts)
            {
                _notifier.Notify(alert.Title, alert.Message);
            }

            foreach (var close in result.Closes)
            {
                var failed = await _processes.CloseAsync(close.Processes, cancellationToken);
                if (failed.Count > 0)
                {
                    _notifier.Notify(
                        "HardCap",
                        $"Couldn't close {string.Join(", ", failed)}. It may be running as administrator; " +
                        "try running HardCap as administrator.");
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

    /// <summary>Flushes usage to disk right away (on app exit).</summary>
    public void Flush() => _ledger.SaveIfDirty();
}
