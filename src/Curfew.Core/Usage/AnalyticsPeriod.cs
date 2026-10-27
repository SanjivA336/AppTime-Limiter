using System.Globalization;
using Curfew.Core.Models;

namespace Curfew.Core.Usage;

/// <summary>How much of the history the Analytics page shows at once.</summary>
public enum AnalyticsView
{
    Day,
    Week,
    Month,
}

/// <summary>
/// The arithmetic of "which days does this view cover, and how do I step to the next one". A view is always given as a
/// date inside the period (the anchor): the day itself, the week (Monday to Sunday) containing it, or its calendar month.
/// </summary>
public static class AnalyticsPeriod
{
    /// <summary>The first and last day (both included) of the period that contains <paramref name="anchor"/>.</summary>
    public static (DateOnly Start, DateOnly End) Bounds(AnalyticsView view, DateOnly anchor)
    {
        switch (view)
        {
            case AnalyticsView.Day:
                return (anchor, anchor);

            case AnalyticsView.Week:
                var monday = UsageAnalytics.WeekStartOf(anchor);
                return (monday, monday.AddDays(6));

            default:
                var first = new DateOnly(anchor.Year, anchor.Month, 1);
                return (first, first.AddMonths(1).AddDays(-1));
        }
    }

    /// <summary>A date in the period <paramref name="steps"/> periods away (negative goes back).</summary>
    public static DateOnly Step(AnalyticsView view, DateOnly anchor, int steps) => view switch
    {
        AnalyticsView.Day => anchor.AddDays(steps),
        AnalyticsView.Week => anchor.AddDays(7 * steps),
        _ => anchor.AddMonths(steps),
    };

    /// <summary>
    /// Whether the next period has begun yet. Going forward past today is never possible, because nothing can have been
    /// recorded there.
    /// </summary>
    public static bool CanStepForward(AnalyticsView view, DateOnly anchor, DateOnly today) =>
        Bounds(view, Step(view, anchor, 1)).Start <= today;

    /// <summary>True when <paramref name="today"/> falls inside the period that contains <paramref name="anchor"/>.</summary>
    public static bool Contains(AnalyticsView view, DateOnly anchor, DateOnly today)
    {
        var (start, end) = Bounds(view, anchor);
        return today >= start && today <= end;
    }

    /// <summary>
    /// The name of a period and, under it, its dates: ("This week", "Oct 19 – 25"), ("Today", "Sun, Oct 25"),
    /// ("Last month", "September 2026"). Always in English.
    /// </summary>
    public static (string Title, string Subtitle) Describe(AnalyticsView view, DateOnly anchor, DateOnly today)
    {
        var culture = CultureInfo.InvariantCulture;
        var (start, _) = Bounds(view, anchor);
        var (todayStart, _) = Bounds(view, today);

        switch (view)
        {
            case AnalyticsView.Day:
                var daysAgo = today.DayNumber - start.DayNumber;
                var dayTitle = daysAgo switch { 0 => "Today", 1 => "Yesterday", _ => $"{daysAgo} days ago" };
                var format = start.Year == today.Year ? "ddd, MMM d" : "ddd, MMM d, yyyy";
                return (dayTitle, start.ToString(format, culture));

            case AnalyticsView.Week:
                var weeksAgo = (todayStart.DayNumber - start.DayNumber) / 7;
                var weekTitle = weeksAgo switch { 0 => "This week", 1 => "Last week", _ => $"{weeksAgo} weeks ago" };
                return (weekTitle, TimeText.WeekRange(start, today));

            default:
                var monthsAgo = (todayStart.Year - start.Year) * 12 + todayStart.Month - start.Month;
                var monthTitle = monthsAgo switch { 0 => "This month", 1 => "Last month", _ => $"{monthsAgo} months ago" };
                return (monthTitle, start.ToString("MMMM yyyy", culture));
        }
    }

    /// <summary>What the "jump back to now" button says for each view.</summary>
    public static string CurrentButtonText(AnalyticsView view) => view switch
    {
        AnalyticsView.Day => "Today",
        AnalyticsView.Week => "This week",
        _ => "This month",
    };

    /// <summary>The ISO week number (weeks start on Monday, and week 1 is the one with the year's first Thursday).</summary>
    public static int WeekNumber(DateOnly day) => ISOWeek.GetWeekOfYear(day.ToDateTime(TimeOnly.MinValue));
}
