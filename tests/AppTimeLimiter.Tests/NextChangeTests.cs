using AppTimeLimiter.Core.Models;

namespace AppTimeLimiter.Tests;

public class NextChangeTests
{
    // T.Monday is 2026-10-26, a Monday.

    private static WeeklySchedule FreeWithLimited(DayOfWeek day, int fromHour, int toHour)
    {
        var schedule = WeeklySchedule.Filled(CellState.Free);
        T.SetRange(schedule, day, fromHour, toHour, CellState.Limited);
        return schedule;
    }

    [Fact]
    public void A_free_group_reports_when_its_limited_time_starts_today()
    {
        var schedule = FreeWithLimited(DayOfWeek.Monday, 20, 22);

        var change = schedule.NextChange(T.At(12, 10));

        Assert.Equal(T.At(20, 0), change!.At);
        Assert.Equal(CellState.Limited, change.To);
    }

    [Fact]
    public void A_limited_group_reports_when_it_goes_back_to_free()
    {
        var schedule = FreeWithLimited(DayOfWeek.Monday, 20, 22);

        var change = schedule.NextChange(T.At(20, 10));

        Assert.Equal(T.At(22, 0), change!.At);
        Assert.Equal(CellState.Free, change.To);
    }

    [Fact]
    public void The_change_lands_on_the_next_cell_boundary()
    {
        var schedule = FreeWithLimited(DayOfWeek.Monday, 20, 22);

        Assert.Equal(T.At(20, 0), schedule.NextChange(T.At(19, 59, 59))!.At);
        Assert.Equal(T.At(20, 0), schedule.NextChange(T.At(19, 30))!.At);
    }

    [Fact]
    public void A_change_tomorrow_is_found()
    {
        var schedule = FreeWithLimited(DayOfWeek.Tuesday, 20, 22);

        var change = schedule.NextChange(T.At(12, 0));

        Assert.Equal(T.At(20, 0).AddDays(1), change!.At);
    }

    [Fact]
    public void The_search_runs_across_midnight()
    {
        var schedule = FreeWithLimited(DayOfWeek.Tuesday, 0, 1); // 12 AM to 1 AM Tuesday

        var change = schedule.NextChange(T.At(23, 40));

        Assert.Equal(T.At(24, 0), change!.At); // midnight, as Tuesday begins
    }

    [Fact]
    public void A_change_exactly_a_week_away_is_found()
    {
        var schedule = FreeWithLimited(DayOfWeek.Monday, 8, 9); // only Monday 8 AM to 9 AM

        var change = schedule.NextChange(T.At(12, 0)); // Monday noon: the next one is next Monday

        Assert.Equal(T.At(8, 0).AddDays(7), change!.At);
    }

    [Fact]
    public void A_free_to_blocked_change_reports_blocked()
    {
        var schedule = WeeklySchedule.Filled(CellState.Free);
        T.SetRange(schedule, DayOfWeek.Monday, 22, 24, CellState.Blocked);

        var change = schedule.NextChange(T.At(9, 0));

        Assert.Equal(T.At(22, 0), change!.At);
        Assert.Equal(CellState.Blocked, change.To);
    }

    [Theory]
    [InlineData(CellState.Free)]
    [InlineData(CellState.Limited)]
    [InlineData(CellState.Blocked)]
    public void A_schedule_that_never_changes_has_no_next_change(CellState everywhere)
    {
        Assert.Null(WeeklySchedule.Filled(everywhere).NextChange(T.At(12, 0)));
    }

    [Theory]
    [InlineData(9, 47, 10, 0)]
    [InlineData(10, 0, 10, 30)]
    [InlineData(10, 29, 10, 30)]
    [InlineData(10, 30, 11, 0)]
    public void StartOfNextCell_rounds_up_to_the_next_half_hour(int hour, int minute, int expectedHour, int expectedMinute)
    {
        Assert.Equal(T.At(expectedHour, expectedMinute), WeeklySchedule.StartOfNextCell(T.At(hour, minute)));
    }

    [Fact]
    public void StartOfNextCell_at_the_end_of_the_day_is_midnight()
    {
        Assert.Equal(T.At(24, 0), WeeklySchedule.StartOfNextCell(T.At(23, 45)));
    }
}

public class WhenTextTests
{
    [Theory]
    [InlineData(0, 0, "12:00 AM")]
    [InlineData(0, 30, "12:30 AM")]
    [InlineData(9, 5, "9:05 AM")]
    [InlineData(12, 0, "12:00 PM")]
    [InlineData(18, 0, "6:00 PM")]
    [InlineData(23, 30, "11:30 PM")]
    public void Clock_writes_the_time_with_AM_or_PM(int hour, int minute, string expected)
    {
        Assert.Equal(expected, TimeText.Clock(T.At(hour, minute)));
    }

    [Fact]
    public void Later_the_same_day_is_today()
    {
        Assert.Equal("today at 6:00 PM", TimeText.When(T.At(18, 0), T.At(12, 0)));
    }

    [Fact]
    public void The_next_calendar_day_is_tomorrow_even_if_it_is_only_minutes_away()
    {
        Assert.Equal("tomorrow at 12:10 AM", TimeText.When(T.At(24, 10), T.At(23, 50)));
    }

    [Fact]
    public void Two_to_six_days_away_is_named_by_weekday()
    {
        Assert.Equal("Friday at 8:00 PM", TimeText.When(T.At(20, 0).AddDays(4), T.At(12, 0)));
    }

    [Fact]
    public void A_full_week_away_says_next()
    {
        Assert.Equal("next Monday at 8:00 AM", TimeText.When(T.At(8, 0).AddDays(7), T.At(12, 0)));
    }
}
