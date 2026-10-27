using Curfew.Core.Models;
using Curfew.Core.Settings;

namespace Curfew.Tests;

public class LockTimeTests
{
    // ---- Turning the minutes and seconds boxes into a total ----

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 30, 30)]
    [InlineData(1, 15, 75)]
    [InlineData(4, 55, 295)]
    [InlineData(5, 0, 300)]
    public void Ordinary_values_just_add_up(int minutes, int seconds, int expected)
    {
        Assert.Equal(expected, LockTime.FromParts(minutes, seconds));
    }

    [Theory]
    [InlineData(0, 60, 60)]    // pushing the seconds to 60 makes it 1:00
    [InlineData(1, 60, 120)]   // 1:60 is 2:00
    [InlineData(2, 65, 185)]   // typed 65 seconds: 3:05
    [InlineData(1, -5, 55)]    // stepping the seconds below 0 borrows a minute: 0:55
    [InlineData(3, -5, 175)]   // 2:55
    public void Seconds_carry_into_minutes_and_borrow_from_them(int minutes, int seconds, int expected)
    {
        Assert.Equal(expected, LockTime.FromParts(minutes, seconds));
    }

    [Theory]
    [InlineData(0, -5, 0)]      // can't go below no lock
    [InlineData(-1, 0, 0)]
    [InlineData(5, 5, 300)]     // can't go above five minutes
    [InlineData(6, 0, 300)]
    [InlineData(4, 60, 300)]    // 4:60 is 5:00, which is allowed
    [InlineData(4, 65, 300)]    // 5:05 is too long
    public void The_total_stays_between_no_lock_and_five_minutes(int minutes, int seconds, int expected)
    {
        Assert.Equal(expected, LockTime.FromParts(minutes, seconds));
    }

    [Theory]
    [InlineData(0, 7, 5)]       // 7 seconds is closest to 5
    [InlineData(0, 8, 10)]      // 8 is closest to 10
    [InlineData(0, 12, 10)]
    [InlineData(0, 13, 15)]
    [InlineData(0, 2, 0)]
    [InlineData(0, 3, 5)]
    public void Typed_values_are_rounded_to_the_nearest_five_seconds(int minutes, int seconds, int expected)
    {
        Assert.Equal(expected, LockTime.FromParts(minutes, seconds));
    }

    [Fact]
    public void Every_step_up_the_whole_range_lands_exactly_on_the_next_step()
    {
        var total = 0;
        for (var step = 0; step < 60; step++)
        {
            var (minutes, seconds) = LockTime.ToParts(total);

            // Pressing "up" on the seconds box adds 5 to what it shows.
            var next = LockTime.FromParts(minutes, seconds + LockTime.StepSeconds);

            Assert.Equal(Math.Min(total + 5, 300), next);
            total = next;
        }

        Assert.Equal(300, total); // it stops at five minutes
    }

    [Fact]
    public void Every_step_down_the_whole_range_lands_exactly_on_the_previous_step()
    {
        var total = 300;
        for (var step = 0; step < 60; step++)
        {
            var (minutes, seconds) = LockTime.ToParts(total);

            var next = LockTime.FromParts(minutes, seconds - LockTime.StepSeconds);

            Assert.Equal(Math.Max(total - 5, 0), next);
            total = next;
        }

        Assert.Equal(0, total); // it stops at no lock
    }

    // ---- Splitting and showing a total ----

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(55, 0, 55)]
    [InlineData(60, 1, 0)]
    [InlineData(75, 1, 15)]
    [InlineData(300, 5, 0)]
    public void ToParts_splits_a_total_into_minutes_and_seconds(int total, int minutes, int seconds)
    {
        Assert.Equal((minutes, seconds), LockTime.ToParts(total));
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(5, "0:05")]
    [InlineData(59, "0:59")]
    [InlineData(60, "1:00")]
    [InlineData(75, "1:15")]
    [InlineData(300, "5:00")]
    public void Format_shows_a_countdown(int total, string expected)
    {
        Assert.Equal(expected, LockTime.Format(total));
    }

    [Theory]
    [InlineData(0, "No wait")]
    [InlineData(1, "1 second")]
    [InlineData(5, "5 seconds")]
    [InlineData(60, "1 minute")]
    [InlineData(75, "1 minute 15 seconds")]
    [InlineData(120, "2 minutes")]
    [InlineData(300, "5 minutes")]
    public void Describe_puts_the_length_in_words(int total, string expected)
    {
        Assert.Equal(expected, LockTime.Describe(total));
    }

    // ---- The lock is part of the group ----

    [Fact]
    public void The_lock_is_copied_compared_and_reverted_with_the_rest_of_the_group()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Free);
        group.LockSeconds = 90;

        var clone = group.Clone();
        Assert.Equal(90, clone.LockSeconds);
        Assert.True(clone.IsSameAs(group));

        clone.LockSeconds = 120;
        Assert.False(clone.IsSameAs(group));

        clone.CopyFrom(group);
        Assert.Equal(90, clone.LockSeconds);
    }

    [Fact]
    public void An_edit_session_sees_a_changed_lock_as_a_change_and_reverts_it()
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Free);
        var settings = T.Settings(60, group);
        var session = new GroupEditSession(settings, group);

        session.Draft.LockSeconds = 45;
        Assert.True(session.HasChanges);
        Assert.Equal(0, group.LockSeconds); // not real until committed

        session.Revert();
        Assert.False(session.HasChanges);
        Assert.Equal(0, session.Draft.LockSeconds);

        session.Draft.LockSeconds = 45;
        session.Commit();
        Assert.Equal(45, group.LockSeconds);
    }

    [Theory]
    [InlineData(-30, 0)]
    [InlineData(45, 45)]
    [InlineData(9999, 300)]
    public void A_lock_in_a_hand_edited_file_is_kept_in_range(int inFile, int expected)
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Free);
        group.LockSeconds = inFile;

        SettingsRules.Sanitize(T.Settings(60, group));

        Assert.Equal(expected, group.LockSeconds);
    }

    [Fact]
    public void The_lock_survives_saving_and_loading()
    {
        var folder = T.TempFolder();
        try
        {
            var group = T.Group("Games", new[] { "game" }, CellState.Free);
            group.LockSeconds = 150;
            var store = new SettingsStore(Path.Combine(folder, "settings.json"));

            store.Save(T.Settings(60, group));
            var loaded = store.Load();

            Assert.Equal(150, loaded.Settings.Groups[0].LockSeconds);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void An_older_file_without_a_lock_loads_as_no_lock()
    {
        var folder = T.TempFolder();
        try
        {
            var path = Path.Combine(folder, "settings.json");
            File.WriteAllText(path, """{ "schemaVersion": 2, "groups": [ { "name": "Old", "processes": ["game"] } ] }""");

            var loaded = new SettingsStore(path).Load();

            Assert.Equal(0, loaded.Settings.Groups[0].LockSeconds);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
