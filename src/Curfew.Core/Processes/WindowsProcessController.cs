using System.ComponentModel;
using System.Diagnostics;
using Curfew.Core.Enforcement;

namespace Curfew.Core.Processes;

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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    /// <summary>
    /// The process that owns the window being used right now. Needs no special rights, even for programs running as
    /// administrator. Null when nothing is in front (the lock screen, a Windows security prompt) or it can't be read.
    /// </summary>
    public string? GetForegroundProcessName()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out var processId) == 0 || processId == 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null; // it closed between the two calls
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [System.Runtime.InteropServices.DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [System.Runtime.InteropServices.DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr processHandle);

    /// <summary>The only right needed to freeze or unfreeze a process.</summary>
    private const uint ProcessSuspendResume = 0x0800;

    // The processes this controller froze (id and name), so exactly those are unfrozen again.
    private readonly Dictionary<int, string> _frozen = new();

    /// <summary>
    /// Freezes every running process with one of these names, a whole name at a time. A name counts as frozen if at least one process
    /// of that name was. Core Windows processes and Curfew itself are never touched. A process that Windows refuses to open
    /// (a protected game, a program running as administrator) is simply left running.
    /// </summary>
    public IReadOnlyList<string> Suspend(IReadOnlyCollection<string> processNames)
    {
        var couldNot = new List<string>();
        foreach (var name in processNames)
        {
            if (ProcessNames.IsProtected(name))
            {
                couldNot.Add(name);
                continue;
            }

            var frozeAny = false;
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    if (process.Id == Environment.ProcessId)
                    {
                        continue;
                    }

                    var handle = OpenProcess(ProcessSuspendResume, false, process.Id);
                    if (handle == IntPtr.Zero)
                    {
                        continue; // refused
                    }

                    try
                    {
                        if (NtSuspendProcess(handle) >= 0)
                        {
                            _frozen[process.Id] = name;
                            frozeAny = true;
                        }
                    }
                    finally
                    {
                        CloseHandle(handle);
                    }
                }
            }

            if (!frozeAny)
            {
                couldNot.Add(name);
            }
        }

        return couldNot;
    }

    public void Resume(IReadOnlyCollection<string> processNames)
    {
        var names = new HashSet<string>(processNames, ProcessNames.Comparer);
        foreach (var (processId, name) in _frozen.ToList())
        {
            if (names.Contains(name))
            {
                ResumeOne(processId);
            }
        }
    }

    public void ResumeAll()
    {
        foreach (var processId in _frozen.Keys.ToList())
        {
            ResumeOne(processId);
        }
    }

    private void ResumeOne(int processId)
    {
        _frozen.Remove(processId);
        var handle = OpenProcess(ProcessSuspendResume, false, processId);
        if (handle == IntPtr.Zero)
        {
            return; // it has already closed
        }

        try
        {
            NtResumeProcess(handle);
        }
        finally
        {
            CloseHandle(handle);
        }
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
