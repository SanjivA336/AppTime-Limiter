using System.IO;
using Microsoft.Win32;

namespace Curfew.App.Services;

/// <summary>Turns "start when I sign in to Windows" on or off via the per-user Run registry key (no admin needed).</summary>
public sealed class AutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Curfew";

    /// <summary>The names the entry had before the app was called Curfew. Each points at an older executable.</summary>
    private static readonly string[] OldValueNames = { "HardCap", "AppTimeLimiter" };

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    /// <summary>
    /// Removes the start-with-Windows entry left by the app's old name. Without this, Windows would keep launching the
    /// old program at sign-in next to the new one, and the two would each count usage.
    /// Startup then registers the new entry as usual if the setting is on.
    /// </summary>
    public void RemoveOldEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            foreach (var name in OldValueNames)
            {
                if (key?.GetValue(name) is not null)
                {
                    key.DeleteValue(name, throwOnMissingValue: false);
                    AppLog.Info($"Removed the start-with-Windows entry from the app's old name ({name})");
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            AppLog.Error("Could not remove the old start-with-Windows entry", ex);
        }
    }

    /// <summary>Returns an error message if the registry couldn't be updated, else null.</summary>
    public string? SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (exe is null)
                {
                    return "Couldn't work out where the app is installed.";
                }

                // --background starts it hidden in the tray, so sign-in isn't interrupted by a window.
                key.SetValue(ValueName, $"\"{exe}\" {StartupOptions.BackgroundFlag}");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            AppLog.Error("Could not update the start-with-Windows setting", ex);
            return "Couldn't change the start-with-Windows setting: " + ex.Message;
        }
    }
}
