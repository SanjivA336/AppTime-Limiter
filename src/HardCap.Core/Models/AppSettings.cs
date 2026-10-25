using System.Text.Json.Serialization;

namespace HardCap.Core.Models;

/// <summary>Everything the user configures. Serialized to settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>
    /// 1 = one budget for every day. 2 = one budget for each day of the week, shared by all groups.
    /// 3 = every group has its own budget for each day. 4 = adds the reset time, the settings wait and open hours,
    /// and each group's grace period and pause. Older files are upgraded automatically when they are loaded.
    /// </summary>
    public const int CurrentSchemaVersion = 4;

    /// <summary>The longest wait that can be set for the settings and for quitting: five minutes (like a group's lock).</summary>
    public const int MaxWaitSeconds = AppGroup.MaxLockSeconds;

    /// <summary>Bumped when the file format changes so old files can be migrated.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public bool StartWithWindows { get; set; } = true;

    /// <summary>
    /// When budgets refill each day, in minutes after midnight (a multiple of 30). Midnight by default. See
    /// <see cref="BudgetDay"/>.
    /// </summary>
    public int ResetMinutes { get; set; }

    /// <summary>
    /// How long you must wait before opening the settings, a group, or quitting the app, except during open hours.
    /// 0 means no wait. A group's own lock still applies on top: the longer of the two is used.
    /// </summary>
    public int WaitSeconds { get; set; }

    /// <summary>When true, there is a daily window (see below) in which the wait above does not apply.</summary>
    public bool OpenHoursEnabled { get; set; }

    /// <summary>Start of the open hours, in minutes after midnight. Default 8:00 AM.</summary>
    public int OpenHoursStartMinutes { get; set; } = 8 * 60;

    /// <summary>End of the open hours, in minutes after midnight. Default 9:00 AM. If earlier than the start, the window runs past midnight.</summary>
    public int OpenHoursEndMinutes { get; set; } = 9 * 60;

    /// <summary>No default groups: a fresh install enforces nothing until the user adds some.</summary>
    public List<AppGroup> Groups { get; set; } = new();

    /// <summary>
    /// Only used to read version-2 files, which had one budget per day for the whole app. On load it is copied into
    /// every group's own budgets, and it is never written back.
    /// </summary>
    [JsonPropertyName("budgetMinutesByDay")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int[]? LegacySharedBudgetMinutesByDay { get; set; }

    /// <summary>
    /// Only used to read version-1 files, which had a single budget for all days. Handled the same way as
    /// <see cref="LegacySharedBudgetMinutesByDay"/>, and never written back.
    /// </summary>
    [JsonPropertyName("dailyBudgetMinutes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LegacyDailyBudgetMinutes { get; set; }

    /// <summary>
    /// Replaces the groups and the general settings with those of <paramref name="other"/> (used by "import settings").
    /// <see cref="StartWithWindows"/> stays as it is, because it describes this computer's startup, not the limits.
    /// </summary>
    public void ReplaceWith(AppSettings other)
    {
        Groups = other.Groups.Select(g => g.Clone()).ToList();
        ResetMinutes = other.ResetMinutes;
        WaitSeconds = other.WaitSeconds;
        OpenHoursEnabled = other.OpenHoursEnabled;
        OpenHoursStartMinutes = other.OpenHoursStartMinutes;
        OpenHoursEndMinutes = other.OpenHoursEndMinutes;
    }
}
