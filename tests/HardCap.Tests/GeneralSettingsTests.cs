using HardCap.Core.Models;

namespace HardCap.Tests;

/// <summary>The app-wide settings the Settings page edits as a draft: copying them back and forth, and telling when they differ.</summary>
public class GeneralSettingsTests
{
    private static AppSettings Changed() => new()
    {
        ResetMinutes = 240,
        WaitSeconds = 45,
        OpenHoursEnabled = true,
        OpenHoursStartMinutes = 7 * 60,
        OpenHoursEndMinutes = 10 * 60,
        IdleDetectionEnabled = true,
        IdleMinutes = 17,
    };

    [Fact]
    public void Copying_brings_over_every_app_wide_setting()
    {
        var copy = new AppSettings();

        copy.CopyGeneralSettingsFrom(Changed());

        Assert.True(copy.HasSameGeneralSettingsAs(Changed()));
        Assert.Equal(240, copy.ResetMinutes);
        Assert.Equal(45, copy.WaitSeconds);
        Assert.True(copy.OpenHoursEnabled);
        Assert.Equal(7 * 60, copy.OpenHoursStartMinutes);
        Assert.Equal(10 * 60, copy.OpenHoursEndMinutes);
        Assert.True(copy.IdleDetectionEnabled);
        Assert.Equal(17, copy.IdleMinutes);
    }

    [Fact]
    public void Copying_leaves_the_groups_and_start_with_windows_alone()
    {
        var target = new AppSettings { StartWithWindows = false, Groups = { T.Group("Games", new[] { "game" }, CellState.Free) } };
        var source = Changed();
        source.StartWithWindows = true;

        target.CopyGeneralSettingsFrom(source);

        Assert.False(target.StartWithWindows);
        Assert.Single(target.Groups);
    }

    [Fact]
    public void Settings_that_match_are_the_same_even_when_their_groups_and_startup_differ()
    {
        var one = Changed();
        one.StartWithWindows = true;
        one.Groups.Add(T.Group("Games", new[] { "game" }, CellState.Free));
        var other = Changed();
        other.StartWithWindows = false;

        Assert.True(one.HasSameGeneralSettingsAs(other));
    }

    [Theory]
    [InlineData("reset")]
    [InlineData("wait")]
    [InlineData("frictionless on")]
    [InlineData("frictionless start")]
    [InlineData("frictionless end")]
    [InlineData("away on")]
    [InlineData("away minutes")]
    public void Any_one_changed_setting_makes_them_different(string which)
    {
        var other = Changed();
        switch (which)
        {
            case "reset": other.ResetMinutes = 0; break;
            case "wait": other.WaitSeconds = 0; break;
            case "frictionless on": other.OpenHoursEnabled = false; break;
            case "frictionless start": other.OpenHoursStartMinutes = 0; break;
            case "frictionless end": other.OpenHoursEndMinutes = 0; break;
            case "away on": other.IdleDetectionEnabled = false; break;
            case "away minutes": other.IdleMinutes = 5; break;
        }

        Assert.False(Changed().HasSameGeneralSettingsAs(other));
    }

    [Fact]
    public void Importing_still_brings_the_groups_and_settings_but_not_start_with_windows()
    {
        var current = new AppSettings { StartWithWindows = false };
        var imported = Changed();
        imported.StartWithWindows = true;
        imported.Groups.Add(T.Group("Games", new[] { "game" }, CellState.Free));

        current.ReplaceWith(imported);

        Assert.False(current.StartWithWindows);
        Assert.Single(current.Groups);
        Assert.True(current.HasSameGeneralSettingsAs(Changed()));
    }
}
