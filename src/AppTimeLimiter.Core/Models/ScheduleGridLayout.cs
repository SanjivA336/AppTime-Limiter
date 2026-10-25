namespace AppTimeLimiter.Core.Models;

/// <summary>
/// The geometry of the schedule grid: seven day columns (Monday first) side by side, with the time of day
/// running down the left from 12 AM. Each row is one 30-minute cell.
///
/// This works out which column and row a mouse position is over. It is kept free of any UI types so it
/// can be unit-tested; the WPF control only draws and passes mouse positions to it.
/// </summary>
public static class ScheduleGridLayout
{
    /// <summary>Width of the strip on the left that holds the times ("12 AM") and their tick marks.</summary>
    public const double TimeColumnWidth = 66;

    /// <summary>Height of the strip along the top that holds the day names.</summary>
    public const double DayHeaderHeight = 28;

    /// <summary>Height of one 30-minute cell.</summary>
    public const double SlotHeight = 14;

    /// <summary>Empty space between neighbouring day columns. It is taken out of each column's width.</summary>
    public const double ColumnGap = 6;

    /// <summary>The columns, left to right. Monday first, though the schedule itself is indexed by <see cref="DayOfWeek"/>.</summary>
    public static readonly DayOfWeek[] ColumnDays =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    };

    public static double TotalHeight => DayHeaderHeight + SlotHeight * WeeklySchedule.SlotsPerDay;

    /// <summary>The room each day gets, including its share of the gap, for a grid that is <paramref name="totalWidth"/> wide.</summary>
    public static double ColumnWidth(double totalWidth) =>
        Math.Max(1, (totalWidth - TimeColumnWidth) / ColumnDays.Length);

    /// <summary>Where a cell's top edge is, measured down from the top of the grid.</summary>
    public static double SlotTop(int slot) => DayHeaderHeight + slot * SlotHeight;

    /// <summary>True when <paramref name="y"/> is in the strip with the day names.</summary>
    public static bool IsInDayHeader(double y) => y >= 0 && y < DayHeaderHeight;

    /// <summary>True when <paramref name="x"/> is over one of the seven day columns (not the time strip, not past the right edge).</summary>
    public static bool TryGetColumn(double x, double totalWidth, out int column)
    {
        column = (int)Math.Floor((x - TimeColumnWidth) / ColumnWidth(totalWidth));
        return column >= 0 && column < ColumnDays.Length && x >= TimeColumnWidth;
    }

    /// <summary>True when <paramref name="y"/> is over one of the 48 cells (not the day-name strip, not below the grid).</summary>
    public static bool TryGetSlot(double y, out int slot)
    {
        slot = (int)Math.Floor((y - DayHeaderHeight) / SlotHeight);
        return y >= DayHeaderHeight && slot >= 0 && slot < WeeklySchedule.SlotsPerDay;
    }

    /// <summary>
    /// The time span a cell covers, like "9:30–10:00 PM". AM/PM is written once when both ends share it,
    /// and twice when the cell crosses noon or midnight ("11:30 AM–12:00 PM", "11:30 PM–12:00 AM").
    /// </summary>
    public static string SlotRangeLabel(int slot)
    {
        var startMinutes = slot * WeeklySchedule.SlotMinutes;
        var endMinutes = (startMinutes + WeeklySchedule.SlotMinutes) % (24 * 60); // the last cell ends at midnight

        var sharesAmPm = IsMorning(startMinutes) == IsMorning(endMinutes);
        var start = ClockTime(startMinutes, includeAmPm: !sharesAmPm);
        var end = ClockTime(endMinutes, includeAmPm: true);
        return $"{start}–{end}";
    }

    private static bool IsMorning(int minutesSinceMidnight) => minutesSinceMidnight < 12 * 60;

    private static string ClockTime(int minutesSinceMidnight, bool includeAmPm)
    {
        var hour = minutesSinceMidnight / 60;
        var minute = minutesSinceMidnight % 60;
        var hourOnClock = hour % 12 == 0 ? 12 : hour % 12;
        var time = $"{hourOnClock}:{minute:00}";
        return includeAmPm ? $"{time} {(IsMorning(minutesSinceMidnight) ? "AM" : "PM")}" : time;
    }

    /// <summary>"12 AM", "1 AM", ... "12 PM", "1 PM", ... "11 PM" for an hour from 0 to 23.</summary>
    public static string HourLabel(int hour)
    {
        var hourOnClock = hour % 12 == 0 ? 12 : hour % 12;
        var amOrPm = hour < 12 ? "AM" : "PM";
        return $"{hourOnClock} {amOrPm}";
    }
}
