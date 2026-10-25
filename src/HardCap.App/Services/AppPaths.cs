using System.IO;
using HardCap.Core.Settings;

namespace HardCap.App.Services;

/// <summary>Where the app keeps its files. Everything lives in one per-user folder, nothing is stored remotely.</summary>
public sealed class AppPaths
{
    /// <summary>The folder's name before the app was called HardCap.</summary>
    private const string OldFolderName = "AppTimeLimiter";

    public AppPaths(string? overrideFolder = null)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var newFolder = overrideFolder ?? Path.Combine(localAppData, "HardCap");
        var oldFolder = Path.Combine(localAppData, OldFolderName);

        // A custom folder (used when developing) is left alone. The normal one takes over a folder from the old name,
        // so settings made before the rename carry across. This has to happen before the new folder is created.
        Migration = overrideFolder is null
            ? DataFolderMigration.MoveIfNeeded(oldFolder, newFolder)
            : new MigrationResult(MigrationOutcome.NothingToDo);

        // If the old folder couldn't be moved (an older copy of the app may still have a file open), keep using it for
        // now so nothing is lost. Creating an empty new folder instead would stop the move from ever being retried.
        Folder = Migration.Outcome == MigrationOutcome.Failed ? oldFolder : newFolder;
        Directory.CreateDirectory(Folder);
    }

    public string Folder { get; }

    /// <summary>Whether the old folder was moved across, so startup can mention it if it couldn't be.</summary>
    public MigrationResult Migration { get; }

    public string Settings => Path.Combine(Folder, "settings.json");

    public string Usage => Path.Combine(Folder, "usage.json");

    public string Log => Path.Combine(Folder, "app.log");
}
