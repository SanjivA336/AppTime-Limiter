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
        Assert.Equal(0, WaitRules.GroupWaitSeconds(group, T.At(12)));
        Assert.Equal(0, WaitRules.ExitWaitSeconds(settings, T.At(12)));
    }

    // ---- Opening a group: only the group's own lock ----

    [Fact]
    public void A_group_waits_only_for_its_own_lock_whatever_the_general_wait_is()
    {
        var group = LockedGroup(60);

        Assert.Equal(60, WaitRules.GroupWaitSeconds(group, T.At(15)));   // the general wait (3 minutes outside open hours) has no say
        Assert.Equal(60, WaitRules.GroupWaitSeconds(group, T.At(8, 30))); // and open hours don't change a lock either
    }

    [Fact]
    public void A_group_with_no_lock_does_not_wait_even_when_a_general_wait_is_set()
    {
        var settings = new AppSettings { WaitSeconds = 180 };
        var group = LockedGroup(0);

        Assert.Equal(0, WaitRules.GroupWaitSeconds(group, T.At(15)));
        Assert.Equal(180, WaitRules.SettingsWaitSeconds(settings, T.At(15))); // the general wait is for the settings page
    }

    [Fact]
    public void A_long_lock_is_used_as_it_is()
    {
        Assert.Equal(300, WaitRules.GroupWaitSeconds(LockedGroup(300), T.At(15)));
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

    // ---- A paused group ignores its lock ----

    private static AppGroup LockedGroup(int lockSeconds)
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Free);
        group.LockSeconds = lockSeconds;
        return group;
    }

    [Fact]
    public void A_group_waits_for_its_lock_when_it_is_not_paused()
    {
        var group = LockedGroup(120);

        Assert.Equal(120, WaitRules.GroupWaitSeconds(group, T.At(12)));
    }

    [Fact]
    public void A_paused_group_ignores_its_lock()
    {
        var group = LockedGroup(120);
        group.IsPaused = true;

        Assert.Equal(0, WaitRules.GroupWaitSeconds(group, T.At(12)));
    }

    [Fact]
    public void Ignoring_the_lock_does_not_change_the_saved_lock()
    {
        var group = LockedGroup(120);
        group.IsPaused = true;

        WaitRules.GroupWaitSeconds(group, T.At(12));

        Assert.Equal(120, group.LockSeconds);
        Assert.Equal(0, group.LockSecondsAt(T.At(12)));
    }

    [Fact]
    public void The_lock_comes_back_by_itself_when_a_timed_pause_ends()
    {
        var group = LockedGroup(120);
        group.IsPaused = true;
        group.PausedUntil = T.At(13);

        Assert.Equal(0, WaitRules.GroupWaitSeconds(group, T.At(12, 59, 59)));
        Assert.Equal(120, WaitRules.GroupWaitSeconds(group, T.At(13)));
    }

    [Fact]
    public void A_paused_group_waits_for_nothing_but_the_settings_and_quitting_still_use_the_general_wait()
    {
        var group = LockedGroup(120);
        group.IsPaused = true;
        var settings = new AppSettings { WaitSeconds = 45, Groups = { group } };

        Assert.Equal(0, WaitRules.GroupWaitSeconds(group, T.At(12)));
        Assert.Equal(45, WaitRules.SettingsWaitSeconds(settings, T.At(12)));
        Assert.Equal(45, WaitRules.ExitWaitSeconds(settings, T.At(12)));
    }

    [Fact]
    public void Quitting_ignores_the_locks_of_paused_groups_but_not_the_others()
    {
        var paused = LockedGroup(240);
        paused.IsPaused = true;
        var active = LockedGroup(90);
        var settings = new AppSettings { Groups = { paused, active } };

        Assert.Equal(90, WaitRules.ExitWaitSeconds(settings, T.At(12)));
    }

    [Fact]
    public void Quitting_with_every_group_paused_waits_only_the_general_wait()
    {
        var one = LockedGroup(240);
        one.IsPaused = true;
        var two = LockedGroup(60);
        two.IsPaused = true;
        var settings = new AppSettings { WaitSeconds = 30, Groups = { one, two } };

        Assert.Equal(30, WaitRules.ExitWaitSeconds(settings, T.At(12)));
    }
}
