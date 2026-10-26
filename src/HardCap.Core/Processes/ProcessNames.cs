namespace HardCap.Core.Processes;

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
        "hardcap", "apptimelimiter", // this app (and the name it had before it was renamed)
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

    /// <summary>Names for a sentence: "Warframe", "Warframe and Discord", "A, B and C", or "A, B and 2 more" past three.</summary>
    public static string Join(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "the app",
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        3 => $"{names[0]}, {names[1]} and {names[2]}",
        _ => $"{names[0]}, {names[1]} and {names.Count - 2} more",
    };

    /// <summary>
    /// Anti-cheat programs that run in the background the whole time the computer is on, not only while the game is open.
    /// (Riot Vanguard, Easy Anti-Cheat, BattlEye.)
    /// </summary>
    private static readonly HashSet<string> KnownAlwaysRunning = new(Comparer)
    {
        "vgc", "vgtray", "easyanticheat", "easyanticheat_eos", "beservice", "bedaisy",
    };

    /// <summary>Words that usually mean "a helper that sits in the background" rather than the app you play or use.</summary>
    private static readonly string[] HelperWords = { "anticheat", "anti-cheat", "updater", "crashhandler", "crashpad", "helper", "service" };

    /// <summary>
    /// A warning for programs that probably run in the background all day, or null for ordinary apps. Adding one to a
    /// group would use up its budget even when you aren't playing, so the app picker shows this beside the name.
    /// </summary>
    public static string? BackgroundWarning(string name)
    {
        var normalized = Normalize(name);

        if (KnownAlwaysRunning.Contains(normalized))
        {
            return "Anti-cheat software that runs all the time, so it would use up your budget even when you aren't playing.";
        }

        var looksLikeHelper = HelperWords.Any(word => normalized.Contains(word, StringComparison.OrdinalIgnoreCase));
        return looksLikeHelper
            ? "Looks like a background helper or updater that may run all day and use up your budget."
            : null;
    }
}
