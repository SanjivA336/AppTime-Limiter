using System.Globalization;
using System.Text.Json;

namespace HardCap.Core.Usage;

/// <summary>Seconds one group was running on one budget day, split by whether it counted toward the budget.</summary>
public sealed class DayUsage
{
    /// <summary>Seconds during Limited cells; these draw down the shared daily budget.</summary>
    public double LimitedSeconds { get; set; }

    /// <summary>Seconds during Free cells; recorded for stats only.</summary>
    public double FreeSeconds { get; set; }

    /// <summary>
    /// Seconds each of the group's apps was running (by process name), for the Analytics page. Each running app is credited
    /// the whole time that passed, so these can add up to more than the group's own time when several run together.
    /// Days recorded before this existed have none.
    /// </summary>
    public Dictionary<string, double> Processes { get; set; } = new();

    /// <summary>
    /// Seconds the group was used in each hour of the clock (index 0 is 12 AM to 1 AM, index 23 is 11 PM to midnight), for
    /// the Analytics day view. Null for days recorded before hours were kept.
    /// </summary>
    public double[]? Hourly { get; set; }

    /// <summary>The same for each app: its seconds in each hour of the clock. Null for days recorded before hours were kept.</summary>
    public Dictionary<string, double[]>? AppHourly { get; set; }
}

/// <summary>
/// The record of how long each group has been used, per budget day (which starts at midnight, or at the reset time the user chose).
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

    /// <summary>How long usage is kept. Older days are deleted. A day is only a few numbers, so this is about tidiness, not space.</summary>
    public const int RetentionMonths = 3;

    public bool IsDirty { get; private set; }

    /// <summary>Set when the existing usage file couldn't be read, so the UI can tell the user.</summary>
    public string? LoadError { get; private set; }

    /// <param name="runningProcesses">The group's apps that were running, each of which is credited <paramref name="seconds"/> too.</param>
    /// <param name="endedAt">
    /// The clock time the credited stretch ended. When given, the time is also filed under the hours of the clock it covers
    /// (split at the hour if it crosses one). Without it, no hourly detail is kept.
    /// </param>
    public void Add(
        DateOnly date,
        Guid groupId,
        double seconds,
        bool countsTowardBudget,
        IEnumerable<string>? runningProcesses = null,
        DateTime? endedAt = null)
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

        var apps = runningProcesses?.ToList() ?? new List<string>();
        usage.Processes ??= new Dictionary<string, double>();
        foreach (var process in apps)
        {
            usage.Processes[process] = usage.Processes.GetValueOrDefault(process) + seconds;
        }

        if (endedAt is DateTime end)
        {
            AddHourly(usage, seconds, apps, end);
        }

        IsDirty = true;
    }

    /// <summary>Files a stretch of time under the clock hours it covers, splitting it where it crosses the top of an hour.</summary>
    private static void AddHourly(DayUsage usage, double seconds, List<string> apps, DateTime endedAt)
    {
        usage.Hourly ??= new double[24];
        usage.AppHourly ??= new Dictionary<string, double[]>();

        var cursor = endedAt.AddSeconds(-seconds);
        while (cursor < endedAt)
        {
            var nextHour = new DateTime(cursor.Year, cursor.Month, cursor.Day, cursor.Hour, 0, 0).AddHours(1);
            var segmentEnd = nextHour < endedAt ? nextHour : endedAt;
            var part = (segmentEnd - cursor).TotalSeconds;

            usage.Hourly[cursor.Hour] += part;
            foreach (var app in apps)
            {
                if (!usage.AppHourly.TryGetValue(app, out var hours) || hours.Length != 24)
                {
                    usage.AppHourly[app] = hours = new double[24];
                }

                hours[cursor.Hour] += part;
            }

            cursor = segmentEnd;
        }
    }

    /// <summary>
    /// The Limited seconds this one group has used on <paramref name="date"/>: what its own daily budget has spent.
    /// Other groups' time is not included, because every group has its own budget.
    /// </summary>
    public double GetBudgetSecondsUsed(DateOnly date, Guid groupId) => GetUsage(date, groupId).LimitedSeconds;

    /// <summary>
    /// Deletes every day before <paramref name="cutoff"/>. Returns how many days were removed (the file is rewritten at
    /// the next save if there were any).
    /// </summary>
    public int PruneOlderThan(DateOnly cutoff)
    {
        var stale = _days.Keys
            .Where(key => DateOnly.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day < cutoff)
            .ToList();

        foreach (var key in stale)
        {
            _days.Remove(key);
        }

        if (stale.Count > 0)
        {
            IsDirty = true;
        }

        return stale.Count;
    }

    /// <summary>The first day anything was recorded for any group, or null if nothing has been. This is when tracking began.</summary>
    public DateOnly? FirstDay
    {
        get
        {
            DateOnly? first = null;
            foreach (var key in _days.Keys)
            {
                if (DateOnly.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && (first is null || day < first))
                {
                    first = day;
                }
            }

            return first;
        }
    }

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
            LoadError = $"usage.json couldn't be read ({ex.Message}), so today's usage restarted from zero.";
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
