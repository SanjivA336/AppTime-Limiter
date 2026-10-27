using Curfew.Core.Models;

namespace Curfew.Core.Settings;

/// <summary>
/// How long you have to wait before you may look at or change something. Each thing has its own wait:
///   opening a group: that group's own lock (always, whatever the time, except while the group is paused);
///   opening the settings page: the general wait from the settings (but not during the open hours, when you can change things freely);
///   quitting: the general wait or the longest lock of any group that isn't paused, whichever is longer.
/// Where two waits apply to the same thing (quitting), the longer one is used, not the two added together: a 1-minute lock and
/// a 3-minute general wait make you wait 3 minutes, never 4.
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

    /// <summary>
    /// The wait before opening one group: only its own lock (none while it is paused). The general wait does not apply to groups;
    /// it is for the settings page and for quitting.
    /// </summary>
    public static int GroupWaitSeconds(AppGroup group, DateTime now) => group.LockSecondsAt(now);

    /// <summary>
    /// The wait before quitting Curfew. Quitting switches every limit off, so it waits as long as the
    /// longest lock of any group that isn't paused (or the general wait, if that is longer).
    /// </summary>
    public static int ExitWaitSeconds(AppSettings settings, DateTime now)
    {
        var longestLock = settings.Groups.Count == 0 ? 0 : settings.Groups.Max(g => g.LockSecondsAt(now));
        return Math.Max(longestLock, GeneralWaitSeconds(settings, now));
    }
}
