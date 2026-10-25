using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using HardCap.App.ViewModels;
using Wpf.Ui.Controls;

namespace HardCap.App.Views;

/// <summary>
/// Shown instead of quitting right away when a wait applies: the longest group lock, or the general wait. The countdown
/// runs only while this window is in front. Once it ends, the user can confirm the quit or keep HardCap running.
/// </summary>
public partial class ExitWaitWindow : FluentWindow
{
    private readonly ExitWaitState _state;

    public ExitWaitWindow(int waitSeconds)
    {
        _state = new ExitWaitState(waitSeconds);
        DataContext = _state;
        InitializeComponent();

        _state.Countdown.FollowActivityOf(this);
        Closed += (_, _) => _state.Countdown.Stop();
    }

    /// <summary>True only if the user waited the countdown out and chose "Quit HardCap".</summary>
    public bool QuitConfirmed { get; private set; }

    private void Keep_Click(object sender, RoutedEventArgs e) => Close();

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        QuitConfirmed = true;
        Close();
    }

    /// <summary>What the window shows: the countdown, then the choice once it has run out.</summary>
    private sealed partial class ExitWaitState : ObservableObject
    {
        public ExitWaitState(int waitSeconds)
        {
            Countdown = new WaitCountdown(waitSeconds);
            IsWaiting = !Countdown.IsFinished;
            Countdown.Finished += () => IsWaiting = false;
        }

        public WaitCountdown Countdown { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsReady))]
        private bool _isWaiting;

        public bool IsReady => !IsWaiting;
    }
}
