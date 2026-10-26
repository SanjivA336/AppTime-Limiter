using System.Text.Json;
using HardCap.Core.Enforcement;
using HardCap.Core.Models;
using HardCap.Core.Settings;
using HardCap.Core.Usage;

namespace HardCap.Tests;

public sealed class CountModeTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 10, 26);
    private static readonly string[] Running = { "game" };

    private readonly string _folder = T.TempFolder();
    private readonly UsageLedger _ledger = new();
    private readonly EnforcementEngine _engine;

    public CountModeTests()
    {
        _engine = new EnforcementEngine(_ledger);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private (AppGroup Group, AppSettings Settings) LimitedGroup(CountMode mode, int budgetMinutes = 10)
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Limited);
        group.CountMode = mode;
        return (group, T.Settings(budgetMinutes, group));
    }

    private DateTime _now = T.At(12);
    private bool _started;

    /// <summary>
    /// Lets <paramref name="minutes"/> pass with the given window in front, ticking every 30 seconds (the most one tick
    /// will credit, so a long gap is never counted as time used).
    /// </summary>
    private void Stay(AppSettings settings, double minutes, string? inFront)
    {
        if (!_started)
        {
            _engine.Tick(settings, _now, Running, inFront);
            _started = true;
        }

        for (var i = 0; i < (int)(minutes * 2); i++)
        {
            _now = _now.AddSeconds(30);
            _engine.Tick(settings, _now, Running, inFront);
        }
    }

    // ---- The setting ----

    [Fact]
    public void A_new_group_counts_whenever_it_is_running()
    {
        Assert.Equal(CountMode.WhileRunning, new AppGroup().CountMode);
    }

    [Fact]
    public void The_count_mode_survives_saving_and_loading_and_a_copy()
    {
        var (group, settings) = LimitedGroup(CountMode.WhileInFront);
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));

        store.Save(settings);
        var back = Assert.Single(store.Load().Settings.Groups);

        Assert.Equal(CountMode.WhileInFront, back.CountMode);
        Assert.Equal(CountMode.WhileInFront, group.Clone().CountMode);
        Assert.False(group.IsSameAs(new AppGroup { Id = group.Id, Name = group.Name, Color = group.Color, Processes = group.Processes, Schedule = group.Schedule, WarningMinutes = group.WarningMinutes, BudgetMinutesByDay = group.BudgetMinutesByDay }));
    }

    [Fact]
    public void A_settings_file_without_the_setting_loads_as_running()
    {
        var (_, settings) = LimitedGroup(CountMode.WhileRunning);
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        store.Save(settings);
        var withoutSetting = File.ReadAllText(store.Path).Replace("\"countMode\": \"WhileRunning\",", string.Empty);
        File.WriteAllText(store.Path, withoutSetting);

        Assert.DoesNotContain("countMode", withoutSetting);
        Assert.Equal(CountMode.WhileRunning, Assert.Single(store.Load().Settings.Groups).CountMode);
    }

    [Fact]
    public void An_unknown_number_in_the_file_plays_it_safe_and_counts_everything()
    {
        var (_, settings) = LimitedGroup(CountMode.WhileInFront);
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        store.Save(settings);
        File.WriteAllText(store.Path, File.ReadAllText(store.Path).Replace("\"WhileInFront\"", "99"));

        Assert.Equal(CountMode.WhileRunning, Assert.Single(store.Load().Settings.Groups).CountMode);
    }

    // ---- Counting ----

    [Fact]
    public void Counting_while_running_ignores_what_is_in_front()
    {
        var (group, settings) = LimitedGroup(CountMode.WhileRunning);

        Stay(settings, 5, inFront: "browser");

        Assert.Equal(5 * 60, _ledger.GetBudgetSecondsUsed(Day, group.Id, CountMode.WhileRunning));
    }

    [Fact]
    public void Counting_while_in_front_only_spends_the_budget_when_the_app_is_in_front()
    {
        var (group, settings) = LimitedGroup(CountMode.WhileInFront);

        Stay(settings, 5, inFront: "browser");              // five minutes in the background
        Assert.Equal(0, _ledger.GetBudgetSecondsUsed(Day, group.Id, CountMode.WhileInFront));

        Stay(settings, 3, inFront: "game");                 // three minutes in front

        Assert.Equal(3 * 60, _ledger.GetBudgetSecondsUsed(Day, group.Id, CountMode.WhileInFront));
    }

    [Fact]
    public void Both_amounts_are_recorded_whichever_mode_is_chosen()
    {
        var (group, settings) = LimitedGroup(CountMode.WhileInFront);

        Stay(settings, 4, inFront: "browser");
        Stay(settings, 2, inFront: "game");

        var usage = _ledger.GetUsage(Day, group.Id);
        Assert.Equal(6 * 60, usage.LimitedSeconds);              // running: all six minutes
        Assert.Equal(2 * 60, usage.Front!.LimitedSeconds);       // in front: the last two
        Assert.Equal(2 * 60, usage.Front.Processes["game"]);
        Assert.Equal(2 * 60, usage.Front.Hourly![12]);
    }

    [Fact]
    public void Switching_the_mode_changes_which_amount_the_budget_reads()
    {
        var (group, settings) = LimitedGroup(CountMode.WhileInFront);
        Stay(settings, 4, inFront: "browser");

        Assert.Equal(0, _ledger.GetBudgetSecondsUsed(Day, group.Id, group.CountMode));
        group.CountMode = CountMode.WhileRunning;
        Assert.Equal(4 * 60, _ledger.GetBudgetSecondsUsed(Day, group.Id, group.CountMode));
    }

    [Fact]
    public void The_app_is_closed_when_the_budget_is_gone_even_if_it_is_in_the_background()
    {
        var (_, settings) = LimitedGroup(CountMode.WhileInFront, budgetMinutes: 2);
        Stay(settings, 2, inFront: "game");                  // the whole budget is spent in front

        _now = _now.AddSeconds(30);
        var result = _engine.Tick(settings, _now, Running, "browser"); // now it sits in the background

        var close = Assert.Single(result.Closes);
        Assert.Equal(CloseReason.BudgetUsedUp, close.Reason);
    }

    [Fact]
    public void A_background_app_is_closed_in_a_blocked_time_like_any_other()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Blocked);
        group.CountMode = CountMode.WhileInFront;
        var settings = T.Settings(60, group);

        var result = _engine.Tick(settings, T.At(12), Running, "browser");

        Assert.Single(result.Closes);
    }

    [Fact]
    public void Nothing_in_front_such_as_the_lock_screen_counts_as_not_in_front()
    {
        var (group, settings) = LimitedGroup(CountMode.WhileInFront);

        Stay(settings, 3, inFront: null);

        Assert.Equal(0, _ledger.GetBudgetSecondsUsed(Day, group.Id, CountMode.WhileInFront));
        Assert.Equal(3 * 60, _ledger.GetUsage(Day, group.Id).LimitedSeconds);
    }

    [Fact]
    public void The_front_window_name_is_matched_the_same_way_as_the_group_apps()
    {
        var (group, settings) = LimitedGroup(CountMode.WhileInFront);

        Stay(settings, 2, inFront: "GAME.exe");

        Assert.Equal(2 * 60, _ledger.GetBudgetSecondsUsed(Day, group.Id, CountMode.WhileInFront));
    }

    // ---- Older days ----

    [Fact]
    public void A_day_with_no_in_front_record_answers_with_the_running_time()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Limited);
        _ledger.Add(Day, group.Id, 600, countsTowardBudget: true); // an older tick: nothing about the front

        Assert.Null(_ledger.GetUsage(Day, group.Id).Front);
        Assert.Equal(600, _ledger.GetBudgetSecondsUsed(Day, group.Id, CountMode.WhileInFront));
    }

    [Fact]
    public void The_first_in_front_record_of_a_day_assumes_earlier_time_was_in_front()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Limited);
        _ledger.Add(Day, group.Id, 600, countsTowardBudget: true);   // recorded before in-front time was kept

        _ledger.Add(Day, group.Id, 60, countsTowardBudget: true, new[] { "game" }, trackInFront: true, inFrontProcess: null);

        Assert.Equal(600, _ledger.GetBudgetSecondsUsed(Day, group.Id, CountMode.WhileInFront)); // charges more, never less
        Assert.Equal(660, _ledger.GetBudgetSecondsUsed(Day, group.Id, CountMode.WhileRunning));
    }

    [Fact]
    public void An_old_usage_file_without_in_front_numbers_still_loads()
    {
        var path = Path.Combine(_folder, "usage.json");
        var id = Guid.NewGuid();
        File.WriteAllText(path, $"{{\"2026-10-20\":{{\"{id}\":{{\"LimitedSeconds\":900,\"FreeSeconds\":30,\"Processes\":{{\"game\":930}}}}}}}}");

        var ledger = new UsageLedger(path);
        ledger.Load();

        Assert.Null(ledger.LoadError);
        Assert.Equal(900, ledger.GetBudgetSecondsUsed(new DateOnly(2026, 10, 20), id));
        Assert.Equal(900, ledger.GetBudgetSecondsUsed(new DateOnly(2026, 10, 20), id, CountMode.WhileInFront));
    }

    [Fact]
    public void In_front_numbers_are_saved_and_loaded_with_the_rest()
    {
        var (group, settings) = LimitedGroup(CountMode.WhileInFront);
        var path = Path.Combine(_folder, "usage.json");
        var ledger = new UsageLedger(path);
        var engine = new EnforcementEngine(ledger);
        engine.Tick(settings, T.At(12), Running, "game");
        engine.Tick(settings, T.At(12, 0, 30), Running, "game");
        ledger.SaveIfDirty();

        var reloaded = new UsageLedger(path);
        reloaded.Load();

        Assert.Null(reloaded.LoadError);
        Assert.Equal(30, reloaded.GetUsage(Day, group.Id).Front!.LimitedSeconds);
        Assert.Equal(30, reloaded.GetBudgetSecondsUsed(Day, group.Id, CountMode.WhileInFront));
    }
}
