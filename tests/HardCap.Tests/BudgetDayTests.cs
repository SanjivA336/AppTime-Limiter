using HardCap.Core.Enforcement;
using HardCap.Core.Models;
using HardCap.Core.Usage;

namespace HardCap.Tests;

public class BudgetDayTests
{
    private const int FourAm = 4 * 60;

    [Fact]
    public void With_the_default_midnight_reset_the_budget_day_is_the_calendar_day()
    {
        Assert.Equal(new DateOnly(2026, 10, 26), BudgetDay.Of(T.At(0), 0));
        Assert.Equal(new DateOnly(2026, 10, 26), BudgetDay.Of(T.At(23, 59), 0));
    }

    [Fact]
    public void Before_the_reset_time_you_are_still_in_yesterdays_budget_day()
    {
        // Monday 1 AM belongs to Sunday's budget day when budgets reset at 4 AM.
        Assert.Equal(new DateOnly(2026, 10, 25), BudgetDay.Of(T.At(1), FourAm));
        Assert.Equal(DayOfWeek.Sunday, BudgetDay.DayOfWeekOf(T.At(3, 59), FourAm));
    }

    [Fact]
    public void At_and_after_the_reset_time_the_new_budget_day_has_begun()
    {
        Assert.Equal(new DateOnly(2026, 10, 26), BudgetDay.Of(T.At(4), FourAm));
        Assert.Equal(DayOfWeek.Monday, BudgetDay.DayOfWeekOf(T.At(15), FourAm));
    }

    [Fact]
    public void The_reset_moment_is_found_exactly()
    {
        Assert.True(BudgetDay.IsResetMoment(T.At(4), FourAm));
        Assert.False(BudgetDay.IsResetMoment(T.At(4, 30), FourAm));
        Assert.True(BudgetDay.IsResetMoment(T.At(0), 0));
    }

    [Fact]
    public void The_next_reset_is_later_today_or_tomorrow()
    {
        Assert.Equal(T.At(4), BudgetDay.NextReset(T.At(1), FourAm));
        Assert.Equal(T.At(4).AddDays(1), BudgetDay.NextReset(T.At(4), FourAm));
        Assert.Equal(T.At(4).AddDays(1), BudgetDay.NextReset(T.At(20), FourAm));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(240, 240)]
    [InlineData(250, 240)]
    [InlineData(255, 270)]
    [InlineData(-30, 0)]
    [InlineData(5000, 1410)]
    public void Reset_time_is_rounded_to_half_hours_inside_one_day(int given, int expected) =>
        Assert.Equal(expected, BudgetDay.Tidy(given));

    [Fact]
    public void The_reset_time_reads_as_a_clock_time()
    {
        Assert.Equal("12:00 AM", BudgetDay.Describe(0));
        Assert.Equal("4:30 AM", BudgetDay.Describe(270));
        Assert.Equal("11:30 PM", BudgetDay.Describe(1410));
    }

    // ---- The engine and forecast follow the reset time ----

    [Fact]
    public void Late_night_use_is_charged_to_the_previous_budget_day_and_uses_that_days_budget()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Limited);
        var settings = T.Settings(120, group);
        settings.ResetMinutes = FourAm;
        group.SetBudgetMinutes(DayOfWeek.Sunday, 5);   // Sunday's budget is only 5 minutes
        group.SetBudgetMinutes(DayOfWeek.Monday, 120);
        var ledger = new UsageLedger();
        var engine = new EnforcementEngine(ledger);
        var running = new[] { "game" };

        // Monday 3:00 AM is still Sunday's budget day. Five minutes of play uses up Sunday's whole budget.
        TickResult? last = null;
        for (var step = 0; step <= 10; step++)
        {
            last = engine.Tick(settings, T.At(3).AddSeconds(30 * step), running);
        }

        Assert.Equal(300, ledger.GetBudgetSecondsUsed(new DateOnly(2026, 10, 25), group.Id));
        Assert.Equal(0, ledger.GetBudgetSecondsUsed(new DateOnly(2026, 10, 26), group.Id));
        Assert.Equal(CloseReason.BudgetUsedUp, Assert.Single(last!.Closes).Reason);

        // At 4:00 AM the new budget day begins with Monday's full budget, so the same app may run again.
        var fresh = engine.Tick(settings, T.At(4), running);
        Assert.Empty(fresh.Closes);
    }

    [Fact]
    public void Blocked_until_a_used_up_budget_resets_uses_the_reset_time_not_midnight()
    {
        var schedule = WeeklySchedule.Filled(CellState.Limited);
        int Budget(DayOfWeek day) => 60;

        var withMidnightReset = LimitForecast.NextAllowedTime(schedule, T.At(1), 0, Budget, resetMinutes: 0);
        var withFourAmReset = LimitForecast.NextAllowedTime(schedule, T.At(1), 0, Budget, resetMinutes: FourAm);

        Assert.Equal(T.At(0).AddDays(1), withMidnightReset);
        Assert.Equal(T.At(4), withFourAmReset);
    }

    [Fact]
    public void The_forecast_refills_the_budget_at_the_reset_time()
    {
        var schedule = WeeklySchedule.Filled(CellState.Limited);

        // At 3:40 AM there are 30 minutes left of a 60-minute budget (30 used). With no reset in sight it would run out at
        // 4:10, 30 minutes from now. With a 4:00 AM reset the budget refills first and lasts until 5:00, 80 minutes from now.
        var withoutReset = LimitForecast.Next(schedule, T.At(3, 40), 30 * 60, _ => 60, TimeSpan.FromHours(3), resetMinutes: 0);
        var withReset = LimitForecast.Next(schedule, T.At(3, 40), 30 * 60, _ => 60, TimeSpan.FromHours(3), resetMinutes: FourAm);

        Assert.Equal(TimeSpan.FromMinutes(30), withoutReset!.In);
        Assert.Equal(TimeSpan.FromMinutes(80), withReset!.In);
        Assert.Equal(LimitReason.Budget, withReset.Reason);
    }
}
