using System.IO;
using Microsoft.Win32;

namespace AppTimeLimiter.App.Services;

/// <summary>Turns "start when I sign in to Windows" on or off via the per-user Run registry key (no admin needed).</summary>
public sealed class AutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AppTimeLimiter";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
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
