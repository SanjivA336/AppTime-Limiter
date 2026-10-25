using System.IO;
using System.Windows;
using System.Windows.Threading;
using AppTimeLimiter.App.Services;
using AppTimeLimiter.App.ViewModels;
using AppTimeLimiter.App.Views;
using AppTimeLimiter.Core.Enforcement;
using AppTimeLimiter.Core.Models;
using AppTimeLimiter.Core.Processes;
using AppTimeLimiter.Core.Settings;
using AppTimeLimiter.Core.Usage;
using Wpf.Ui.Appearance;

namespace AppTimeLimiter.App;

public partial class App : Application
{
    /// <summary>How often running apps are checked. Short, so a blocked app doesn't stay open for long.</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(3);

    private SingleInstance? _singleInstance;
    private TrayService? _tray;
    private EnforcementRunner? _runner;
    private DispatcherTimer? _checkTimer;
    private MainViewModel? _viewModel;
    private MainWindow? _window;
    private bool _isExiting;
    private bool _hasToldUserAboutTray;

    /// <summary>Everything read from disk at startup.</summary>
    private sealed record SavedData(AppSettings Settings, SettingsStore Store, UsageLedger Ledger, List<string> Notices);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var options = StartupOptions.Parse(e.Args);
        var isScreenshotRun = options.ScreenshotPath is not null;
        var isSelfTestRun = options.SelfTestReportPath is not null;

        if (!isScreenshotRun && !isSelfTestRun && !ClaimSingleInstance())
        {
            Shutdown();
            return;
        }

        var paths = new AppPaths(options.DataFolder);
        AppLog.Init(paths.Log);
        AppLog.Info("Starting AppTime Limiter");
        DispatcherUnhandledException += OnUnhandledException;

        var saved = LoadSavedData(paths);
        var autostart = new AutostartService();
        var icons = new AppIconService(Path.Combine(paths.Folder, "icons"));

        _viewModel = new MainViewModel(saved.Settings, saved.Store, saved.Ledger, autostart, icons, saved.Notices);
        ApplicationThemeManager.ApplySystemTheme();
        _window = new MainWindow(_viewModel);
        _window.Closing += OnWindowClosing;

        if (isSelfTestRun)
        {
            // Development aid: run the editor's rules against this data folder, write a report, and quit.
            _ = Dispatcher.InvokeAsync(async () =>
            {
                var passed = await EditorSelfTest.RunAsync(_viewModel, paths.Settings, options.SelfTestReportPath!);
                Shutdown(passed ? 0 : 1);
            });
            return;
        }

        if (isScreenshotRun)
        {
            ShowScreenshotThenQuit(options, saved.Settings);
            return;
        }

        StartEnforcing(saved, autostart, options);
    }

    /// <summary>
    /// Only one copy may run: two would each count usage and charge the budget twice.
    /// If another copy is already running, ask it to show its window and return false.
    /// </summary>
    private bool ClaimSingleInstance()
    {
        _singleInstance = new SingleInstance();
        if (_singleInstance.IsFirst)
        {
            return true;
        }

        _singleInstance.SignalFirstInstance();
        return false;
    }

    private static SavedData LoadSavedData(AppPaths paths)
    {
        var store = new SettingsStore(paths.Settings);
        var loaded = store.Load();

        var ledger = new UsageLedger(paths.Usage);
        ledger.Load();

        // Things the user should hear about: settings we had to repair, or files we couldn't read.
        var notices = loaded.Repairs.Append(loaded.Error).Append(ledger.LoadError).OfType<string>().ToList();
        foreach (var notice in notices)
        {
            AppLog.Info("Startup notice: " + notice);
        }

        return new SavedData(loaded.Settings, store, ledger, notices);
    }

    /// <summary>Normal startup: tray icon, the check-every-few-seconds loop, and (usually) the window.</summary>
    private void StartEnforcing(SavedData saved, AutostartService autostart, StartupOptions options)
    {
        // Make sure Windows' "run at sign-in" entry matches the saved setting.
        // (Skipped with --no-autostart, so development builds never register themselves to start with Windows.)
        var shouldRegisterAutostart = saved.Settings.StartWithWindows && !autostart.IsEnabled();
        if (shouldRegisterAutostart && !options.NoAutostart)
        {
            autostart.SetEnabled(true);
        }

        _tray = new TrayService(ShowWindow, ExitApp);
        _runner = new EnforcementRunner(() => saved.Settings, new WindowsProcessController(), _tray, saved.Ledger);
        _runner.Ticked += _viewModel!.OnTick;

        // A second launch asks us to show the window; that request arrives on a background thread.
        _singleInstance!.ListenForShowRequests(() => Dispatcher.BeginInvoke(ShowWindow));

        _checkTimer = new DispatcherTimer { Interval = CheckInterval };
        _checkTimer.Tick += async (_, _) => await RunOneCheckAsync();
        _checkTimer.Start();
        _ = RunOneCheckAsync(); // Don't wait three seconds for the first check.

        if (!options.StartHidden)
        {
            _window!.Show();
        }
    }

    private async Task RunOneCheckAsync()
    {
        try
        {
            await _runner!.RunOnceAsync(DateTime.Now);
        }
        catch (Exception ex)
        {
            // One failed check must never stop all the future ones.
            AppLog.Error("Enforcement check failed", ex);
        }
    }

    /// <summary>
    /// Development aid (--screenshot): show the window, save a picture of it, and quit.
    /// Nothing is enforced and no tray icon is created.
    /// </summary>
    private void ShowScreenshotThenQuit(StartupOptions options, AppSettings settings)
    {
        // Use the full screen height so the whole page fits in one picture.
        _window!.WindowStartupLocation = WindowStartupLocation.Manual;
        _window.Top = 0;
        _window.Left = 40;
        _window.Height = SystemParameters.WorkArea.Height;
        _window.Show();
        _window.Activate();

        Window windowToCapture = _window;
        if (options.ScreenshotPicker && settings.Groups.Count > 0)
        {
            // --picker: capture the "Add apps" dialog instead. It's shown non-modally so we can carry on.
            var picker = new ProcessPickerWindow(settings, settings.Groups[0]) { Owner = _window };
            picker.WindowStartupLocation = WindowStartupLocation.Manual;
            picker.Left = 120;
            picker.Top = 60;
            picker.Show();
            windowToCapture = picker;
        }
        else if (options.ScreenshotEditor && _viewModel!.Groups.Count > 0)
        {
            // --editor: capture the group editor for the first group (also non-modal).
            var editor = new GroupEditorWindow(_viewModel, _viewModel.Groups[0]) { Owner = _window };
            editor.WindowStartupLocation = WindowStartupLocation.Manual;
            editor.Left = 120;
            editor.Top = 0;
            editor.Height = SystemParameters.WorkArea.Height;
            editor.Show();
            editor.SelectTab(options.EditorTab);
            windowToCapture = editor;
        }

        if (options.SimulateRunning is { } simulated)
        {
            // Pretend some groups' apps are running ("all", or the group names separated by ";"), so the glow and
            // stripes can be seen without opening real apps, and so groups that are NOT running can be checked too.
            var names = simulated.Split(';', StringSplitOptions.TrimEntries);
            var everyGroup = simulated.Equals("all", StringComparison.OrdinalIgnoreCase);
            var statuses = _viewModel!.Groups
                .Select(g => new GroupStatus(g.Model.Id, everyGroup || names.Contains(g.Name, StringComparer.OrdinalIgnoreCase), g.CurrentState))
                .ToList();
            _viewModel.OnTick(new TickResult(Array.Empty<CloseRequest>(), Array.Empty<Alert>(), statuses));
        }

        if (options.Frames > 0)
        {
            // --demo-jump: after the second frame, cut today's budget to 40 minutes so every bar's goal jumps at once.
            Action? afterSecondFrame = options.DemoJump
                ? () =>
                {
                    foreach (var group in _viewModel!.Settings.Groups)
                    {
                        group.SetBudgetMinutes(DateTime.Now.DayOfWeek, 40);
                    }

                    _viewModel.RefreshStatuses();
                }
                : null;
            CaptureFrames(windowToCapture, Path.ChangeExtension(options.ScreenshotPath!, null), options.Frames, afterSecondFrame);
            return;
        }

        // Give Windows a moment to paint the frosted (Mica) background before taking the picture.
        var waitBeforeCapture = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        waitBeforeCapture.Tick += (_, _) =>
        {
            waitBeforeCapture.Stop();
            ScreenshotService.Save(windowToCapture, options.ScreenshotPath!);
            Shutdown();
        };
        waitBeforeCapture.Start();
    }

    /// <summary>
    /// Development aid (--frames N): draws the window to PNG files named prefix-1.png, prefix-2.png, and so on,
    /// about 0.4 seconds apart, then quits. It draws the window itself rather than the screen, so it works
    /// even when something else is in front, and shows animations at different moments.
    /// </summary>
    private void CaptureFrames(Window window, string pathPrefix, int count, Action? afterSecondFrame = null)
    {
        var taken = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; // a first pause so the layout settles
        timer.Tick += (_, _) =>
        {
            timer.Interval = TimeSpan.FromMilliseconds(400);
            taken++;
            ScreenshotService.SaveOffscreen(window, $"{pathPrefix}-{taken}.png");
            if (taken == 2)
            {
                afterSecondFrame?.Invoke();
            }

            if (taken >= count)
            {
                timer.Stop();
                Shutdown();
            }
        };
        timer.Start();
    }

    private void ShowWindow()
    {
        if (_window is null)
        {
            return;
        }

        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }

    /// <summary>
    /// Closing the window only hides it, so limits keep being enforced from the tray.
    /// "Exit" in the tray menu is the way to really quit.
    /// </summary>
    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;
        _window!.Hide();
        _viewModel!.SaveNow();

        if (!_hasToldUserAboutTray)
        {
            _hasToldUserAboutTray = true;
            _tray?.Notify("AppTime Limiter is still running", "It keeps enforcing your limits from the notification area. Right-click the icon to exit.");
        }
    }

    private void ExitApp()
    {
        _isExiting = true;
        _checkTimer?.Stop();
        _viewModel?.SaveNow();
        _runner?.Flush();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        AppLog.Info("Exiting");
        Shutdown();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Unhandled UI exception", e.Exception);
        e.Handled = true; // Keep running: this app is a safety net and should survive a bad click.
    }
}
