using System.Diagnostics;
using HardCap.Core.Processes;

namespace HardCap.Tests;

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
