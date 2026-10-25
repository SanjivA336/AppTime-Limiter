namespace HardCap.Core.Models;

/// <summary>
/// The arithmetic behind a "wait" screen. It is told how much real time has passed and whether the wait window is the
/// one being looked at (the active window); time only counts while it is. Tabbing away to do something else pauses
/// the countdown instead of letting it run out in the background.
/// It knows nothing about timers or windows, which is what lets the tests drive it with made-up time.
/// </summary>
public sealed class CountdownClock
{
    public CountdownClock(int totalSeconds)
    {
        TotalSeconds = Math.Max(0, totalSeconds);
    }

    public int TotalSeconds { get; }

    /// <summary>Seconds that have counted so far (only time spent with the window active).</summary>
    public double ElapsedSeconds { get; private set; }

    public double RemainingSeconds => Math.Max(0, TotalSeconds - ElapsedSeconds);

    public bool IsFinished => RemainingSeconds <= 0;

    /// <summary>How much of the wait is left, from 1 (all of it) down to 0.</summary>
    public double Progress => TotalSeconds <= 0 ? 0 : RemainingSeconds / TotalSeconds;

    /// <summary>Adds <paramref name="seconds"/> to the countdown, but only if the window is active. Negative time is ignored.</summary>
    public void Advance(double seconds, bool windowIsActive)
    {
        if (windowIsActive && seconds > 0)
        {
            ElapsedSeconds += seconds;
        }
    }

    /// <summary>
    /// What the countdown reads, such as "0:45". It is rounded up, so it shows the full length at the start and "0:01"
    /// for the final second, and only reads "0:00" when the wait is really over.
    /// </summary>
    public string Text => LockTime.Format((int)Math.Ceiling(RemainingSeconds));
}
