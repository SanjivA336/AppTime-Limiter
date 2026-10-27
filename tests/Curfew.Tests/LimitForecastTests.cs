using Curfew.Core.Enforcement;
using Curfew.Core.Models;

namespace Curfew.Tests;

public class LimitForecastTests
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    [Fact]
    public void Returns_null_when_nothing_ends_within_the_horizon()
    {
        var schedule = WeeklySchedule.Filled(CellState.Free);

        Assert.Null(LimitForecast.Next(schedule, T.At(12), 0, 60, Hour));
    }

    [Fact]
    public void Finds_the_next_blocked_cell()
    {
        var schedule = WeeklySchedule.Filled(CellState.Free);
        T.SetRange(schedule, DayOfWeek.Monday, 22, 24, CellState.Blocked);

        var end = LimitForecast.Next(schedule, T.At(21, 50), 0, 60, Hour);

        Assert.NotNull(end);
        Assert.Equal(LimitReason.Schedule, end.Reason);
        Assert.Equal(TimeSpan.FromMinutes(10), end.In);
    }

    [Fact]
    public void Already_blocked_ends_immediately()
    {
        var schedule = WeeklySchedule.Filled(CellState.Blocked);

        var end = LimitForecast.Next(schedule, T.At(12, 7), 0, 60, Hour);

        Assert.Equal(TimeSpan.Zero, end!.In);
        Assert.Equal(LimitReason.Schedule, end.Reason);
    }

    [Fact]
    public void Budget_running_out_in_a_limited_cell_ends_the_session()
    {
        var schedule = WeeklySchedule.Filled(CellState.Limited);

        // 60-minute budget, 57 minutes already used.
        var end = LimitForecast.Next(schedule, T.At(12, 10), 57 * 60, 60, Hour);

        Assert.Equal(LimitReason.Budget, end!.Reason);
        Assert.Equal(TimeSpan.FromMinutes(3), end.In);
    }

    [Fact]
    public void Empty_budget_in_a_limited_cell_ends_now()
    {
        var schedule = WeeklySchedule.Filled(CellState.Limited);

        var end = LimitForecast.Next(schedule, T.At(12), 3600, 60, Hour);

        Assert.Equal(TimeSpan.Zero, end!.In);
        Assert.Equal(LimitReason.Budget, end.Reason);
    }

    [Fact]
    public void Free_time_does_not_spend_the_budget()
    {
        // 12:00-12:30 Free, then Limited. Only 5 minutes of budget left.
        var schedule = WeeklySchedule.Filled(CellState.Limited);
        schedule.SetState(DayOfWeek.Monday, 24, CellState.Free);

        var end = LimitForecast.Next(schedule, T.At(12), 55 * 60, 60, Hour);

        Assert.Equal(LimitReason.Budget, end!.Reason);
        Assert.Equal(TimeSpan.FromMinutes(35), end.In); // 30 free minutes + 5 budget minutes
    }

    [Fact]
    public void Budget_ending_beyond_the_horizon_is_not_reported()
    {
        var schedule = WeeklySchedule.Filled(CellState.Limited);

        Assert.Null(LimitForecast.Next(schedule, T.At(12), 0, 120, Hour));
    }

    [Fact]
    public void Budget_refills_at_midnight()
    {
        var schedule = WeeklySchedule.Filled(CellState.Limited);

        // 11:59pm with 2 minutes of the 10-minute budget left: 1 minute to midnight, then a fresh 10.
        var end = LimitForecast.Next(schedule, T.At(23, 59), 8 * 60, 10, TimeSpan.FromHours(2));

        Assert.Equal(LimitReason.Budget, end!.Reason);
        Assert.Equal(TimeSpan.FromMinutes(11), end.In);
    }

    [Fact]
    public void A_blocked_cell_after_midnight_ends_the_session_at_midnight()
    {
        var schedule = WeeklySchedule.Filled(CellState.Free);
        T.SetRange(schedule, DayOfWeek.Tuesday, 0, 6, CellState.Blocked);

        var end = LimitForecast.Next(schedule, T.At(23, 40), 0, 60, Hour);

        Assert.Equal(LimitReason.Schedule, end!.Reason);
        Assert.Equal(TimeSpan.FromMinutes(20), end.In);
    }

    [Fact]
    public void Partial_cells_are_measured_to_the_second()
    {
        var schedule = WeeklySchedule.Filled(CellState.Free);
        T.SetRange(schedule, DayOfWeek.Monday, 13, 14, CellState.Blocked);

        var end = LimitForecast.Next(schedule, T.At(12, 52, 30), 0, 60, Hour);

        Assert.Equal(TimeSpan.FromSeconds(7 * 60 + 30), end!.In);
    }
}
