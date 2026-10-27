using Curfew.Core.Models;

namespace Curfew.Tests;

public class ScheduleGridLayoutTests
{
    // A grid that is wide enough for exactly 100px per day column.
    private const double Width = ScheduleGridLayout.TimeColumnWidth + 7 * 100;

    [Fact]
    public void Columns_run_Monday_to_Sunday()
    {
        Assert.Equal(DayOfWeek.Monday, ScheduleGridLayout.ColumnDays[0]);
        Assert.Equal(DayOfWeek.Sunday, ScheduleGridLayout.ColumnDays[6]);
        Assert.Equal(7, ScheduleGridLayout.ColumnDays.Distinct().Count());
    }

    [Theory]
    [InlineData(0, false, 0)]                                                // inside the time strip
    [InlineData(ScheduleGridLayout.TimeColumnWidth - 0.1, false, 0)]         // just left of Monday
    [InlineData(ScheduleGridLayout.TimeColumnWidth, true, 0)]                // left edge of Monday
    [InlineData(ScheduleGridLayout.TimeColumnWidth + 99.9, true, 0)]         // right edge of Monday
    [InlineData(ScheduleGridLayout.TimeColumnWidth + 100, true, 1)]          // Tuesday
    [InlineData(ScheduleGridLayout.TimeColumnWidth + 650, true, 6)]          // Sunday
    [InlineData(ScheduleGridLayout.TimeColumnWidth + 700, false, 7)]         // past the right edge
    public void TryGetColumn_maps_x_to_a_day(double x, bool expectedHit, int expectedColumn)
    {
        Assert.Equal(expectedHit, ScheduleGridLayout.TryGetColumn(x, Width, out var column));
        if (expectedHit)
        {
            Assert.Equal(expectedColumn, column);
        }
    }

    [Theory]
    [InlineData(0, false, 0)]                                                // day-name strip
    [InlineData(ScheduleGridLayout.DayHeaderHeight - 0.1, false, 0)]         // just above 12:00 AM
    [InlineData(ScheduleGridLayout.DayHeaderHeight, true, 0)]                // top edge of 12:00 AM
    [InlineData(ScheduleGridLayout.DayHeaderHeight + 13.9, true, 0)]         // bottom of the 12:00 AM cell
    [InlineData(ScheduleGridLayout.DayHeaderHeight + 14, true, 1)]           // 12:30 AM
    [InlineData(ScheduleGridLayout.DayHeaderHeight + 14 * 47 + 5, true, 47)] // 11:30 PM
    [InlineData(ScheduleGridLayout.DayHeaderHeight + 14 * 48, false, 48)]    // below the grid
    public void TryGetSlot_maps_y_to_a_half_hour_cell(double y, bool expectedHit, int expectedSlot)
    {
        Assert.Equal(expectedHit, ScheduleGridLayout.TryGetSlot(y, out var slot));
        if (expectedHit)
        {
            Assert.Equal(expectedSlot, slot);
        }
    }

    [Fact]
    public void Day_name_strip_is_detected()
    {
        Assert.True(ScheduleGridLayout.IsInDayHeader(5));
        Assert.False(ScheduleGridLayout.IsInDayHeader(ScheduleGridLayout.DayHeaderHeight));
        Assert.False(ScheduleGridLayout.IsInDayHeader(-1));
    }

    [Fact]
    public void SlotTop_and_TryGetSlot_agree()
    {
        for (var slot = 0; slot < WeeklySchedule.SlotsPerDay; slot++)
        {
            Assert.True(ScheduleGridLayout.TryGetSlot(ScheduleGridLayout.SlotTop(slot) + 1, out var found));
            Assert.Equal(slot, found);
        }
    }

    [Fact]
    public void Painting_the_cell_a_click_lands_on_changes_the_expected_time()
    {
        // Click in the Friday column (5th), 21 cells down = 10:30 AM.
        var schedule = new WeeklySchedule();
        ScheduleGridLayout.TryGetColumn(ScheduleGridLayout.TimeColumnWidth + 4 * 100 + 30, Width, out var column);
        ScheduleGridLayout.TryGetSlot(ScheduleGridLayout.SlotTop(21) + 3, out var slot);

        schedule.SetState(ScheduleGridLayout.ColumnDays[column], slot, CellState.Blocked);

        Assert.Equal(CellState.Blocked, schedule.GetState(new DateTime(2026, 10, 30, 10, 30, 0))); // a Friday
    }

    [Fact]
    public void Total_height_fits_the_header_and_48_cells()
    {
        Assert.Equal(ScheduleGridLayout.DayHeaderHeight + 48 * ScheduleGridLayout.SlotHeight, ScheduleGridLayout.TotalHeight);
    }

    [Theory]
    [InlineData(0, "12:00–12:30 AM")]
    [InlineData(1, "12:30–1:00 AM")]
    [InlineData(19, "9:30–10:00 AM")]
    [InlineData(23, "11:30 AM–12:00 PM")]  // crosses noon
    [InlineData(24, "12:00–12:30 PM")]
    [InlineData(43, "9:30–10:00 PM")]
    [InlineData(47, "11:30 PM–12:00 AM")]  // crosses midnight
    public void SlotRangeLabel_shows_the_time_span_a_cell_covers(int slot, string expected)
    {
        Assert.Equal(expected, ScheduleGridLayout.SlotRangeLabel(slot));
    }

    [Fact]
    public void SlotRangeLabel_is_distinct_for_every_cell()
    {
        var labels = Enumerable.Range(0, WeeklySchedule.SlotsPerDay).Select(ScheduleGridLayout.SlotRangeLabel);

        Assert.Equal(WeeklySchedule.SlotsPerDay, labels.Distinct().Count());
    }

    [Theory]
    [InlineData(0, "12 AM")]
    [InlineData(1, "1 AM")]
    [InlineData(11, "11 AM")]
    [InlineData(12, "12 PM")]
    [InlineData(13, "1 PM")]
    [InlineData(23, "11 PM")]
    public void HourLabel_spells_out_AM_and_PM(int hour, string expected)
    {
        Assert.Equal(expected, ScheduleGridLayout.HourLabel(hour));
    }
}
