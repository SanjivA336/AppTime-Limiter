namespace Curfew.App.Services;

/// <summary>
/// Makes sure only one copy runs. Two copies would each count usage, doubling the time charged to the budget.
/// A second launch pokes the first one to show its window, then exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Curfew.SingleInstance";
    private const string ShowEventName = @"Local\Curfew.ShowWindow";

    // The names this app had when it was called HardCap. A copy of that older version holds them, so claiming them too means
    // the two can never run side by side (each would count usage), whichever starts first.
    private const string OldMutexName = @"Local\HardCap.SingleInstance";
    private const string OldShowEventName = @"Local\HardCap.ShowWindow";

    private readonly Mutex _mutex;
    private readonly Mutex _oldMutex;
    private readonly bool _ownsMutex;
    private readonly bool _ownsOldMutex;
    private readonly EventWaitHandle _showEvent;
    private readonly EventWaitHandle _oldShowEvent;
    private readonly CancellationTokenSource _stop = new();

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out _ownsMutex);
        _oldMutex = new Mutex(initiallyOwned: true, OldMutexName, out _ownsOldMutex);
        IsFirst = _ownsMutex && _ownsOldMutex;
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _oldShowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, OldShowEventName);
    }

    public bool IsFirst { get; }

    /// <summary>Asks the already-running copy to show its window (an older HardCap listens on its own event).</summary>
    public void SignalFirstInstance()
    {
        _showEvent.Set();
        _oldShowEvent.Set();
    }

    /// <summary>Calls <paramref name="onShowRequested"/> (on a background thread) whenever a later launch asks for the window.</summary>
    public void ListenForShowRequests(Action onShowRequested)
    {
        var thread = new Thread(() =>
        {
            var handles = new[] { _showEvent, _stop.Token.WaitHandle };
            while (WaitHandle.WaitAny(handles) == 0)
            {
                onShowRequested();
            }
        })
        {
            IsBackground = true,
            Name = "Curfew show-window listener",
        };
        thread.Start();
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
        }

        if (_ownsOldMutex)
        {
            _oldMutex.ReleaseMutex();
        }

        _mutex.Dispose();
        _oldMutex.Dispose();
        _showEvent.Dispose();
        _oldShowEvent.Dispose();
        _stop.Dispose();
    }
}
