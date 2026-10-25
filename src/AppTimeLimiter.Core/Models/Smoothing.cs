namespace AppTimeLimiter.Core.Models;

/// <summary>Helpers for making a number glide toward a goal instead of jumping.</summary>
public static class Smoothing
{
    /// <summary>
    /// Moves <paramref name="current"/> toward <paramref name="target"/> to show the passing of <paramref name="seconds"/>.
    ///
    /// Each moment it covers a fixed share of whatever distance is left, so it moves fast when it is far away and
    /// slows down as it gets close, and it can never go past the target. Because the share is worked out from the
    /// elapsed time, the result is the same however often this is called: ten steps of 0.1 seconds land in the same
    /// place as one step of 1 second.
    ///
    /// <paramref name="timeConstantSeconds"/> is how fast it settles: after that many seconds it has covered about
    /// 63% of the distance, after three times that about 95%. A time constant of 0 or less means "arrive instantly".
    /// </summary>
    public static double Approach(double current, double target, double seconds, double timeConstantSeconds)
    {
        if (double.IsNaN(current) || double.IsInfinity(current) || timeConstantSeconds <= 0)
        {
            return target;
        }

        if (seconds <= 0)
        {
            return current;
        }

        // How much of the starting distance is still left: 1 (all of it) down toward 0 as time passes.
        var shareStillToGo = Math.Exp(-seconds / timeConstantSeconds);
        var next = target - (target - current) * shareStillToGo;

        // Rounding can leave the answer a hair past the target. Keep it between where it started and where it's going.
        return Math.Clamp(next, Math.Min(current, target), Math.Max(current, target));
    }
}
