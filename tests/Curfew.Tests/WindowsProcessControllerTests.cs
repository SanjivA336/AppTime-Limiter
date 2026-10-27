using System.Diagnostics;
using Curfew.Core.Processes;

namespace Curfew.Tests;

/// <summary>
/// Runs against real Windows processes. Each test launches a copy of ping.exe renamed to a unique
/// name, so there is no way for it to close anything the user is actually running.
/// </summary>
public sealed class WindowsProcessControllerTests : IDisposable
{
    private readonly string _folder = T.TempFolder();
    private readonly List<Process> _started = new();
    private readonly WindowsProcessController _controller = new();

    public void Dispose()
    {
        _controller.ResumeAll(); // never leave a test process frozen
        foreach (var process in _started)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(2000);
                }
            }
            catch (InvalidOperationException)
            {
            }

            process.Dispose();
        }

        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // The exe can stay locked for a moment after it exits; temp files are harmless.
        }
    }

    private string StartLongRunningProcess()
    {
        var name = "atl-test-" + Guid.NewGuid().ToString("N")[..8];
        var exe = Path.Combine(_folder, name + ".exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), exe);

        var process = Process.Start(new ProcessStartInfo(exe, "-n 120 127.0.0.1")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        _started.Add(process);
        return name;
    }

    // ---- Freezing (launch friction) ----

    private static bool IsFrozen(string name)
    {
        var processes = Process.GetProcessesByName(name);
        try
        {
            return processes.Length > 0 && processes.All(p =>
                p.Threads.Cast<ProcessThread>().All(t => t.ThreadState == System.Diagnostics.ThreadState.Wait && t.WaitReason == ThreadWaitReason.Suspended));
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static bool WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 40; i++)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return condition();
    }

    [Fact]
    public void A_running_process_can_be_frozen_and_then_unfrozen()
    {
        var name = StartLongRunningProcess();
        Assert.True(WaitUntil(() => _controller.GetRunningProcessNames().Contains(name)));

        var couldNot = _controller.Suspend(new[] { name });

        Assert.Empty(couldNot);
        Assert.True(WaitUntil(() => IsFrozen(name)), "the process should be frozen");

        _controller.Resume(new[] { name });

        Assert.True(WaitUntil(() => !IsFrozen(name)), "the process should be running again");
    }

    [Fact]
    public void Resuming_everything_unfreezes_what_was_frozen()
    {
        var name = StartLongRunningProcess();
        Assert.True(WaitUntil(() => _controller.GetRunningProcessNames().Contains(name)));
        _controller.Suspend(new[] { name });
        Assert.True(WaitUntil(() => IsFrozen(name)));

        _controller.ResumeAll();

        Assert.True(WaitUntil(() => !IsFrozen(name)));
    }

    [Fact]
    public void Core_windows_processes_are_never_frozen()
    {
        var couldNot = _controller.Suspend(new[] { "explorer" });

        Assert.Equal(new[] { "explorer" }, couldNot);
        Assert.False(IsFrozen("explorer"));
    }

    [Fact]
    public void A_name_that_is_not_running_cannot_be_frozen()
    {
        var couldNot = _controller.Suspend(new[] { "atl-test-nothing-runs-with-this-name" });

        Assert.Equal(new[] { "atl-test-nothing-runs-with-this-name" }, couldNot);
    }

    [Fact]
    public void Asking_for_the_front_window_works_without_special_rights()
    {
        // Whatever is in front on the machine running the tests (or nothing, on a locked or headless one), it must not throw.
        var name = _controller.GetForegroundProcessName();

        Assert.True(name is null or { Length: > 0 });
    }

    [Fact]
    public void Running_process_names_include_a_process_we_started()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var name = StartLongRunningProcess();

        Assert.Contains(name, _controller.GetRunningProcessNames(), ProcessNames.Comparer);
    }

    [Fact]
    public async Task CloseAsync_ends_the_process_and_reports_no_failures()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var name = StartLongRunningProcess();

        var failed = await _controller.CloseAsync(new[] { name });

        Assert.Empty(failed);
        Assert.DoesNotContain(name, _controller.GetRunningProcessNames(), ProcessNames.Comparer);
    }

    [Fact]
    public async Task CloseAsync_ignores_names_that_are_not_running()
    {
        var failed = await _controller.CloseAsync(new[] { "atl-no-such-process-" + Guid.NewGuid().ToString("N") });

        Assert.Empty(failed);
    }

    [Fact]
    public async Task CloseAsync_refuses_protected_names_even_if_asked()
    {
        var before = _controller.GetRunningProcessNames().Contains("explorer", ProcessNames.Comparer);

        var failed = await _controller.CloseAsync(new[] { "explorer" });

        Assert.Empty(failed);
        Assert.Equal(before, _controller.GetRunningProcessNames().Contains("explorer", ProcessNames.Comparer));
    }

    [Theory]
    [InlineData("Warframe.x64.exe", "Warframe.x64")]
    [InlineData("  helldivers2.EXE ", "helldivers2")]
    [InlineData("notepad", "notepad")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_strips_exe_and_whitespace(string? input, string expected)
    {
        Assert.Equal(expected, ProcessNames.Normalize(input));
    }
}
