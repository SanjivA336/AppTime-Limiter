namespace HardCap.Core.Models;

/// <summary>
/// The length of a group's lock. It is stored as a plain number of seconds, and shown (and edited) as minutes and
/// seconds. These helpers do the arithmetic between the two, including carrying: pushing the seconds up to 60
/// becomes one more minute and 0 seconds, and pushing them below 0 borrows a minute.
/// </summary>
public static class LockTime
{
    /// <summary>The seconds box goes up and down in steps of this many seconds.</summary>
    public const int StepSeconds = 5;

    /// <summary>
    /// Turns what is in the minutes and seconds boxes into a total number of seconds. The seconds don't have to
    /// be between 0 and 59: 60 seconds is a minute, and -5 seconds takes five off the minutes. The result is
    /// rounded to the nearest step and kept between 0 and <see cref="AppGroup.MaxLockSeconds"/>.
    /// </summary>
    public static int FromParts(int minutes, int seconds) => Tidy(minutes * 60 + seconds);

    /// <summary>Rounds to the nearest step and keeps the value between "no lock" and the longest allowed lock.</summary>
    public static int Tidy(int totalSeconds)
    {
        var rounded = (int)(Math.Round(totalSeconds / (double)StepSeconds, MidpointRounding.AwayFromZero) * StepSeconds);
        return Math.Clamp(rounded, 0, AppGroup.MaxLockSeconds);
    }

    /// <summary>Splits a total into whole minutes and the seconds left over (0 to 59).</summary>
    public static (int Minutes, int Seconds) ToParts(int totalSeconds)
    {
        var total = Math.Clamp(totalSeconds, 0, AppGroup.MaxLockSeconds);
        return (total / 60, total % 60);
    }

    /// <summary>"1:05", "0:30", "5:00": what a countdown shows.</summary>
    public static string Format(int totalSeconds)
    {
        var (minutes, seconds) = ToParts(totalSeconds);
        return $"{minutes}:{seconds:00}";
    }

    /// <summary>"No lock", "30 seconds", "1 minute", "1 minute 15 seconds": the length in words.</summary>
    public static string Describe(int totalSeconds) => Describe(totalSeconds, "No lock");

    /// <summary>The length in words, or <paramref name="zeroText"/> (such as "No grace period") when it is 0.</summary>
    public static string Describe(int totalSeconds, string zeroText)
    {
        var (minutes, seconds) = ToParts(totalSeconds);
        if (minutes == 0 && seconds == 0)
        {
            return zeroText;
        }

        var parts = new List<string>();
        if (minutes > 0)
        {
            parts.Add(minutes == 1 ? "1 minute" : $"{minutes} minutes");
        }

        if (seconds > 0)
        {
            parts.Add(seconds == 1 ? "1 second" : $"{seconds} seconds");
        }

        return string.Join(" ", parts);
    }
}
