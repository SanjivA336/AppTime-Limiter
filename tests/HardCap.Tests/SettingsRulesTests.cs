using HardCap.Core.Models;
using HardCap.Core.Settings;

namespace HardCap.Tests;

public class SettingsRulesTests
{
    // ---- Sanitize ----

    [Fact]
    public void Clean_settings_need_no_repairs()
    {
        var settings = T.Settings(90, T.Group("Games", new[] { "game" }, CellState.Free, 5));

        Assert.Empty(SettingsRules.Sanitize(settings));
    }

    [Fact]
    public void Process_names_are_normalized()
    {
        var group = T.Group("Games", new[] { "  Warframe.x64.EXE ", "", "  " }, CellState.Free);

        SettingsRules.Sanitize(T.Settings(60, group));

        Assert.Equal(new[] { "Warframe.x64" }, group.Processes);
    }

    [Fact]
    public void A_process_in_two_groups_stays_only_in_the_first()
    {
        var a = T.Group("A", new[] { "game" }, CellState.Free);
        var b = T.Group("B", new[] { "GAME", "other" }, CellState.Free);

        var repairs = SettingsRules.Sanitize(T.Settings(60, a, b));

        Assert.Equal(new[] { "game" }, a.Processes);
        Assert.Equal(new[] { "other" }, b.Processes);
        Assert.Single(repairs);
    }

    [Fact]
    public void Protected_system_processes_are_removed()
    {
        var group = T.Group("Oops", new[] { "explorer", "game", "WINLOGON.exe" }, CellState.Blocked);

        var repairs = SettingsRules.Sanitize(T.Settings(60, group));

        Assert.Equal(new[] { "game" }, group.Processes);
        Assert.Equal(2, repairs.Count);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(99999, 1440)]
    public void Out_of_range_budget_is_clamped(int input, int expected)
    {
        var group = T.Group("Games", new[] { "game" }, CellState.Free);
        var settings = T.Settings(60, group);
        group.BudgetMinutesByDay[(int)DayOfWeek.Tuesday] = input; // set directly: SetBudgetMinutes would clamp it

        var repairs = SettingsRules.Sanitize(settings);

        Assert.Equal(expected, group.BudgetMinutesFor(DayOfWeek.Tuesday));
        Assert.Equal(60, group.BudgetMinutesFor(DayOfWeek.Monday)); // the other days are left alone
        var repair = Assert.Single(repairs);
        Assert.Contains("Tuesday", repair);
        Assert.Contains("Games", repair); // says which group it was
    }

    [Fact]
    public void Each_groups_budgets_are_repaired_on_their_own()
    {
        var a = T.Group("A", new[] { "a" }, CellState.Free);
        var b = T.Group("B", new[] { "b" }, CellState.Free);
        var settings = T.Settings(60, a, b);
        a.BudgetMinutesByDay = new[] { 60, 60, 60, 60, 60, 60, 99999 };
        b.BudgetMinutesByDay = new[] { 5 }; // too few

        SettingsRules.Sanitize(settings);

        Assert.Equal(1440, a.BudgetMinutesFor(DayOfWeek.Saturday));
        Assert.Equal(new[] { 5, 120, 120, 120, 120, 120, 120 }, b.BudgetMinutesByDay);
    }

    [Fact]
    public void Duplicate_and_blank_group_names_are_made_unique()
    {
        var a = T.Group("Games", Array.Empty<string>(), CellState.Free);
        var b = T.Group("games", Array.Empty<string>(), CellState.Free);
        var c = T.Group("  ", Array.Empty<string>(), CellState.Free);

        SettingsRules.Sanitize(T.Settings(60, a, b, c));

        Assert.Equal("Games", a.Name);
        Assert.Equal("games (2)", b.Name);
        Assert.Equal("Group", c.Name);
    }

    [Fact]
    public void Duplicate_group_ids_are_repaired()
    {
        var a = T.Group("A", Array.Empty<string>(), CellState.Free);
        var b = T.Group("B", Array.Empty<string>(), CellState.Free);
        b.Id = a.Id;

        SettingsRules.Sanitize(T.Settings(60, a, b));

        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void Invalid_colors_fall_back_to_the_default()
    {
        var group = T.Group("A", Array.Empty<string>(), CellState.Free);
        group.Color = "red";

        SettingsRules.Sanitize(T.Settings(60, group));

        Assert.Equal(AppGroup.DefaultColor, group.Color);
    }

    [Fact]
    public void Sanitize_repairs_the_schedule_too()
    {
        var group = T.Group("A", Array.Empty<string>(), CellState.Free);
        group.Schedule = new WeeklySchedule { Days = new[] { "F" } };

        SettingsRules.Sanitize(T.Settings(60, group));

        Assert.Equal(7, group.Schedule.Days.Length);
    }

    // ---- Warnings ----

    [Fact]
    public void Warnings_are_distinct_positive_descending_and_capped_at_three()
    {
        var result = SettingsRules.SanitizeWarnings(new[] { 2, 10, 0, -4, 10, 5, 1 });

        Assert.Equal(new[] { 10, 5, 2 }, result);
    }

    // ---- CanAddProcess / CanRename ----

    [Fact]
    public void Can_add_a_new_process()
    {
        var group = T.Group("A", Array.Empty<string>(), CellState.Free);

        Assert.Null(SettingsRules.CanAddProcess(T.Settings(60, group), group, "game.exe"));
    }

    [Fact]
    public void Adding_a_process_from_another_group_names_that_group()
    {
        var a = T.Group("Shooters", new[] { "game" }, CellState.Free);
        var b = T.Group("Other", Array.Empty<string>(), CellState.Free);

        var error = SettingsRules.CanAddProcess(T.Settings(60, a, b), b, "GAME");

        Assert.NotNull(error);
        Assert.Contains("Shooters", error);
    }

    [Fact]
    public void Adding_a_process_already_in_the_same_group_is_refused()
    {
        var group = T.Group("A", new[] { "game" }, CellState.Free);

        var error = SettingsRules.CanAddProcess(T.Settings(60, group), group, "game");

        Assert.Contains("already in this group", error);
    }

    [Theory]
    [InlineData("explorer")]
    [InlineData("Explorer.EXE")]
    [InlineData("svchost")]
    [InlineData("HardCap")]
    public void Core_windows_processes_cannot_be_added(string name)
    {
        var group = T.Group("A", Array.Empty<string>(), CellState.Free);

        Assert.NotNull(SettingsRules.CanAddProcess(T.Settings(60, group), group, name));
    }

    [Fact]
    public void Blank_process_names_cannot_be_added()
    {
        var group = T.Group("A", Array.Empty<string>(), CellState.Free);

        Assert.NotNull(SettingsRules.CanAddProcess(T.Settings(60, group), group, "   "));
    }

    [Fact]
    public void Rename_rejects_blank_and_clashing_names_but_allows_keeping_your_own()
    {
        var a = T.Group("Games", Array.Empty<string>(), CellState.Free);
        var b = T.Group("Work", Array.Empty<string>(), CellState.Free);
        var settings = T.Settings(60, a, b);

        Assert.NotNull(SettingsRules.CanRename(settings, a, "  "));
        Assert.NotNull(SettingsRules.CanRename(settings, a, "work"));
        Assert.Null(SettingsRules.CanRename(settings, a, "GAMES"));
        Assert.Null(SettingsRules.CanRename(settings, a, "Shooters"));
    }
}
