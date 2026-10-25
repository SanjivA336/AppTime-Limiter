using HardCap.Core.Models;

namespace HardCap.Core.Enforcement;

public enum LimitReason
{
    /// <summary>The schedule reaches a Blocked cell.</summary>
    Schedule,

    /// <summary>The daily budget runs out during a Limited cell.</summary>
    Budget,
}

/// <summary>When a group will be cut off, and why.</summary>
public sealed record LimitEnd(TimeSpan In, LimitReason Reason);

/// <summary>
/// Answers "if this app keeps running, when does it get closed?". The warnings use it, so the
/// "5 minutes left" message and the real cutoff always agree.
/// </summary>
public static class LimitForecast
{
    /// <summary>
    /// Walks forward through the schedule one 30-minute cell at a time, like this:
    ///   Blocked cell   -> the app gets closed here, so stop and report it.
    ///   Limited cell   -> the budget is spent while we cross it. If it runs out part-way, report that moment.
    ///   Free cell      -> costs nothing, keep going.
    /// When the budget day changes (at midnight, or at the reset time the user picked) the budget refills, to whatever
    /// the new budget day's amount is. If nothing ends within <paramref name="lookAhead"/>, returns null.
    /// </summary>
    /// <param name="budgetSecondsUsedToday">How much of the current budget day's budget is already gone.</param>
    /// <param name="budgetMinutesFor">The budget for a given day of the week (it can differ from day to day).</param>
    /// <param name="resetMinutes">When budgets refill, in minutes after midnight (0 = midnight).</param>
    public static LimitEnd? Next(
        WeeklySchedule schedule,
        DateTime now,
        double budgetSecondsUsedToday,
        Func<DayOfWeek, int> budgetMinutesFor,
        TimeSpan lookAhead,
        int resetMinutes = 0)
    {
        double BudgetSecondsOn(DateTime moment) => budgetMinutesFor(BudgetDay.DayOfWeekOf(moment, resetMinutes)) * 60.0;

        var budgetLeftSeconds = BudgetSecondsOn(now) - budgetSecondsUsedToday;

        var position = now;           // Where we are in the walk.
        double secondsWalked = 0;     // How far we've walked from "now".

        while (secondsWalked < lookAhead.TotalSeconds)
        {
            var nextCellStart = WeeklySchedule.StartOfNextCell(position);
            var secondsInThisStretch = (nextCellStart - position).TotalSeconds;

            switch (schedule.GetState(position))
            {
                case CellState.Blocked:
                    return new LimitEnd(TimeSpan.FromSeconds(secondsWalked), LimitReason.Schedule);

                case CellState.Limited:
                    if (budgetLeftSeconds <= 0)
                    {
                        return new LimitEnd(TimeSpan.FromSeconds(secondsWalked), LimitReason.Budget);
                    }

                    if (budgetLeftSeconds < secondsInThisStretch)
                    {
                        // The budget runs out part-way through this cell.
                        var secondsUntilEmpty = secondsWalked + budgetLeftSeconds;
                        var withinLookAhead = secondsUntilEmpty <= lookAhead.TotalSeconds;
                        return withinLookAhead
                            ? new LimitEnd(TimeSpan.FromSeconds(secondsUntilEmpty), LimitReason.Budget)
                            : null;
                    }

                    budgetLeftSeconds -= secondsInThisStretch;
                    break;
            }

            secondsWalked += secondsInThisStretch;
            position = nextCellStart;

            if (BudgetDay.IsResetMoment(position, resetMinutes))
            {
                budgetLeftSeconds = BudgetSecondsOn(position); // A fresh budget day, with that day's own budget.
            }
        }

        return null;
    }

    /// <summary>
    /// The earliest moment at or after <paramref name="now"/> when the group's apps are allowed to run:
    ///   a Free cell is always fine, and
    ///   a Limited cell is fine only if there is budget left. What is left applies until the budget day ends (midnight,
    ///   or the reset time the user picked); after that each new budget day starts with that day's full budget (which is
    ///   no use at all if that day's budget is 0).
    /// Returns "now" if they are allowed already, or null if that never happens within the next week.
    /// This is what "Blocked until ..." on a card is based on, including when the budget is used up.
    /// </summary>
    /// <param name="budgetSecondsLeftToday">How much of the current budget day's budget is left.</param>
    /// <param name="resetMinutes">When budgets refill, in minutes after midnight (0 = midnight).</param>
    public static DateTime? NextAllowedTime(
        WeeklySchedule schedule,
        DateTime now,
        double budgetSecondsLeftToday,
        Func<DayOfWeek, int> budgetMinutesFor,
        int resetMinutes = 0)
    {
        var budgetLeftSeconds = budgetSecondsLeftToday;
        var position = now;
        var cellsToCheck = 7 * WeeklySchedule.SlotsPerDay + 1; // a whole week, plus the cell we started in

        for (var i = 0; i < cellsToCheck; i++)
        {
            var allowed = schedule.GetState(position) switch
            {
                CellState.Free => true,
                CellState.Limited => budgetLeftSeconds > 0,
                _ => false,
            };

            if (allowed)
            {
                return position;
            }

            position = WeeklySchedule.StartOfNextCell(position);

            if (BudgetDay.IsResetMoment(position, resetMinutes))
            {
                budgetLeftSeconds = budgetMinutesFor(BudgetDay.DayOfWeekOf(position, resetMinutes)) * 60.0;
            }
        }

        return null;
    }

    /// <summary>The same, for when every day has the same budget.</summary>
    public static LimitEnd? Next(
        WeeklySchedule schedule,
        DateTime now,
        double budgetSecondsUsedToday,
        int dailyBudgetMinutes,
        TimeSpan lookAhead) =>
        Next(schedule, now, budgetSecondsUsedToday, _ => dailyBudgetMinutes, lookAhead);
}
