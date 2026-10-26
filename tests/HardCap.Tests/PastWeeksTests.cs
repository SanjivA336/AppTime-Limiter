using HardCap.Core.Enforcement;
using HardCap.Core.Models;
using HardCap.Core.Usage;

namespace HardCap.Tests;

public class PastWeeksTests
{
    private static readonly DateOnly ThisMonday = new(2026, 10, 26);
    private static readonly DateTime Wednesday = new(2026, 10, 28, 12, 0, 0);

    private readonly UsageLedger _ledger = new();
    private readonly AppGroup _games = T.Group("Games", new[] { "game" }, CellState.Free);

    private PeriodReport Report(int weeksBack) =>
        UsageAnalytics.Build(_ledger, new[] { _games }, null, Wednesday, resetMinutes: 0, AnalyticsView.Week, DateOnly.FromDateTime(Wednesday).AddDays(-7 * weeksBack));

    private void Use(DateOnly day, double hours) => _ledger.Add(day, _games.Id, hours * 3600, countsTowardBudget: false);

    // ---- Looking at an earlier week ----

    [Fact]
    public void Last_week_shows_last_weeks_days_not_this_weeks()
    {
        Use(ThisMonday.AddDays(-7), 2);   // last Monday
        Use(ThisMonday.AddDays(-5), 3);   // last Wednesday
        Use(ThisMonday, 9);               // this Monday

        var report = Report(1);

        Assert.Equal(ThisMonday.AddDays(-7), report.Start);
        Assert.Equal(7, report.Days.Count);
        Assert.Equal(2 * 3600, report.Days[0].TotalSeconds);
        Assert.Equal(3 * 3600, report.Days[2].TotalSeconds);
        Assert.Equal(5 * 3600, report.TotalSeconds);
        Assert.False(report.IsCurrent);
    }

    [Fact]
    public void An_earlier_week_is_averaged_over_all_seven_days()
    {
        Use(ThisMonday.AddDays(-14), 1); // so tracking began before last week
        Use(ThisMonday.AddDays(-7), 7);

        Assert.Equal(1 * 3600, Report(1).DailyAverageSeconds); // 7 hours over 7 days
    }

    [Fact]
    public void The_current_week_is_still_averaged_up_to_today()
    {
        Use(ThisMonday, 3);
        Use(ThisMonday.AddDays(1), 3);
        Use(ThisMonday.AddDays(2), 3);

        var report = Report(0);

        Assert.True(report.IsCurrent);
        Assert.Equal(3 * 3600, report.DailyAverageSeconds); // 9 hours over Monday to Wednesday
    }

    [Fact]
    public void A_week_that_was_only_partly_tracked_is_averaged_over_the_tracked_days()
    {
        // Tracking began last Friday: Friday, Saturday and Sunday are the only tracked days of last week.
        Use(ThisMonday.AddDays(-3), 3);
        Use(ThisMonday.AddDays(-2), 3);
        Use(ThisMonday.AddDays(-1), 3);

        Assert.Equal(3 * 3600, Report(1).DailyAverageSeconds); // 9 hours over 3 days, not 7
    }

    [Fact]
    public void An_earlier_weeks_change_compares_with_the_weeks_before_that_one()
    {
        // Two weeks ago: 1 hour a day. Last week: 2 hours a day.
        for (var offset = 0; offset < 7; offset++)
        {
            Use(ThisMonday.AddDays(-14 + offset), 1);
            Use(ThisMonday.AddDays(-7 + offset), 2);
        }

        var report = Report(1);

        Assert.Equal(1 * 3600, report.PreviousDailyAverageSeconds);
        Assert.Equal(100, report.ChangePercent!.Value, precision: 6);
    }

    [Fact]
    public void Going_back_past_the_data_finds_nothing_to_compare()
    {
        Use(ThisMonday, 1);

        var report = Report(3);

        Assert.Equal(0, report.TotalSeconds);
        Assert.Null(report.ChangePercent);
    }


    // ---- The week's date range ----

    [Theory]
    [InlineData(2026, 10, 19, "Oct 19 – 25")]
    [InlineData(2026, 10, 26, "Oct 26 – Nov 1")]
    [InlineData(2026, 3, 2, "Mar 2 – 8")]
    public void A_week_reads_as_a_short_date_range(int y, int m, int d, string expected) =>
        Assert.Equal(expected, TimeText.WeekRange(new DateOnly(y, m, d), new DateOnly(2026, 10, 28)));

    [Fact]
    public void A_week_in_another_year_shows_the_years()
    {
        Assert.Equal("Dec 29, 2025 – Jan 4, 2026", TimeText.WeekRange(new DateOnly(2025, 12, 29), new DateOnly(2026, 1, 8)));
        Assert.Equal("Oct 20, 2025 – Oct 26, 2025", TimeText.WeekRange(new DateOnly(2025, 10, 20), new DateOnly(2026, 10, 28)));
    }

    // ---- Keeping three months ----

    [Fact]
    public void Days_older_than_the_cutoff_are_deleted_and_newer_ones_kept()
    {
        var cutoff = new DateOnly(2026, 7, 28);
        _ledger.Add(cutoff.AddDays(-1), _games.Id, 100, false);
        _ledger.Add(cutoff, _games.Id, 200, false);
        _ledger.Add(cutoff.AddDays(30), _games.Id, 300, false);

        var removed = _ledger.PruneOlderThan(cutoff);

        Assert.Equal(1, removed);
        Assert.Equal(0, _ledger.GetUsage(cutoff.AddDays(-1), _games.Id).FreeSeconds);
        Assert.Equal(200, _ledger.GetUsage(cutoff, _games.Id).FreeSeconds);
        Assert.Equal(300, _ledger.GetUsage(cutoff.AddDays(30), _games.Id).FreeSeconds);
        Assert.Equal(cutoff, _ledger.FirstDay);
    }

    [Fact]
    public void Pruning_nothing_leaves_the_record_unchanged_and_not_dirty()
    {
        var folder = T.TempFolder();
        try
        {
            var ledger = new UsageLedger(Path.Combine(folder, "usage.json"));
            ledger.Add(new DateOnly(2026, 10, 1), _games.Id, 100, false);
            ledger.SaveIfDirty();

            Assert.Equal(0, ledger.PruneOlderThan(new DateOnly(2026, 1, 1)));
            Assert.False(ledger.IsDirty);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task The_regular_save_drops_usage_older_than_three_months()
    {
        var folder = T.TempFolder();
        try
        {
            var path = Path.Combine(folder, "usage.json");
            var ledger = new UsageLedger(path);
            var now = new DateTime(2026, 10, 28, 12, 0, 0);
            var today = DateOnly.FromDateTime(now);
            ledger.Add(today.AddMonths(-3).AddDays(-1), _games.Id, 100, false); // just too old
            ledger.Add(today.AddMonths(-3), _games.Id, 100, false);              // the oldest day kept
            ledger.Add(today.AddDays(-1), _games.Id, 100, false);

            var settings = T.Settings(60, T.Group("Games", new[] { "game" }, CellState.Free));
            var runner = new EnforcementRunner(() => settings, new FakeProcessController(), new FakeNotifier(), ledger);
            await runner.RunOnceAsync(now);

            var reloaded = new UsageLedger(path);
            reloaded.Load();
            Assert.Equal(today.AddMonths(-3), reloaded.FirstDay);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
