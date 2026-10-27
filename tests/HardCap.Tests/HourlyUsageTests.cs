using HardCap.Core.Enforcement;
using HardCap.Core.Models;
using HardCap.Core.Usage;

namespace HardCap.Tests;

public class HourlyUsageTests
{
    private static readonly DateOnly Day = new(2026, 10, 26);

    private readonly UsageLedger _ledger = new();
    private readonly AppGroup _games = T.Group("Games", new[] { "game", "launcher" }, CellState.Free);
    private readonly AppGroup _work = T.Group("Work", new[] { "editor" }, CellState.Free);

    private DayUsage Usage(AppGroup group) => _ledger.GetUsage(Day, group.Id);

    // ---- Recording ----

    [Fact]
    public void A_stretch_inside_one_hour_is_filed_under_that_hour()
    {
        _ledger.Add(Day, _games.Id, 600, false, new[] { "game" }, endedAt: new DateTime(2026, 10, 26, 15, 30, 0));

        var usage = Usage(_games);
        Assert.Equal(600, usage.Hourly![15]);
        Assert.Equal(600, usage.AppHourly!["game"][15]);
        Assert.Equal(600, usage.Hourly.Sum());
    }

    [Fact]
    public void A_stretch_that_crosses_the_top_of_an_hour_is_split()
    {
        // 15:59:50 to 16:00:10: ten seconds in each hour.
        _ledger.Add(Day, _games.Id, 20, false, new[] { "game" }, endedAt: new DateTime(2026, 10, 26, 16, 0, 10));

        var usage = Usage(_games);
        Assert.Equal(10, usage.Hourly![15], precision: 6);
        Assert.Equal(10, usage.Hourly[16], precision: 6);
        Assert.Equal(10, usage.AppHourly!["game"][15], precision: 6);
        Assert.Equal(10, usage.AppHourly["game"][16], precision: 6);
    }

    [Fact]
    public void Hours_add_up_over_several_stretches_and_across_limited_and_free_time()
    {
        var at = new DateTime(2026, 10, 26, 9, 20, 0);
        _ledger.Add(Day, _games.Id, 300, countsTowardBudget: true, new[] { "game" }, endedAt: at);
        _ledger.Add(Day, _games.Id, 200, countsTowardBudget: false, new[] { "game", "launcher" }, endedAt: at.AddMinutes(10));

        var usage = Usage(_games);
        Assert.Equal(500, usage.Hourly![9]);
        Assert.Equal(500, usage.AppHourly!["game"][9]);
        Assert.Equal(200, usage.AppHourly["launcher"][9]);
    }

    [Fact]
    public void Without_a_clock_time_no_hourly_detail_is_kept()
    {
        _ledger.Add(Day, _games.Id, 600, false, new[] { "game" });

        Assert.Null(Usage(_games).Hourly);
        Assert.Equal(600, Usage(_games).FreeSeconds);
    }

    [Fact]
    public void The_engine_files_each_tick_under_the_hour_it_happened_in()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Free);
        var settings = T.Settings(60, group);
        var engine = new EnforcementEngine(_ledger);
        var running = new[] { "game" };

        engine.Tick(settings, T.At(15, 59, 50), running);
        engine.Tick(settings, T.At(16, 0, 10), running); // credits 20 seconds spanning the hour

        var usage = _ledger.GetUsage(DateOnly.FromDateTime(T.Monday), group.Id);
        Assert.Equal(10, usage.Hourly![15], precision: 6);
        Assert.Equal(10, usage.Hourly[16], precision: 6);
        Assert.Equal(20, usage.FreeSeconds);
    }

    [Fact]
    public void Nothing_is_filed_by_hour_during_a_block()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Blocked);
        var engine = new EnforcementEngine(_ledger);

        engine.Tick(T.Settings(60, group), T.At(12), new[] { "game" });
        engine.Tick(T.Settings(60, group), T.At(12, 0, 10), new[] { "game" });

        Assert.Null(_ledger.GetUsage(DateOnly.FromDateTime(T.Monday), group.Id).Hourly);
    }

    [Fact]
    public void Hourly_detail_survives_a_save_and_load_and_old_files_still_load()
    {
        var folder = T.TempFolder();
        try
        {
            var path = Path.Combine(folder, "usage.json");
            var first = new UsageLedger(path);
            first.Add(Day, _games.Id, 300, false, new[] { "game" }, endedAt: new DateTime(2026, 10, 26, 20, 10, 0));
            first.SaveIfDirty();

            var second = new UsageLedger(path);
            second.Load();
            Assert.Equal(300, second.GetUsage(Day, _games.Id).Hourly![20]);
            Assert.Equal(300, second.GetUsage(Day, _games.Id).AppHourly!["game"][20]);

            // A file written before hours were kept has no hourly fields.
            File.WriteAllText(path, $$"""{ "2026-10-26": { "{{_games.Id}}": { "LimitedSeconds": 60, "FreeSeconds": 30, "Processes": { "game": 90 } } } }""");
            var old = new UsageLedger(path);
            old.Load();
            Assert.Null(old.LoadError);
            Assert.Null(old.GetUsage(Day, _games.Id).Hourly);
            Assert.Equal(90, old.GetUsage(Day, _games.Id).Processes["game"]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // ---- The day's hours in the report ----

    private PeriodReport DayReport(int resetMinutes = 0, ISet<Guid>? only = null) =>
        UsageAnalytics.Build(_ledger, new[] { _games, _work }, only, new DateTime(2026, 10, 26, 18, 0, 0), resetMinutes, AnalyticsView.Day, Day);

    [Fact]
    public void A_days_report_has_twenty_four_hours_starting_at_midnight()
    {
        _ledger.Add(Day, _games.Id, 1800, false, new[] { "game" }, endedAt: new DateTime(2026, 10, 26, 9, 45, 0));
        _ledger.Add(Day, _work.Id, 600, false, new[] { "editor" }, endedAt: new DateTime(2026, 10, 26, 9, 55, 0));

        var report = DayReport();

        Assert.Equal(24, report.Hours.Count);
        Assert.Equal(Enumerable.Range(0, 24), report.Hours.Select(h => h.ClockHour));
        Assert.Equal(2400, report.Hours[9].TotalSeconds);
        Assert.Equal(1800, report.Hours[9].Groups.Single(g => g.Name == "Games").Seconds);
        Assert.Equal(0, report.Hours[10].TotalSeconds);
    }

    [Fact]
    public void The_hours_start_at_the_hour_budgets_reset()
    {
        var report = DayReport(resetMinutes: 4 * 60);

        Assert.Equal(4, report.Hours[0].ClockHour);
        Assert.Equal(23, report.Hours[19].ClockHour);
        Assert.Equal(0, report.Hours[20].ClockHour);
        Assert.Equal(3, report.Hours[23].ClockHour);
    }

    [Fact]
    public void An_hour_lists_the_apps_that_ran_in_it()
    {
        _ledger.Add(Day, _games.Id, 900, false, new[] { "game", "launcher" }, endedAt: new DateTime(2026, 10, 26, 21, 15, 0));
        _ledger.Add(Day, _games.Id, 300, false, new[] { "game" }, endedAt: new DateTime(2026, 10, 26, 21, 20, 0));

        var apps = DayReport().Hours[21].Groups.Single(g => g.Name == "Games").Apps;

        Assert.Equal(new[] { "game", "launcher" }, apps.Select(a => a.Name));
        Assert.Equal(1200 * 1200 / 2100.0, apps[0].Seconds, 3);   // the apps share the group's 1200 seconds in this hour
        Assert.Equal(1200 * 900 / 2100.0, apps[1].Seconds, 3);
        Assert.Equal(1200, apps.Sum(a => a.Seconds), 3);
    }

    [Fact]
    public void The_hours_follow_the_chosen_groups()
    {
        _ledger.Add(Day, _games.Id, 600, false, new[] { "game" }, endedAt: new DateTime(2026, 10, 26, 8, 30, 0));
        _ledger.Add(Day, _work.Id, 600, false, new[] { "editor" }, endedAt: new DateTime(2026, 10, 26, 8, 40, 0));

        var onlyWork = DayReport(only: new HashSet<Guid> { _work.Id });
        var none = DayReport(only: new HashSet<Guid>());

        Assert.Equal(600, onlyWork.Hours[8].TotalSeconds);
        Assert.Equal("Work", Assert.Single(onlyWork.Hours[8].Groups).Name);
        Assert.All(none.Hours, hour => Assert.Empty(hour.Groups));
    }

    [Fact]
    public void A_day_recorded_before_hours_were_kept_has_a_total_but_empty_hours()
    {
        _ledger.Add(Day, _games.Id, 3600, false, new[] { "game" }); // no clock time: no hourly detail

        var report = DayReport();

        Assert.Equal(3600, report.TotalSeconds);
        Assert.All(report.Hours, hour => Assert.Equal(0, hour.TotalSeconds));
    }

    [Fact]
    public void Weeks_and_months_have_no_hours()
    {
        _ledger.Add(Day, _games.Id, 600, false, new[] { "game" }, endedAt: new DateTime(2026, 10, 26, 8, 30, 0));

        var week = UsageAnalytics.Build(_ledger, new[] { _games }, null, new DateTime(2026, 10, 26, 18, 0, 0), 0, AnalyticsView.Week, Day);
        var month = UsageAnalytics.Build(_ledger, new[] { _games }, null, new DateTime(2026, 10, 26, 18, 0, 0), 0, AnalyticsView.Month, Day);

        Assert.Empty(week.Hours);
        Assert.Empty(month.Hours);
    }
}
