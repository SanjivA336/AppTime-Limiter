using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Curfew.App.ViewModels;
using Curfew.Core.Enforcement;
using Curfew.Core.Processes;
using Wpf.Ui.Controls;

namespace Curfew.App.Views;

/// <summary>
/// The window shown while a newly started app is frozen (launch friction). It looks like the "Quit Curfew" window: a countdown that
/// only runs while this window is in front, then a choice to open the app or not. Closing the window any other way (the X, Alt+F4)
/// means "don't open it".
/// </summary>
public partial class LaunchGateWindow : FluentWindow
{
    private readonly GateState _state;
    private bool _closeRequested;
    private bool _opened;

    public LaunchGateWindow(HoldRequest request)
    {
        GroupId = request.GroupId;
        var apps = ProcessNames.Join(request.Processes);
        _state = new GateState(request.Seconds, $"Opening {apps}", $"Open {apps}?");
        DataContext = _state;
        InitializeComponent();

        // The countdown runs only while this window is the active one, like the other waits.
        _state.Countdown.FollowActivityOf(this);

        Closing += (_, _) =>
        {
            var wasRequested = _closeRequested;
            _closeRequested = true;
            _state.Countdown.Stop();
            if (!_opened && !wasRequested)
            {
                Cancelled?.Invoke(GroupId); // closed by the person: they chose not to open it
            }
        };

        // A new app usually takes the focus as it starts; ask for it back so the countdown can begin.
        Loaded += (_, _) => Activate();
    }

    public Guid GroupId { get; }

    public WaitCountdown Countdown => _state.Countdown;

    /// <summary>True once the countdown has run out and the choice is showing.</summary>
    public bool IsReady => _state.IsReady;

    /// <summary>Raised when the person chose to open the app: it should carry on.</summary>
    public event Action<Guid>? Completed;

    /// <summary>Raised when the person chose not to open it: the app should be closed.</summary>
    public event Action<Guid>? Cancelled;

    private void Open_Click(object sender, RoutedEventArgs e) => ChooseOpen();

    private void DontOpen_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Opens the app (only once the countdown has run out).</summary>
    public void ChooseOpen()
    {
        if (!_state.IsReady || _closeRequested)
        {
            return;
        }

        _opened = true;
        Completed?.Invoke(GroupId);
        CloseQuietly();
    }

    /// <summary>Closes the window without treating it as "don't open" (the hold ended for another reason).</summary>
    public void CloseQuietly()
    {
        if (_closeRequested)
        {
            return;
        }

        _closeRequested = true;
        _state.Countdown.Stop();
        Close();
    }

    /// <summary>What the window shows: the countdown, then the choice once it has run out.</summary>
    private sealed partial class GateState : ObservableObject
    {
        public GateState(int seconds, string heading, string readyHeading)
        {
            Heading = heading;
            ReadyHeading = readyHeading;
            Countdown = new WaitCountdown(seconds);
            IsWaiting = !Countdown.IsFinished;
            Countdown.Finished += () => IsWaiting = false;
        }

        public string Heading { get; }

        public string ReadyHeading { get; }

        public WaitCountdown Countdown { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsReady))]
        private bool _isWaiting;

        public bool IsReady => !IsWaiting;
    }
}
