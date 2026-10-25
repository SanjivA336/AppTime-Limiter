using HardCap.Core.Enforcement;
using HardCap.Core.Models;
using HardCap.Core.Settings;
using HardCap.Core.Usage;

namespace HardCap.Tests;

public class GraceAndPauseTests
{
    private static readonly string[] Running = { "game" };
    private static readonly string[] Nothing = Array.Empty<string>();

    private readonly UsageLedger _ledger = new();
    private readonly EnforcementEngine _engine;

    public GraceAndPauseTests()
    {
        _engine = new EnforcementEngine(_ledger);
    }

    /// <summary>A group that is Free all day except Blocked from 12:00 onwards.</summary>
    private static AppGroup FreeUntilNoon(int graceSeconds)
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Free);
        T.SetRange(group.Schedule, DayOfWeek.Monday, 12, 24, CellState.Blocked);
        group.GraceSeconds = graceSeconds;
        return group;
    }

    // ---- Grace period: the budget runs out while the app is running ----

    [Fact]
    public void Running_out_of_budget_starts_the_grace_period_instead_of_closing()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Limited);
        group.GraceSeconds = 60;
        var settings = T.Settings(1, group); // one minute of budget

        _engine.Tick(settings, T.At(12), Running);
        _engine.Tick(settings, T.At(12, 0, 30), Running);
        var budgetRunsOut = _engine.Tick(settings, T.At(12, 1, 0), Running);

        Assert.Empty(budgetRunsOut.Closes);
        var alert = Assert.Single(budgetRunsOut.Alerts);
        Assert.Contains("1 minute", alert.Message);
        Assert.Contains("used up", alert.Message);
    }

    [Fact]
    public void The_app_is_closed_once_the_grace_period_has_run_out()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Limited);
        group.GraceSeconds = 60;
        var settings = T.Settings(1, group);

        _engine.Tick(settings, T.At(12), Running);
        _engine.Tick(settings, T.At(12, 0, 30), Running);
        _engine.Tick(settings, T.At(12, 1, 0), Running);                       // grace starts here, ends at 12:02:00
        var midway = _engine.Tick(settings, T.At(12, 1, 30), Running);
        var justBefore = _engine.Tick(settings, T.At(12, 1, 59), Running);
        var atTheEnd = _engine.Tick(settings, T.At(12, 2, 0), Running);

        Assert.Empty(midway.Closes);
        Assert.Empty(justBefore.Closes);
        var close = Assert.Single(atTheEnd.Closes);
        Assert.Equal(CloseReason.BudgetUsedUp, close.Reason);
    }

    [Fact]
    public void Nothing_more_is_charged_to_the_budget_during_grace()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Limited);
        group.GraceSeconds = 60;
        var settings = T.Settings(1, group);

        _engine.Tick(settings, T.At(12), Running);
        _engine.Tick(settings, T.At(12, 0, 30), Running);
        _engine.Tick(settings, T.At(12, 1, 0), Running);
        _engine.Tick(settings, T.At(12, 1, 30), Running);

        Assert.Equal(60, _ledger.GetBudgetSecondsUsed(DateOnly.FromDateTime(T.Monday), group.Id));
    }

    // ---- Grace period: a Blocked time begins while the app is running ----

    [Fact]
    public void A_block_that_begins_while_the_app_runs_gives_it_the_grace_period()
    {
        var group = FreeUntilNoon(graceSeconds: 90);
        var settings = T.Settings(60, group);

        var before = _engine.Tick(settings, T.At(11, 59, 50), Running);
        var blockStarts = _engine.Tick(settings, T.At(12, 0, 5), Running);
        var duringGrace = _engine.Tick(settings, T.At(12, 1, 0), Running);
        var afterGrace = _engine.Tick(settings, T.At(12, 1, 36), Running); // 91 seconds after the block began

        Assert.Empty(before.Closes);
        Assert.Empty(blockStarts.Closes);
        Assert.Contains("1 minute 30 seconds", Assert.Single(blockStarts.Alerts).Message);
        Assert.Empty(duringGrace.Closes);
        Assert.Equal(CloseReason.Blocked, Assert.Single(afterGrace.Closes).Reason);
    }

    [Fact]
    public void An_app_opened_during_a_block_gets_no_grace_period()
    {
        var group = FreeUntilNoon(graceSeconds: 120);
        var settings = T.Settings(60, group);

        _engine.Tick(settings, T.At(12, 0, 5), Nothing);                  // the block is on, and the app isn't running
        var opened = _engine.Tick(settings, T.At(12, 0, 8), Running);     // it is launched into the block

        Assert.Equal(CloseReason.Blocked, Assert.Single(opened.Closes).Reason);
    }

    [Fact]
    public void Reopening_an_app_after_it_was_closed_gives_no_new_grace_period()
    {
        var group = FreeUntilNoon(graceSeconds: 30);
        var settings = T.Settings(60, group);

        _engine.Tick(settings, T.At(11, 59, 50), Running);
        _engine.Tick(settings, T.At(12, 0, 5), Running);                       // grace starts
        _engine.Tick(settings, T.At(12, 0, 40), Running);                      // closed here
        _engine.Tick(settings, T.At(12, 0, 43), Nothing);                      // gone
        var relaunched = _engine.Tick(settings, T.At(12, 0, 50), Running);

        Assert.Single(relaunched.Closes);
    }

    [Fact]
    public void The_very_first_check_does_not_grant_grace()
    {
        // The app was already running when HardCap started: it might have been opened during the block.
        var group = FreeUntilNoon(graceSeconds: 120);
        var settings = T.Settings(60, group);

        var first = _engine.Tick(settings, T.At(12, 0, 5), Running);

        Assert.Single(first.Closes);
    }

    [Fact]
    public void Without_a_grace_period_the_app_is_closed_at_once_as_before()
    {
        var group = FreeUntilNoon(graceSeconds: 0);
        var settings = T.Settings(60, group);

        _engine.Tick(settings, T.At(11, 59, 50), Running);
        var blockStarts = _engine.Tick(settings, T.At(12, 0, 5), Running);

        Assert.Single(blockStarts.Closes);
    }

    [Fact]
    public void An_app_that_will_not_close_keeps_being_closed_after_grace_ends()
    {
        var group = FreeUntilNoon(graceSeconds: 30);
        var settings = T.Settings(60, group);

        _engine.Tick(settings, T.At(11, 59, 50), Running);
        _engine.Tick(settings, T.At(12, 0, 5), Running);
        var firstClose = _engine.Tick(settings, T.At(12, 0, 40), Running);
        var secondClose = _engine.Tick(settings, T.At(12, 0, 43), Running); // still there

        Assert.Single(firstClose.Closes);
        Assert.Single(secondClose.Closes);
    }

    [Fact]
    public void Grace_is_cancelled_if_the_limit_lifts_and_a_later_block_gets_a_fresh_one()
    {
        var group = FreeUntilNoon(graceSeconds: 60);
        var settings = T.Settings(60, group);

        _engine.Tick(settings, T.At(11, 59, 50), Running);
        _engine.Tick(settings, T.At(12, 0, 5), Running);                        // grace starts
        T.SetRange(group.Schedule, DayOfWeek.Monday, 12, 24, CellState.Free);   // the user edits the schedule: no block now
        var lifted = _engine.Tick(settings, T.At(12, 0, 20), Running);

        T.SetRange(group.Schedule, DayOfWeek.Monday, 12, 24, CellState.Blocked);
        var blockedAgain = _engine.Tick(settings, T.At(12, 0, 25), Running);

        Assert.Empty(lifted.Closes);
        Assert.Empty(blockedAgain.Closes);
        Assert.Contains("closed in", Assert.Single(blockedAgain.Alerts).Message);
    }

    // ---- Pause ----

    [Fact]
    public void A_paused_group_is_not_closed_even_in_a_blocked_cell()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Blocked);
        group.IsPaused = true;

        var result = _engine.Tick(T.Settings(60, group), T.At(12), Running);

        Assert.Empty(result.Closes);
        Assert.Empty(result.Alerts);
    }

    [Fact]
    public void Time_in_a_paused_group_is_tracked_but_never_charged_to_the_budget()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Limited);
        group.IsPaused = true;
        var settings = T.Settings(60, group);

        _engine.Tick(settings, T.At(12), Running);
        _engine.Tick(settings, T.At(12, 0, 20), Running);

        var day = DateOnly.FromDateTime(T.Monday);
        Assert.Equal(0, _ledger.GetBudgetSecondsUsed(day, group.Id));
        Assert.Equal(20, _ledger.GetUsage(day, group.Id).FreeSeconds);
    }

    [Fact]
    public void A_paused_group_is_reported_as_free()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Blocked);
        group.IsPaused = true;

        var result = _engine.Tick(T.Settings(60, group), T.At(12), Running);

        Assert.Equal(new GroupStatus(group.Id, true, CellState.Free), Assert.Single(result.Statuses));
    }

    [Fact]
    public void A_paused_group_gets_no_warnings()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Free, 5);
        T.SetRange(group.Schedule, DayOfWeek.Monday, 12, 24, CellState.Blocked);
        group.IsPaused = true;

        var result = _engine.Tick(T.Settings(60, group), T.At(11, 56), Running);

        Assert.Empty(result.Alerts);
    }

    [Fact]
    public void A_pause_with_an_end_time_ends_by_itself()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Blocked);
        group.IsPaused = true;
        group.PausedUntil = T.At(13);
        var settings = T.Settings(60, group);

        var during = _engine.Tick(settings, T.At(12, 59), Running);
        var after = _engine.Tick(settings, T.At(13, 0, 3), Running);

        Assert.Empty(during.Closes);
        Assert.Equal(CloseReason.Blocked, Assert.Single(after.Closes).Reason);
    }

    [Fact]
    public void A_pause_with_no_end_time_lasts_until_it_is_switched_off()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Blocked);
        group.IsPaused = true;
        group.PausedUntil = null;
        var settings = T.Settings(60, group);

        Assert.Empty(_engine.Tick(settings, T.At(12), Running).Closes);
        Assert.Empty(_engine.Tick(settings, T.At(12).AddDays(30), Running).Closes);

        group.IsPaused = false;
        Assert.Single(_engine.Tick(settings, T.At(12).AddDays(30).AddSeconds(3), Running).Closes);
    }

    [Fact]
    public void IsPausedAt_is_true_only_before_the_end_time()
    {
        var group = new AppGroup { IsPaused = true, PausedUntil = T.At(10) };

        Assert.True(group.IsPausedAt(T.At(9, 59)));
        Assert.False(group.IsPausedAt(T.At(10)));
        Assert.False(new AppGroup { IsPaused = false, PausedUntil = T.At(10) }.IsPausedAt(T.At(9)));
    }

    // ---- Saving, copying and tidying the new settings ----

    [Fact]
    public void Group_and_general_settings_survive_a_save_and_load()
    {
        var folder = T.TempFolder();
        try
        {
            var group = T.Group("Games", new[] { "game" }, CellState.Free);
            group.GraceSeconds = 45;
            group.IsPaused = true;
            group.PausedUntil = new DateTime(2026, 11, 3, 9, 30, 0);
            var settings = T.Settings(60, group);
            settings.ResetMinutes = 270;
            settings.WaitSeconds = 125;
            settings.OpenHoursEnabled = true;
            settings.OpenHoursStartMinutes = 7 * 60;
            settings.OpenHoursEndMinutes = 8 * 60 + 30;
            var store = new SettingsStore(Path.Combine(folder, "settings.json"));

            store.Save(settings);
            var loaded = store.Load();

            Assert.Empty(loaded.Repairs);
            var back = Assert.Single(loaded.Settings.Groups);
            Assert.Equal(45, back.GraceSeconds);
            Assert.True(back.IsPaused);
            Assert.Equal(new DateTime(2026, 11, 3, 9, 30, 0), back.PausedUntil);
            Assert.Equal(270, loaded.Settings.ResetMinutes);
            Assert.Equal(125, loaded.Settings.WaitSeconds);
            Assert.True(loaded.Settings.OpenHoursEnabled);
            Assert.Equal(7 * 60, loaded.Settings.OpenHoursStartMinutes);
            Assert.Equal(8 * 60 + 30, loaded.Settings.OpenHoursEndMinutes);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_file_from_before_these_settings_loads_with_the_old_behaviour()
    {
        var folder = T.TempFolder();
        try
        {
            var path = Path.Combine(folder, "settings.json");
            File.WriteAllText(path, """{ "schemaVersion": 3, "startWithWindows": false, "groups": [ { "name": "Games", "processes": ["game"] } ] }""");

            var loaded = new SettingsStore(path).Load();

            Assert.Null(loaded.Error);
            Assert.Equal(0, loaded.Settings.ResetMinutes);          // midnight, as before
            Assert.Equal(0, loaded.Settings.WaitSeconds);           // no wait
            Assert.False(loaded.Settings.OpenHoursEnabled);
            var group = Assert.Single(loaded.Settings.Groups);
            Assert.Equal(0, group.GraceSeconds);
            Assert.False(group.IsPaused);
            Assert.Equal(AppSettings.CurrentSchemaVersion, loaded.Settings.SchemaVersion);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Out_of_range_values_are_pulled_back_in_when_loaded()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Free);
        group.GraceSeconds = 9999;
        group.IsPaused = false;
        group.PausedUntil = T.At(10); // an end time with no pause
        var settings = T.Settings(60, group);
        settings.ResetMinutes = 250;   // not a multiple of 30
        settings.WaitSeconds = 10_000;
        settings.OpenHoursStartMinutes = -5;
        settings.OpenHoursEndMinutes = 99_999;

        var repairs = SettingsRules.Sanitize(settings);

        Assert.Equal(240, settings.ResetMinutes);
        Assert.Equal(AppSettings.MaxWaitSeconds, settings.WaitSeconds);
        Assert.Equal(0, settings.OpenHoursStartMinutes);
        Assert.Equal(24 * 60 - 1, settings.OpenHoursEndMinutes);
        Assert.Equal(AppGroup.MaxGraceSeconds, group.GraceSeconds);
        Assert.Null(group.PausedUntil);
        Assert.Contains(repairs, r => r.Contains("reset time"));
    }

    [Fact]
    public void Cloning_and_comparing_groups_includes_grace_and_pause()
    {
        var original = T.Group("Games", new[] { "game" }, CellState.Free);
        original.GraceSeconds = 30;
        original.IsPaused = true;
        original.PausedUntil = T.At(8);

        var copy = original.Clone();
        Assert.True(copy.IsSameAs(original));
        Assert.Equal(30, copy.GraceSeconds);
        Assert.True(copy.IsPaused);
        Assert.Equal(T.At(8), copy.PausedUntil);

        copy.GraceSeconds = 31;
        Assert.False(copy.IsSameAs(original));
        copy.GraceSeconds = 30;
        copy.IsPaused = false;
        Assert.False(copy.IsSameAs(original));

        var target = T.Group("Other", new[] { "x" }, CellState.Blocked);
        target.CopyFrom(original);
        Assert.Equal(30, target.GraceSeconds);
        Assert.True(target.IsPaused);
        Assert.Equal(T.At(8), target.PausedUntil);
    }

    [Fact]
    public void Importing_replaces_the_groups_and_general_settings_but_keeps_start_with_windows()
    {
        var current = new AppSettings { StartWithWindows = true, ResetMinutes = 0, WaitSeconds = 0 };
        current.Groups.Add(T.Group("Old", new[] { "old" }, CellState.Free));

        var imported = new AppSettings { StartWithWindows = false, ResetMinutes = 240, WaitSeconds = 90, OpenHoursEnabled = true };
        var newGroup = T.Group("New", new[] { "new" }, CellState.Blocked);
        imported.Groups.Add(newGroup);

        current.ReplaceWith(imported);

        Assert.Equal("New", Assert.Single(current.Groups).Name);
        Assert.NotSame(newGroup, current.Groups[0]);   // a copy, so editing one never changes the other
        Assert.Equal(240, current.ResetMinutes);
        Assert.Equal(90, current.WaitSeconds);
        Assert.True(current.OpenHoursEnabled);
        Assert.True(current.StartWithWindows);
    }
}
