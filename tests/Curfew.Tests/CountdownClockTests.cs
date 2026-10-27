using Curfew.Core.Models;

namespace Curfew.Tests;

public class CountdownClockTests
{
    [Fact]
    public void A_new_countdown_shows_its_full_length()
    {
        var clock = new CountdownClock(75);

        Assert.Equal("1:15", clock.Text);
        Assert.Equal(1, clock.Progress);
        Assert.False(clock.IsFinished);
    }

    [Fact]
    public void Time_counts_down_while_the_window_is_active()
    {
        var clock = new CountdownClock(60);

        clock.Advance(15, windowIsActive: true);

        Assert.Equal(45, clock.RemainingSeconds);
        Assert.Equal("0:45", clock.Text);
        Assert.Equal(0.75, clock.Progress, precision: 6);
    }

    [Fact]
    public void Time_spent_in_another_window_does_not_count()
    {
        var clock = new CountdownClock(60);

        clock.Advance(10, windowIsActive: true);
        clock.Advance(500, windowIsActive: false); // tabbed away for a long while
        clock.Advance(5, windowIsActive: true);

        Assert.Equal(45, clock.RemainingSeconds);
        Assert.False(clock.IsFinished);
    }

    [Fact]
    public void The_countdown_finishes_after_exactly_its_length_of_active_time()
    {
        var clock = new CountdownClock(30);

        clock.Advance(29.9, windowIsActive: true);
        Assert.False(clock.IsFinished);
        Assert.Equal("0:01", clock.Text); // rounded up, so it never reads 0:00 early

        clock.Advance(0.2, windowIsActive: true);
        Assert.True(clock.IsFinished);
        Assert.Equal("0:00", clock.Text);
        Assert.Equal(0, clock.Progress);
    }

    [Fact]
    public void Negative_or_zero_time_changes_nothing()
    {
        var clock = new CountdownClock(30);

        clock.Advance(-5, windowIsActive: true);
        clock.Advance(0, windowIsActive: true);

        Assert.Equal(30, clock.RemainingSeconds);
    }

    [Fact]
    public void A_zero_length_countdown_is_finished_straight_away()
    {
        var clock = new CountdownClock(0);

        Assert.True(clock.IsFinished);
        Assert.Equal("0:00", clock.Text);
    }
}
