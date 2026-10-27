using System.IO;
using Curfew.Core.Settings;

namespace Curfew.App.Services;

/// <summary>Where the app keeps its files. Everything lives in one per-user folder, nothing is stored remotely.</summary>
public sealed class AppPaths
{
    /// <summary>The names the folder had before the app was called Curfew, newest first.</summary>
    private static readonly string[] OldFolderNames = { "HardCap", "AppTimeLimiter" };

    public AppPaths(string? overrideFolder = null)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var newFolder = overrideFolder ?? Path.Combine(localAppData, "Curfew");
        var folder = newFolder;
        var migration = new MigrationResult(MigrationOutcome.NothingToDo);

        // A custom folder (used when developing) is left alone. The normal one takes over a folder from an old name,
        // so settings made before a rename carry across. This has to happen before the new folder is created.
        // Once one old folder has been dealt with, the new folder exists (or we are keeping the old one), so the loop stops.
        if (overrideFolder is null)
        {
            foreach (var name in OldFolderNames)
            {
                var oldFolder = Path.Combine(localAppData, name);
                var result = DataFolderMigration.MoveIfNeeded(oldFolder, newFolder);
                if (result.Outcome == MigrationOutcome.NothingToDo)
                {
                    continue;
                }

                migration = result;

                // If the old folder couldn't be moved (an older copy of the app may still have a file open), keep using it for
                // now so nothing is lost. Creating an empty new folder instead would stop the move from ever being retried.
                if (result.Outcome == MigrationOutcome.Failed)
                {
                    folder = oldFolder;
                }

                break;
            }
        }

        Migration = migration;
        Folder = folder;
        Directory.CreateDirectory(Folder);
    }

    public string Folder { get; }

    /// <summary>Whether the old folder was moved across, so startup can mention it if it couldn't be.</summary>
    public MigrationResult Migration { get; }

    public string Settings => Path.Combine(Folder, "settings.json");

    public string Usage => Path.Combine(Folder, "usage.json");

    public string Log => Path.Combine(Folder, "app.log");
}
