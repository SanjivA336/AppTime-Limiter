using HardCap.Core.Models;

namespace HardCap.Tests;

public class TimeTextTests
{
    [Theory]
    [InlineData(0, "0m")]
    [InlineData(-30, "0m")]
    [InlineData(30, "<1m")]
    [InlineData(59, "<1m")]
    [InlineData(60, "1m")]
    [InlineData(45 * 60, "45m")]
    [InlineData(60 * 60, "1h")]
    [InlineData(72 * 60, "1h 12m")]
    [InlineData(119 * 60 + 59, "1h 59m")]   // seconds are dropped, not rounded up
    [InlineData(120 * 60, "2h")]
    [InlineData(24 * 3600, "24h")]
    public void Compact_shows_hours_and_minutes(double seconds, string expected)
    {
        Assert.Equal(expected, TimeText.Compact(TimeSpan.FromSeconds(seconds)));
    }
}

public class WeeklyScheduleLimitedTests
{
    [Fact]
    public void A_schedule_with_no_limited_cells_does_not_use_the_budget()
    {
        var schedule = WeeklySchedule.Filled(CellState.Free);
        schedule.SetState(DayOfWeek.Monday, 10, CellState.Blocked);

        Assert.False(schedule.HasLimitedCells());
    }

    [Fact]
    public void A_single_limited_cell_anywhere_is_enough()
    {
        var schedule = WeeklySchedule.Filled(CellState.Free);
        schedule.SetState(DayOfWeek.Sunday, 47, CellState.Limited); // the very last cell of the week

        Assert.True(schedule.HasLimitedCells());
    }

    [Fact]
    public void Painting_the_cell_back_removes_it()
    {
        var schedule = WeeklySchedule.Filled(CellState.Blocked);
        schedule.SetState(DayOfWeek.Wednesday, 20, CellState.Limited);
        schedule.SetState(DayOfWeek.Wednesday, 20, CellState.Free);

        Assert.False(schedule.HasLimitedCells());
    }
}
