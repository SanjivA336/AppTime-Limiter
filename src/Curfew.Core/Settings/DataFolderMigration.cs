namespace Curfew.Core.Settings;

/// <summary>What <see cref="DataFolderMigration.MoveIfNeeded"/> did.</summary>
public enum MigrationOutcome
{
    /// <summary>There was no old folder, or the new one already exists, so nothing needed doing.</summary>
    NothingToDo,

    /// <summary>The old folder was moved to the new name, with everything in it.</summary>
    Moved,

    /// <summary>The old folder was found but couldn't be moved. It is left exactly where it was.</summary>
    Failed,
}

public sealed record MigrationResult(MigrationOutcome Outcome, string? Error = null);

/// <summary>
/// The app was renamed, and its settings folder is named after it. On the first start under the new name, this moves
/// the old folder (settings, usage history, saved icons) over, so nothing the user set up is lost.
/// </summary>
public static class DataFolderMigration
{
    /// <summary>
    /// Moves <paramref name="oldFolder"/> to <paramref name="newFolder"/>, but only when the old one exists and the new
    /// one does not. It never overwrites or merges into a folder that is already there, so running it again, or after
    /// the new folder has been used, does nothing.
    /// </summary>
    public static MigrationResult MoveIfNeeded(string oldFolder, string newFolder)
    {
        if (!Directory.Exists(oldFolder) || Directory.Exists(newFolder))
        {
            return new MigrationResult(MigrationOutcome.NothingToDo);
        }

        try
        {
            Directory.Move(oldFolder, newFolder);
            return new MigrationResult(MigrationOutcome.Moved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new MigrationResult(MigrationOutcome.Failed, ex.Message);
        }
    }
}
