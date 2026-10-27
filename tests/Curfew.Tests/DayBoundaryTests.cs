using Curfew.Core.Enforcement;
using Curfew.Core.Models;
using Curfew.Core.Usage;

namespace Curfew.Tests;

/// <summary>
/// The check right after a new budget day begins covers a few seconds from before it began. Those seconds belong to the day
/// that was ending (and to its last hour), not to the new day, where they would look like use in a time that hasn't come yet.
/// </summary>
public class DayBoundaryTests
{
    private static readonly DateOnly Monday = new(2026, 10, 26);
    private static readonly DateOnly Tuesday = new(2026, 10, 27);
    private static readonly string[] Running = { "game" };

    private readonly UsageLedger _ledger = new();
    private readonly EnforcementEngine _engine;
    private readonly AppGroup _group = T.Group("Games", Running, CellState.Free);

    public DayBoundaryTests()
    {
        _engine = new EnforcementEngine(_ledger);
    }

    private AppSettings SettingsWithReset(int resetMinutes)
    {
        var settings = T.Settings(60, _group);
        settings.ResetMinutes = resetMinutes;
        return settings;
    }

    private void TickAcross(AppSettings settings, DateTime before, DateTime after)
    {
        _engine.Tick(settings, before, Running);   // the first check only starts the clock
        _engine.Tick(settings, after, Running);
    }

    private DayUsage Usage(DateOnly day) => _ledger.GetUsage(day, _group.Id);

    [Fact]
    public void Time_just_before_midnight_stays_on_the_day_that_was_ending()
    {
        TickAcross(SettingsWithReset(0), T.At(23, 59, 57), T.At(24, 0, 3));   // 3 seconds either side of midnight

        Assert.Equal(3, Usage(Monday).FreeSeconds, 3);
        Assert.Equal(3, Usage(Monday).Hourly![23], 3);
        Assert.Equal(3, Usage(Tuesday).FreeSeconds, 3);
        Assert.Equal(3, Usage(Tuesday).Hourly![0], 3);
    }

    [Fact]
    public void The_new_day_has_nothing_in_its_last_hour()
    {
        TickAcross(SettingsWithReset(0), T.At(23, 59, 57), T.At(24, 0, 3));

        Assert.Equal(0, Usage(Tuesday).Hourly![23], 3);
        Assert.Equal(0, Usage(Tuesday).Hourly!.Skip(1).Sum(), 3);   // only 12 AM to 1 AM has anything
    }

    [Fact]
    public void An_apps_time_is_split_the_same_way()
    {
        TickAcross(SettingsWithReset(0), T.At(23, 59, 57), T.At(24, 0, 3));

        Assert.Equal(3, Usage(Monday).AppHourly!["game"][23], 3);
        Assert.Equal(3, Usage(Tuesday).AppHourly!["game"][0], 3);
        Assert.Equal(0, Usage(Tuesday).AppHourly!["game"][23], 3);
    }

    [Fact]
    public void A_custom_reset_time_splits_at_the_reset_not_at_midnight()
    {
        TickAcross(SettingsWithReset(4 * 60), T.At(24 + 3, 59, 57), T.At(24 + 4, 0, 3));   // Tuesday 3:59:57 to 4:00:03, reset at 4 AM

        Assert.Equal(3, Usage(Monday).FreeSeconds, 3);      // Monday's budget day runs until 4 AM
        Assert.Equal(3, Usage(Monday).Hourly![3], 3);
        Assert.Equal(3, Usage(Tuesday).FreeSeconds, 3);
        Assert.Equal(0, Usage(Tuesday).Hourly![3], 3);
        Assert.Equal(3, Usage(Tuesday).Hourly![4], 3);
    }

    [Fact]
    public void Nothing_is_lost_or_added_across_the_boundary()
    {
        TickAcross(SettingsWithReset(0), T.At(23, 59, 57), T.At(24, 0, 3));

        Assert.Equal(6, Usage(Monday).FreeSeconds + Usage(Tuesday).FreeSeconds, 3);
    }

    [Fact]
    public void A_check_in_the_middle_of_a_day_is_not_split()
    {
        TickAcross(SettingsWithReset(0), T.At(12, 0, 0), T.At(12, 0, 3));

        Assert.Equal(3, Usage(Monday).FreeSeconds, 3);
        Assert.Equal(3, Usage(Monday).Hourly![12], 3);
        Assert.Equal(0, Usage(Tuesday).FreeSeconds, 3);
    }

    [Fact]
    public void A_limited_group_charges_only_the_new_days_part_to_the_new_days_budget()
    {
        var group = T.Group("Limited", Running, CellState.Limited);
        var settings = T.Settings(60, group);
        _engine.Tick(settings, T.At(23, 59, 57), Running);
        _engine.Tick(settings, T.At(24, 0, 3), Running);

        Assert.Equal(3, _ledger.GetBudgetSecondsUsed(Monday, group.Id), 3);
        Assert.Equal(3, _ledger.GetBudgetSecondsUsed(Tuesday, group.Id), 3);
    }
}
