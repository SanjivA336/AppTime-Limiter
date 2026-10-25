using HardCap.Core.Models;
using HardCap.Core.Settings;

namespace HardCap.Tests;

public class GroupEditSessionTests
{
    private static (AppSettings Settings, AppGroup Group) Setup()
    {
        var group = T.Group("Games", new[] { "game", "other" }, CellState.Free, 10, 5);
        group.Color = "#EB5757";
        var settings = T.Settings(120, group, T.Group("Work", new[] { "editor" }, CellState.Free));
        return (settings, group);
    }

    // ---- AppGroup copying and comparing ----

    [Fact]
    public void A_clone_is_equal_but_fully_independent()
    {
        var (_, group) = Setup();

        var clone = group.Clone();

        Assert.True(clone.IsSameAs(group));
        clone.Name = "Changed";
        clone.Processes.Add("new");
        clone.WarningMinutes.Clear();
        clone.Schedule.SetState(DayOfWeek.Monday, 0, CellState.Blocked);
        Assert.Equal("Games", group.Name);
        Assert.Equal(2, group.Processes.Count);
        Assert.Equal(2, group.WarningMinutes.Count);
        Assert.Equal(CellState.Free, group.Schedule.GetState(DayOfWeek.Monday, 0));
    }

    [Fact]
    public void IsSameAs_notices_each_kind_of_difference()
    {
        var (_, group) = Setup();

        foreach (var change in new Action<AppGroup>[]
        {
            g => g.Name = "Other",
            g => g.Color = "#000000",
            g => g.Processes.Add("x"),
            g => g.WarningMinutes[0] = 99,
            g => g.Schedule.SetState(DayOfWeek.Friday, 10, CellState.Limited),
        })
        {
            var edited = group.Clone();
            change(edited);
            Assert.False(edited.IsSameAs(group));
        }
    }

    [Fact]
    public void CopyFrom_copies_everything_but_the_id()
    {
        var (_, group) = Setup();
        var other = new AppGroup { Name = "Elsewhere", Color = "#112233", Processes = new List<string> { "z" } };

        other.CopyFrom(group);

        Assert.Equal("Games", other.Name);
        Assert.Equal(new[] { "game", "other" }, other.Processes);
        Assert.NotEqual(group.Id, other.Id);
        Assert.NotSame(group.Schedule, other.Schedule);
    }

    // ---- The edit session ----

    [Fact]
    public void A_new_session_has_no_changes()
    {
        var (settings, group) = Setup();

        Assert.False(new GroupEditSession(settings, group).HasChanges);
    }

    [Fact]
    public void Editing_the_draft_leaves_the_real_group_and_budgets_untouched()
    {
        var (settings, group) = Setup();
        var session = new GroupEditSession(settings, group);

        session.Draft.Name = "Renamed";
        session.Draft.Processes.Add("brand-new");
        session.Draft.Schedule.SetState(DayOfWeek.Monday, 0, CellState.Blocked);
        session.Draft.WarningMinutes.Clear();
        session.Draft.SetBudgetMinutes(DayOfWeek.Saturday, 999);

        Assert.True(session.HasChanges);
        Assert.Equal("Games", group.Name);
        Assert.Equal(new[] { "game", "other" }, group.Processes);
        Assert.Equal(CellState.Free, group.Schedule.GetState(DayOfWeek.Monday, 0));
        Assert.Equal(new[] { 10, 5 }, group.WarningMinutes);
        Assert.Equal(120, group.BudgetMinutesFor(DayOfWeek.Saturday));
    }

    [Fact]
    public void Commit_copies_the_draft_into_the_real_group_including_its_budgets()
    {
        var (settings, group) = Setup();
        var session = new GroupEditSession(settings, group);
        session.Draft.Name = "Renamed";
        session.Draft.Schedule.SetState(DayOfWeek.Monday, 0, CellState.Blocked);
        session.Draft.SetBudgetMinutes(DayOfWeek.Saturday, 180);

        session.Commit();

        Assert.Equal("Renamed", group.Name);
        Assert.Equal(CellState.Blocked, group.Schedule.GetState(DayOfWeek.Monday, 0));
        Assert.Equal(180, group.BudgetMinutesFor(DayOfWeek.Saturday));
        Assert.Equal(2, settings.Groups.Count);
        Assert.Same(group, settings.Groups[0]); // the real object was updated, not replaced
    }

    [Fact]
    public void Committing_one_groups_budgets_leaves_every_other_group_alone()
    {
        var (settings, group) = Setup();
        var other = settings.Groups[1];
        var session = new GroupEditSession(settings, group);

        session.Draft.SetBudgetMinutes(DayOfWeek.Saturday, 180);
        session.Commit();

        Assert.Equal(180, group.BudgetMinutesFor(DayOfWeek.Saturday));
        Assert.Equal(120, other.BudgetMinutesFor(DayOfWeek.Saturday)); // each group has its own budgets
    }

    [Fact]
    public void Revert_puts_the_draft_and_budgets_back_to_how_they_started()
    {
        var (settings, group) = Setup();
        var session = new GroupEditSession(settings, group);
        session.Draft.Name = "Renamed";
        session.Draft.Processes.Clear();
        session.Draft.Schedule.SetState(DayOfWeek.Monday, 0, CellState.Blocked);
        session.Draft.SetBudgetMinutes(DayOfWeek.Saturday, 180);

        session.Revert();

        Assert.False(session.HasChanges);
        Assert.Equal("Games", session.Draft.Name);
        Assert.Equal(new[] { "game", "other" }, session.Draft.Processes);
        Assert.Equal(CellState.Free, session.Draft.Schedule.GetState(DayOfWeek.Monday, 0));
        Assert.Equal(120, session.Draft.BudgetMinutesFor(DayOfWeek.Saturday));
    }

    [Fact]
    public void After_a_revert_the_draft_can_be_edited_and_committed_again()
    {
        var (settings, group) = Setup();
        var session = new GroupEditSession(settings, group);
        session.Draft.Name = "First try";
        session.Revert();

        session.Draft.Name = "Second try";
        session.Commit();

        Assert.Equal("Second try", group.Name);
    }

    [Fact]
    public void Changing_a_budget_back_to_its_old_value_is_not_a_change()
    {
        var (settings, group) = Setup();
        var session = new GroupEditSession(settings, group);

        session.Draft.SetBudgetMinutes(DayOfWeek.Monday, 30);
        session.Draft.SetBudgetMinutes(DayOfWeek.Monday, 120);

        Assert.False(session.HasChanges);
    }

    // ---- Validation sees the draft, not the real group ----

    [Fact]
    public void The_draft_is_what_name_checks_are_made_against()
    {
        var (settings, group) = Setup();
        var session = new GroupEditSession(settings, group);

        Assert.Null(SettingsRules.CanRename(session.DraftSettings, session.Draft, "Games"));  // keeping its own name is fine
        Assert.NotNull(SettingsRules.CanRename(session.DraftSettings, session.Draft, "work")); // another group has it
    }

    [Fact]
    public void An_app_added_to_the_draft_is_seen_as_taken_by_the_draft_and_not_by_the_real_group()
    {
        var (settings, group) = Setup();
        var session = new GroupEditSession(settings, group);
        session.Draft.Processes.Add("brand-new");

        Assert.Contains("already in this group", SettingsRules.CanAddProcess(session.DraftSettings, session.Draft, "brand-new"));
        Assert.DoesNotContain("brand-new", group.Processes);
    }

    [Fact]
    public void An_app_in_another_group_is_refused_for_the_draft()
    {
        var (settings, group) = Setup();
        var session = new GroupEditSession(settings, group);

        var error = SettingsRules.CanAddProcess(session.DraftSettings, session.Draft, "editor");

        Assert.Contains("Work", error);
    }

    [Fact]
    public void Removing_an_app_from_the_draft_frees_it_up_within_the_draft_only()
    {
        var (settings, group) = Setup();
        var session = new GroupEditSession(settings, group);

        session.Draft.Processes.Remove("game");

        Assert.Null(SettingsRules.CanAddProcess(session.DraftSettings, session.Draft, "game"));
        Assert.Contains("game", group.Processes); // the real group still has it
    }

    // ---- New groups ----

    [Fact]
    public void A_new_group_is_not_added_until_it_is_committed()
    {
        var (settings, _) = Setup();
        var created = new AppGroup { Name = "Fresh" };

        var session = new GroupEditSession(settings, created);

        Assert.True(session.IsNew);
        Assert.True(session.HasChanges); // saving would create it
        Assert.Equal(2, settings.Groups.Count);

        session.Draft.Name = "Fresh start";
        session.Commit();

        Assert.Equal(3, settings.Groups.Count);
        Assert.Same(created, settings.Groups[2]);
        Assert.Equal("Fresh start", created.Name);
        Assert.False(session.IsNew);
    }

    [Fact]
    public void Walking_away_from_a_new_group_leaves_no_trace()
    {
        var (settings, _) = Setup();
        var session = new GroupEditSession(settings, new AppGroup { Name = "Fresh" });
        session.Draft.Name = "Never saved";

        // (the user closes the window: nothing is committed)

        Assert.Equal(2, settings.Groups.Count);
    }

    [Fact]
    public void A_new_group_cannot_take_an_existing_name()
    {
        var (settings, _) = Setup();
        var session = new GroupEditSession(settings, new AppGroup { Name = "Fresh" });

        Assert.NotNull(SettingsRules.CanRename(session.DraftSettings, session.Draft, "Games"));
    }
}
