using HardCap.Core.Models;

namespace HardCap.App.ViewModels;

/// <summary>What a pause choice does when it is picked.</summary>
public enum PauseKind
{
    NotPaused,
    UntilResumed,

    /// <summary>The pause the group already had when the window opened (it ends at a particular time).</summary>
    KeepCurrent,

    ForDuration,
    UntilDateTime,
}

/// <summary>One entry in the pause box, such as "For 1 hour".</summary>
public sealed record PauseOption(string Label, PauseKind Kind, TimeSpan? Duration = null)
{
    public override string ToString() => Label;
}

/// <summary>One day in the "pause until" day box.</summary>
public sealed record DayChoice(DateTime Date, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A time of day on the half hour, such as "4:30 AM", for the time boxes in the pause and the settings.</summary>
public sealed record TimeChoice(int Minutes, string Label)
{
    /// <summary>All 48 half-hours, from 12:00 AM to 11:30 PM.</summary>
    public static readonly IReadOnlyList<TimeChoice> All = Enumerable.Range(0, 48)
        .Select(slot => new TimeChoice(slot * BudgetDay.StepMinutes, BudgetDay.Describe(slot * BudgetDay.StepMinutes)))
        .ToList();

    /// <summary>The choice closest to a time given in minutes after midnight (rounded to the half hour).</summary>
    public static TimeChoice Nearest(int minutesAfterMidnight) => All[Math.Clamp((int)Math.Round(minutesAfterMidnight / (double)BudgetDay.StepMinutes), 0, 47)];

    /// <summary>The choice for exactly this time, or the nearest one if it is not on the half hour.</summary>
    public static TimeChoice For(int minutesAfterMidnight) => Nearest(minutesAfterMidnight);

    public override string ToString() => Label;
}
