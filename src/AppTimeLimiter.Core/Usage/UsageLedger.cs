using System.Globalization;
using System.Text.Json;

namespace AppTimeLimiter.Core.Usage;

/// <summary>Seconds one group was running on one calendar day, split by whether it counted toward the budget.</summary>
public sealed class DayUsage
{
    /// <summary>Seconds during Limited cells; these draw down the shared daily budget.</summary>
    public double LimitedSeconds { get; set; }

    /// <summary>Seconds during Free cells; recorded for stats only.</summary>
    public double FreeSeconds { get; set; }
}

/// <summary>
/// The record of how long each group has been used, per calendar day (midnight is the day boundary).
/// Kept in memory and saved to usage.json. Not thread-safe: call it from one thread (the UI thread).
/// </summary>
public sealed class UsageLedger
{
    private readonly string? _path;
    private Dictionary<string, Dictionary<Guid, DayUsage>> _days = new();

    /// <summary>Pass null for a purely in-memory ledger (tests).</summary>
    public UsageLedger(string? path = null)
    {
        _path = path;
    }

    public bool IsDirty { get; private set; }

    /// <summary>Set when the existing usage file couldn't be read, so the UI can tell the user.</summary>
    public string? LoadError { get; private set; }

    public void Add(DateOnly date, Guid groupId, double seconds, bool countsTowardBudget)
    {
        if (seconds <= 0)
        {
            return;
        }

        var key = Key(date);
        if (!_days.TryGetValue(key, out var groups))
        {
            _days[key] = groups = new Dictionary<Guid, DayUsage>();
        }

        if (!groups.TryGetValue(groupId, out var usage))
        {
            groups[groupId] = usage = new DayUsage();
        }

        if (countsTowardBudget)
        {
            usage.LimitedSeconds += seconds;
        }
        else
        {
            usage.FreeSeconds += seconds;
        }

        IsDirty = true;
    }

    /// <summary>
    /// The Limited seconds this one group has used on <paramref name="date"/>: what its own daily budget has spent.
    /// Other groups' time is not included, because every group has its own budget.
    /// </summary>
    public double GetBudgetSecondsUsed(DateOnly date, Guid groupId) => GetUsage(date, groupId).LimitedSeconds;

    public DayUsage GetUsage(DateOnly date, Guid groupId) =>
        _days.TryGetValue(Key(date), out var groups) && groups.TryGetValue(groupId, out var usage)
            ? usage
            : new DayUsage();

    public void Load()
    {
        if (_path is null || !File.Exists(_path))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_path);
            _days = JsonSerializer.Deserialize<Dictionary<string, Dictionary<Guid, DayUsage>>>(json) ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Today's usage is lost, which would reset the budget. Keep the bad file for inspection and say so.
            LoadError = $"usage.json couldn't be read ({ex.Message}); today's usage started from zero.";
            try
            {
                File.Copy(_path, Path.ChangeExtension(_path, $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json"), overwrite: true);
            }
            catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException)
            {
                // Nothing more we can do; the load error above already tells the user.
            }

            _days = new();
        }
    }

    public void SaveIfDirty()
    {
        if (!IsDirty || _path is null)
        {
            return;
        }

        var folder = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_days));
        if (File.Exists(_path))
        {
            File.Replace(temp, _path, null);
        }
        else
        {
            File.Move(temp, _path);
        }

        IsDirty = false;
    }

    private static string Key(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
