using Curfew.Core.Enforcement;
using Curfew.Core.Models;
using Curfew.Core.Usage;

namespace Curfew.Tests;

public class UsageAnalyticsTests
{
    private static readonly DateOnly Monday = new(2026, 10, 26);
    private static readonly DateTime Wednesday = new DateTime(2026, 10, 28, 12, 0, 0);

    private readonly UsageLedger _ledger = new();
    private readonly AppGroup _games = T.Group("Games", new[] { "game", "launcher" }, CellState.Free);
    private readonly AppGroup _work = T.Group("Work", new[] { "editor" }, CellState.Free);

    private PeriodReport Report(DateTime? now = null, params AppGroup[] only) =>
        UsageAnalytics.Build(
            _ledger,
            new[] { _games, _work },
            only.Length == 0 ? null : only.Select(g => g.Id).ToHashSet(),
            now ?? Wednesday,
            resetMinutes: 0);

    private void Use(AppGroup group, DateOnly day, double hours, bool limited = false, params string[] apps) =>
        _ledger.Add(day, group.Id, hours * 3600, countsTowardBudget: limited, apps);

    // ---- The week ----

    [Theory]
    [InlineData(2026, 10, 26, 2026, 10, 26)] // Monday
    [InlineData(2026, 10, 28, 2026, 10, 26)] // Wednesday
    [InlineData(2026, 11, 1, 2026, 10, 26)]  // Sunday belongs to the week that began the Monday before
    [InlineData(2026, 11, 2, 2026, 11, 2)]
    public void Weeks_start_on_monday(int y, int m, int d, int wy, int wm, int wd) =>
        Assert.Equal(new DateOnly(wy, wm, wd), UsageAnalytics.WeekStartOf(new DateOnly(y, m, d)));

    [Fact]
    public void The_report_has_seven_days_monday_first_with_each_groups_time()
    {
        Use(_games, Monday, 2);
        Use(_games, Monday.AddDays(2), 1.5);
        Use(_work, Monday, 1);

        var report = Report();

        Assert.Equal(Monday, report.Start);
        Assert.Equal(7, report.Days.Count);
        Assert.Equal(Monday, report.Days[0].Date);
        Assert.Equal(Monday.AddDays(6), report.Days[6].Date);
        Assert.Equal(3 * 3600, report.Days[0].TotalSeconds);
        Assert.Equal(2 * 3600, report.Days[0].Groups.Single(g => g.Name == "Games").Seconds);
        Assert.Equal(1.5 * 3600, report.Days[2].TotalSeconds);
        Assert.Equal(0, report.Days[3].TotalSeconds);
    }

    [Fact]
    public void Free_and_limited_time_both_count()
    {
        Use(_games, Monday, 1, limited: true);
        Use(_games, Monday, 2, limited: false);

        Assert.Equal(3 * 3600, Report().TotalSeconds);
    }

    // ---- Choosing groups ----

    [Fact]
    public void One_group_can_be_picked()
    {
        Use(_games, Monday, 2);
        Use(_work, Monday, 1);

        var report = Report(null, _work);

        Assert.Equal(3600, report.TotalSeconds);
        Assert.Equal("Work", Assert.Single(report.Groups).Name);
    }

    [Fact]
    public void A_collection_of_groups_adds_up_and_no_selection_means_all()
    {
        Use(_games, Monday, 2);
        Use(_work, Monday, 1);

        Assert.Equal(3 * 3600, Report(null, _games, _work).TotalSeconds);
        Assert.Equal(3 * 3600, Report().TotalSeconds);
    }

    [Fact]
    public void Usage_of_a_group_that_no_longer_exists_is_not_counted()
    {
        _ledger.Add(Monday, Guid.NewGuid(), 5 * 3600, true);
        Use(_games, Monday, 1);

        Assert.Equal(3600, Report().TotalSeconds);
    }

    // ---- Today, totals and averages ----

    [Fact]
    public void Today_and_week_totals_and_the_daily_average()
    {
        Use(_games, Monday, 3);                    // Monday
        Use(_games, Monday.AddDays(1), 3);         // Tuesday
        Use(_games, Monday.AddDays(2), 6);         // Wednesday (today)

        var report = Report();

        Assert.Equal(6 * 3600, report.TodaySeconds);
        Assert.Equal(12 * 3600, report.TotalSeconds);
        Assert.Equal(4 * 3600, report.DailyAverageSeconds); // 12 hours over Monday, Tuesday and Wednesday
    }

    [Fact]
    public void Groups_are_listed_largest_first()
    {
        Use(_work, Monday, 5);
        Use(_games, Monday, 1);

        var report = Report();

        Assert.Equal(new[] { "Work", "Games" }, report.Groups.Select(g => g.Name));
    }

    // ---- Apps ----

    [Fact]
    public void A_group_lists_its_apps_with_their_own_time_largest_first()
    {
        _ledger.Add(Monday, _games.Id, 3600, false, new[] { "game", "launcher" });
        _ledger.Add(Monday, _games.Id, 1800, false, new[] { "game" });

        var apps = Report().Groups.Single(g => g.Name == "Games").Apps;

        Assert.Equal(new[] { "game", "launcher" }, apps.Select(a => a.Name));
        Assert.Equal(3240, apps[0].Seconds, 3);   // the apps share the group's 5400 seconds (9000 recorded, scaled to 5400)
        Assert.Equal(2160, apps[1].Seconds, 3);
        Assert.Equal(5400, apps.Sum(a => a.Seconds), 3);
    }

    [Fact]
    public void Apps_with_no_time_are_still_listed_and_old_data_without_apps_is_fine()
    {
        Use(_games, Monday, 1); // no app detail, as in days recorded before apps were tracked

        var apps = Report().Groups.Single(g => g.Name == "Games").Apps;

        Assert.Equal(2, apps.Count);
        Assert.All(apps, a => Assert.Equal(0, a.Seconds));
    }

    [Fact]
    public void Each_day_keeps_its_own_app_breakdown()
    {
        _ledger.Add(Monday, _games.Id, 3600, false, new[] { "game" });
        _ledger.Add(Monday.AddDays(1), _games.Id, 1200, false, new[] { "launcher" });

        var report = Report();

        Assert.Equal(3600, report.Days[0].Groups.Single(g => g.Name == "Games").Apps.Single(a => a.Name == "game").Seconds);
        Assert.Equal(0, report.Days[1].Groups.Single(g => g.Name == "Games").Apps.Single(a => a.Name == "game").Seconds);
        Assert.Equal(1200, report.Days[1].Groups.Single(g => g.Name == "Games").Apps.Single(a => a.Name == "launcher").Seconds);
    }

    // ---- Comparing with earlier weeks ----

    [Fact]
    public void Nothing_is_compared_when_there_is_no_earlier_week()
    {
        Use(_games, Monday, 2);

        var report = Report();

        Assert.Null(report.PreviousDailyAverageSeconds);
        Assert.Null(report.PreviousWeeklyAverageSeconds);
        Assert.Null(report.ChangePercent);
    }

    [Fact]
    public void Nothing_is_compared_with_an_empty_ledger()
    {
        var report = Report();

        Assert.Null(report.ChangePercent);
        Assert.Equal(0, report.TotalSeconds);
    }

    [Fact]
    public void The_change_compares_this_weeks_daily_average_with_earlier_weeks()
    {
        // Two full earlier weeks, 2 hours every day: a daily average of 2 hours (14 a week).
        for (var offset = -14; offset < 0; offset++)
        {
            Use(_games, Monday.AddDays(offset), 2);
        }

        // This week so far: 3 hours a day for Monday to Wednesday, a daily average of 3 hours.
        for (var offset = 0; offset < 3; offset++)
        {
            Use(_games, Monday.AddDays(offset), 3);
        }

        var report = Report();

        Assert.Equal(2 * 3600, report.PreviousDailyAverageSeconds);
        Assert.Equal(14 * 3600, report.PreviousWeeklyAverageSeconds);
        Assert.Equal(50, report.ChangePercent!.Value, precision: 6); // 3 hours is 50% more than 2 hours
    }

    [Fact]
    public void Less_use_than_before_is_a_negative_change()
    {
        for (var offset = -7; offset < 0; offset++)
        {
            Use(_games, Monday.AddDays(offset), 4);
        }

        Use(_games, Monday, 1);
        Use(_games, Monday.AddDays(1), 1);
        Use(_games, Monday.AddDays(2), 1);

        Assert.Equal(-75, Report().ChangePercent!.Value, precision: 6);
    }

    [Fact]
    public void Days_before_tracking_began_are_left_out_of_the_comparison()
    {
        // Tracking began last Saturday and Sunday (2 days, 6 hours each); earlier days are not "zero use", just unknown.
        Use(_games, Monday.AddDays(-2), 6);
        Use(_games, Monday.AddDays(-1), 6);
        Use(_games, Monday, 3);

        var report = Report();

        Assert.Equal(6 * 3600, report.PreviousDailyAverageSeconds);
        Assert.Equal(1 * 3600, report.DailyAverageSeconds); // 3 hours over Monday to Wednesday
        Assert.Equal(-83.333333, report.ChangePercent!.Value, precision: 4);
    }

    [Fact]
    public void Only_the_last_four_earlier_weeks_are_compared()
    {
        Use(_games, Monday.AddDays(-7 * 6), 24); // six weeks ago: too old to count
        for (var offset = -7; offset < 0; offset++)
        {
            Use(_games, Monday.AddDays(offset), 1);
        }

        var report = Report();

        // The four weeks back from this one hold 7 hours across 28 days.
        Assert.Equal(7.0 / 28 * 3600, report.PreviousDailyAverageSeconds!.Value, precision: 6);
    }

    [Fact]
    public void No_percentage_when_the_earlier_average_is_zero()
    {
        // Work was used before this week, so tracking was on, but Games (the only group picked) was not.
        Use(_work, Monday.AddDays(-10), 3);
        Use(_games, Monday, 2);

        var report = Report(null, _games);

        Assert.Equal(0, report.PreviousDailyAverageSeconds);
        Assert.Null(report.ChangePercent); // a percentage of zero means nothing
    }

    // ---- The reset time ----

    [Fact]
    public void The_week_and_today_follow_the_reset_time()
    {
        // Monday 1 AM with a 4 AM reset is still Sunday's budget day, which belongs to the previous week.
        var mondayEarly = new DateTime(2026, 10, 26, 1, 0, 0);

        var report = UsageAnalytics.Build(_ledger, new[] { _games }, null, mondayEarly, resetMinutes: 240);

        Assert.Equal(new DateOnly(2026, 10, 25), report.Today);
        Assert.Equal(new DateOnly(2026, 10, 19), report.Start);
    }

    // ---- The record itself ----

    [Fact]
    public void The_engine_records_each_running_apps_time()
    {
        var group = T.Group("Games", new[] { "game", "launcher" }, CellState.Free);
        var settings = T.Settings(60, group);
        var engine = new EnforcementEngine(_ledger);

        engine.Tick(settings, T.At(12), new[] { "game", "launcher" });
        engine.Tick(settings, T.At(12, 0, 10), new[] { "game", "launcher" });
        engine.Tick(settings, T.At(12, 0, 20), new[] { "game" }); // the launcher closed

        var usage = _ledger.GetUsage(DateOnly.FromDateTime(T.Monday), group.Id);
        Assert.Equal(20, usage.FreeSeconds);
        Assert.Equal(20, usage.Processes["game"]);
        Assert.Equal(10, usage.Processes["launcher"]);
    }

    [Fact]
    public void Nothing_is_recorded_for_apps_during_a_block()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Blocked);
        var settings = T.Settings(60, group);
        var engine = new EnforcementEngine(_ledger);

        engine.Tick(settings, T.At(12), new[] { "game" });
        engine.Tick(settings, T.At(12, 0, 10), new[] { "game" });

        Assert.Empty(_ledger.GetUsage(DateOnly.FromDateTime(T.Monday), group.Id).Processes);
    }

    [Fact]
    public void App_times_survive_a_save_and_load_and_old_files_still_load()
    {
        var folder = T.TempFolder();
        try
        {
            var path = Path.Combine(folder, "usage.json");
            var first = new UsageLedger(path);
            first.Add(Monday, _games.Id, 100, false, new[] { "game" });
            first.SaveIfDirty();

            var second = new UsageLedger(path);
            second.Load();
            Assert.Equal(100, second.GetUsage(Monday, _games.Id).Processes["game"]);
            Assert.Equal(Monday, second.FirstDay);

            // A file written before apps were recorded has no "Processes" at all.
            File.WriteAllText(path, $$"""{ "2026-10-26": { "{{_games.Id}}": { "LimitedSeconds": 60, "FreeSeconds": 30 } } }""");
            var old = new UsageLedger(path);
            old.Load();
            Assert.Null(old.LoadError);
            Assert.Equal(90, old.GetUsage(Monday, _games.Id).LimitedSeconds + old.GetUsage(Monday, _games.Id).FreeSeconds);
            Assert.Empty(old.GetUsage(Monday, _games.Id).Processes);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
