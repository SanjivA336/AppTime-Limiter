using HardCap.Core.Enforcement;
using HardCap.Core.Models;
using HardCap.Core.Processes;
using HardCap.Core.Settings;
using HardCap.Core.Usage;

namespace HardCap.Tests;

/// <summary>When the setting is on and nobody has touched the keyboard or mouse for long enough, time stops counting.</summary>
public sealed class IdleTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 10, 26);
    private static readonly string[] Running = { "game" };

    private readonly string _folder = T.TempFolder();
    private readonly UsageLedger _ledger = new();
    private readonly EnforcementEngine _engine;
    private DateTime _now = T.At(12);

    public IdleTests()
    {
        _engine = new EnforcementEngine(_ledger);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static (AppGroup Group, AppSettings Settings) LimitedGroup(bool idleOn, int idleMinutes = 5, int budgetMinutes = 60)
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Limited);
        var settings = T.Settings(budgetMinutes, group);
        settings.IdleDetectionEnabled = idleOn;
        settings.IdleMinutes = idleMinutes;
        return (group, settings);
    }

    /// <summary>One check 30 seconds after the last, with the given time since the last input (the front window is the game).</summary>
    private TickResult Tick(AppSettings settings, TimeSpan? idleFor)
    {
        _now = _now.AddSeconds(30);
        return _engine.Tick(settings, _now, Running, "game", idleFor);
    }

    // ---- Counting ----

    [Fact]
    public void Nothing_is_counted_while_idle()
    {
        var (group, settings) = LimitedGroup(idleOn: true);
        _engine.Tick(settings, _now, Running, "game", TimeSpan.Zero);

        for (var i = 0; i < 4; i++)
        {
            Tick(settings, TimeSpan.FromMinutes(10));
        }

        var usage = _ledger.GetUsage(Day, group.Id);
        Assert.Equal(0, usage.LimitedSeconds + usage.FreeSeconds);
        Assert.Equal(0, _ledger.GetBudgetSecondsUsed(Day, group.Id));
    }

    [Fact]
    public void Time_counts_normally_below_the_idle_limit()
    {
        var (group, settings) = LimitedGroup(idleOn: true, idleMinutes: 5);
        _engine.Tick(settings, _now, Running, "game", TimeSpan.Zero);

        Tick(settings, TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        Tick(settings, TimeSpan.FromSeconds(10));

        Assert.Equal(60, _ledger.GetBudgetSecondsUsed(Day, group.Id));
    }

    [Fact]
    public void Exactly_the_idle_limit_counts_as_idle()
    {
        var (group, settings) = LimitedGroup(idleOn: true, idleMinutes: 5);
        _engine.Tick(settings, _now, Running, "game", TimeSpan.Zero);

        Tick(settings, TimeSpan.FromMinutes(5));

        Assert.Equal(0, _ledger.GetBudgetSecondsUsed(Day, group.Id));
    }

    [Fact]
    public void With_the_setting_off_idle_time_is_ignored_and_everything_counts()
    {
        var (group, settings) = LimitedGroup(idleOn: false);
        _engine.Tick(settings, _now, Running, "game", TimeSpan.FromHours(3));

        Tick(settings, TimeSpan.FromHours(3));
        Tick(settings, TimeSpan.FromHours(3));

        Assert.Equal(60, _ledger.GetBudgetSecondsUsed(Day, group.Id));
    }

    [Fact]
    public void A_probe_that_cannot_tell_counts_as_not_idle()
    {
        var (group, settings) = LimitedGroup(idleOn: true);
        _engine.Tick(settings, _now, Running, "game", null);

        Tick(settings, null);

        Assert.Equal(30, _ledger.GetBudgetSecondsUsed(Day, group.Id));
    }

    [Fact]
    public void Coming_back_counts_only_from_the_last_check_not_the_whole_time_away()
    {
        var (group, settings) = LimitedGroup(idleOn: true);
        _engine.Tick(settings, _now, Running, "game", TimeSpan.Zero);
        for (var i = 0; i < 20; i++)
        {
            Tick(settings, TimeSpan.FromMinutes(30)); // ten minutes away
        }

        Tick(settings, TimeSpan.Zero);                 // back

        Assert.Equal(30, _ledger.GetBudgetSecondsUsed(Day, group.Id));
    }

    [Fact]
    public void Both_the_running_and_in_front_records_skip_idle_time()
    {
        var (group, settings) = LimitedGroup(idleOn: true);
        _engine.Tick(settings, _now, Running, "game", TimeSpan.Zero);
        Tick(settings, TimeSpan.Zero);                          // 30 seconds of use
        Tick(settings, TimeSpan.FromMinutes(20));               // 30 seconds idle

        var usage = _ledger.GetUsage(Day, group.Id);
        Assert.Equal(30, usage.LimitedSeconds);
        Assert.Equal(30, usage.Front!.LimitedSeconds);
        Assert.Equal(30, usage.Hourly![12]);
    }

    // ---- Limits are still enforced, and the card stops animating ----

    [Fact]
    public void An_app_is_still_closed_in_a_blocked_time_while_idle()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Blocked);
        var settings = T.Settings(60, group);
        settings.IdleDetectionEnabled = true;

        var result = _engine.Tick(settings, _now, Running, "game", TimeSpan.FromHours(1));

        Assert.Single(result.Closes);
    }

    [Fact]
    public void An_app_is_still_closed_when_the_budget_was_already_spent_even_if_idle()
    {
        var (group, settings) = LimitedGroup(idleOn: true, budgetMinutes: 1);
        _ledger.Add(Day, group.Id, 60, countsTowardBudget: true); // the minute is already gone

        var result = _engine.Tick(settings, _now, Running, "game", TimeSpan.FromHours(1));

        Assert.Equal(CloseReason.BudgetUsedUp, Assert.Single(result.Closes).Reason);
    }

    [Fact]
    public void The_budget_is_not_draining_while_idle()
    {
        var (group, settings) = LimitedGroup(idleOn: true);
        _engine.Tick(settings, _now, Running, "game", TimeSpan.Zero);

        Assert.True(Tick(settings, TimeSpan.Zero).Statuses.Single().IsBudgetDraining);
        Assert.False(Tick(settings, TimeSpan.FromMinutes(10)).Statuses.Single().IsBudgetDraining);
    }

    // ---- The runner only asks when the setting is on ----

    private sealed class CountingProbe : IIdleProbe
    {
        public int Calls { get; private set; }

        public TimeSpan? GetIdleTime()
        {
            Calls++;
            return TimeSpan.FromHours(1);
        }
    }

    [Fact]
    public async Task With_the_setting_off_the_computer_is_never_asked()
    {
        var (_, settings) = LimitedGroup(idleOn: false);
        var probe = new CountingProbe();
        var processes = new FakeProcessController();
        processes.Running.Add("game");
        var runner = new EnforcementRunner(() => settings, processes, new FakeNotifier(), _ledger, probe);

        await runner.RunOnceAsync(T.At(12));
        await runner.RunOnceAsync(T.At(12, 0, 30));

        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task With_the_setting_on_the_computer_is_asked_on_each_check()
    {
        var (group, settings) = LimitedGroup(idleOn: true);
        var probe = new CountingProbe();
        var processes = new FakeProcessController();
        processes.Running.Add("game");
        var runner = new EnforcementRunner(() => settings, processes, new FakeNotifier(), _ledger, probe);

        await runner.RunOnceAsync(T.At(12));
        await runner.RunOnceAsync(T.At(12, 0, 30));

        Assert.Equal(2, probe.Calls);
        Assert.Equal(0, _ledger.GetBudgetSecondsUsed(Day, group.Id)); // the fake says an hour idle
    }

    // ---- Settings ----

    [Fact]
    public void The_idle_settings_default_to_off_and_five_minutes()
    {
        var settings = new AppSettings();

        Assert.False(settings.IdleDetectionEnabled);
        Assert.Equal(5, settings.IdleMinutes);
    }

    [Fact]
    public void The_idle_settings_survive_saving_and_loading()
    {
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        store.Save(new AppSettings { IdleDetectionEnabled = true, IdleMinutes = 12 });

        var loaded = store.Load().Settings;

        Assert.True(loaded.IdleDetectionEnabled);
        Assert.Equal(12, loaded.IdleMinutes);
    }

    [Fact]
    public void An_older_file_without_the_idle_settings_loads_with_it_off()
    {
        var path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, "{\"schemaVersion\":5,\"groups\":[]}");

        var loaded = new SettingsStore(path).Load();

        Assert.Null(loaded.Error);
        Assert.False(loaded.Settings.IdleDetectionEnabled);
        Assert.Equal(5, loaded.Settings.IdleMinutes);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    [InlineData(25, 25)]
    [InlineData(999, 60)]
    public void The_idle_minutes_are_kept_between_1_and_60(int written, int expected)
    {
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        store.Save(new AppSettings { IdleMinutes = written });

        Assert.Equal(expected, store.Load().Settings.IdleMinutes);
    }

    [Fact]
    public void Importing_settings_brings_the_idle_settings_too()
    {
        var current = new AppSettings();

        current.ReplaceWith(new AppSettings { IdleDetectionEnabled = true, IdleMinutes = 9 });

        Assert.True(current.IdleDetectionEnabled);
        Assert.Equal(9, current.IdleMinutes);
    }

    // ---- The real Windows call ----

    [Fact]
    public void Asking_windows_how_long_it_has_been_idle_works_without_special_rights()
    {
        var idle = new WindowsIdleProbe().GetIdleTime();

        Assert.True(idle is null || idle.Value >= TimeSpan.Zero);
    }
}
