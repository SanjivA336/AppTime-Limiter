using HardCap.Core.Models;
using HardCap.Core.Processes;

namespace HardCap.Core.Usage;

/// <summary>How long one app ran.</summary>
public sealed record AppUsage(string Name, double Seconds);

/// <summary>How long one group was used, with the apps it is made of (largest first).</summary>
public sealed record GroupUsage(Guid GroupId, string Name, string Color, double Seconds, IReadOnlyList<AppUsage> Apps);

/// <summary>One day: how much each selected group was used.</summary>
public sealed record DayUsageSlice(DateOnly Date, IReadOnlyList<GroupUsage> Groups)
{
    public double TotalSeconds => Groups.Sum(g => g.Seconds);
}

/// <summary>One hour of the clock on one day: how much each selected group was used in it.</summary>
/// <param name="ClockHour">The hour of the clock, 0 (12 AM to 1 AM) to 23 (11 PM to midnight).</param>
public sealed record HourUsageSlice(int ClockHour, IReadOnlyList<GroupUsage> Groups)
{
    public double TotalSeconds => Groups.Sum(g => g.Seconds);
}

/// <summary>Everything the Analytics page shows for one day, week or month, worked out from the usage record.</summary>
/// <param name="Start">The first day of the period.</param>
/// <param name="End">The last day of the period.</param>
/// <param name="IsCurrent">True when today falls inside the period.</param>
/// <param name="Days">Every day of the period, with each selected group's use on it.</param>
/// <param name="Hours">For a day: its 24 hours, starting at the hour budgets reset (midnight by default), each with the groups' use in it. Empty for a week or a month.</param>
/// <param name="Groups">Each selected group's use over the whole period, largest first.</param>
/// <param name="TotalSeconds">All the selected groups' time over the period.</param>
/// <param name="TodaySeconds">Today's total, whatever period is shown.</param>
/// <param name="DailyAverageSeconds">The total divided by the days that count: those from the first tracked day, and up to today if the period isn't over.</param>
/// <param name="CountedDays">How many days that average was spread over.</param>
/// <param name="PreviousDailyAverageSeconds">The same kind of average over up to the four weeks before the period, or null if nothing was tracked then.</param>
/// <param name="PreviousWeeklyAverageSeconds">That earlier daily average times seven.</param>
/// <param name="ChangePercent">The period's daily average compared with the earlier one, as a percent (+12 means 12% more). Null when there is nothing to compare with.</param>
/// <param name="BusiestDay">The day of the period with the most time, or null if there was none.</param>
/// <param name="BusiestDaySeconds">That day's time.</param>
/// <param name="ContainingWeekSeconds">The total for the Monday-to-Sunday week the period starts in (up to today).</param>
public sealed record PeriodReport(
    AnalyticsView View,
    DateOnly Start,
    DateOnly End,
    DateOnly Today,
    bool IsCurrent,
    IReadOnlyList<DayUsageSlice> Days,
    IReadOnlyList<HourUsageSlice> Hours,
    IReadOnlyList<GroupUsage> Groups,
    double TotalSeconds,
    double TodaySeconds,
    double DailyAverageSeconds,
    int CountedDays,
    double? PreviousDailyAverageSeconds,
    double? PreviousWeeklyAverageSeconds,
    double? ChangePercent,
    DateOnly? BusiestDay,
    double BusiestDaySeconds,
    double ContainingWeekSeconds);

/// <summary>
/// Turns the usage record into the numbers and charts of the Analytics page. It only reads the record and takes "now" as
/// an input, so tests can try any day.
///
/// Weeks start on Monday, like the schedule grid. Days are budget days (they begin at the reset time the user chose).
/// Time counts whether it was Free or Limited, so paused time and Free cells are included. Only groups that still exist
/// are listed.
/// </summary>
public static class UsageAnalytics
{
    /// <summary>How many earlier weeks the "compared with before" figure looks back over.</summary>
    public const int ComparisonWeeks = 4;

    /// <summary>The Monday of the week that <paramref name="day"/> is in.</summary>
    public static DateOnly WeekStartOf(DateOnly day)
    {
        var daysSinceMonday = ((int)day.DayOfWeek + 6) % 7;
        return day.AddDays(-daysSinceMonday);
    }

    /// <param name="selectedGroupIds">The groups to include. Null means every group; an empty set means none.</param>
    /// <param name="view">Whether to cover a day, a week or a month.</param>
    /// <param name="anchor">Any date inside the period wanted. Null means today.</param>
    public static PeriodReport Build(
        UsageLedger ledger,
        IReadOnlyList<AppGroup> groups,
        ISet<Guid>? selectedGroupIds,
        DateTime now,
        int resetMinutes,
        AnalyticsView view = AnalyticsView.Week,
        DateOnly? anchor = null)
    {
        var selected = groups
            .Where(g => selectedGroupIds is null || selectedGroupIds.Contains(g.Id))
            .ToList();

        var today = BudgetDay.Of(now, resetMinutes);
        var (start, end) = AnalyticsPeriod.Bounds(view, anchor ?? today);
        var isCurrent = today >= start && today <= end;

        var dates = Enumerable.Range(0, end.DayNumber - start.DayNumber + 1).Select(i => start.AddDays(i)).ToList();
        var days = dates
            .Select(date => new DayUsageSlice(date, selected.Select(g => GroupOnDays(ledger, g, new[] { date })).ToList()))
            .ToList();

        // A day also gets its hours, in order from the hour the budget day begins.
        var hours = view != AnalyticsView.Day
            ? new List<HourUsageSlice>()
            : Enumerable.Range(0, 24)
                .Select(i => (resetMinutes / 60 + i) % 24)
                .Select(hour => new HourUsageSlice(hour, selected.Select(g => GroupInHour(ledger, g, start, hour)).ToList()))
                .ToList();

        var groupUsage = Ranked(selected.Select(g => GroupOnDays(ledger, g, dates)));
        var total = groupUsage.Sum(g => g.Seconds);

        // The days that count toward the average: up to today if the period isn't over yet, and not before tracking began
        // (days nobody was watching would count as zero and drag the average down).
        var lastCounted = isCurrent ? today : end;
        var firstCounted = ledger.FirstDay is DateOnly tracked && tracked > start ? tracked : start;
        var countedDays = Math.Max(1, lastCounted.DayNumber - firstCounted.DayNumber + 1);
        var dailyAverage = total / countedDays;

        var (previousDaily, change) = CompareWithEarlierWeeks(ledger, selected, start, dailyAverage);

        var busiest = days.OrderByDescending(d => d.TotalSeconds).First();
        var todaySeconds = selected.Sum(g => GroupOnDays(ledger, g, new[] { today }).Seconds);

        var weekStart = WeekStartOf(start);
        var weekDays = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).Where(d => d <= today).ToList();
        var containingWeek = weekDays.Count == 0 ? 0 : selected.Sum(g => GroupOnDays(ledger, g, weekDays).Seconds);

        return new PeriodReport(
            view, start, end, today, isCurrent, days, hours, groupUsage, total, todaySeconds, dailyAverage, countedDays,
            previousDaily, previousDaily is null ? null : previousDaily * 7, change,
            busiest.TotalSeconds > 0 ? busiest.Date : null, busiest.TotalSeconds, containingWeek);
    }

    /// <summary>
    /// The average use per day over up to <see cref="ComparisonWeeks"/> weeks before the period, and how far the period's
    /// daily average is from it. Days before tracking began are left out (they would count as zero, but nobody was
    /// watching), so a young install compares against what little history it has.
    /// </summary>
    private static (double? PreviousDaily, double? ChangePercent) CompareWithEarlierWeeks(
        UsageLedger ledger, IReadOnlyList<AppGroup> selected, DateOnly periodStart, double dailyAverage)
    {
        if (ledger.FirstDay is not DateOnly firstTracked)
        {
            return (null, null);
        }

        var rangeStart = Max(firstTracked, periodStart.AddDays(-7 * ComparisonWeeks));
        var rangeEnd = periodStart.AddDays(-1);
        if (rangeStart > rangeEnd)
        {
            return (null, null); // nothing recorded before the period
        }

        var dayCount = rangeEnd.DayNumber - rangeStart.DayNumber + 1;
        var allDays = Enumerable.Range(0, dayCount).Select(i => rangeStart.AddDays(i)).ToList();
        var total = selected.Sum(g => GroupOnDays(ledger, g, allDays).Seconds);
        var previousDaily = total / dayCount;

        double? change = previousDaily > 0 ? (dailyAverage - previousDaily) / previousDaily * 100 : null;
        return (previousDaily, change);
    }

    /// <summary>One group's use over some days: its total, and the time of each app (largest first).</summary>
    private static GroupUsage GroupOnDays(UsageLedger ledger, AppGroup group, IReadOnlyList<DateOnly> days)
    {
        double seconds = 0;
        var perApp = new Dictionary<string, double>(ProcessNames.Comparer);

        foreach (var day in days)
        {
            var usage = ledger.GetUsage(day, group.Id);
            seconds += usage.LimitedSeconds + usage.FreeSeconds;

            foreach (var (app, appSeconds) in usage.Processes ?? new Dictionary<string, double>())
            {
                perApp[app] = perApp.GetValueOrDefault(app) + appSeconds;
            }
        }

        // Every app the group has now is listed (even at zero); apps removed from the group appear if they have time.
        foreach (var app in group.Processes)
        {
            perApp.TryAdd(app, 0);
        }

        var apps = perApp
            .Select(pair => new AppUsage(pair.Key, pair.Value))
            .OrderByDescending(a => a.Seconds)
            .ThenBy(a => a.Name, ProcessNames.Comparer)
            .ToList();

        return new GroupUsage(group.Id, group.Name, group.Color, seconds, apps);
    }

    /// <summary>
    /// One group's use in one hour of the clock on one day, with the time of each app in that hour. Days recorded before
    /// hours were kept have no hourly detail, so they read as zero here.
    /// </summary>
    private static GroupUsage GroupInHour(UsageLedger ledger, AppGroup group, DateOnly day, int clockHour)
    {
        var usage = ledger.GetUsage(day, group.Id);
        var seconds = usage.Hourly is { Length: 24 } hourly ? hourly[clockHour] : 0;

        var apps = (usage.AppHourly ?? new Dictionary<string, double[]>())
            .Where(pair => pair.Value.Length == 24 && pair.Value[clockHour] > 0)
            .Select(pair => new AppUsage(pair.Key, pair.Value[clockHour]))
            .OrderByDescending(a => a.Seconds)
            .ThenBy(a => a.Name, ProcessNames.Comparer)
            .ToList();

        return new GroupUsage(group.Id, group.Name, group.Color, seconds, apps);
    }

    /// <summary>Largest first; ties keep the groups in their usual order.</summary>
    private static List<GroupUsage> Ranked(IEnumerable<GroupUsage> groups) =>
        groups.Select((group, index) => (group, index))
            .OrderByDescending(x => x.group.Seconds)
            .ThenBy(x => x.index)
            .Select(x => x.group)
            .ToList();

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
}
