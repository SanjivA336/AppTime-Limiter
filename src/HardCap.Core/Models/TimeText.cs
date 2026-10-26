namespace HardCap.Core.Models;

/// <summary>Turns lengths of time into short, readable text.</summary>
public static class TimeText
{
    /// <summary>"6:00 PM", "12:30 AM".</summary>
    public static string Clock(DateTime time)
    {
        var hourOnClock = time.Hour % 12 == 0 ? 12 : time.Hour % 12;
        var amOrPm = time.Hour < 12 ? "AM" : "PM";
        return $"{hourOnClock}:{time.Minute:00} {amOrPm}";
    }

    /// <summary>
    /// A moment described the way people say it, relative to <paramref name="now"/>:
    /// "today at 6:00 PM", "tomorrow at 8:00 PM", "Friday at 8:00 PM", or "next Wednesday at 1:00 PM"
    /// when it's a full week away.
    /// </summary>
    public static string When(DateTime at, DateTime now)
    {
        var daysAway = (at.Date - now.Date).Days;
        var clock = Clock(at);

        return daysAway switch
        {
            0 => $"today at {clock}",
            1 => $"tomorrow at {clock}",
            >= 7 => $"next {at.DayOfWeek} at {clock}",
            _ => $"{at.DayOfWeek} at {clock}",
        };
    }

    /// <summary>
    /// The seven days of a week as a short range: "Oct 19 – 25", "Oct 26 – Nov 1", and with the years when the week isn't
    /// wholly in the same year as <paramref name="today"/> ("Dec 29, 2025 – Jan 4, 2026"). Always in English month names.
    /// </summary>
    public static string WeekRange(DateOnly weekStart, DateOnly today)
    {
        var end = weekStart.AddDays(6);
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var showYear = weekStart.Year != today.Year || end.Year != today.Year;

        if (!showYear && weekStart.Month == end.Month)
        {
            return $"{weekStart.ToString("MMM d", culture)} – {end.Day}";
        }

        var format = showYear ? "MMM d, yyyy" : "MMM d";
        return $"{weekStart.ToString(format, culture)} – {end.ToString(format, culture)}";
    }

    /// <summary>
    /// "1h 12m", "2h", "45m". Shown in whole minutes (rounded down); anything under a minute is "&lt;1m",
    /// and zero or negative time is "0m".
    /// </summary>
    public static string Compact(TimeSpan time)
    {
        if (time <= TimeSpan.Zero)
        {
            return "0m";
        }

        if (time < TimeSpan.FromMinutes(1))
        {
            return "<1m";
        }

        var hours = (int)time.TotalHours;
        var minutes = time.Minutes;

        if (hours == 0)
        {
            return $"{minutes}m";
        }

        return minutes == 0 ? $"{hours}h" : $"{hours}h {minutes}m";
    }
}
