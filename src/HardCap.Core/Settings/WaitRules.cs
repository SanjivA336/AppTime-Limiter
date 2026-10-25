using HardCap.Core.Models;

namespace HardCap.Core.Settings;

/// <summary>
/// How long you have to wait before you may look at or change something. Two things can make you wait:
///   a group's own lock (always, whatever the time), and
///   the general wait from the settings (but not during the open hours, when you can change things freely).
/// When both apply, the longer one is used, not the two added together: a 1-minute lock and a 3-minute general wait
/// make you wait 3 minutes, never 4.
/// </summary>
public static class WaitRules
{
    /// <summary>True if the open hours are switched on and <paramref name="now"/> falls inside them.</summary>
    public static bool IsInOpenHours(AppSettings settings, DateTime now)
    {
        if (!settings.OpenHoursEnabled)
        {
            return false;
        }

        var minuteOfDay = now.Hour * 60 + now.Minute;
        var start = settings.OpenHoursStartMinutes;
        var end = settings.OpenHoursEndMinutes;

        if (start == end)
        {
            return false; // an empty window
        }

        // A window like 10 PM to 2 AM runs past midnight: it is "after the start OR before the end".
        return start < end
            ? minuteOfDay >= start && minuteOfDay < end
            : minuteOfDay >= start || minuteOfDay < end;
    }

    /// <summary>The general wait that applies right now: none during open hours, otherwise the one set in the settings.</summary>
    public static int GeneralWaitSeconds(AppSettings settings, DateTime now) =>
        IsInOpenHours(settings, now) ? 0 : Math.Clamp(settings.WaitSeconds, 0, AppSettings.MaxWaitSeconds);

    /// <summary>The wait before opening the settings page.</summary>
    public static int SettingsWaitSeconds(AppSettings settings, DateTime now) => GeneralWaitSeconds(settings, now);

    /// <summary>The wait before opening one group: its own lock or the general wait, whichever is longer.</summary>
    public static int GroupWaitSeconds(AppSettings settings, AppGroup group, DateTime now) =>
        Math.Max(Math.Clamp(group.LockSeconds, 0, AppGroup.MaxLockSeconds), GeneralWaitSeconds(settings, now));

    /// <summary>
    /// The wait before quitting HardCap. Quitting switches every limit off, so it waits as long as the
    /// longest lock of any group (or the general wait, if that is longer).
    /// </summary>
    public static int ExitWaitSeconds(AppSettings settings, DateTime now)
    {
        var longestLock = settings.Groups.Count == 0 ? 0 : settings.Groups.Max(g => Math.Clamp(g.LockSeconds, 0, AppGroup.MaxLockSeconds));
        return Math.Max(longestLock, GeneralWaitSeconds(settings, now));
    }
}
