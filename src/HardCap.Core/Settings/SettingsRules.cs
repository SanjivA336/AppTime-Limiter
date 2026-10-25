using HardCap.Core.Models;
using HardCap.Core.Processes;

namespace HardCap.Core.Settings;

/// <summary>
/// Rules about what a valid configuration looks like. <see cref="Sanitize"/> repairs a loaded file;
/// the Can* methods give the UI a human-readable reason when an edit must be refused.
/// </summary>
public static class SettingsRules
{
    /// <summary>
    /// Fixes anything odd in <paramref name="settings"/> (a hand-edited or damaged file, say) so the rest of
    /// the app can trust it. Returns a plain-English note for each fix (empty if nothing needed fixing).
    /// </summary>
    public static List<string> Sanitize(AppSettings settings)
    {
        var repairs = new List<string>();

        settings.Groups ??= new List<AppGroup>();
        UpgradeSharedBudgets(settings, repairs);
        FixGeneralSettings(settings, repairs);

        // Shared across all groups, because ids, names and apps must each be unique across the whole file.
        var idsSeen = new HashSet<Guid>();
        var namesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groupThatOwnsProcess = new Dictionary<string, string>(ProcessNames.Comparer);

        foreach (var group in settings.Groups)
        {
            FixId(group, idsSeen);
            FixName(group, namesSeen);

            if (!IsHexColor(group.Color))
            {
                group.Color = AppGroup.DefaultColor;
            }

            group.Schedule ??= new WeeklySchedule();
            group.Schedule.Sanitize();

            group.Processes = SanitizeProcesses(group, groupThatOwnsProcess, repairs);
            group.WarningMinutes = SanitizeWarnings(group.WarningMinutes);
            group.LockSeconds = Math.Clamp(group.LockSeconds, 0, AppGroup.MaxLockSeconds);
            group.GraceSeconds = Math.Clamp(group.GraceSeconds, 0, AppGroup.MaxGraceSeconds);
            if (!group.IsPaused)
            {
                group.PausedUntil = null; // an end time means nothing without a pause
            }

            FixBudgets(group, $"{group.Name}: ", repairs);
        }

        return repairs;
    }

    /// <summary>Keeps the reset time, the wait and the open hours inside what the app can handle.</summary>
    private static void FixGeneralSettings(AppSettings settings, List<string> repairs)
    {
        var tidyReset = BudgetDay.Tidy(settings.ResetMinutes);
        if (tidyReset != settings.ResetMinutes)
        {
            repairs.Add($"The daily reset time was out of range; using {BudgetDay.Describe(tidyReset)}.");
            settings.ResetMinutes = tidyReset;
        }

        settings.WaitSeconds = Math.Clamp(settings.WaitSeconds, 0, AppSettings.MaxWaitSeconds);

        const int lastMinuteOfDay = 24 * 60 - 1;
        settings.OpenHoursStartMinutes = Math.Clamp(settings.OpenHoursStartMinutes, 0, lastMinuteOfDay);
        settings.OpenHoursEndMinutes = Math.Clamp(settings.OpenHoursEndMinutes, 0, lastMinuteOfDay);
    }

    /// <summary>
    /// Older files kept one budget for the whole app (one number for every day, or one per day). Now every group has its
    /// own. This reads the old budgets once, tidies them, gives every group a copy, and forgets the old fields so they
    /// are not written again. A file already in the new format has no old fields and passes straight through.
    /// </summary>
    private static void UpgradeSharedBudgets(AppSettings settings, List<string> repairs)
    {
        int[]? shared = null;

        if (settings.LegacySharedBudgetMinutesByDay is { } perDay)
        {
            shared = FixBudgets(perDay, prefix: string.Empty, repairs);
        }
        else if (settings.LegacyDailyBudgetMinutes is int single)
        {
            var clamped = Math.Clamp(single, 0, AppGroup.MaxDailyBudgetMinutes);
            if (clamped != single)
            {
                repairs.Add($"Daily budget {single} min is out of range; using {clamped}.");
            }

            shared = AppGroup.NewBudgets(clamped);
        }

        if (shared is not null)
        {
            foreach (var group in settings.Groups)
            {
                group.BudgetMinutesByDay = (int[])shared.Clone();
            }
        }

        settings.LegacySharedBudgetMinutesByDay = null;
        settings.LegacyDailyBudgetMinutes = null;
        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
    }

    /// <summary>Makes sure a group has exactly one sensible budget for each of the seven days.</summary>
    private static void FixBudgets(AppGroup group, string prefix, List<string> repairs) =>
        group.BudgetMinutesByDay = FixBudgets(group.BudgetMinutesByDay, prefix, repairs);

    /// <summary>
    /// Returns seven budgets, each between 0 and 24 hours. A missing day gets the default. Anything out of range is
    /// pulled in, with a note saying so.
    /// </summary>
    private static int[] FixBudgets(int[]? found, string prefix, List<string> repairs)
    {
        found ??= Array.Empty<int>();
        var repaired = new int[7];

        for (var day = 0; day < 7; day++)
        {
            var value = day < found.Length ? found[day] : AppGroup.DefaultDailyBudgetMinutes;
            repaired[day] = Math.Clamp(value, 0, AppGroup.MaxDailyBudgetMinutes);

            if (repaired[day] != value)
            {
                repairs.Add($"{prefix}{(DayOfWeek)day} budget {value} min is out of range; using {repaired[day]}.");
            }
        }

        return repaired;
    }

    /// <summary>Every group needs its own id. A missing or repeated one gets a fresh id.</summary>
    private static void FixId(AppGroup group, HashSet<Guid> idsSeen)
    {
        if (group.Id == Guid.Empty || !idsSeen.Add(group.Id))
        {
            group.Id = Guid.NewGuid();
            idsSeen.Add(group.Id);
        }
    }

    /// <summary>Names can't be blank or repeated: "Games" and "games" become "Games" and "games (2)".</summary>
    private static void FixName(AppGroup group, HashSet<string> namesSeen)
    {
        group.Name = string.IsNullOrWhiteSpace(group.Name) ? "Group" : group.Name.Trim();

        var originalName = group.Name;
        for (var copyNumber = 2; !namesSeen.Add(group.Name); copyNumber++)
        {
            group.Name = $"{originalName} ({copyNumber})";
        }
    }

    /// <summary>Returns null when <paramref name="rawName"/> can be added to <paramref name="group"/>, else the reason it can't.</summary>
    public static string? CanAddProcess(AppSettings settings, AppGroup group, string rawName)
    {
        var name = ProcessNames.Normalize(rawName);
        if (name.Length == 0)
        {
            return "Process name is empty.";
        }

        if (ProcessNames.IsProtected(name))
        {
            return $"\"{name}\" is a core Windows process (or this app) and can't be limited.";
        }

        foreach (var other in settings.Groups)
        {
            if (other.Processes.Contains(name, ProcessNames.Comparer))
            {
                return ReferenceEquals(other, group)
                    ? $"\"{name}\" is already in this group."
                    : $"\"{name}\" is already in the group \"{other.Name}\". A process can only belong to one group.";
            }
        }

        return null;
    }

    /// <summary>Returns null when the name is acceptable for <paramref name="group"/>.</summary>
    public static string? CanRename(AppSettings settings, AppGroup group, string newName)
    {
        var trimmed = (newName ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return "Group name can't be empty.";
        }

        var clash = settings.Groups.Any(g => !ReferenceEquals(g, group)
            && string.Equals(g.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        return clash ? $"A group named \"{trimmed}\" already exists." : null;
    }

    /// <summary>Distinct, positive, descending, capped at <see cref="AppGroup.MaxWarnings"/>.</summary>
    public static List<int> SanitizeWarnings(IEnumerable<int>? minutes) =>
        (minutes ?? Enumerable.Empty<int>())
            .Where(m => m is > 0 and <= AppGroup.MaxDailyBudgetMinutes)
            .Distinct()
            .OrderByDescending(m => m)
            .Take(AppGroup.MaxWarnings)
            .ToList();

    /// <summary>
    /// Cleans one group's app list: tidy the names, drop blanks, drop core Windows processes, and drop any
    /// app another group already claimed (the first group to list an app keeps it).
    /// </summary>
    private static List<string> SanitizeProcesses(
        AppGroup group, Dictionary<string, string> groupThatOwnsProcess, List<string> repairs)
    {
        var processesToKeep = new List<string>();

        foreach (var rawName in group.Processes ?? new List<string>())
        {
            var name = ProcessNames.Normalize(rawName);
            if (name.Length == 0)
            {
                continue;
            }

            if (ProcessNames.IsProtected(name))
            {
                repairs.Add($"Removed protected process \"{name}\" from \"{group.Name}\".");
                continue;
            }

            if (groupThatOwnsProcess.TryGetValue(name, out var owningGroupName))
            {
                var listedTwiceInSameGroup = owningGroupName == group.Name;
                if (!listedTwiceInSameGroup)
                {
                    repairs.Add($"\"{name}\" was in both \"{owningGroupName}\" and \"{group.Name}\"; kept it in \"{owningGroupName}\".");
                }

                continue;
            }

            groupThatOwnsProcess[name] = group.Name;
            processesToKeep.Add(name);
        }

        return processesToKeep;
    }

    private static bool IsHexColor(string? color) =>
        color is { Length: 7 } && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit);
}
