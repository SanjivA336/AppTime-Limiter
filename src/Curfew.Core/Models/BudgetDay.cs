namespace Curfew.Core.Models;

/// <summary>
/// Which "budget day" a moment belongs to. Budgets refill once a day at the <b>reset time</b> the user picks (midnight by
/// default). With a 4:00 AM reset, 1:00 AM on Saturday still belongs to Friday's budget day, so a late Friday-night
/// session is charged to Friday's budget, and Friday's budget amount applies to it.
///
/// The reset time is stored as minutes after midnight, in steps of 30 so it always falls on an edge of the schedule's
/// 30-minute cells.
/// </summary>
public static class BudgetDay
{
    /// <summary>The reset time moves in steps of this many minutes, to match the schedule grid's cells.</summary>
    public const int StepMinutes = 30;

    /// <summary>The latest reset time: 11:30 PM.</summary>
    public const int MaxResetMinutes = 24 * 60 - StepMinutes;

    /// <summary>Rounds to the nearest step and keeps the value inside one day (0 to 11:30 PM).</summary>
    public static int Tidy(int resetMinutes)
    {
        var rounded = (int)(Math.Round(resetMinutes / (double)StepMinutes, MidpointRounding.AwayFromZero) * StepMinutes);
        return Math.Clamp(rounded, 0, MaxResetMinutes);
    }

    /// <summary>The calendar date a budget day is named after (the date it started on).</summary>
    public static DateOnly Of(DateTime moment, int resetMinutes) =>
        DateOnly.FromDateTime(moment.AddMinutes(-resetMinutes));

    /// <summary>The moment a budget day begins: midnight of its date plus the reset time.</summary>
    public static DateTime StartOf(DateOnly day, int resetMinutes) =>
        day.ToDateTime(TimeOnly.MinValue).AddMinutes(resetMinutes);

    /// <summary>Which weekday's budget applies at <paramref name="moment"/>.</summary>
    public static DayOfWeek DayOfWeekOf(DateTime moment, int resetMinutes) => Of(moment, resetMinutes).DayOfWeek;

    /// <summary>True when <paramref name="moment"/> is exactly the instant a new budget day begins.</summary>
    public static bool IsResetMoment(DateTime moment, int resetMinutes) =>
        moment.TimeOfDay == TimeSpan.FromMinutes(resetMinutes);

    /// <summary>The next time budgets refill, strictly after <paramref name="now"/>.</summary>
    public static DateTime NextReset(DateTime now, int resetMinutes)
    {
        var todaysReset = now.Date.AddMinutes(resetMinutes);
        return todaysReset > now ? todaysReset : todaysReset.AddDays(1);
    }

    /// <summary>"12:00 AM", "4:30 AM": the reset time as a clock reading.</summary>
    public static string Describe(int resetMinutes) => TimeText.Clock(DateTime.Today.AddMinutes(resetMinutes));
}
