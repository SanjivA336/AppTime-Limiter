using HardCap.Core.Models;

namespace HardCap.Tests;

public class SmoothingTests
{
    private const double Tau = 1.5; // the time constant used in the tests, in seconds

    [Fact]
    public void No_time_passing_means_no_movement()
    {
        Assert.Equal(0.8, Smoothing.Approach(0.8, 0.2, 0, Tau));
        Assert.Equal(0.8, Smoothing.Approach(0.8, 0.2, -1, Tau));
    }

    [Fact]
    public void It_moves_toward_the_target_in_either_direction()
    {
        Assert.InRange(Smoothing.Approach(0.8, 0.2, 0.1, Tau), 0.2, 0.8 - 0.001); // going down
        Assert.InRange(Smoothing.Approach(0.2, 0.8, 0.1, Tau), 0.2 + 0.001, 0.8); // going up
    }

    [Theory]
    [InlineData(0.8, 0.2)]
    [InlineData(0.2, 0.8)]
    [InlineData(1.0, 0.0)]
    public void It_never_goes_past_the_target_however_long_the_step(double start, double target)
    {
        foreach (var seconds in new[] { 0.001, 0.016, 0.1, 1, 10, 1000 })
        {
            var next = Smoothing.Approach(start, target, seconds, Tau);

            Assert.InRange(next, Math.Min(start, target), Math.Max(start, target));
        }
    }

    [Fact]
    public void A_far_away_target_is_approached_faster_than_a_near_one()
    {
        var bigGapStep = 0.8 - Smoothing.Approach(0.8, 0.0, 0.1, Tau);   // gap of 0.8
        var smallGapStep = 0.2 - Smoothing.Approach(0.2, 0.0, 0.1, Tau); // gap of 0.2

        Assert.Equal(4.0, bigGapStep / smallGapStep, precision: 6); // four times the gap, four times the speed
    }

    [Fact]
    public void After_one_time_constant_it_has_covered_about_63_percent_of_the_distance()
    {
        var result = Smoothing.Approach(0, 1, Tau, Tau);

        Assert.Equal(1 - Math.Exp(-1), result, precision: 9);
        Assert.InRange(result, 0.63, 0.64);
    }

    [Fact]
    public void After_a_long_time_it_has_all_but_arrived()
    {
        Assert.InRange(Smoothing.Approach(0, 1, Tau * 10, Tau), 0.9999, 1.0);
    }

    [Fact]
    public void The_result_does_not_depend_on_how_often_it_is_called()
    {
        // One 1-second step...
        var oneStep = Smoothing.Approach(0.9, 0.1, 1.0, Tau);

        // ...ends in the same place as sixty steps of 1/60 second (a typical screen refresh).
        var manySteps = 0.9;
        for (var i = 0; i < 60; i++)
        {
            manySteps = Smoothing.Approach(manySteps, 0.1, 1.0 / 60, Tau);
        }

        Assert.Equal(oneStep, manySteps, precision: 9);
    }

    [Fact]
    public void A_moving_target_is_followed_without_a_jump()
    {
        // The target moves partway through. The value keeps going from where it is, now toward the new target.
        var value = Smoothing.Approach(1.0, 0.5, 0.5, Tau);
        var before = value;
        value = Smoothing.Approach(value, 0.9, 0.0, Tau); // the target changes but no time has passed

        Assert.Equal(before, value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_time_constant_of_zero_or_less_arrives_instantly(double timeConstant)
    {
        Assert.Equal(0.25, Smoothing.Approach(0.9, 0.25, 0.016, timeConstant));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_broken_starting_value_recovers_by_snapping_to_the_target(double broken)
    {
        Assert.Equal(0.5, Smoothing.Approach(broken, 0.5, 0.016, Tau));
    }
}
