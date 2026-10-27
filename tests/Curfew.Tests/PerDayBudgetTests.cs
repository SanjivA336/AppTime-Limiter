using Curfew.Core.Enforcement;
using Curfew.Core.Models;
using Curfew.Core.Usage;

namespace Curfew.Tests;

/// <summary>
/// Each day of the week can have its own budget. These tests use the example that motivated it:
/// 3 hours on weekends (6 PM to 11 PM) and 2 hours on weekdays (8 PM to 10 PM).
/// </summary>
public class PerDayBudgetTests
{
    private static readonly string[] Running = { "game" };

    /// <summary>Monday 2026-10-26 is a weekday; Saturday 2026-10-31 is a weekend day.</summary>
    private static DateTime Saturday(int hour, int minute = 0, int second = 0) =>
        new DateTime(2026, 10, 31).AddHours(hour).AddMinutes(minute).AddSeconds(second);

    private static AppGroup GroupWithTheExampleSchedule()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Blocked);
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
        {
            T.SetRange(group.Schedule, day, 20, 22, CellState.Limited); // 8 PM to 10 PM
        }

        foreach (var day in new[] { DayOfWeek.Saturday, DayOfWeek.Sunday })
        {
            T.SetRange(group.Schedule, day, 18, 23, CellState.Limited); // 6 PM to 11 PM
        }

        return group;
    }

    private static AppSettings TheExampleSettings(AppGroup group)
    {
        var settings = T.Settings(120, group); // 2 hours on every day...
        group.SetBudgetMinutes(DayOfWeek.Saturday, 180); // ...but 3 hours on weekends
        group.SetBudgetMinutes(DayOfWeek.Sunday, 180);
        return settings;
    }

    [Fact]
    public void Budget_for_a_day_is_independent_of_the_other_days()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Free);
        T.Settings(60, group);

        group.SetBudgetMinutes(DayOfWeek.Saturday, 200);

        Assert.Equal(200, group.BudgetMinutesFor(DayOfWeek.Saturday));
        Assert.Equal(60, group.BudgetMinutesFor(DayOfWeek.Sunday));
        Assert.Equal(60, group.BudgetMinutesFor(DayOfWeek.Friday));
    }

    [Fact]
    public void Budget_for_a_day_is_independent_of_the_other_groups()
    {
        var games = T.Group("Games", new[] { "game" }, CellState.Free);
        var work = T.Group("Work", new[] { "editor" }, CellState.Free);
        T.Settings(60, games, work);

        games.SetBudgetMinutes(DayOfWeek.Saturday, 200);

        Assert.Equal(200, games.BudgetMinutesFor(DayOfWeek.Saturday));
        Assert.Equal(60, work.BudgetMinutesFor(DayOfWeek.Saturday));
    }

    [Fact]
    public void A_new_group_starts_with_the_default_budget_every_day()
    {
        var group = new AppGroup();

        Assert.All(Enum.GetValues<DayOfWeek>(), day => Assert.Equal(AppGroup.DefaultDailyBudgetMinutes, group.BudgetMinutesFor(day)));
    }

    [Fact]
    public void Copies_of_a_group_do_not_share_their_budgets()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Free);
        T.Settings(60, group);
        var copy = group.Clone();

        copy.SetBudgetMinutes(DayOfWeek.Monday, 5);

        Assert.Equal(60, group.BudgetMinutesFor(DayOfWeek.Monday));
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(5000, 1440)]
    public void SetBudgetMinutes_keeps_the_value_in_range(int input, int expected)
    {
        var group = new AppGroup();

        group.SetBudgetMinutes(DayOfWeek.Monday, input);

        Assert.Equal(expected, group.BudgetMinutesFor(DayOfWeek.Monday));
    }

    [Fact]
    public void A_weekday_runs_out_after_its_own_two_hours()
    {
        var group = GroupWithTheExampleSchedule();
        var settings = TheExampleSettings(group);
        var ledger = new UsageLedger();
        var engine = new EnforcementEngine(ledger);
        var monday = T.At(20, 0); // 8 PM

        // 119 minutes already used: one minute left of Monday's 120.
        ledger.Add(DateOnly.FromDateTime(monday), group.Id, 119 * 60, countsTowardBudget: true);
        engine.Tick(settings, monday, Running);
        var halfwayThroughTheLastMinute = engine.Tick(settings, monday.AddSeconds(30), Running);
        var lastMinuteUsedUp = engine.Tick(settings, monday.AddSeconds(60), Running);

        Assert.Empty(halfwayThroughTheLastMinute.Closes);
        Assert.Equal(CloseReason.BudgetUsedUp, Assert.Single(lastMinuteUsedUp.Closes).Reason);
    }

    [Fact]
    public void The_same_usage_is_still_within_budget_on_a_weekend()
    {
        var group = GroupWithTheExampleSchedule();
        var settings = TheExampleSettings(group);
        var ledger = new UsageLedger();
        var engine = new EnforcementEngine(ledger);
        var saturday = Saturday(19, 0); // 7 PM

        // 2 hours used: a weekday would be out of budget by now, but Saturday allows 3.
        ledger.Add(DateOnly.FromDateTime(saturday), group.Id, 120 * 60, countsTowardBudget: true);
        engine.Tick(settings, saturday, Running);
        var result = engine.Tick(settings, saturday.AddSeconds(20), Running);

        Assert.Empty(result.Closes);
    }

    [Fact]
    public void A_weekend_runs_out_after_its_own_three_hours()
    {
        var group = GroupWithTheExampleSchedule();
        var settings = TheExampleSettings(group);
        var ledger = new UsageLedger();
        var engine = new EnforcementEngine(ledger);
        var saturday = Saturday(21, 0);

        ledger.Add(DateOnly.FromDateTime(saturday), group.Id, 179 * 60, countsTowardBudget: true);
        engine.Tick(settings, saturday, Running);
        var halfwayThroughTheLastMinute = engine.Tick(settings, saturday.AddSeconds(30), Running);
        var lastMinuteUsedUp = engine.Tick(settings, saturday.AddSeconds(60), Running);

        Assert.Empty(halfwayThroughTheLastMinute.Closes);
        Assert.Equal(CloseReason.BudgetUsedUp, Assert.Single(lastMinuteUsedUp.Closes).Reason);
    }

    [Fact]
    public void Outside_the_painted_window_the_app_is_blocked_whatever_the_budget()
    {
        var group = GroupWithTheExampleSchedule();
        var settings = TheExampleSettings(group);
        var engine = new EnforcementEngine(new UsageLedger());

        var result = engine.Tick(settings, Saturday(16, 0), Running); // 4 PM: before the 6 PM window opens

        Assert.Equal(CloseReason.Blocked, Assert.Single(result.Closes).Reason);
    }

    // ---- Forecast across midnight ----

    [Fact]
    public void The_forecast_uses_the_next_days_budget_after_midnight()
    {
        var schedule = WeeklySchedule.Filled(CellState.Limited);
        Func<DayOfWeek, int> budgets = day => day == DayOfWeek.Monday ? 10 : 30; // Monday 10 min, Tuesday 30 min

        // Monday 11:59 PM with 2 minutes of Monday's budget left (8 of 10 used): 1 minute to midnight, then a fresh
        // 30 minutes for Tuesday: 1 + 30 = 31 minutes.
        var end = LimitForecast.Next(schedule, T.At(23, 59), 8 * 60, budgets, TimeSpan.FromHours(2));

        Assert.Equal(LimitReason.Budget, end!.Reason);
        Assert.Equal(TimeSpan.FromMinutes(31), end.In);
    }

    [Fact]
    public void The_forecast_starts_from_todays_budget()
    {
        var schedule = WeeklySchedule.Filled(CellState.Limited);
        Func<DayOfWeek, int> budgets = day => day == DayOfWeek.Saturday ? 180 : 120;

        var saturdayEnd = LimitForecast.Next(schedule, Saturday(18), 0, budgets, TimeSpan.FromHours(4));
        var mondayEnd = LimitForecast.Next(schedule, T.At(18), 0, budgets, TimeSpan.FromHours(4));

        Assert.Equal(TimeSpan.FromHours(3), saturdayEnd!.In);
        Assert.Equal(TimeSpan.FromHours(2), mondayEnd!.In);
    }

    // ---- Which days use a budget ----

    [Fact]
    public void HasLimitedCells_can_be_asked_one_day_at_a_time()
    {
        var group = GroupWithTheExampleSchedule();
        group.Schedule.SetState(DayOfWeek.Wednesday, 40, CellState.Free);
        T.SetRange(group.Schedule, DayOfWeek.Wednesday, 20, 22, CellState.Blocked); // Wednesday no longer has any Limited time

        Assert.False(group.Schedule.HasLimitedCells(DayOfWeek.Wednesday));
        Assert.True(group.Schedule.HasLimitedCells(DayOfWeek.Saturday));
        Assert.True(group.Schedule.HasLimitedCells());
    }
}
