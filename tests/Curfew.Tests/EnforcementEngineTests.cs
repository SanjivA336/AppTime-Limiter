using Curfew.Core.Enforcement;
using Curfew.Core.Models;
using Curfew.Core.Usage;

namespace Curfew.Tests;

public class EnforcementEngineTests
{
    private static readonly string[] Running = { "game" };
    private static readonly string[] Nothing = Array.Empty<string>();

    private readonly UsageLedger _ledger = new();
    private readonly EnforcementEngine _engine;

    public EnforcementEngineTests()
    {
        _engine = new EnforcementEngine(_ledger);
    }

    private static DateOnly Day => DateOnly.FromDateTime(T.Monday);

    // ---- Blocked ----

    [Fact]
    public void Blocked_cell_closes_a_running_process_and_notifies()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Blocked);

        var result = _engine.Tick(T.Settings(60, group), T.At(12), Running);

        var close = Assert.Single(result.Closes);
        Assert.Equal(CloseReason.Blocked, close.Reason);
        Assert.Equal(new[] { "game" }, close.Processes);
        Assert.Contains("Games", Assert.Single(result.Alerts).Message);
    }

    [Fact]
    public void Blocked_cell_records_no_usage()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Blocked);
        var settings = T.Settings(60, group);

        _engine.Tick(settings, T.At(12), Running);
        _engine.Tick(settings, T.At(12, 0, 5), Running);

        Assert.Equal(0, _ledger.GetBudgetSecondsUsed(Day, group.Id));
        Assert.Equal(0, _ledger.GetUsage(Day, group.Id).FreeSeconds);
    }

    [Fact]
    public void Blocked_group_that_is_not_running_does_nothing()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Blocked);

        var result = _engine.Tick(T.Settings(60, group), T.At(12), Nothing);

        Assert.Empty(result.Closes);
        Assert.Empty(result.Alerts);
    }

    [Fact]
    public void Process_names_match_case_insensitively()
    {
        var group = T.Group("Games", new[] { "Warframe.x64" }, CellState.Blocked);

        var result = _engine.Tick(T.Settings(60, group), T.At(12), new[] { "WARFRAME.X64" });

        Assert.Single(result.Closes);
    }

    [Fact]
    public void Only_the_group_in_a_blocked_cell_is_closed()
    {
        var blocked = T.Group("A", new[] { "a" }, CellState.Blocked);
        var free = T.Group("B", new[] { "b" }, CellState.Free);

        var result = _engine.Tick(T.Settings(60, blocked, free), T.At(12), new[] { "a", "b" });

        Assert.Equal("A", Assert.Single(result.Closes).GroupName);
    }

    // ---- Free ----

    [Fact]
    public void Free_cell_tracks_time_without_touching_the_budget()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Free);
        var settings = T.Settings(60, group);

        _engine.Tick(settings, T.At(12), Running);
        var result = _engine.Tick(settings, T.At(12, 0, 20), Running);

        Assert.Empty(result.Closes);
        Assert.Equal(20, _ledger.GetUsage(Day, group.Id).FreeSeconds);
        Assert.Equal(0, _ledger.GetBudgetSecondsUsed(Day, group.Id));
    }

    // ---- Limited + budget ----

    [Fact]
    public void Limited_cell_spends_the_budget()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Limited);
        var settings = T.Settings(60, group);

        _engine.Tick(settings, T.At(12), Running);
        var result = _engine.Tick(settings, T.At(12, 0, 25), Running);

        Assert.Empty(result.Closes);
        Assert.Equal(25, _ledger.GetBudgetSecondsUsed(Day, group.Id));
    }

    [Fact]
    public void Process_is_closed_in_the_tick_that_uses_up_the_budget()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Limited);
        var settings = T.Settings(1, group); // one minute

        _engine.Tick(settings, T.At(12), Running);
        Assert.Empty(_engine.Tick(settings, T.At(12, 0, 30), Running).Closes);
        var result = _engine.Tick(settings, T.At(12, 1, 0), Running);

        Assert.Equal(CloseReason.BudgetUsedUp, Assert.Single(result.Closes).Reason);
        Assert.Contains("budget", Assert.Single(result.Alerts).Message);
    }

    [Fact]
    public void Process_started_after_the_budget_is_gone_is_closed_right_away()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Limited);
        _ledger.Add(Day, group.Id, 3600, countsTowardBudget: true);

        var result = _engine.Tick(T.Settings(60, group), T.At(12), Running);

        Assert.Equal(CloseReason.BudgetUsedUp, Assert.Single(result.Closes).Reason);
    }

    [Fact]
    public void Zero_budget_blocks_limited_cells_entirely()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Limited);

        var result = _engine.Tick(T.Settings(0, group), T.At(12), Running);

        Assert.Single(result.Closes);
    }

    [Fact]
    public void Each_group_runs_out_on_its_own_budget()
    {
        var shortBudget = T.Group("Short", new[] { "a" }, CellState.Limited);
        var longBudget = T.Group("Long", new[] { "b" }, CellState.Limited);
        var settings = T.Settings(10, shortBudget, longBudget);
        shortBudget.BudgetMinutesByDay = AppGroup.NewBudgets(1); // 1 minute, while the other keeps 10
        var both = new[] { "a", "b" };

        _engine.Tick(settings, T.At(12), both);
        _engine.Tick(settings, T.At(12, 0, 30), both);
        var result = _engine.Tick(settings, T.At(12, 1, 0), both);

        // Both ran for the same time, but only the group with the small budget is out.
        Assert.Equal("Short", Assert.Single(result.Closes).GroupName);
    }

    [Fact]
    public void One_groups_usage_never_uses_up_another_groups_budget()
    {
        var first = T.Group("First", new[] { "a" }, CellState.Limited);
        var second = T.Group("Second", new[] { "b" }, CellState.Limited);
        var settings = T.Settings(1, first, second); // 1 minute each

        // Only the first group's app runs, long enough to use up its whole minute.
        _engine.Tick(settings, T.At(12), new[] { "a" });
        _engine.Tick(settings, T.At(12, 0, 30), new[] { "a" });
        var firstCutOff = _engine.Tick(settings, T.At(12, 1, 0), new[] { "a" });

        // Now the second group's app starts: it still has all of its own minute.
        var secondStarts = _engine.Tick(settings, T.At(12, 1, 3), new[] { "b" });

        Assert.Equal("First", Assert.Single(firstCutOff.Closes).GroupName);
        Assert.Empty(secondStarts.Closes);
        Assert.Equal(0, _ledger.GetBudgetSecondsUsed(Day, second.Id) - 3, precision: 6); // only its own 3 seconds so far
    }

    [Fact]
    public void Two_groups_running_at_once_each_spend_only_their_own_budget()
    {
        var a = T.Group("A", new[] { "a" }, CellState.Limited);
        var b = T.Group("B", new[] { "b" }, CellState.Limited);
        var settings = T.Settings(600, a, b);
        var both = new[] { "a", "b" };

        _engine.Tick(settings, T.At(12), both);
        _engine.Tick(settings, T.At(12, 0, 20), both);

        Assert.Equal(20, _ledger.GetBudgetSecondsUsed(Day, a.Id));
        Assert.Equal(20, _ledger.GetBudgetSecondsUsed(Day, b.Id)); // not 40: nothing is pooled
    }

    [Fact]
    public void Free_time_does_not_spend_any_groups_budget()
    {
        var free = T.Group("Free", new[] { "f" }, CellState.Free);
        var settings = T.Settings(1, free);

        _engine.Tick(settings, T.At(12), new[] { "f" });
        _engine.Tick(settings, T.At(12, 0, 30), new[] { "f" });
        var result = _engine.Tick(settings, T.At(12, 1, 0), new[] { "f" });

        Assert.Empty(result.Closes);
        Assert.Equal(0, _ledger.GetBudgetSecondsUsed(Day, free.Id));
    }

    // ---- Time handling ----

    [Fact]
    public void A_long_gap_between_ticks_credits_only_a_capped_amount()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Limited);
        var settings = T.Settings(600, group);

        _engine.Tick(settings, T.At(12), Running);
        _engine.Tick(settings, T.At(15), Running); // e.g. the laptop slept for three hours

        Assert.Equal(EnforcementEngine.MaxCreditedGap.TotalSeconds, _ledger.GetBudgetSecondsUsed(Day, group.Id));
    }

    [Fact]
    public void Clock_moving_backwards_credits_nothing()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Limited);
        var settings = T.Settings(60, group);

        _engine.Tick(settings, T.At(12), Running);
        _engine.Tick(settings, T.At(11), Running);

        Assert.Equal(0, _ledger.GetBudgetSecondsUsed(Day, group.Id));
    }

    [Fact]
    public void The_first_tick_credits_nothing()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Limited);

        _engine.Tick(T.Settings(60, group), T.At(12), Running);

        Assert.Equal(0, _ledger.GetBudgetSecondsUsed(Day, group.Id));
    }

    [Fact]
    public void Usage_is_attributed_to_the_day_each_moment_fell_in()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Limited);
        var settings = T.Settings(600, group);

        _engine.Tick(settings, T.At(23, 59, 40), Running);
        _engine.Tick(settings, T.At(23, 59, 50), Running);
        _engine.Tick(settings, T.At(24, 0, 10), Running); // 00:00:10 on Tuesday: its 20 seconds are 10 before midnight and 10 after

        Assert.Equal(20, _ledger.GetBudgetSecondsUsed(Day, group.Id));
        Assert.Equal(10, _ledger.GetBudgetSecondsUsed(Day.AddDays(1), group.Id));
    }

    // ---- Warnings ----

    [Fact]
    public void Warns_before_the_schedule_blocks_the_group()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Free, 5);
        T.SetRange(group.Schedule, DayOfWeek.Monday, 22, 24, CellState.Blocked);
        var settings = T.Settings(600, group);

        var early = _engine.Tick(settings, T.At(21, 54), Running);
        var warning = _engine.Tick(settings, T.At(21, 55), Running);

        Assert.Empty(early.Alerts);
        var message = Assert.Single(warning.Alerts).Message;
        Assert.Contains("game will close in 5 minutes", message);
        Assert.Contains("apps in Games are about to be blocked", message);
    }

    [Fact]
    public void Warns_when_the_budget_is_nearly_gone()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Limited, 5);
        _ledger.Add(Day, group.Id, 295, countsTowardBudget: true); // 10-minute budget: 5:05 left
        var settings = T.Settings(10, group);

        var first = _engine.Tick(settings, T.At(12), Running);
        var second = _engine.Tick(settings, T.At(12, 0, 10), Running); // 4:55 left

        Assert.Empty(first.Alerts);
        Assert.Equal("game will close in 5 minutes: apps in Games are almost out of budget.", Assert.Single(second.Alerts).Message);
    }

    [Fact]
    public void Each_warning_fires_only_once()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Free, 5);
        T.SetRange(group.Schedule, DayOfWeek.Monday, 22, 24, CellState.Blocked);
        var settings = T.Settings(600, group);

        var counts = new[] { T.At(21, 55), T.At(21, 55, 20), T.At(21, 56), T.At(21, 57) }
            .Select(time => _engine.Tick(settings, time, Running).Alerts.Count)
            .ToArray();

        Assert.Equal(new[] { 1, 0, 0, 0 }, counts);
    }

    [Fact]
    public void Several_warning_times_fire_one_after_another()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Free, 5, 2);
        T.SetRange(group.Schedule, DayOfWeek.Monday, 22, 24, CellState.Blocked);
        var settings = T.Settings(600, group);

        var counts = new[] { T.At(21, 54), T.At(21, 55), T.At(21, 58) }
            .Select(time => _engine.Tick(settings, time, Running).Alerts.Count)
            .ToArray();

        Assert.Equal(new[] { 0, 1, 1 }, counts);
    }

    [Fact]
    public void Crossing_several_warning_times_at_once_sends_a_single_alert()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Free, 5, 2);
        T.SetRange(group.Schedule, DayOfWeek.Monday, 22, 24, CellState.Blocked);
        var settings = T.Settings(600, group);

        var launched = _engine.Tick(settings, T.At(21, 58), Running); // 2 minutes left: both 5 and 2 crossed
        var later = _engine.Tick(settings, T.At(21, 58, 20), Running);

        Assert.Single(launched.Alerts);
        Assert.Empty(later.Alerts);
    }

    [Fact]
    public void Warnings_rearm_after_the_app_is_closed_and_reopened()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Free, 5);
        T.SetRange(group.Schedule, DayOfWeek.Monday, 22, 24, CellState.Blocked);
        var settings = T.Settings(600, group);

        Assert.Single(_engine.Tick(settings, T.At(21, 55), Running).Alerts);
        _engine.Tick(settings, T.At(21, 56), Nothing);
        var again = _engine.Tick(settings, T.At(21, 57), Running);

        Assert.Single(again.Alerts);
    }

    [Fact]
    public void Group_with_no_warnings_configured_never_warns()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Free);
        T.SetRange(group.Schedule, DayOfWeek.Monday, 22, 24, CellState.Blocked);

        var result = _engine.Tick(T.Settings(600, group), T.At(21, 59), Running);

        Assert.Empty(result.Alerts);
    }

    [Fact]
    public void Warns_for_one_minute_left_in_the_singular()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Free, 1);
        T.SetRange(group.Schedule, DayOfWeek.Monday, 22, 24, CellState.Blocked);

        var result = _engine.Tick(T.Settings(600, group), T.At(21, 59), Running);

        Assert.Contains("will close in 1 minute:", Assert.Single(result.Alerts).Message);
    }

    // ---- Notification noise and status ----

    [Fact]
    public void Repeated_close_notices_for_the_same_group_are_rate_limited()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Blocked);
        var settings = T.Settings(60, group);

        var counts = new[] { T.At(12), T.At(12, 0, 3), T.At(12, 0, 10), T.At(12, 0, 40) }
            .Select(time => _engine.Tick(settings, time, Running))
            .Select(r => (Closes: r.Closes.Count, Alerts: r.Alerts.Count))
            .ToArray();

        // Still told to close every time, but only notified at the start and again after the cooldown.
        Assert.All(counts, c => Assert.Equal(1, c.Closes));
        Assert.Equal(new[] { 1, 0, 0, 1 }, counts.Select(c => c.Alerts));
    }

    [Fact]
    public void Statuses_report_running_and_cell_state_for_every_group()
    {
        var running = T.Group("A", new[] { "a" }, CellState.Free);
        var idle = T.Group("B", new[] { "b" }, CellState.Limited);

        var result = _engine.Tick(T.Settings(60, running, idle), T.At(12), new[] { "a" });

        Assert.Equal(new GroupStatus(running.Id, true, CellState.Free), result.Statuses[0]);
        Assert.Equal(new GroupStatus(idle.Id, false, CellState.Limited), result.Statuses[1]);
    }

    [Fact]
    public void Settings_changes_take_effect_on_the_next_tick()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Free);
        var settings = T.Settings(60, group);

        Assert.Empty(_engine.Tick(settings, T.At(12), Running).Closes);
        group.Schedule = WeeklySchedule.Filled(CellState.Blocked);

        Assert.Single(_engine.Tick(settings, T.At(12, 0, 3), Running).Closes);
    }
}
