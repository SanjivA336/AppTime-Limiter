namespace Curfew.Core.Models;

/// <summary>A moment when the schedule switches to a different state.</summary>
public sealed record ScheduleChange(DateTime At, CellState To);

/// <summary>
/// A weekly grid: seven days of 48 half-hour cells (12:00am to 11:30pm), each Free, Limited or Blocked.
/// Stored as one 48-character string per day ('F', 'L', 'B'), indexed by <see cref="DayOfWeek"/>
/// (0 = Sunday), so the settings file stays compact and hand-editable.
/// Anything malformed is treated as Blocked, the safe direction for a limiter.
/// </summary>
public sealed class WeeklySchedule
{
    public const int SlotsPerDay = 48;
    public const int SlotMinutes = 30;

    private const char FreeChar = 'F';
    private const char LimitedChar = 'L';
    private const char BlockedChar = 'B';

    /// <summary>Seven 48-character strings, index = (int)DayOfWeek. Serialized as-is.</summary>
    public string[] Days { get; set; } = NewDays(CellState.Free);

    public static WeeklySchedule Filled(CellState state) => new() { Days = NewDays(state) };

    public CellState GetState(DayOfWeek day, int slot)
    {
        var days = Days;
        if (days is null || (int)day >= days.Length || slot < 0 || slot >= SlotsPerDay)
        {
            return CellState.Blocked;
        }

        var row = days[(int)day];
        if (row is null || slot >= row.Length)
        {
            return CellState.Blocked;
        }

        return FromChar(row[slot]);
    }

    public CellState GetState(DateTime time) => GetState(time.DayOfWeek, SlotOf(time));

    /// <summary>True if any cell in the week is Limited, meaning this schedule draws on a daily budget.</summary>
    public bool HasLimitedCells() => Enum.GetValues<DayOfWeek>().Any(HasLimitedCells);

    /// <summary>True if any cell on this day is Limited, meaning this day uses its budget.</summary>
    public bool HasLimitedCells(DayOfWeek day)
    {
        for (var slot = 0; slot < SlotsPerDay; slot++)
        {
            if (GetState(day, slot) == CellState.Limited)
            {
                return true;
            }
        }

        return false;
    }

    public void SetState(DayOfWeek day, int slot, CellState state)
    {
        if (slot < 0 || slot >= SlotsPerDay)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        Sanitize();
        var chars = Days[(int)day].ToCharArray();
        chars[slot] = ToChar(state);
        Days[(int)day] = new string(chars);
    }

    /// <summary>Overwrites each target day with the contents of <paramref name="from"/>.</summary>
    public void CopyDay(DayOfWeek from, IEnumerable<DayOfWeek> targets)
    {
        Sanitize();
        var source = Days[(int)from];
        foreach (var target in targets)
        {
            Days[(int)target] = source;
        }
    }

    /// <summary>Forces the grid into a valid shape: 7 rows of 48 cells; unknown or missing cells become Blocked.</summary>
    public void Sanitize()
    {
        var fixedDays = new string[7];
        for (var d = 0; d < 7; d++)
        {
            var chars = new char[SlotsPerDay];
            var row = Days is not null && d < Days.Length ? Days[d] : null;
            for (var i = 0; i < SlotsPerDay; i++)
            {
                chars[i] = ToChar(row is not null && i < row.Length ? FromChar(row[i]) : CellState.Blocked);
            }

            fixedDays[d] = new string(chars);
        }

        Days = fixedDays;
    }

    public WeeklySchedule Clone() => new() { Days = (string[])Days.Clone() };

    /// <summary>
    /// When the schedule next changes to a different state than it is in at <paramref name="now"/>, and what it
    /// changes to. Looks a full week ahead; returns null if the state never changes (for example, Free all week).
    /// </summary>
    public ScheduleChange? NextChange(DateTime now)
    {
        var currentState = GetState(now);
        var cellsInAWeek = 7 * SlotsPerDay;

        var position = StartOfNextCell(now);
        for (var i = 0; i < cellsInAWeek; i++)
        {
            var stateThere = GetState(position);
            if (stateThere != currentState)
            {
                return new ScheduleChange(position, stateThere);
            }

            position = StartOfNextCell(position);
        }

        return null;
    }

    /// <summary>
    /// The moment the next 30-minute cell begins. From 9:47 that's 10:00; from 10:00 it's 10:30.
    /// Midnight counts as a cell start too.
    /// </summary>
    public static DateTime StartOfNextCell(DateTime time)
    {
        var cellsSinceMidnight = Math.Floor(time.TimeOfDay.TotalMinutes / SlotMinutes);
        var thisCellStart = time.Date.AddMinutes(cellsSinceMidnight * SlotMinutes);
        return thisCellStart.AddMinutes(SlotMinutes);
    }

    public static int SlotOf(DateTime time) => (time.Hour * 60 + time.Minute) / SlotMinutes;

    public static string SlotLabel(int slot)
    {
        var minutes = slot * SlotMinutes;
        return DateTime.Today.AddMinutes(minutes).ToString("h:mm tt");
    }

    private static string[] NewDays(CellState state)
    {
        var row = new string(ToChar(state), SlotsPerDay);
        return Enumerable.Repeat(row, 7).ToArray();
    }

    private static char ToChar(CellState state) => state switch
    {
        CellState.Free => FreeChar,
        CellState.Limited => LimitedChar,
        _ => BlockedChar,
    };

    private static CellState FromChar(char c) => char.ToUpperInvariant(c) switch
    {
        FreeChar => CellState.Free,
        LimitedChar => CellState.Limited,
        _ => CellState.Blocked,
    };
}
