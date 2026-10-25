namespace AppTimeLimiter.Core.Models;

/// <summary>A named, colored set of processes that share one weekly schedule, one set of daily budgets, and one set of warnings.</summary>
public sealed class AppGroup
{
    public const int MaxWarnings = 3;
    public const string DefaultColor = "#4F8CFF";

    /// <summary>The longest lock that can be set: five minutes.</summary>
    public const int MaxLockSeconds = 300;

    public const int DefaultDailyBudgetMinutes = 120;
    public const int MaxDailyBudgetMinutes = 24 * 60;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "New group";

    /// <summary>Hex color like "#RRGGBB".</summary>
    public string Color { get; set; } = DefaultColor;

    /// <summary>Process names without ".exe" (e.g. "Warframe.x64"). A process belongs to at most one group.</summary>
    public List<string> Processes { get; set; } = new();

    public WeeklySchedule Schedule { get; set; } = new();

    /// <summary>Minutes before a limit ends at which to warn. At most <see cref="MaxWarnings"/>.</summary>
    public List<int> WarningMinutes { get; set; } = new() { 5 };

    /// <summary>
    /// How long, in seconds, you must wait before you can see or change this group. Opening the group shows only
    /// a countdown for this long. 0 means no lock. The longest is <see cref="MaxLockSeconds"/>.
    /// </summary>
    public int LockSeconds { get; set; }

    /// <summary>
    /// How many minutes of Limited time this group allows on each day, indexed by <see cref="DayOfWeek"/> (0 = Sunday).
    /// Each group has its own, and only its own Limited time is counted against it. Read it with
    /// <see cref="BudgetMinutesFor"/> and change it with <see cref="SetBudgetMinutes"/>.
    /// </summary>
    public int[] BudgetMinutesByDay { get; set; } = NewBudgets(DefaultDailyBudgetMinutes);

    /// <summary>This group's budget for the given day of the week, in minutes.</summary>
    public int BudgetMinutesFor(DayOfWeek day)
    {
        var budgets = BudgetMinutesByDay;
        return budgets is not null && (int)day < budgets.Length ? budgets[(int)day] : DefaultDailyBudgetMinutes;
    }

    public void SetBudgetMinutes(DayOfWeek day, int minutes)
    {
        if (BudgetMinutesByDay is null || BudgetMinutesByDay.Length != 7)
        {
            BudgetMinutesByDay = NewBudgets(DefaultDailyBudgetMinutes);
        }

        BudgetMinutesByDay[(int)day] = Math.Clamp(minutes, 0, MaxDailyBudgetMinutes);
    }

    /// <summary>Seven days, all the same length.</summary>
    public static int[] NewBudgets(int minutes) => Enumerable.Repeat(minutes, 7).ToArray();

    /// <summary>A full, independent copy (the lists and the schedule are copied too), with the same id.</summary>
    public AppGroup Clone() => new()
    {
        Id = Id,
        Name = Name,
        Color = Color,
        Processes = new List<string>(Processes),
        Schedule = Schedule.Clone(),
        WarningMinutes = new List<int>(WarningMinutes),
        LockSeconds = LockSeconds,
        BudgetMinutesByDay = (int[])(BudgetMinutesByDay ?? NewBudgets(DefaultDailyBudgetMinutes)).Clone(),
    };

    /// <summary>Overwrites everything except the id with a copy of <paramref name="other"/>'s settings.</summary>
    public void CopyFrom(AppGroup other)
    {
        Name = other.Name;
        Color = other.Color;
        Processes = new List<string>(other.Processes);
        Schedule = other.Schedule.Clone();
        WarningMinutes = new List<int>(other.WarningMinutes);
        LockSeconds = other.LockSeconds;
        BudgetMinutesByDay = (int[])(other.BudgetMinutesByDay ?? NewBudgets(DefaultDailyBudgetMinutes)).Clone();
    }

    /// <summary>True if both groups would behave identically: same name, color, apps, warnings, lock, budgets and schedule.</summary>
    public bool IsSameAs(AppGroup other) =>
        Id == other.Id
        && Name == other.Name
        && Color == other.Color
        && LockSeconds == other.LockSeconds
        && Processes.SequenceEqual(other.Processes)
        && WarningMinutes.SequenceEqual(other.WarningMinutes)
        && BudgetMinutesByDay.SequenceEqual(other.BudgetMinutesByDay)
        && Schedule.Days.SequenceEqual(other.Schedule.Days);
}
