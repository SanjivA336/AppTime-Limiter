using Curfew.Core.Settings;

namespace Curfew.Tests;

public sealed class DataFolderMigrationTests : IDisposable
{
    private readonly string _root = T.TempFolder();

    private string Old => Path.Combine(_root, "AppTimeLimiter");

    private string New => Path.Combine(_root, "Curfew");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void MakeOldFolderWithSettings()
    {
        Directory.CreateDirectory(Path.Combine(Old, "icons"));
        File.WriteAllText(Path.Combine(Old, "settings.json"), "{ \"groups\": [] }");
        File.WriteAllText(Path.Combine(Old, "usage.json"), "{}");
        File.WriteAllText(Path.Combine(Old, "icons", "steam.png"), "png");
    }

    [Fact]
    public void The_old_folder_and_everything_in_it_moves_to_the_new_name()
    {
        MakeOldFolderWithSettings();

        var result = DataFolderMigration.MoveIfNeeded(Old, New);

        Assert.Equal(MigrationOutcome.Moved, result.Outcome);
        Assert.False(Directory.Exists(Old));
        Assert.Equal("{ \"groups\": [] }", File.ReadAllText(Path.Combine(New, "settings.json")));
        Assert.True(File.Exists(Path.Combine(New, "usage.json")));
        Assert.True(File.Exists(Path.Combine(New, "icons", "steam.png"))); // sub-folders come along too
    }

    [Fact]
    public void With_no_old_folder_there_is_nothing_to_do()
    {
        var result = DataFolderMigration.MoveIfNeeded(Old, New);

        Assert.Equal(MigrationOutcome.NothingToDo, result.Outcome);
        Assert.False(Directory.Exists(New)); // it doesn't create the new folder; that is the caller's job
    }

    [Fact]
    public void An_existing_new_folder_is_never_overwritten_or_merged_into()
    {
        MakeOldFolderWithSettings();
        Directory.CreateDirectory(New);
        File.WriteAllText(Path.Combine(New, "settings.json"), "the newer settings");

        var result = DataFolderMigration.MoveIfNeeded(Old, New);

        Assert.Equal(MigrationOutcome.NothingToDo, result.Outcome);
        Assert.Equal("the newer settings", File.ReadAllText(Path.Combine(New, "settings.json")));
        Assert.True(Directory.Exists(Old)); // the old data is left alone
    }

    [Fact]
    public void Running_it_twice_is_harmless()
    {
        MakeOldFolderWithSettings();

        var first = DataFolderMigration.MoveIfNeeded(Old, New);
        var second = DataFolderMigration.MoveIfNeeded(Old, New);

        Assert.Equal(MigrationOutcome.Moved, first.Outcome);
        Assert.Equal(MigrationOutcome.NothingToDo, second.Outcome);
        Assert.True(File.Exists(Path.Combine(New, "settings.json")));
    }

    [Fact]
    public void A_folder_that_cannot_be_moved_is_reported_and_left_in_place()
    {
        MakeOldFolderWithSettings();

        // A file open with no sharing blocks the move on Windows, like an older copy of the app still running would.
        using var locked = new FileStream(Path.Combine(Old, "usage.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = DataFolderMigration.MoveIfNeeded(Old, New);

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(MigrationOutcome.Failed, result.Outcome);
            Assert.False(string.IsNullOrEmpty(result.Error));
            Assert.True(File.Exists(Path.Combine(Old, "settings.json")));
            Assert.False(Directory.Exists(New));
        }
    }
}
