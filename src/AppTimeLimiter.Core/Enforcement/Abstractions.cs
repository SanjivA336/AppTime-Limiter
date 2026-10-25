namespace AppTimeLimiter.Core.Enforcement;

/// <summary>Looks at and closes processes. The real one uses Windows; tests use a fake.</summary>
public interface IProcessController
{
    /// <summary>Names (no ".exe") of every process currently running.</summary>
    IReadOnlyCollection<string> GetRunningProcessNames();

    /// <summary>Closes every running process with one of these names. Returns the names that could not be closed.</summary>
    Task<IReadOnlyList<string>> CloseAsync(IReadOnlyCollection<string> processNames, CancellationToken cancellationToken = default);
}

/// <summary>Shows the user a message (a Windows notification in the real app).</summary>
public interface INotifier
{
    void Notify(string title, string message);
}
