namespace HardCap.App.Services;

/// <summary>
/// Makes sure only one copy runs. Two copies would each count usage, doubling the time charged to the budget.
/// A second launch pokes the first one to show its window, then exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\HardCap.SingleInstance";
    private const string ShowEventName = @"Local\HardCap.ShowWindow";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly CancellationTokenSource _stop = new();

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var created);
        IsFirst = created;
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
    }

    public bool IsFirst { get; }

    /// <summary>Asks the already-running copy to show its window.</summary>
    public void SignalFirstInstance() => _showEvent.Set();

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
            Name = "HardCap show-window listener",
        };
        thread.Start();
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (IsFirst)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
        _showEvent.Dispose();
        _stop.Dispose();
    }
}
