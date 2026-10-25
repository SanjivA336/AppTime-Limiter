using HardCap.Core.Models;
using HardCap.Core.Settings;

namespace HardCap.Tests;

public class WaitRulesTests
{
    private static AppSettings WithOpenHours(int startHour, int endHour, int waitSeconds = 180) => new()
    {
        WaitSeconds = waitSeconds,
        OpenHoursEnabled = true,
        OpenHoursStartMinutes = startHour * 60,
        OpenHoursEndMinutes = endHour * 60,
    };

    // ---- Open hours ----

    [Fact]
    public void Open_hours_that_are_switched_off_never_apply()
    {
        var settings = WithOpenHours(8, 9);
        settings.OpenHoursEnabled = false;

        Assert.False(WaitRules.IsInOpenHours(settings, T.At(8, 30)));
    }

    [Theory]
    [InlineData(7, 59, false)]
    [InlineData(8, 0, true)]   // the start counts
    [InlineData(8, 59, true)]
    [InlineData(9, 0, false)]  // the end does not
    [InlineData(15, 0, false)]
    public void A_normal_open_hours_window_covers_its_start_up_to_its_end(int hour, int minute, bool expected) =>
        Assert.Equal(expected, WaitRules.IsInOpenHours(WithOpenHours(8, 9), T.At(hour, minute)));

    [Theory]
    [InlineData(21, 59, false)]
    [InlineData(22, 0, true)]
    [InlineData(23, 59, true)]
    [InlineData(0, 0, true)]
    [InlineData(1, 59, true)]
    [InlineData(2, 0, false)]
    public void An_open_hours_window_can_run_past_midnight(int hour, int minute, bool expected) =>
        Assert.Equal(expected, WaitRules.IsInOpenHours(WithOpenHours(22, 2), T.At(hour, minute)));

    [Fact]
    public void An_open_hours_window_with_the_same_start_and_end_is_empty()
    {
        Assert.False(WaitRules.IsInOpenHours(WithOpenHours(8, 8), T.At(8, 0)));
    }

    // ---- The general wait ----

    [Fact]
    public void The_general_wait_applies_outside_open_hours_and_not_inside_them()
    {
        var settings = WithOpenHours(8, 9, waitSeconds: 180);

        Assert.Equal(0, WaitRules.GeneralWaitSeconds(settings, T.At(8, 30)));
        Assert.Equal(180, WaitRules.GeneralWaitSeconds(settings, T.At(15)));
    }

    [Fact]
    public void Without_open_hours_the_general_wait_always_applies()
    {
        var settings = new AppSettings { WaitSeconds = 60 };

        Assert.Equal(60, WaitRules.GeneralWaitSeconds(settings, T.At(3)));
        Assert.Equal(60, WaitRules.SettingsWaitSeconds(settings, T.At(12)));
    }

    [Fact]
    public void No_wait_is_set_by_default()
    {
        var settings = new AppSettings();
        var group = T.Group("Games", new[] { "game" }, CellState.Free);

        Assert.Equal(0, WaitRules.SettingsWaitSeconds(settings, T.At(12)));
        Assert.Equal(0, WaitRules.GroupWaitSeconds(settings, group, T.At(12)));
        Assert.Equal(0, WaitRules.ExitWaitSeconds(settings, T.At(12)));
    }

    // ---- Opening a group: the longer of the lock and the general wait ----

    [Fact]
    public void A_group_waits_for_the_longer_of_its_lock_and_the_general_wait_never_both_added()
    {
        var settings = WithOpenHours(8, 9, waitSeconds: 180);
        var group = T.Group("Games", new[] { "game" }, CellState.Free);
        group.LockSeconds = 60;

        Assert.Equal(180, WaitRules.GroupWaitSeconds(settings, group, T.At(15)));  // 3 minutes, not 4
        Assert.Equal(60, WaitRules.GroupWaitSeconds(settings, group, T.At(8, 30))); // the lock still applies in open hours
    }

    [Fact]
    public void A_long_lock_beats_a_short_general_wait()
    {
        var settings = new AppSettings { WaitSeconds = 30 };
        var group = T.Group("Games", new[] { "game" }, CellState.Free);
        group.LockSeconds = 120;

        Assert.Equal(120, WaitRules.GroupWaitSeconds(settings, group, T.At(15)));
    }

    // ---- Quitting ----

    [Fact]
    public void Quitting_waits_as_long_as_the_longest_group_lock()
    {
        var settings = new AppSettings();
        var short1 = T.Group("A", new[] { "a" }, CellState.Free);
        var long1 = T.Group("B", new[] { "b" }, CellState.Free);
        short1.LockSeconds = 15;
        long1.LockSeconds = 240;
        settings.Groups.AddRange(new[] { short1, long1 });

        Assert.Equal(240, WaitRules.ExitWaitSeconds(settings, T.At(12)));
    }

    [Fact]
    public void Quitting_uses_the_general_wait_when_that_is_longer_and_it_applies()
    {
        var settings = WithOpenHours(8, 9, waitSeconds: 200);
        var group = T.Group("A", new[] { "a" }, CellState.Free);
        group.LockSeconds = 30;
        settings.Groups.Add(group);

        Assert.Equal(200, WaitRules.ExitWaitSeconds(settings, T.At(15)));
        Assert.Equal(30, WaitRules.ExitWaitSeconds(settings, T.At(8, 15)));
    }
}
