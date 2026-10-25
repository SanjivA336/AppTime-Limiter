using System.Text.Json.Serialization;

namespace AppTimeLimiter.Core.Models;

/// <summary>Everything the user configures. Serialized to settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>
    /// 1 = one budget for every day. 2 = one budget for each day of the week, shared by all groups.
    /// 3 = every group has its own budget for each day. Older files are upgraded automatically when they are loaded.
    /// </summary>
    public const int CurrentSchemaVersion = 3;

    /// <summary>Bumped when the file format changes so old files can be migrated.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public bool StartWithWindows { get; set; } = true;

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
}
