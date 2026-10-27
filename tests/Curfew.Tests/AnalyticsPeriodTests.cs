using Curfew.Core.Models;
using Curfew.Core.Usage;

namespace Curfew.Tests;

public class AnalyticsPeriodTests
{
    private static readonly DateOnly Sunday = new(2026, 10, 25);           // the day the app was built
    private static readonly DateTime SundayNoon = new(2026, 10, 25, 12, 0, 0);

    private readonly UsageLedger _ledger = new();
    private readonly AppGroup _games = T.Group("Games", new[] { "game" }, CellState.Free);
    private readonly AppGroup _work = T.Group("Work", new[] { "editor" }, CellState.Free);

    private PeriodReport Report(AnalyticsView view, DateOnly? anchor = null, ISet<Guid>? only = null) =>
        UsageAnalytics.Build(_ledger, new[] { _games, _work }, only, SundayNoon, resetMinutes: 0, view, anchor);

    private void Use(AppGroup group, DateOnly day, double hours) =>
        _ledger.Add(day, group.Id, hours * 3600, countsTowardBudget: false);

    // ---- Which days each view covers ----

    [Fact]
    public void A_day_covers_just_that_day()
    {
        var report = Report(AnalyticsView.Day);

        Assert.Equal(Sunday, report.Start);
        Assert.Equal(Sunday, report.End);
        Assert.Single(report.Days);
    }

    [Fact]
    public void A_week_runs_monday_to_sunday()
    {
        var (start, end) = AnalyticsPeriod.Bounds(AnalyticsView.Week, new DateOnly(2026, 10, 22));

        Assert.Equal(new DateOnly(2026, 10, 19), start);
        Assert.Equal(new DateOnly(2026, 10, 25), end);
    }

    [Theory]
    [InlineData(2026, 10, 15, 2026, 10, 1, 2026, 10, 31)]
    [InlineData(2026, 2, 28, 2026, 2, 1, 2026, 2, 28)]
    [InlineData(2028, 2, 10, 2028, 2, 1, 2028, 2, 29)]  // a leap year
    [InlineData(2026, 12, 31, 2026, 12, 1, 2026, 12, 31)]
    public void A_month_runs_from_its_first_to_its_last_day(int y, int m, int d, int sy, int sm, int sd, int ey, int em, int ed)
    {
        var (start, end) = AnalyticsPeriod.Bounds(AnalyticsView.Month, new DateOnly(y, m, d));

        Assert.Equal(new DateOnly(sy, sm, sd), start);
        Assert.Equal(new DateOnly(ey, em, ed), end);
    }

    [Fact]
    public void A_month_report_has_a_slice_for_every_day_of_the_month()
    {
        Use(_games, new DateOnly(2026, 10, 3), 2);
        Use(_work, new DateOnly(2026, 10, 3), 1);
        Use(_games, new DateOnly(2026, 10, 20), 4);

        var report = Report(AnalyticsView.Month);

        Assert.Equal(31, report.Days.Count);
        Assert.Equal(new DateOnly(2026, 10, 1), report.Days[0].Date);
        Assert.Equal(3 * 3600, report.Days[2].TotalSeconds);
        Assert.Equal(7 * 3600, report.TotalSeconds);
        Assert.Equal(new DateOnly(2026, 10, 20), report.BusiestDay);
        Assert.Equal(4 * 3600, report.BusiestDaySeconds);
    }

    [Fact]
    public void A_months_average_is_over_the_days_so_far_when_the_month_isnt_over()
    {
        Use(_games, new DateOnly(2026, 10, 1), 25);  // tracking began on the 1st
        // today is the 25th: 25 days count
        Assert.Equal(25 * 3600.0 / 25, Report(AnalyticsView.Month).DailyAverageSeconds);
        Assert.Equal(25, Report(AnalyticsView.Month).CountedDays);
    }

    [Fact]
    public void A_finished_month_is_averaged_over_all_its_days()
    {
        Use(_games, new DateOnly(2026, 9, 1), 30);

        var report = Report(AnalyticsView.Month, new DateOnly(2026, 9, 10));

        Assert.False(report.IsCurrent);
        Assert.Equal(30, report.CountedDays);
        Assert.Equal(3600, report.DailyAverageSeconds);
    }

    [Fact]
    public void A_days_report_is_that_days_total()
    {
        Use(_games, Sunday, 2);
        Use(_work, Sunday, 1);
        Use(_games, Sunday.AddDays(-1), 9);

        var report = Report(AnalyticsView.Day);

        Assert.Equal(3 * 3600, report.TotalSeconds);
        Assert.Equal(3 * 3600, report.DailyAverageSeconds);
        Assert.Equal("Games", report.Groups[0].Name);
    }

    [Fact]
    public void The_containing_week_total_for_a_day_covers_its_whole_week()
    {
        Use(_games, new DateOnly(2026, 10, 19), 2);  // Monday
        Use(_games, Sunday, 1);                      // Sunday, the same week
        Use(_games, new DateOnly(2026, 10, 18), 9);  // the Sunday before

        Assert.Equal(3 * 3600, Report(AnalyticsView.Day, new DateOnly(2026, 10, 21)).ContainingWeekSeconds);
    }

    // ---- Choosing groups ----

    [Fact]
    public void No_selected_groups_means_no_time()
    {
        Use(_games, Sunday, 2);

        var report = Report(AnalyticsView.Week, only: new HashSet<Guid>());

        Assert.Empty(report.Groups);
        Assert.Equal(0, report.TotalSeconds);
        Assert.All(report.Days, day => Assert.Empty(day.Groups));
    }

    [Fact]
    public void A_null_selection_means_every_group()
    {
        Use(_games, Sunday, 2);
        Use(_work, Sunday, 1);

        Assert.Equal(3 * 3600, Report(AnalyticsView.Week, only: null).TotalSeconds);
    }

    // ---- Stepping ----

    [Fact]
    public void Stepping_moves_by_a_day_a_week_or_a_month()
    {
        Assert.Equal(new DateOnly(2026, 10, 24), AnalyticsPeriod.Step(AnalyticsView.Day, Sunday, -1));
        Assert.Equal(new DateOnly(2026, 10, 18), AnalyticsPeriod.Step(AnalyticsView.Week, Sunday, -1));
        Assert.Equal(new DateOnly(2026, 9, 25), AnalyticsPeriod.Step(AnalyticsView.Month, Sunday, -1));
        Assert.Equal(new DateOnly(2026, 2, 28), AnalyticsPeriod.Step(AnalyticsView.Month, new DateOnly(2026, 1, 31), 1)); // no 31st in February
    }

    [Theory]
    [InlineData(AnalyticsView.Day, 2026, 10, 25, false)]   // today: the next day hasn't begun
    [InlineData(AnalyticsView.Day, 2026, 10, 24, true)]
    [InlineData(AnalyticsView.Week, 2026, 10, 22, false)]  // this week
    [InlineData(AnalyticsView.Week, 2026, 10, 14, true)]
    [InlineData(AnalyticsView.Month, 2026, 10, 3, false)]  // this month
    [InlineData(AnalyticsView.Month, 2026, 9, 30, true)]
    public void You_can_go_forward_only_up_to_the_current_period(AnalyticsView view, int y, int m, int d, bool expected) =>
        Assert.Equal(expected, AnalyticsPeriod.CanStepForward(view, new DateOnly(y, m, d), Sunday));

    [Fact]
    public void You_can_always_go_back_even_where_there_is_no_data()
    {
        // (There is deliberately no lower limit.)
        var farBack = AnalyticsPeriod.Step(AnalyticsView.Week, Sunday, -200);

        var report = Report(AnalyticsView.Week, farBack);

        Assert.Equal(0, report.TotalSeconds);
        Assert.False(report.IsCurrent);
    }

    // ---- Names and dates ----

    [Fact]
    public void Days_are_named_relative_to_today()
    {
        Assert.Equal(("Today", "Sun, Oct 25"), AnalyticsPeriod.Describe(AnalyticsView.Day, Sunday, Sunday));
        Assert.Equal(("Yesterday", "Sat, Oct 24"), AnalyticsPeriod.Describe(AnalyticsView.Day, Sunday.AddDays(-1), Sunday));
        Assert.Equal(("5 days ago", "Tue, Oct 20"), AnalyticsPeriod.Describe(AnalyticsView.Day, Sunday.AddDays(-5), Sunday));
        Assert.Equal("Thu, Dec 25, 2025", AnalyticsPeriod.Describe(AnalyticsView.Day, new DateOnly(2025, 12, 25), Sunday).Subtitle);
    }

    [Fact]
    public void Weeks_are_named_relative_to_this_week_and_show_their_dates()
    {
        Assert.Equal(("This week", "Oct 19 – 25"), AnalyticsPeriod.Describe(AnalyticsView.Week, Sunday, Sunday));
        Assert.Equal(("Last week", "Oct 12 – 18"), AnalyticsPeriod.Describe(AnalyticsView.Week, Sunday.AddDays(-7), Sunday));
        Assert.Equal(("3 weeks ago", "Sep 28 – Oct 4"), AnalyticsPeriod.Describe(AnalyticsView.Week, Sunday.AddDays(-21), Sunday));
    }

    [Fact]
    public void Months_are_named_with_the_month_and_year()
    {
        Assert.Equal(("This month", "October 2026"), AnalyticsPeriod.Describe(AnalyticsView.Month, Sunday, Sunday));
        Assert.Equal(("Last month", "September 2026"), AnalyticsPeriod.Describe(AnalyticsView.Month, new DateOnly(2026, 9, 14), Sunday));
        Assert.Equal(("10 months ago", "December 2025"), AnalyticsPeriod.Describe(AnalyticsView.Month, new DateOnly(2025, 12, 31), Sunday));
    }

    [Fact]
    public void The_jump_back_button_is_named_for_the_view()
    {
        Assert.Equal("Today", AnalyticsPeriod.CurrentButtonText(AnalyticsView.Day));
        Assert.Equal("This week", AnalyticsPeriod.CurrentButtonText(AnalyticsView.Week));
        Assert.Equal("This month", AnalyticsPeriod.CurrentButtonText(AnalyticsView.Month));
    }

    [Theory]
    [InlineData(2026, 10, 19, 43)]
    [InlineData(2026, 10, 25, 43)]
    [InlineData(2026, 10, 26, 44)]
    [InlineData(2026, 1, 1, 1)]
    [InlineData(2027, 1, 1, 53)]   // the Friday belongs to the last week of 2026
    public void Week_numbers_follow_the_iso_calendar(int y, int m, int d, int expected) =>
        Assert.Equal(expected, AnalyticsPeriod.WeekNumber(new DateOnly(y, m, d)));
}
