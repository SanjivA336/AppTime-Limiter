namespace AppTimeLimiter.App.Services;

/// <summary>Command-line switches. The last two exist for development: screenshots and isolated test data.</summary>
public sealed record StartupOptions(
    bool StartHidden,
    string? DataFolder,
    string? ScreenshotPath,
    bool ScreenshotPicker,
    bool ScreenshotEditor,
    bool NoAutostart,
    int Frames,
    string? SimulateRunning,
    bool DemoJump,
    string? SelfTestReportPath,
    int EditorTab)
{
    public const string BackgroundFlag = "--background";
    private const string DataFlag = "--data-dir";
    private const string ScreenshotFlag = "--screenshot";
    private const string PickerFlag = "--picker";
    private const string EditorFlag = "--editor";
    private const string NoAutostartFlag = "--no-autostart";
    private const string FramesFlag = "--frames";
    private const string SimulateRunningFlag = "--simulate-running";
    private const string DemoJumpFlag = "--demo-jump";
    private const string SelfTestFlag = "--self-test";
    private const string EditorTabFlag = "--editor-tab";

    public static StartupOptions Parse(string[] args)
    {
        return new StartupOptions(
            StartHidden: Has(args, BackgroundFlag),
            DataFolder: ValueOf(args, DataFlag),
            ScreenshotPath: ValueOf(args, ScreenshotFlag),
            ScreenshotPicker: Has(args, PickerFlag),
            ScreenshotEditor: Has(args, EditorFlag),
            NoAutostart: Has(args, NoAutostartFlag),
            Frames: int.TryParse(ValueOf(args, FramesFlag), out var frames) ? frames : 0,
            SimulateRunning: ValueOf(args, SimulateRunningFlag), // "all", or group names separated by ";"
            DemoJump: Has(args, DemoJumpFlag),
            SelfTestReportPath: ValueOf(args, SelfTestFlag),
            EditorTab: int.TryParse(ValueOf(args, EditorTabFlag), out var tab) ? tab : 0);
    }

    private static bool Has(string[] args, string flag) => args.Contains(flag, StringComparer.OrdinalIgnoreCase);

    private static string? ValueOf(string[] args, string flag)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
