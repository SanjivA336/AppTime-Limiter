using HardCap.Core.Enforcement;
using HardCap.Core.Models;
using HardCap.Core.Usage;

namespace HardCap.Tests;

public class EnforcementRunnerTests
{
    private readonly FakeProcessController _processes = new();
    private readonly FakeNotifier _notifier = new();
    private readonly UsageLedger _ledger = new();

    private EnforcementRunner Runner(AppSettings settings) =>
        new(() => settings, _processes, _notifier, _ledger);

    [Fact]
    public async Task Blocked_process_is_closed_and_the_user_is_told()
    {
        var settings = T.Settings(60, T.Group("Games", new[] { "game" }, CellState.Blocked));
        _processes.Running.Add("game");

        await Runner(settings).RunOnceAsync(T.At(12));

        Assert.Equal(new[] { "game" }, Assert.Single(_processes.CloseCalls));
        Assert.Contains("Games", Assert.Single(_notifier.Messages));
        Assert.DoesNotContain("game", _processes.Running);
    }

    [Fact]
    public async Task Allowed_process_is_left_alone()
    {
        var settings = T.Settings(60, T.Group("Games", new[] { "game" }, CellState.Free));
        _processes.Running.Add("game");

        await Runner(settings).RunOnceAsync(T.At(12));

        Assert.Empty(_processes.CloseCalls);
        Assert.Empty(_notifier.Messages);
    }

    [Fact]
    public async Task Processes_outside_any_group_are_never_touched()
    {
        var settings = T.Settings(60, T.Group("Games", new[] { "game" }, CellState.Blocked));
        _processes.Running.UnionWith(new[] { "game", "notepad", "chrome" });

        await Runner(settings).RunOnceAsync(T.At(12));

        Assert.Equal(new[] { "game" }, Assert.Single(_processes.CloseCalls));
        Assert.Contains("notepad", _processes.Running);
        Assert.Contains("chrome", _processes.Running);
    }

    [Fact]
    public async Task A_process_that_cannot_be_closed_produces_a_second_notification()
    {
        var settings = T.Settings(60, T.Group("Games", new[] { "game" }, CellState.Blocked));
        _processes.Running.Add("game");
        _processes.Unkillable.Add("game");

        await Runner(settings).RunOnceAsync(T.At(12));

        Assert.Equal(2, _notifier.Messages.Count);
        Assert.Contains("Couldn't close game", _notifier.Messages[1]);
    }

    [Fact]
    public async Task Overlapping_passes_are_skipped_while_a_close_is_in_progress()
    {
        var settings = T.Settings(60, T.Group("Games", new[] { "game" }, CellState.Blocked));
        _processes.Running.Add("game");
        _processes.CloseGate = new TaskCompletionSource();
        var runner = Runner(settings);

        var first = runner.RunOnceAsync(T.At(12));
        await runner.RunOnceAsync(T.At(12, 0, 3)); // returns immediately: first pass still closing
        _processes.CloseGate.SetResult();
        await first;

        Assert.Single(_processes.CloseCalls);
    }

    [Fact]
    public async Task Runner_can_run_again_after_a_pass_finishes()
    {
        var settings = T.Settings(60, T.Group("Games", new[] { "game" }, CellState.Blocked));
        var runner = Runner(settings);

        _processes.Running.Add("game");
        await runner.RunOnceAsync(T.At(12));
        _processes.Running.Add("game");
        await runner.RunOnceAsync(T.At(12, 0, 3));

        Assert.Equal(2, _processes.CloseCalls.Count);
    }

    [Fact]
    public async Task Ticked_event_reports_each_pass()
    {
        var settings = T.Settings(60, T.Group("Games", new[] { "game" }, CellState.Free));
        _processes.Running.Add("game");
        TickResult? seen = null;
        var runner = Runner(settings);
        runner.Ticked += result => seen = result;

        await runner.RunOnceAsync(T.At(12));

        Assert.True(Assert.Single(seen!.Statuses).IsRunning);
    }

    [Fact]
    public async Task Usage_is_flushed_to_disk_on_the_save_interval()
    {
        var folder = T.TempFolder();
        try
        {
            var path = Path.Combine(folder, "usage.json");
            var ledger = new UsageLedger(path);
            var settings = T.Settings(60, T.Group("Games", new[] { "game" }, CellState.Free));
            var runner = new EnforcementRunner(() => settings, _processes, _notifier, ledger);
            _processes.Running.Add("game");

            await runner.RunOnceAsync(T.At(12));
            File.Delete(path);
            await runner.RunOnceAsync(T.At(12, 0, 10)); // too soon after the first save
            Assert.False(File.Exists(path));

            await runner.RunOnceAsync(T.At(12, 0, 20));
            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Flush_saves_immediately()
    {
        var folder = T.TempFolder();
        try
        {
            var path = Path.Combine(folder, "usage.json");
            var ledger = new UsageLedger(path);
            ledger.Add(new DateOnly(2026, 10, 26), Guid.NewGuid(), 5, true);
            var runner = new EnforcementRunner(() => T.Settings(60), _processes, _notifier, ledger);

            runner.Flush();

            Assert.True(File.Exists(path));
            await Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
