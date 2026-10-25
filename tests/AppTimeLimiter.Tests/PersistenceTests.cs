using AppTimeLimiter.Core.Models;
using AppTimeLimiter.Core.Settings;
using AppTimeLimiter.Core.Usage;

namespace AppTimeLimiter.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _folder = T.TempFolder();

    private string SettingsPath => Path.Combine(_folder, "settings.json");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Missing_file_loads_empty_defaults_without_an_error()
    {
        var result = new SettingsStore(SettingsPath).Load();

        Assert.Null(result.Error);
        Assert.Empty(result.Settings.Groups);
        Assert.Equal(AppSettings.CurrentSchemaVersion, result.Settings.SchemaVersion);
        Assert.True(result.Settings.StartWithWindows);
    }

    [Fact]
    public void Settings_round_trip_through_the_file()
    {
        var group = T.Group("Games", new[] { "Warframe.x64", "helldivers2" }, CellState.Free, 10, 5);
        group.Color = "#FF8800";
        T.SetRange(group.Schedule, DayOfWeek.Friday, 22, 24, CellState.Blocked);
        T.SetRange(group.Schedule, DayOfWeek.Saturday, 10, 12, CellState.Limited);
        var settings = T.Settings(45, group);
        group.SetBudgetMinutes(DayOfWeek.Saturday, 180); // days can have different budgets
        settings.StartWithWindows = false;
        var store = new SettingsStore(SettingsPath);

        store.Save(settings);
        var loaded = store.Load();

        Assert.Null(loaded.Error);
        Assert.Empty(loaded.Repairs);
        Assert.False(loaded.Settings.StartWithWindows);
        var back = Assert.Single(loaded.Settings.Groups);
        Assert.Equal(45, back.BudgetMinutesFor(DayOfWeek.Monday));
        Assert.Equal(180, back.BudgetMinutesFor(DayOfWeek.Saturday));
        Assert.Equal(group.Id, back.Id);
        Assert.Equal("Games", back.Name);
        Assert.Equal("#FF8800", back.Color);
        Assert.Equal(new[] { "Warframe.x64", "helldivers2" }, back.Processes);
        Assert.Equal(new[] { 10, 5 }, back.WarningMinutes);
        Assert.Equal(group.Schedule.Days, back.Schedule.Days);
    }

    [Fact]
    public void Each_group_keeps_its_own_budgets_through_a_save_and_load()
    {
        var games = T.Group("Games", new[] { "game" }, CellState.Free);
        var work = T.Group("Work", new[] { "editor" }, CellState.Free);
        var settings = T.Settings(60, games, work);
        games.SetBudgetMinutes(DayOfWeek.Saturday, 180);
        work.SetBudgetMinutes(DayOfWeek.Monday, 15);
        var store = new SettingsStore(SettingsPath);

        store.Save(settings);
        var loaded = store.Load().Settings;

        Assert.Equal(180, loaded.Groups[0].BudgetMinutesFor(DayOfWeek.Saturday));
        Assert.Equal(60, loaded.Groups[0].BudgetMinutesFor(DayOfWeek.Monday));
        Assert.Equal(15, loaded.Groups[1].BudgetMinutesFor(DayOfWeek.Monday));
        Assert.Equal(60, loaded.Groups[1].BudgetMinutesFor(DayOfWeek.Saturday));
    }

    [Fact]
    public void Saving_over_an_existing_file_replaces_it()
    {
        var store = new SettingsStore(SettingsPath);
        store.Save(T.Settings(30, T.Group("First", Array.Empty<string>(), CellState.Free)));

        store.Save(T.Settings(30, T.Group("Second", Array.Empty<string>(), CellState.Free)));

        Assert.Equal("Second", store.Load().Settings.Groups[0].Name);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void Save_creates_the_folder_when_it_does_not_exist()
    {
        var nested = Path.Combine(_folder, "a", "b", "settings.json");

        new SettingsStore(nested).Save(T.Settings(30));

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Corrupt_file_loads_defaults_reports_an_error_and_keeps_a_backup()
    {
        File.WriteAllText(SettingsPath, "{ this is not json");

        var result = new SettingsStore(SettingsPath).Load();

        Assert.NotNull(result.Error);
        Assert.Empty(result.Settings.Groups);
        var backup = Assert.Single(Directory.GetFiles(_folder, "settings.corrupt-*.json"));
        Assert.Equal("{ this is not json", File.ReadAllText(backup));
    }

    [Fact]
    public void A_json_null_file_is_treated_as_corrupt()
    {
        File.WriteAllText(SettingsPath, "null");

        Assert.NotNull(new SettingsStore(SettingsPath).Load().Error);
    }

    [Fact]
    public void Hand_edited_files_with_comments_and_trailing_commas_still_load()
    {
        File.WriteAllText(SettingsPath, """
            {
              // my budget
              "dailyBudgetMinutes": 33,
              "groups": [ { "name": "G" } ],
            }
            """);

        var result = new SettingsStore(SettingsPath).Load();

        Assert.Null(result.Error);
        Assert.All(Enum.GetValues<DayOfWeek>(), day => Assert.Equal(33, result.Settings.Groups[0].BudgetMinutesFor(day)));
    }

    [Fact]
    public void An_old_single_budget_file_gives_every_group_that_budget_on_every_day()
    {
        File.WriteAllText(SettingsPath, """
            { "schemaVersion": 1, "dailyBudgetMinutes": 90,
              "groups": [ { "name": "A" }, { "name": "B" } ] }
            """);
        var store = new SettingsStore(SettingsPath);

        var loaded = store.Load();

        Assert.Null(loaded.Error);
        Assert.Equal(AppSettings.CurrentSchemaVersion, loaded.Settings.SchemaVersion);
        Assert.All(loaded.Settings.Groups, g => Assert.Equal(new[] { 90, 90, 90, 90, 90, 90, 90 }, g.BudgetMinutesByDay));
        Assert.Null(loaded.Settings.LegacyDailyBudgetMinutes);

        // Saving writes the new format and doesn't bring the old field back.
        store.Save(loaded.Settings);
        Assert.DoesNotContain("dailyBudgetMinutes", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void An_old_shared_per_day_file_gives_every_group_a_copy_of_those_budgets()
    {
        // Version 2 had one budget per weekday, shared by all groups (Sunday first).
        File.WriteAllText(SettingsPath, """
            { "schemaVersion": 2, "budgetMinutesByDay": [180, 120, 120, 120, 120, 120, 180],
              "groups": [ { "name": "A" }, { "name": "B" } ] }
            """);
        var store = new SettingsStore(SettingsPath);

        var loaded = store.Load();

        Assert.Null(loaded.Error);
        Assert.Equal(AppSettings.CurrentSchemaVersion, loaded.Settings.SchemaVersion);
        var shared = new[] { 180, 120, 120, 120, 120, 120, 180 };
        Assert.Equal(shared, loaded.Settings.Groups[0].BudgetMinutesByDay);
        Assert.Equal(shared, loaded.Settings.Groups[1].BudgetMinutesByDay);
        Assert.NotSame(loaded.Settings.Groups[0].BudgetMinutesByDay, loaded.Settings.Groups[1].BudgetMinutesByDay);
        Assert.Null(loaded.Settings.LegacySharedBudgetMinutesByDay);

        // After the upgrade the groups are independent, and the file no longer has an app-wide budget list.
        loaded.Settings.Groups[0].SetBudgetMinutes(DayOfWeek.Saturday, 30);
        Assert.Equal(180, loaded.Settings.Groups[1].BudgetMinutesFor(DayOfWeek.Saturday));

        store.Save(loaded.Settings);
        using var saved = System.Text.Json.JsonDocument.Parse(File.ReadAllText(SettingsPath));
        Assert.False(saved.RootElement.TryGetProperty("budgetMinutesByDay", out _));
        Assert.All(saved.RootElement.GetProperty("groups").EnumerateArray(),
            group => Assert.True(group.TryGetProperty("budgetMinutesByDay", out _)));
    }

    [Fact]
    public void A_group_with_too_few_budgets_fills_in_the_rest_with_the_default()
    {
        File.WriteAllText(SettingsPath, """
            { "schemaVersion": 3, "groups": [ { "name": "G", "budgetMinutesByDay": [10, 20] } ] }
            """);

        var group = new SettingsStore(SettingsPath).Load().Settings.Groups[0];

        Assert.Equal(new[] { 10, 20, 120, 120, 120, 120, 120 }, group.BudgetMinutesByDay);
    }

    [Fact]
    public void A_group_without_budgets_in_a_current_file_gets_the_default_on_every_day()
    {
        File.WriteAllText(SettingsPath, """{ "schemaVersion": 3, "groups": [ { "name": "G" } ] }""");

        var group = new SettingsStore(SettingsPath).Load().Settings.Groups[0];

        Assert.All(Enum.GetValues<DayOfWeek>(), day => Assert.Equal(AppGroup.DefaultDailyBudgetMinutes, group.BudgetMinutesFor(day)));
    }

    [Fact]
    public void Loading_repairs_bad_data_and_reports_what_it_fixed()
    {
        File.WriteAllText(SettingsPath, """
            { "dailyBudgetMinutes": -10,
              "groups": [ { "name": "Oops", "processes": ["explorer", "game"] } ] }
            """);

        var result = new SettingsStore(SettingsPath).Load();

        Assert.Null(result.Error);
        Assert.Equal(0, result.Settings.Groups[0].BudgetMinutesFor(DayOfWeek.Monday));
        Assert.Equal(new[] { "game" }, result.Settings.Groups[0].Processes);
        Assert.Equal(2, result.Repairs.Count); // the budget once (not once per group), and the protected app
    }

    [Fact]
    public void A_group_with_no_schedule_in_the_file_is_fully_blocked()
    {
        File.WriteAllText(SettingsPath, """{ "groups": [ { "name": "G", "schedule": { "days": [] } } ] }""");

        var group = new SettingsStore(SettingsPath).Load().Settings.Groups[0];

        Assert.Equal(CellState.Blocked, group.Schedule.GetState(DayOfWeek.Monday, 20));
    }

    [Fact]
    public void Enums_in_the_file_are_not_involved_in_the_schedule_format()
    {
        new SettingsStore(SettingsPath).Save(T.Settings(30, T.Group("G", Array.Empty<string>(), CellState.Limited)));

        var text = File.ReadAllText(SettingsPath);

        Assert.Contains(new string('L', 48), text);
    }
}

public sealed class UsageLedgerTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 26);
    private readonly string _folder = T.TempFolder();

    private string UsagePath => Path.Combine(_folder, "usage.json");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Limited_and_free_time_are_tracked_separately()
    {
        var ledger = new UsageLedger();
        var id = Guid.NewGuid();

        ledger.Add(Today, id, 30, countsTowardBudget: true);
        ledger.Add(Today, id, 12, countsTowardBudget: false);

        Assert.Equal(30, ledger.GetUsage(Today, id).LimitedSeconds);
        Assert.Equal(12, ledger.GetUsage(Today, id).FreeSeconds);
        Assert.Equal(30, ledger.GetBudgetSecondsUsed(Today, id));
    }

    [Fact]
    public void Budget_used_is_counted_for_one_group_and_one_day_at_a_time()
    {
        var ledger = new UsageLedger();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        ledger.Add(Today, first, 10, true);
        ledger.Add(Today, second, 20, true);
        ledger.Add(Today.AddDays(1), first, 99, true);

        Assert.Equal(10, ledger.GetBudgetSecondsUsed(Today, first));   // not 30: the other group's time isn't included
        Assert.Equal(20, ledger.GetBudgetSecondsUsed(Today, second));
        Assert.Equal(99, ledger.GetBudgetSecondsUsed(Today.AddDays(1), first));
        Assert.Equal(0, ledger.GetBudgetSecondsUsed(Today.AddDays(-1), first));
        Assert.Equal(0, ledger.GetBudgetSecondsUsed(Today, Guid.NewGuid())); // a group with no usage
    }

    [Fact]
    public void Zero_or_negative_seconds_are_ignored()
    {
        var ledger = new UsageLedger();

        ledger.Add(Today, Guid.NewGuid(), 0, true);
        ledger.Add(Today, Guid.NewGuid(), -5, true);

        Assert.False(ledger.IsDirty);
    }

    [Fact]
    public void Usage_survives_a_save_and_load()
    {
        var id = Guid.NewGuid();
        var first = new UsageLedger(UsagePath);
        first.Add(Today, id, 90, true);
        first.Add(Today, id, 15, false);
        first.SaveIfDirty();

        var second = new UsageLedger(UsagePath);
        second.Load();

        Assert.Equal(90, second.GetUsage(Today, id).LimitedSeconds);
        Assert.Equal(15, second.GetUsage(Today, id).FreeSeconds);
        Assert.Null(second.LoadError);
    }

    [Fact]
    public void SaveIfDirty_writes_only_when_something_changed()
    {
        var ledger = new UsageLedger(UsagePath);

        ledger.SaveIfDirty();
        Assert.False(File.Exists(UsagePath));

        ledger.Add(Today, Guid.NewGuid(), 5, true);
        Assert.True(ledger.IsDirty);
        ledger.SaveIfDirty();

        Assert.True(File.Exists(UsagePath));
        Assert.False(ledger.IsDirty);
    }

    [Fact]
    public void Corrupt_usage_file_is_reported_and_backed_up()
    {
        File.WriteAllText(UsagePath, "not json");
        var ledger = new UsageLedger(UsagePath);

        ledger.Load();

        Assert.NotNull(ledger.LoadError);
        Assert.Equal(0, ledger.GetBudgetSecondsUsed(Today, Guid.NewGuid()));
        Assert.Single(Directory.GetFiles(_folder, "usage.corrupt-*.json"));
    }

    [Fact]
    public void In_memory_ledger_never_touches_the_disk()
    {
        var ledger = new UsageLedger();
        var id = Guid.NewGuid();
        ledger.Add(Today, id, 5, true);

        ledger.SaveIfDirty();
        ledger.Load();

        Assert.Equal(5, ledger.GetBudgetSecondsUsed(Today, id));
    }
}
