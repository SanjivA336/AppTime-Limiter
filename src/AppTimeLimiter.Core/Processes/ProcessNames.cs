namespace AppTimeLimiter.Core.Processes;

/// <summary>Helpers for comparing process names the way Windows users expect (no ".exe", any casing).</summary>
public static class ProcessNames
{
    public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Processes that must never be closed: killing them logs the user out, blanks the desktop, or
    /// destabilizes Windows. They are refused when added to a group and stripped from loaded settings.
    /// </summary>
    private static readonly HashSet<string> Protected = new(Comparer)
    {
        "system", "idle", "registry", "memory compression", "secure system",
        "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsm", "svchost",
        "dwm", "explorer", "fontdrvhost", "sihost", "ctfmon", "taskhostw", "userinit",
        "runtimebroker", "searchhost", "searchindexer", "startmenuexperiencehost", "shellexperiencehost",
        "applicationframehost", "textinputhost", "systemsettings", "taskmgr",
        "audiodg", "spoolsv", "dllhost", "conhost", "wmiprvse", "wudfhost",
        "msmpeng", "securityhealthservice", "securityhealthsystray",
        "apptimelimiter",
    };

    /// <summary>"  Warframe.x64.EXE " becomes "Warframe.x64". Returns empty for blank input.</summary>
    public static string Normalize(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        return trimmed.Trim();
    }

    public static bool IsProtected(string name) => Protected.Contains(Normalize(name));
}
