using Curfew.Core.Enforcement;
using Curfew.Core.Models;
using Curfew.Core.Usage;

namespace Curfew.Tests;

/// <summary>
/// Apps and groups that run at the same time must not make time run faster: a group's budget drains once per second however
/// many of its apps are open, the card only animates while the budget is really going down, and the Analytics page never
/// shows more than 60 minutes in an hour or more than 24 hours in a day.
/// </summary>
public class OverlapTests
{
    private static readonly DateOnly Day = new(2026, 10, 26);
    private static readonly string[] BothApps = { "game", "launcher" };

    private readonly UsageLedger _ledger = new();
    private readonly EnforcementEngine _engine;
    private DateTime _now = T.At(12);

    public OverlapTests()
    {
        _engine = new EnforcementEngine(_ledger);
    }

    private TickResult Tick(AppSettings settings, string[] running, string? inFront = null)
    {
        _now = _now.AddSeconds(30);
        return _engine.Tick(settings, _now, running, inFront);
    }

    // ---- Budget: two apps do not drain it twice as fast ----

    [Fact]
    public void Two_apps_in_one_group_use_the_budget_once_not_twice()
    {
        var group = T.Group("Games", new[] { "game", "launcher" }, CellState.Limited);
        var settings = T.Settings(60, group);
        _engine.Tick(settings, _now, BothApps);

        for (var i = 0; i < 10; i++)
        {
            Tick(settings, BothApps); // five minutes with both open
        }

        Assert.Equal(5 * 60, _ledger.GetBudgetSecondsUsed(Day, group.Id));
    }

    [Fact]
    public void One_app_and_two_apps_use_the_same_budget()
    {
        var one = T.Group("One", new[] { "game", "launcher" }, CellState.Limited);
        var two = T.Group("Two", new[] { "other", "extra" }, CellState.Limited);
        var settings = T.Settings(60, one, two);
        var running = new[] { "game", "other", "extra" };
        _engine.Tick(settings, _now, running);

        for (var i = 0; i < 10; i++)
        {
            Tick(settings, running);
        }

        Assert.Equal(_ledger.GetBudgetSecondsUsed(Day, one.Id), _ledger.GetBudgetSecondsUsed(Day, two.Id));
    }

    // ---- The card only animates while the budget is going down ----

    private static bool Draining(TickResult result, Guid groupId) => result.Statuses.Single(s => s.GroupId == groupId).IsBudgetDraining;

    [Fact]
    public void The_budget_is_draining_while_a_limited_group_is_running_with_budget_left()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Limited);
        var settings = T.Settings(60, group);
        _engine.Tick(settings, _now, new[] { "game" });

        Assert.True(Draining(Tick(settings, new[] { "game" }), group.Id));
    }

    [Fact]
    public void The_budget_is_not_draining_when_nothing_is_running_or_the_cell_is_free_or_blocked()
    {
        var limited = T.Group("Limited", new[] { "a" }, CellState.Limited);
        var free = T.Group("Free", new[] { "b" }, CellState.Free);
        var blocked = T.Group("Blocked", new[] { "c" }, CellState.Blocked);
        var settings = T.Settings(60, limited, free, blocked);
        _engine.Tick(settings, _now, new[] { "b" });

        var onlyFreeRunning = Tick(settings, new[] { "b" });
        Assert.False(Draining(onlyFreeRunning, limited.Id));   // limited, but nothing of it is running
        Assert.False(Draining(onlyFreeRunning, free.Id));      // running, but a Free cell spends nothing

        var blockedRunning = Tick(settings, new[] { "c" });
        Assert.False(Draining(blockedRunning, blocked.Id));    // being closed, not spending
    }

    [Fact]
    public void A_paused_group_does_not_drain()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Limited);
        group.IsPaused = true;
        var settings = T.Settings(60, group);
        _engine.Tick(settings, _now, new[] { "game" });

        Assert.False(Draining(Tick(settings, new[] { "game" }), group.Id));
    }

    [Fact]
    public void A_group_with_no_budget_left_does_not_drain()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Limited);
        var settings = T.Settings(1, group); // one minute
        _engine.Tick(settings, _now, new[] { "game" });
        Tick(settings, new[] { "game" });
        var spent = Tick(settings, new[] { "game" });          // the minute is gone

        Assert.False(Draining(spent, group.Id));
    }

    [Fact]
    public void In_front_mode_only_drains_while_the_app_is_in_front()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Limited);
        group.CountMode = CountMode.WhileInFront;
        var settings = T.Settings(60, group);
        _engine.Tick(settings, _now, new[] { "game" }, "browser");

        Assert.False(Draining(Tick(settings, new[] { "game" }, "browser"), group.Id));
        Assert.True(Draining(Tick(settings, new[] { "game" }, "game"), group.Id));
    }

    // ---- Analytics: no hour over 60 minutes, no day over 24 hours ----

    private readonly AppGroup _games = T.Group("Games", new[] { "game", "launcher" }, CellState.Free);
    private readonly AppGroup _work = T.Group("Work", new[] { "editor" }, CellState.Free);

    private PeriodReport DayReport(ISet<Guid>? only = null) =>
        UsageAnalytics.Build(_ledger, new[] { _games, _work }, only, new DateTime(2026, 10, 26, 23, 0, 0), 0, AnalyticsView.Day, Day);

    private void FillHour(AppGroup group, int hour, double seconds, params string[] apps) =>
        _ledger.Add(Day, group.Id, seconds, false, apps, endedAt: new DateTime(2026, 10, 26, hour, 0, 0).AddSeconds(seconds));

    [Fact]
    public void Two_groups_in_the_same_hour_are_shown_as_no_more_than_that_hour()
    {
        FillHour(_games, 20, 3000, "game");   // 50 minutes
        FillHour(_work, 20, 3000, "editor");  // another 50 minutes at the same time

        var hour = DayReport().Hours[20];

        Assert.Equal(3600, hour.TotalSeconds, 3);
        Assert.Equal(1800, hour.Groups.Single(g => g.Name == "Games").Seconds, 3);   // 3000 * 3600/6000
        Assert.Equal(1800, hour.Groups.Single(g => g.Name == "Work").Seconds, 3);
    }

    [Fact]
    public void An_hour_that_does_not_overlap_is_left_alone()
    {
        FillHour(_games, 20, 2400, "game");
        FillHour(_work, 20, 600, "editor");

        Assert.Equal(3000, DayReport().Hours[20].TotalSeconds, 3);
    }

    [Fact]
    public void A_group_shows_the_same_hour_whichever_groups_are_selected()
    {
        FillHour(_games, 20, 3000, "game");
        FillHour(_work, 20, 3000, "editor");

        var alone = DayReport(new HashSet<Guid> { _games.Id }).Hours[20].TotalSeconds;
        var together = DayReport().Hours[20].Groups.Single(g => g.Name == "Games").Seconds;

        Assert.Equal(together, alone, 3);
    }

    [Fact]
    public void A_day_never_passes_24_hours_even_when_groups_ran_together_all_day()
    {
        for (var hour = 0; hour < 24; hour++)
        {
            FillHour(_games, hour, 3600, "game");
            FillHour(_work, hour, 3600, "editor");
        }

        var report = DayReport();

        Assert.Equal(24 * 3600, report.TotalSeconds, 3);
        Assert.All(report.Hours, h => Assert.True(h.TotalSeconds <= 3600.001));
        Assert.Equal(24 * 3600, report.Days.Single().TotalSeconds, 3);
    }

    [Fact]
    public void Apps_in_a_group_never_add_up_to_more_than_the_group()
    {
        _ledger.Add(Day, _games.Id, 3600, false, BothApps, endedAt: new DateTime(2026, 10, 26, 21, 0, 0)); // two apps, one hour

        var games = DayReport().Groups.Single(g => g.Name == "Games");

        Assert.Equal(3600, games.Seconds, 3);
        Assert.Equal(3600, games.Apps.Sum(a => a.Seconds), 3);
        Assert.All(games.Apps, a => Assert.Equal(1800, a.Seconds, 3));
    }

    [Fact]
    public void Days_with_no_hourly_detail_are_shown_as_recorded()
    {
        _ledger.Add(Day, _games.Id, 7200, false);   // recorded before hours were kept
        _ledger.Add(Day, _work.Id, 7200, false);

        Assert.Equal(14400, DayReport().TotalSeconds, 3);
    }

    [Fact]
    public void Only_the_part_of_a_day_with_hourly_detail_is_scaled()
    {
        _ledger.Add(Day, _games.Id, 3600, false);                       // an hour recorded without hourly detail
        FillHour(_games, 20, 3000, "game");
        FillHour(_work, 20, 3000, "editor");

        var games = DayReport().Groups.Single(g => g.Name == "Games");

        Assert.Equal(3600 + 1800, games.Seconds, 3);   // the plain hour stays whole; the overlapping hour is halved
    }
}
