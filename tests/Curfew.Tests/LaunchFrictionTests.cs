using Curfew.Core.Enforcement;
using Curfew.Core.Models;
using Curfew.Core.Settings;
using Curfew.Core.Usage;

namespace Curfew.Tests;

/// <summary>
/// Launch friction: a newly started app of a group is frozen behind a countdown, never closed and relaunched, and it ends the
/// moment it stops making sense (a pause, a block, the app closing).
/// </summary>
public sealed class LaunchFrictionTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 10, 26);
    private static readonly string[] None = Array.Empty<string>();
    private static readonly string[] Game = { "game" };
    private static readonly string[] BothApps = { "game", "launcher" };

    private readonly string _folder = T.TempFolder();
    private readonly UsageLedger _ledger = new();
    private readonly EnforcementEngine _engine;
    private DateTime _now = T.At(12);

    public LaunchFrictionTests()
    {
        _engine = new EnforcementEngine(_ledger);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static (AppGroup Group, AppSettings Settings) FrictionGroup(
        CellState state = CellState.Free, int seconds = 15, int budgetMinutes = 60)
    {
        var group = T.Group("Games", new[] { "game", "launcher" }, state);
        group.LaunchFrictionSeconds = seconds;
        return (group, T.Settings(budgetMinutes, group));
    }

    /// <summary>One check, 30 seconds after the last, with these apps running.</summary>
    private TickResult Tick(AppSettings settings, string[] running)
    {
        _now = _now.AddSeconds(30);
        return _engine.Tick(settings, _now, running);
    }

    // ---- Which apps are held ----

    [Fact]
    public void A_newly_started_app_is_held_for_the_chosen_time()
    {
        var (group, settings) = FrictionGroup(seconds: 15);
        _engine.Tick(settings, _now, None);

        var hold = Assert.Single(Tick(settings, Game).NewHolds!);

        Assert.Equal(group.Id, hold.GroupId);
        Assert.Equal(Game, hold.Processes);
        Assert.Equal(15, hold.Seconds);
    }

    [Fact]
    public void Nothing_is_held_when_launch_friction_is_off()
    {
        var (_, settings) = FrictionGroup(seconds: 0);
        _engine.Tick(settings, _now, None);

        Assert.Empty(Tick(settings, Game).NewHolds!);
    }

    [Fact]
    public void Apps_already_running_when_curfew_starts_are_not_held()
    {
        var (_, settings) = FrictionGroup();

        Assert.Empty(_engine.Tick(settings, _now, Game).NewHolds!);
        Assert.Empty(Tick(settings, Game).NewHolds!);
    }

    [Fact]
    public void An_app_that_keeps_running_is_not_held_again()
    {
        var (group, settings) = FrictionGroup();
        _engine.Tick(settings, _now, None);
        Tick(settings, Game);
        _engine.ReleaseHold(group.Id);

        Assert.Empty(Tick(settings, Game).NewHolds!);
        Assert.Empty(Tick(settings, Game).NewHolds!);
    }

    [Fact]
    public void A_second_app_starting_later_is_held_on_its_own()
    {
        var (group, settings) = FrictionGroup();
        _engine.Tick(settings, _now, None);
        Tick(settings, Game);
        _engine.ReleaseHold(group.Id);

        var later = Assert.Single(Tick(settings, BothApps).NewHolds!);

        Assert.Equal(new[] { "launcher" }, later.Processes);
    }

    [Fact]
    public void A_second_app_starting_during_a_hold_joins_it()
    {
        var (_, settings) = FrictionGroup();
        _engine.Tick(settings, _now, None);
        Tick(settings, Game); // held, not released

        var joined = Assert.Single(Tick(settings, BothApps).NewHolds!);

        Assert.Equal(new[] { "launcher" }, joined.Processes);
    }

    [Fact]
    public void An_app_that_is_closed_and_opened_again_is_held_again()
    {
        var (group, settings) = FrictionGroup();
        _engine.Tick(settings, _now, None);
        Tick(settings, Game);
        _engine.ReleaseHold(group.Id);
        Tick(settings, None);

        Assert.Single(Tick(settings, Game).NewHolds!);
    }

    [Fact]
    public void A_paused_group_is_not_held()
    {
        var (group, settings) = FrictionGroup();
        group.IsPaused = true;
        _engine.Tick(settings, _now, None);

        Assert.Empty(Tick(settings, Game).NewHolds!);
    }

    [Fact]
    public void An_app_started_in_a_blocked_time_is_closed_not_held()
    {
        var (_, settings) = FrictionGroup(CellState.Blocked);
        _engine.Tick(settings, _now, None);

        var result = Tick(settings, Game);

        Assert.Empty(result.NewHolds!);
        Assert.Single(result.Closes);
    }

    [Fact]
    public void An_app_started_with_no_budget_left_is_closed_not_held()
    {
        var (group, settings) = FrictionGroup(CellState.Limited, budgetMinutes: 1);
        _ledger.Add(Day, group.Id, 60, countsTowardBudget: true);
        _engine.Tick(settings, _now, None);

        var result = Tick(settings, Game);

        Assert.Empty(result.NewHolds!);
        Assert.Equal(CloseReason.BudgetUsedUp, Assert.Single(result.Closes).Reason);
    }

    // ---- While held ----

    [Fact]
    public void While_held_nothing_is_counted_or_closed()
    {
        var (group, settings) = FrictionGroup(CellState.Limited, budgetMinutes: 1);
        _engine.Tick(settings, _now, None);
        Tick(settings, Game); // the hold starts

        for (var i = 0; i < 6; i++)
        {
            var result = Tick(settings, Game); // three minutes frozen, over the one-minute budget

            Assert.Empty(result.Closes);
            Assert.Empty(result.Alerts);
        }

        Assert.Equal(0, _ledger.GetBudgetSecondsUsed(Day, group.Id));
    }

    [Fact]
    public void After_the_hold_ends_counting_carries_on_and_the_app_is_not_held_again()
    {
        var (group, settings) = FrictionGroup(CellState.Limited);
        _engine.Tick(settings, _now, None);
        Tick(settings, Game);
        _engine.ReleaseHold(group.Id);

        var after = Tick(settings, Game);

        Assert.Empty(after.NewHolds!);
        Assert.Equal(30, _ledger.GetBudgetSecondsUsed(Day, group.Id));
    }

    // ---- When a hold ends by itself ----

    [Fact]
    public void A_pause_starting_during_a_hold_ends_it()
    {
        var (group, settings) = FrictionGroup();
        _engine.Tick(settings, _now, None);
        Tick(settings, Game);
        group.IsPaused = true;

        var result = Tick(settings, Game);

        Assert.Equal(group.Id, Assert.Single(result.EndedHolds!));
    }

    [Fact]
    public void The_apps_closing_ends_the_hold()
    {
        var (group, settings) = FrictionGroup();
        _engine.Tick(settings, _now, None);
        Tick(settings, Game);

        Assert.Equal(group.Id, Assert.Single(Tick(settings, None).EndedHolds!));
    }

    [Fact]
    public void Switching_the_friction_off_ends_the_hold()
    {
        var (group, settings) = FrictionGroup();
        _engine.Tick(settings, _now, None);
        Tick(settings, Game);
        group.LaunchFrictionSeconds = 0;

        Assert.Equal(group.Id, Assert.Single(Tick(settings, Game).EndedHolds!));
    }

    [Fact]
    public void A_blocked_time_beginning_ends_the_hold_and_closes_the_apps()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Free);
        group.LaunchFrictionSeconds = 15;
        T.SetRange(group.Schedule, DayOfWeek.Monday, 12, 24, CellState.Blocked);
        var settings = T.Settings(60, group);
        T.SetRange(group.Schedule, DayOfWeek.Monday, 12, 24, CellState.Free);
        _engine.Tick(settings, _now, None);
        Tick(settings, Game);
        T.SetRange(group.Schedule, DayOfWeek.Monday, 12, 24, CellState.Blocked);

        var result = Tick(settings, Game);

        Assert.Equal(group.Id, Assert.Single(result.EndedHolds!));
        Assert.Single(result.Closes);
    }

    // ---- The runner: freezing, unfreezing, and never relaunching ----

    private sealed class Rig
    {
        public Rig(AppGroup group, AppSettings settings, UsageLedger ledger)
        {
            Group = group;
            Settings = settings;
            Runner = new EnforcementRunner(() => settings, Processes, Notifier, ledger);
            Runner.HoldStarted += request => Started.Add(request);
            Runner.HoldEnded += id => Ended.Add(id);
        }

        public AppGroup Group { get; }

        public AppSettings Settings { get; }

        public FakeProcessController Processes { get; } = new();

        public FakeNotifier Notifier { get; } = new();

        public EnforcementRunner Runner { get; }

        public List<HoldRequest> Started { get; } = new();

        public List<Guid> Ended { get; } = new();
    }

    private Rig NewRig(int seconds = 15)
    {
        var (group, settings) = FrictionGroup(CellState.Limited, seconds);
        return new Rig(group, settings, _ledger);
    }

    /// <summary>The runner has seen the group with nothing running, then one app starts.</summary>
    private async Task<Rig> StartedApp(int seconds = 15, params string[] apps)
    {
        var rig = NewRig(seconds);
        await rig.Runner.RunOnceAsync(T.At(12));
        foreach (var app in apps.Length == 0 ? Game : apps)
        {
            rig.Processes.Running.Add(app);
        }

        await rig.Runner.RunOnceAsync(T.At(12, 0, 1));
        return rig;
    }

    [Fact]
    public async Task A_new_app_is_frozen_and_the_countdown_is_announced()
    {
        var rig = await StartedApp();

        Assert.Contains("game", rig.Processes.Frozen);
        var request = Assert.Single(rig.Started);
        Assert.Equal(Game, request.Processes);
        Assert.Equal(15, request.Seconds);
        Assert.Empty(rig.Processes.CloseCalls);
    }

    [Fact]
    public async Task An_app_that_cannot_be_frozen_is_left_alone_and_the_person_is_told_once()
    {
        var rig = NewRig();
        rig.Processes.CannotFreeze.Add("game");
        await rig.Runner.RunOnceAsync(T.At(12));
        rig.Processes.Running.Add("game");
        await rig.Runner.RunOnceAsync(T.At(12, 0, 1));

        Assert.Empty(rig.Started);
        Assert.Empty(rig.Processes.Frozen);
        Assert.Empty(rig.Processes.CloseCalls);          // never closed as a fallback
        Assert.Contains("game", rig.Processes.Running);
        Assert.Contains("Couldn't hold game", Assert.Single(rig.Notifier.Messages));

        rig.Processes.Running.Remove("game");
        await rig.Runner.RunOnceAsync(T.At(12, 0, 2));
        rig.Processes.Running.Add("game");
        await rig.Runner.RunOnceAsync(T.At(12, 0, 3));   // opened again

        Assert.Single(rig.Notifier.Messages);             // not told a second time
    }

    [Fact]
    public async Task An_app_that_cannot_be_frozen_is_counted_normally_afterwards()
    {
        var rig = NewRig();
        rig.Processes.CannotFreeze.Add("game");
        await rig.Runner.RunOnceAsync(T.At(12));
        rig.Processes.Running.Add("game");
        await rig.Runner.RunOnceAsync(T.At(12, 0, 1));

        await rig.Runner.RunOnceAsync(T.At(12, 0, 31));

        Assert.Equal(30, _ledger.GetBudgetSecondsUsed(Day, rig.Group.Id));
    }

    [Fact]
    public async Task Only_the_apps_that_can_be_frozen_are_held()
    {
        var rig = NewRig();
        rig.Processes.CannotFreeze.Add("launcher");
        await rig.Runner.RunOnceAsync(T.At(12));
        rig.Processes.Running.Add("game");
        rig.Processes.Running.Add("launcher");
        await rig.Runner.RunOnceAsync(T.At(12, 0, 1));

        Assert.Equal(new[] { "game" }, Assert.Single(rig.Started).Processes);
        Assert.Contains("game", rig.Processes.Frozen);
        Assert.DoesNotContain("launcher", rig.Processes.Frozen);
        Assert.Contains("launcher", Assert.Single(rig.Notifier.Messages));
    }

    [Fact]
    public async Task Completing_the_countdown_unfreezes_the_app_and_counting_resumes()
    {
        var rig = await StartedApp();

        rig.Runner.CompleteHold(rig.Group.Id);

        Assert.Empty(rig.Processes.Frozen);
        Assert.Equal(rig.Group.Id, Assert.Single(rig.Ended));
        await rig.Runner.RunOnceAsync(T.At(12, 0, 31));
        Assert.Equal(30, _ledger.GetBudgetSecondsUsed(Day, rig.Group.Id));
        Assert.Single(rig.Started); // not held a second time
    }

    [Fact]
    public async Task Cancelling_unfreezes_and_closes_the_app_and_never_relaunches_it()
    {
        var rig = await StartedApp();

        await rig.Runner.CancelHoldAsync(rig.Group.Id);

        Assert.Empty(rig.Processes.Frozen);
        Assert.Equal(new[] { "game" }, Assert.Single(rig.Processes.CloseCalls));
        Assert.DoesNotContain("game", rig.Processes.Running);
        await rig.Runner.RunOnceAsync(T.At(12, 0, 5));
        await rig.Runner.RunOnceAsync(T.At(12, 0, 35));
        Assert.Single(rig.Started);                          // nothing started it again, so nothing was held again
        Assert.Single(rig.Processes.CloseCalls);
    }

    [Fact]
    public async Task A_pause_during_the_hold_unfreezes_the_app_at_once_without_closing_it()
    {
        var rig = await StartedApp();

        rig.Group.IsPaused = true;
        await rig.Runner.RunOnceAsync(T.At(12, 0, 4));

        Assert.Empty(rig.Processes.Frozen);
        Assert.Equal(rig.Group.Id, Assert.Single(rig.Ended));
        Assert.Empty(rig.Processes.CloseCalls);
        Assert.Contains("game", rig.Processes.Running);
    }

    [Fact]
    public async Task A_hold_that_goes_on_too_long_ends_by_unfreezing_and_closing_the_app()
    {
        var rig = await StartedApp(seconds: 15);
        var started = T.At(12, 0, 1);

        await rig.Runner.RunOnceAsync(started.AddMinutes(4));
        Assert.Contains("game", rig.Processes.Frozen);   // the countdown only runs while its window is in front, so a hold can last
        Assert.Empty(rig.Processes.CloseCalls);

        await rig.Runner.RunOnceAsync(started.AddMinutes(5).AddSeconds(1));

        Assert.Empty(rig.Processes.Frozen);              // nothing stays frozen for good
        Assert.Equal(new[] { "game" }, Assert.Single(rig.Processes.CloseCalls)); // and waiting it out is not a free pass
        Assert.Equal(rig.Group.Id, Assert.Single(rig.Ended));
    }

    [Fact]
    public async Task A_hold_does_not_end_just_because_the_countdown_time_has_passed()
    {
        var rig = await StartedApp(seconds: 15);

        await rig.Runner.RunOnceAsync(T.At(12, 0, 1).AddSeconds(15 + 30));

        Assert.Contains("game", rig.Processes.Frozen);   // the countdown may be paused; only choosing to open (or the limit) ends it
        Assert.Empty(rig.Ended);
    }

    [Fact]
    public async Task Releasing_all_holds_unfreezes_everything_when_curfew_quits()
    {
        var rig = await StartedApp();

        rig.Runner.ReleaseAllHolds();

        Assert.Empty(rig.Processes.Frozen);
        Assert.Equal(1, rig.Processes.ResumeAllCalls);
        Assert.Equal(rig.Group.Id, Assert.Single(rig.Ended));
    }

    [Fact]
    public async Task Apps_already_open_when_the_runner_first_looks_are_not_held()
    {
        var rig = NewRig();
        rig.Processes.Running.Add("game");

        await rig.Runner.RunOnceAsync(T.At(12));
        await rig.Runner.RunOnceAsync(T.At(12, 0, 1));

        Assert.Empty(rig.Started);
        Assert.Empty(rig.Processes.Frozen);
    }

    // ---- The setting ----

    [Fact]
    public void Launch_friction_is_off_by_default()
    {
        Assert.Equal(0, new AppGroup().LaunchFrictionSeconds);
    }

    [Fact]
    public void Launch_friction_survives_saving_loading_and_copying()
    {
        var (group, settings) = FrictionGroup(seconds: 25);
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));

        store.Save(settings);
        var back = Assert.Single(store.Load().Settings.Groups);

        Assert.Equal(25, back.LaunchFrictionSeconds);
        Assert.Equal(25, group.Clone().LaunchFrictionSeconds);
        var different = group.Clone();
        different.LaunchFrictionSeconds = 30;
        Assert.False(group.IsSameAs(different));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(45, 45)]
    [InlineData(500, 60)]
    public void Launch_friction_is_kept_between_0_and_60_seconds(int written, int expected)
    {
        var (_, settings) = FrictionGroup(seconds: written);
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        store.Save(settings);

        Assert.Equal(expected, Assert.Single(store.Load().Settings.Groups).LaunchFrictionSeconds);
    }

    [Fact]
    public void An_older_file_without_launch_friction_loads_with_it_off()
    {
        var (_, settings) = FrictionGroup(seconds: 20);
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        store.Save(settings);
        File.WriteAllText(store.Path, File.ReadAllText(store.Path).Replace("\"launchFrictionSeconds\": 20,", string.Empty));

        Assert.DoesNotContain("launchFrictionSeconds", File.ReadAllText(store.Path));
        Assert.Equal(0, Assert.Single(store.Load().Settings.Groups).LaunchFrictionSeconds);
    }
}
