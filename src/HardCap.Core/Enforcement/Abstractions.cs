namespace HardCap.Core.Enforcement;

/// <summary>Looks at and closes processes. The real one uses Windows; tests use a fake.</summary>
public interface IProcessController
{
    /// <summary>Names (no ".exe") of every process currently running.</summary>
    IReadOnlyCollection<string> GetRunningProcessNames();

    /// <summary>
    /// The name (no ".exe") of the process that owns the window being used right now, or null if there isn't one. A fake
    /// that doesn't care about focus can leave this as it is.
    /// </summary>
    string? GetForegroundProcessName() => null;

    /// <summary>Closes every running process with one of these names. Returns the names that could not be closed.</summary>
    Task<IReadOnlyList<string>> CloseAsync(IReadOnlyCollection<string> processNames, CancellationToken cancellationToken = default);
}

/// <summary>Shows the user a message (a Windows notification in the real app).</summary>
public interface INotifier
{
    void Notify(string title, string message);
}

/// <summary>Says how long it has been since the person last used the keyboard or mouse. The real one asks Windows; tests use a fake.</summary>
public interface IIdleProbe
{
    /// <summary>The time since the last keyboard or mouse input, or null if it can't be told.</summary>
    TimeSpan? GetIdleTime();
}
