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

    /// <summary>
    /// Freezes every running process with one of these names, so it does nothing until <see cref="Resume"/>. Returns the names that
    /// could NOT be frozen (Windows or an anti-cheat refused, or none was running). By default nothing can be frozen, so a fake
    /// that doesn't care leaves launch friction switched off.
    /// </summary>
    IReadOnlyList<string> Suspend(IReadOnlyCollection<string> processNames) => processNames.ToList();

    /// <summary>Unfreezes what <see cref="Suspend"/> froze for these names.</summary>
    void Resume(IReadOnlyCollection<string> processNames) { }

    /// <summary>Unfreezes everything that was frozen. Called when HardCap quits, so nothing is left frozen behind it.</summary>
    void ResumeAll() { }

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
