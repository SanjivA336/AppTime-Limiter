using System.IO;
using Curfew.Core.Processes;
using Microsoft.Win32;

namespace Curfew.App.Services;

/// <summary>An app found on disk: the process name it would run under, and where its program file is.</summary>
public sealed record InstalledApp(string ProcessName, string Path);

/// <summary>
/// Finds programs that are installed but not necessarily running, so they can be searched for in the app picker.
/// Two places are looked at: the Start Menu shortcuts (each one points at a program file) and the "App Paths" entries
/// in the registry that Windows keeps for many installed programs. A game that is in neither (a lot of Steam and Epic
/// games are) can still be added with the picker's "Browse" button.
/// A process's name is just its program file's name without ".exe", which is why a file is enough to describe it.
/// </summary>
public static class InstalledAppFinder
{
    private const string AppPathsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

    /// <summary>Looks on a background thread (reading hundreds of shortcuts takes a moment).</summary>
    public static Task<IReadOnlyList<InstalledApp>> FindAsync()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<InstalledApp>>();

        // Shortcut files are read through a Windows COM object, which wants a single-threaded apartment.
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(Find());
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not list installed apps", ex);
                completion.SetResult(Array.Empty<InstalledApp>());
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static IReadOnlyList<InstalledApp> Find()
    {
        var found = new Dictionary<string, InstalledApp>(ProcessNames.Comparer);

        foreach (var path in StartMenuTargets().Concat(AppPathsTargets()))
        {
            TryAdd(found, path);
        }

        return found.Values.ToList();
    }

    private static void TryAdd(Dictionary<string, InstalledApp> found, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            return;
        }

        var name = ProcessNames.Normalize(Path.GetFileName(path));
        var isUninstaller = name.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
            || name.Contains("uninstall", StringComparison.OrdinalIgnoreCase);

        if (name.Length > 0 && !isUninstaller && !ProcessNames.IsProtected(name))
        {
            found.TryAdd(name, new InstalledApp(name, path));
        }
    }

    /// <summary>The program each Start Menu shortcut starts (for every user, and for this one).</summary>
    private static IEnumerable<string> StartMenuTargets()
    {
        var folders = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\Start Menu\Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs"),
        };

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            yield break;
        }

        dynamic shell = Activator.CreateInstance(shellType)!;
        foreach (var folder in folders.Where(Directory.Exists))
        {
            foreach (var shortcut in SafeFiles(folder, "*.lnk"))
            {
                string? target = null;
                try
                {
                    target = shell.CreateShortcut(shortcut).TargetPath as string;
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException)
                {
                    // A broken shortcut; skip it.
                }

                if (target is not null)
                {
                    yield return target;
                }
            }
        }
    }

    /// <summary>The programs Windows has registered under "App Paths" (the same places the Run box looks).</summary>
    private static IEnumerable<string> AppPathsTargets()
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var root = hive.OpenSubKey(AppPathsKey);
            if (root is null)
            {
                continue;
            }

            foreach (var name in root.GetSubKeyNames())
            {
                string? path = null;
                try
                {
                    using var key = root.OpenSubKey(name);
                    path = (key?.GetValue(null) as string)?.Trim('"');
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                    // Not readable; skip it.
                }

                if (path is not null)
                {
                    yield return path;
                }
            }
        }
    }

    /// <summary>All files under a folder matching a pattern, skipping any folder that can't be read.</summary>
    private static IEnumerable<string> SafeFiles(string folder, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(folder, pattern, new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
