using Curfew.Core.Models;

namespace Curfew.Tests;

public class WeeklyScheduleTests
{
    [Fact]
    public void New_schedule_is_free_everywhere()
    {
        var schedule = new WeeklySchedule();

        Assert.All(Enum.GetValues<DayOfWeek>(), day =>
        {
            for (var slot = 0; slot < WeeklySchedule.SlotsPerDay; slot++)
            {
                Assert.Equal(CellState.Free, schedule.GetState(day, slot));
            }
        });
    }

    [Fact]
    public void SetState_changes_only_the_chosen_cell()
    {
        var schedule = new WeeklySchedule();

        schedule.SetState(DayOfWeek.Tuesday, 5, CellState.Blocked);
        schedule.SetState(DayOfWeek.Tuesday, 6, CellState.Limited);

        Assert.Equal(CellState.Free, schedule.GetState(DayOfWeek.Tuesday, 4));
        Assert.Equal(CellState.Blocked, schedule.GetState(DayOfWeek.Tuesday, 5));
        Assert.Equal(CellState.Limited, schedule.GetState(DayOfWeek.Tuesday, 6));
        Assert.Equal(CellState.Free, schedule.GetState(DayOfWeek.Wednesday, 5));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 29, 0)]
    [InlineData(0, 30, 1)]
    [InlineData(13, 45, 27)]
    [InlineData(23, 59, 47)]
    public void SlotOf_maps_time_to_half_hour_cell(int hour, int minute, int expected)
    {
        Assert.Equal(expected, WeeklySchedule.SlotOf(T.At(hour, minute)));
    }

    [Fact]
    public void GetState_by_datetime_uses_that_days_row()
    {
        var schedule = new WeeklySchedule();
        schedule.SetState(DayOfWeek.Monday, 44, CellState.Blocked); // 10:00pm Monday

        Assert.Equal(CellState.Blocked, schedule.GetState(T.At(22, 10)));
        Assert.Equal(CellState.Free, schedule.GetState(T.At(22, 10).AddDays(1)));
    }

    [Fact]
    public void Malformed_cells_are_treated_as_blocked()
    {
        var schedule = new WeeklySchedule { Days = new[] { "FLX", "", "F" } };

        Assert.Equal(CellState.Free, schedule.GetState(DayOfWeek.Sunday, 0));
        Assert.Equal(CellState.Limited, schedule.GetState(DayOfWeek.Sunday, 1));
        Assert.Equal(CellState.Blocked, schedule.GetState(DayOfWeek.Sunday, 2)); // unknown letter
        Assert.Equal(CellState.Blocked, schedule.GetState(DayOfWeek.Sunday, 10)); // row too short
        Assert.Equal(CellState.Blocked, schedule.GetState(DayOfWeek.Thursday, 0)); // row missing
        Assert.Equal(CellState.Blocked, schedule.GetState(DayOfWeek.Sunday, 99)); // slot out of range
    }

    [Fact]
    public void Sanitize_repairs_shape_and_keeps_valid_cells()
    {
        var schedule = new WeeklySchedule { Days = new[] { "FLB" } };

        schedule.Sanitize();

        Assert.Equal(7, schedule.Days.Length);
        Assert.All(schedule.Days, row => Assert.Equal(WeeklySchedule.SlotsPerDay, row.Length));
        Assert.Equal(CellState.Limited, schedule.GetState(DayOfWeek.Sunday, 1));
        Assert.Equal(CellState.Blocked, schedule.GetState(DayOfWeek.Sunday, 3));
    }

    [Fact]
    public void Lowercase_letters_are_accepted()
    {
        var schedule = new WeeklySchedule { Days = Enumerable.Repeat("lb" + new string('f', 46), 7).ToArray() };

        Assert.Equal(CellState.Limited, schedule.GetState(DayOfWeek.Monday, 0));
        Assert.Equal(CellState.Blocked, schedule.GetState(DayOfWeek.Monday, 1));
        Assert.Equal(CellState.Free, schedule.GetState(DayOfWeek.Monday, 2));
    }

    [Fact]
    public void CopyDay_overwrites_targets_and_leaves_others_alone()
    {
        var schedule = new WeeklySchedule();
        T.SetRange(schedule, DayOfWeek.Monday, 20, 24, CellState.Blocked);

        schedule.CopyDay(DayOfWeek.Monday, new[] { DayOfWeek.Tuesday, DayOfWeek.Friday });

        Assert.Equal(schedule.Days[(int)DayOfWeek.Monday], schedule.Days[(int)DayOfWeek.Tuesday]);
        Assert.Equal(schedule.Days[(int)DayOfWeek.Monday], schedule.Days[(int)DayOfWeek.Friday]);
        Assert.Equal(CellState.Free, schedule.GetState(DayOfWeek.Wednesday, 45));
    }

    [Fact]
    public void Clone_is_independent_of_the_original()
    {
        var original = new WeeklySchedule();
        var copy = original.Clone();

        copy.SetState(DayOfWeek.Monday, 0, CellState.Blocked);

        Assert.Equal(CellState.Free, original.GetState(DayOfWeek.Monday, 0));
    }

    [Fact]
    public void SetState_rejects_out_of_range_slot()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WeeklySchedule().SetState(DayOfWeek.Monday, 48, CellState.Free));
    }

    [Theory]
    [InlineData(0, "12:00 AM")]
    [InlineData(1, "12:30 AM")]
    [InlineData(27, "1:30 PM")]
    public void SlotLabel_formats_the_start_time(int slot, string expected)
    {
        Assert.Equal(expected, WeeklySchedule.SlotLabel(slot).ToUpperInvariant());
    }
}
