using AppTimeLimiter.Core.Enforcement;
using AppTimeLimiter.Core.Models;

namespace AppTimeLimiter.Tests;

/// <summary>Shared builders and fakes so each test reads as a short scenario.</summary>
internal static class T
{
    /// <summary>A Monday. Tests never depend on the real clock.</summary>
    public static readonly DateTime Monday = new(2026, 10, 26);

    public static DateTime At(int hour, int minute = 0, int second = 0) =>
        Monday.AddHours(hour).AddMinutes(minute).AddSeconds(second);

    public static AppGroup Group(string name, string[] processes, CellState fill, params int[] warnings) => new()
    {
        Name = name,
        Processes = processes.ToList(),
        Schedule = WeeklySchedule.Filled(fill),
        WarningMinutes = warnings.ToList(),
    };

    /// <summary>Sets every cell from <paramref name="fromHour"/> up to (not including) <paramref name="toHour"/>.</summary>
    public static void SetRange(WeeklySchedule schedule, DayOfWeek day, int fromHour, int toHour, CellState state)
    {
        for (var slot = fromHour * 2; slot < toHour * 2; slot++)
        {
            schedule.SetState(day, slot, state);
        }
    }

    /// <summary>
    /// Settings holding these groups. Every group is given the same daily budget on every day, unless a test changes
    /// it afterwards.
    /// </summary>
    public static AppSettings Settings(int budgetMinutes, params AppGroup[] groups)
    {
        foreach (var group in groups)
        {
            group.BudgetMinutesByDay = AppGroup.NewBudgets(budgetMinutes);
        }

        return new AppSettings { Groups = groups.ToList() };
    }

    public static string TempFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "atl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

internal sealed class FakeProcessController : IProcessController
{
    public HashSet<string> Running { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string[]> CloseCalls { get; } = new();

    /// <summary>Names that "can't be closed" (e.g. elevated processes).</summary>
    public HashSet<string> Unkillable { get; } = new(StringComparer.OrdinalIgnoreCase);

    public TaskCompletionSource? CloseGate { get; set; }

    public IReadOnlyCollection<string> GetRunningProcessNames() => Running.ToList();

    public async Task<IReadOnlyList<string>> CloseAsync(
        IReadOnlyCollection<string> processNames, CancellationToken cancellationToken = default)
    {
        CloseCalls.Add(processNames.ToArray());
        if (CloseGate is not null)
        {
            await CloseGate.Task;
        }

        var failed = processNames.Where(Unkillable.Contains).ToList();
        foreach (var name in processNames.Where(n => !Unkillable.Contains(n)))
        {
            Running.Remove(name);
        }

        return failed;
    }
}

internal sealed class FakeNotifier : INotifier
{
    public List<string> Messages { get; } = new();

    public void Notify(string title, string message) => Messages.Add(message);
}
