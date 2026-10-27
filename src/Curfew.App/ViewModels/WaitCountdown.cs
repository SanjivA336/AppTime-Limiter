using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Curfew.Core.Models;

namespace Curfew.App.ViewModels;

/// <summary>
/// A countdown shown before something can be opened (a locked group, the settings, quitting). It runs a
/// <see cref="CountdownClock"/>, which only counts time while the window is the active one: switch to another window and
/// it stops, so the wait can't be sat out in the background. The stopwatch (not the clock) measures the time, so
/// changing the computer's clock can't skip ahead.
/// </summary>
public sealed partial class WaitCountdown : ObservableObject
{
    private readonly CountdownClock _clock;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly DispatcherTimer? _timer;
    private TimeSpan _lastTick = TimeSpan.Zero;
    private bool _windowIsActive = true;
    private Action? _stopFollowingWindow;

    /// <summary>What the countdown reads, such as "0:45".</summary>
    [ObservableProperty]
    private string _remainingText = string.Empty;

    /// <summary>How much of the wait is left, from 1 (all of it) down to 0. It sizes the bar under the countdown.</summary>
    [ObservableProperty]
    private double _progress = 1;

    /// <summary>True while the window is not the active one, so the countdown is standing still.</summary>
    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isFinished;

    public WaitCountdown(int totalSeconds)
    {
        _clock = new CountdownClock(totalSeconds);
        Update();

        if (!_clock.IsFinished)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _timer.Tick += (_, _) => Step();
            _timer.Start();
        }
    }

    /// <summary>Raised once, when the countdown reaches zero.</summary>
    public event Action? Finished;

    public int TotalSeconds => _clock.TotalSeconds;

    /// <summary>
    /// Tells the countdown whether its window is the active one. Time up to this moment is counted under the old state first.
    /// </summary>
    public void SetWindowActive(bool isActive)
    {
        Step();
        _windowIsActive = isActive;
        IsPaused = !isActive && !_clock.IsFinished;
    }

    /// <summary>
    /// Makes the countdown follow a window: it counts while that window is active and waits while it isn't. It stops
    /// following when the countdown is stopped.
    /// </summary>
    public void FollowActivityOf(Window window)
    {
        SetWindowActive(window.IsActive);

        void Activated(object? sender, EventArgs e) => SetWindowActive(true);
        void Deactivated(object? sender, EventArgs e) => SetWindowActive(false);

        window.Activated += Activated;
        window.Deactivated += Deactivated;
        _stopFollowingWindow = () =>
        {
            window.Activated -= Activated;
            window.Deactivated -= Deactivated;
        };
    }

    /// <summary>Stops the timer. Called when the countdown ends and when the window closes.</summary>
    public void Stop()
    {
        _timer?.Stop();
        _stopFollowingWindow?.Invoke();
        _stopFollowingWindow = null;
    }

    private void Step()
    {
        if (_clock.IsFinished)
        {
            return;
        }

        var now = _stopwatch.Elapsed;
        _clock.Advance((now - _lastTick).TotalSeconds, _windowIsActive);
        _lastTick = now;
        Update();

        if (_clock.IsFinished)
        {
            Stop();
            IsPaused = false;
            Finished?.Invoke();
        }
    }

    private void Update()
    {
        RemainingText = _clock.Text;
        Progress = _clock.Progress;
        IsFinished = _clock.IsFinished;
    }
}
