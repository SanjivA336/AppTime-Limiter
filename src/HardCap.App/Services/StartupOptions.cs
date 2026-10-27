namespace HardCap.App.Services;

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
    int EditorTab,
    bool ScreenshotSettings,
    bool ScreenshotAnalytics,
    string? AnalyticsHover,
    string? AnalyticsView,
    int AnalyticsBack,
    bool ScreenshotGate = false,
    string? EditorSection = null)
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
    private const string SettingsFlag = "--settings";
    private const string AnalyticsFlag = "--analytics";
    private const string AnalyticsHoverFlag = "--analytics-hover";
    private const string AnalyticsViewFlag = "--analytics-view";
    private const string AnalyticsBackFlag = "--analytics-back";
    private const string GateFlag = "--gate";
    private const string EditorSectionFlag = "--editor-section";

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
            EditorTab: int.TryParse(ValueOf(args, EditorTabFlag), out var tab) ? tab : 0,
            ScreenshotSettings: Has(args, SettingsFlag),
            ScreenshotAnalytics: Has(args, AnalyticsFlag),
            AnalyticsHover: ValueOf(args, AnalyticsHoverFlag), // "day,group", e.g. "2,0"
            AnalyticsView: ValueOf(args, AnalyticsViewFlag),                 // "day", "week" or "month"
            AnalyticsBack: int.TryParse(ValueOf(args, AnalyticsBackFlag), out var back) ? back : 0, // periods back from now
            ScreenshotGate: Has(args, GateFlag), // --gate: the launch friction countdown
            EditorSection: ValueOf(args, EditorSectionFlag)); // --editor-section "Launch friction": which card the editor shows
    }

    private static bool Has(string[] args, string flag) => args.Contains(flag, StringComparer.OrdinalIgnoreCase);

    private static string? ValueOf(string[] args, string flag)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
