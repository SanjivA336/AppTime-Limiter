using System.ComponentModel;
using System.Diagnostics;
using HardCap.Core.Enforcement;

namespace HardCap.Core.Processes;

/// <summary>Lists and closes real Windows processes by name.</summary>
public sealed class WindowsProcessController : IProcessController
{
    /// <summary>How long an app gets to close itself politely before it is forced to.</summary>
    public static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(3);

    public IReadOnlyCollection<string> GetRunningProcessNames()
    {
        var names = new HashSet<string>(ProcessNames.Comparer);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                names.Add(process.ProcessName);
            }
        }

        return names;
    }

    public async Task<IReadOnlyList<string>> CloseAsync(
        IReadOnlyCollection<string> processNames, CancellationToken cancellationToken = default)
    {
        var namesThatWouldNotClose = new List<string>();
        var thisAppsOwnId = Environment.ProcessId;

        foreach (var name in processNames)
        {
            // A second safety check: even if a core Windows name somehow got into the settings, never touch it.
            if (ProcessNames.IsProtected(name))
            {
                continue;
            }

            // One name can match several running copies (think of browser windows), so close every one.
            var anyCopyStillRunning = false;
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    if (process.Id == thisAppsOwnId)
                    {
                        continue;
                    }

                    var closed = await TryCloseAsync(process, cancellationToken);
                    anyCopyStillRunning |= !closed;
                }
            }

            if (anyCopyStillRunning)
            {
                namesThatWouldNotClose.Add(name);
            }
        }

        return namesThatWouldNotClose;
    }

    /// <summary>Closes one process: asks politely first, then forces it. Returns true once it is gone.</summary>
    private static async Task<bool> TryCloseAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            if (process.HasExited)
            {
                return true;
            }

            // Step 1: ask nicely. This is like clicking the window's X, so games and editors can save and
            // shut down properly. It does nothing for apps that have no window.
            process.CloseMainWindow();
            if (await ExitedWithinGracePeriodAsync(process, cancellationToken))
            {
                return true;
            }

            // Step 2: it ignored us, so force it to stop.
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(cancellationToken).WaitAsync(GracePeriod, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or TimeoutException or NotSupportedException)
        {
            // Win32Exception means "access denied", usually because the app runs as administrator and we don't.
            // InvalidOperationException means it exited by itself in the meantime, which is what we wanted.
            return ex is InvalidOperationException;
        }
    }

    private static async Task<bool> ExitedWithinGracePeriodAsync(Process process, CancellationToken cancellationToken)
    {
        using var gracePeriodTimer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        gracePeriodTimer.CancelAfter(GracePeriod);

        try
        {
            await process.WaitForExitAsync(gracePeriodTimer.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false; // The grace period ran out. (The caller didn't cancel, our own timer did.)
        }
    }
}
