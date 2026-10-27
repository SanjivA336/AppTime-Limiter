using Curfew.Core.Enforcement;
using Curfew.Core.Models;

namespace Curfew.Tests;

/// <summary>
/// "When can this group next run?" This is what a card says after "Blocked until", including when the
/// group is in a Limited cell but today's budget is already spent.
/// T.Monday is 2026-10-26, a Monday.
/// </summary>
public class NextAllowedTimeTests
{
    private static Func<DayOfWeek, int> Budget(int minutes) => _ => minutes;

    private static WeeklySchedule Limited(DayOfWeek day, int fromHour, int toHour)
    {
        var schedule = WeeklySchedule.Filled(CellState.Blocked);
        T.SetRange(schedule, day, fromHour, toHour, CellState.Limited);
        return schedule;
    }

    [Fact]
    public void A_group_that_is_allowed_now_is_allowed_now()
    {
        var schedule = Limited(DayOfWeek.Monday, 18, 23);

        Assert.Equal(T.At(19, 0), LimitForecast.NextAllowedTime(schedule, T.At(19, 0), 3600, Budget(60)));
        Assert.Equal(T.At(12, 0), LimitForecast.NextAllowedTime(WeeklySchedule.Filled(CellState.Free), T.At(12, 0), 0, Budget(60)));
    }

    [Fact]
    public void Blocked_cells_are_waited_out()
    {
        var schedule = Limited(DayOfWeek.Monday, 18, 23);

        Assert.Equal(T.At(18, 0), LimitForecast.NextAllowedTime(schedule, T.At(12, 0), 3600, Budget(60)));
    }

    [Fact]
    public void With_the_budget_spent_a_limited_cell_waits_for_the_next_free_cell()
    {
        // Monday: Limited 6 PM to 11 PM, then Free from 11 PM. The budget ran out at 7 PM.
        var schedule = Limited(DayOfWeek.Monday, 18, 23);
        T.SetRange(schedule, DayOfWeek.Monday, 23, 24, CellState.Free);

        var allowedAgain = LimitForecast.NextAllowedTime(schedule, T.At(19, 0), 0, Budget(60));

        Assert.Equal(T.At(23, 0), allowedAgain);
    }

    [Fact]
    public void With_the_budget_spent_a_limited_cell_waits_for_the_reset_at_midnight()
    {
        // Limited all day, every day. Today's budget is gone; tomorrow starts fresh at midnight.
        var schedule = WeeklySchedule.Filled(CellState.Limited);

        var allowedAgain = LimitForecast.NextAllowedTime(schedule, T.At(15, 0), 0, Budget(60));

        Assert.Equal(T.At(24, 0), allowedAgain); // midnight, as Tuesday begins
    }

    [Fact]
    public void After_the_reset_it_waits_for_the_next_limited_window()
    {
        // Limited only Monday and Tuesday evenings. Monday's budget is spent at 7 PM, so the next chance
        // is Tuesday 6 PM: a Limited cell, but on a day with a fresh budget.
        var schedule = Limited(DayOfWeek.Monday, 18, 23);
        T.SetRange(schedule, DayOfWeek.Tuesday, 18, 23, CellState.Limited);

        var allowedAgain = LimitForecast.NextAllowedTime(schedule, T.At(19, 0), 0, Budget(60));

        Assert.Equal(T.At(18, 0).AddDays(1), allowedAgain);
    }

    [Fact]
    public void A_free_cell_before_the_reset_beats_a_limited_cell_after_it()
    {
        var schedule = Limited(DayOfWeek.Monday, 18, 23);
        T.SetRange(schedule, DayOfWeek.Monday, 23, 24, CellState.Free);   // free again at 11 PM tonight...
        T.SetRange(schedule, DayOfWeek.Tuesday, 0, 24, CellState.Limited); // ...and Limited all of tomorrow

        Assert.Equal(T.At(23, 0), LimitForecast.NextAllowedTime(schedule, T.At(19, 0), 0, Budget(60)));
    }

    [Fact]
    public void A_day_with_no_budget_is_skipped()
    {
        // Limited every day, but Tuesday's budget is 0, so Tuesday can't be used. Wednesday is the next chance.
        var schedule = WeeklySchedule.Filled(CellState.Limited);
        Func<DayOfWeek, int> budgets = day => day == DayOfWeek.Tuesday ? 0 : 60;

        var allowedAgain = LimitForecast.NextAllowedTime(schedule, T.At(15, 0), 0, budgets);

        Assert.Equal(T.At(24, 0).AddDays(1), allowedAgain); // Wednesday 12:00 AM
    }

    [Fact]
    public void A_day_with_no_budget_still_allows_its_free_cells()
    {
        var schedule = WeeklySchedule.Filled(CellState.Limited);
        T.SetRange(schedule, DayOfWeek.Tuesday, 9, 10, CellState.Free);
        Func<DayOfWeek, int> budgets = day => day == DayOfWeek.Tuesday ? 0 : 60;

        var allowedAgain = LimitForecast.NextAllowedTime(schedule, T.At(15, 0), 0, budgets);

        Assert.Equal(T.At(9, 0).AddDays(1), allowedAgain); // Tuesday 9 AM, the Free hour
    }

    [Fact]
    public void Mid_cell_the_answer_is_the_next_cell_boundary()
    {
        var schedule = Limited(DayOfWeek.Monday, 18, 23);

        Assert.Equal(T.At(18, 0), LimitForecast.NextAllowedTime(schedule, T.At(17, 47), 3600, Budget(60)));
    }

    [Fact]
    public void A_group_that_can_never_run_has_no_allowed_time()
    {
        Assert.Null(LimitForecast.NextAllowedTime(WeeklySchedule.Filled(CellState.Blocked), T.At(12, 0), 0, Budget(60)));
        Assert.Null(LimitForecast.NextAllowedTime(WeeklySchedule.Filled(CellState.Limited), T.At(12, 0), 0, Budget(0)));
    }
}
