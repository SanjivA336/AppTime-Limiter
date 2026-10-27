using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Curfew.Core.Models;
using Curfew.Core.Usage;

namespace Curfew.App.ViewModels;

/// <summary>One group in the selector row at the top of the Analytics page.</summary>
public sealed partial class GroupChoice : ObservableObject
{
    private readonly Action _selectionChanged;

    [ObservableProperty]
    private bool _isSelected;

    public GroupChoice(AppGroup group, Action selectionChanged)
    {
        Id = group.Id;
        Name = group.Name;
        ColorBrush = GroupViewModel.Swatch(group.Color);
        _selectionChanged = selectionChanged;
    }

    public Guid Id { get; }

    public string Name { get; }

    public Brush ColorBrush { get; }

    partial void OnIsSelectedChanged(bool value) => _selectionChanged();
}

/// <summary>One entry of the Day / Week / Month box.</summary>
public sealed record ViewChoice(AnalyticsView View, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// One bar in the horizontal chart: a group (whose apps show when you point at it) or a single app. The bar is as long as
/// its time compared with the longest row.
/// </summary>
public sealed class UsageRow
{
    public required string Name { get; init; }

    /// <summary>Smaller text under the name: for an app, the group it is in.</summary>
    public string Subtitle { get; init; } = string.Empty;

    public required Brush ColorBrush { get; init; }

    public double Fraction { get; init; }

    public required string TimeText { get; init; }

    /// <summary>The apps a group is made of. They fade in under the bar when the pointer is over the group.</summary>
    public IReadOnlyList<UsageRow> Apps { get; init; } = Array.Empty<UsageRow>();

    public bool HasApps => Apps.Count > 0;

    public bool HasSubtitle => Subtitle.Length > 0;
}

/// <summary>
/// The Analytics page: how much time went on the chosen groups, for a day, a week or a month. It shows a headline average
/// with how it compares with earlier weeks, a chart that depends on the view (a ring for a day, stacked columns for a
/// week, a calendar of pies for a month), and a bar for each group or app. The numbers come from
/// <see cref="UsageAnalytics"/>; this class chooses what to show and how to word it.
/// </summary>
public sealed partial class AnalyticsViewModel : ObservableObject
{
    private static readonly Brush UpBrush = GroupViewModel.Swatch("#EB5757");   // using more than before
    private static readonly Brush DownBrush = GroupViewModel.Swatch("#27AE60"); // using less than before
    private static readonly Brush SameBrush = GroupViewModel.Swatch("#9AA0A6");

    private readonly MainViewModel _main;
    private bool _isRebuildingChoices;
    private bool _isMoving;
    private PeriodReport? _report;

    /// <summary>Whether each bar is a group (apps appear on hover) or a single app.</summary>
    [ObservableProperty]
    private bool _showApps;

    // ---- What is being looked at ----

    [ObservableProperty]
    private ViewChoice _selectedViewChoice;

    /// <summary>Any date inside the period shown (the day itself, a day of the week, a day of the month).</summary>
    [ObservableProperty]
    private DateOnly _anchor;

    [ObservableProperty]
    private string _periodTitle = string.Empty;

    [ObservableProperty]
    private string _periodRange = string.Empty;

    [ObservableProperty]
    private string _currentButtonText = "This week";

    [ObservableProperty]
    private string _chartTitle = string.Empty;

    // ---- The headline numbers ----

    [ObservableProperty]
    private string _heroLabel = "Daily average";

    [ObservableProperty]
    private string _heroText = "0m";

    [ObservableProperty]
    private string _tile1Label = string.Empty;

    [ObservableProperty]
    private string _tile1Text = string.Empty;

    [ObservableProperty]
    private string _tile2Label = string.Empty;

    [ObservableProperty]
    private string _tile2Text = string.Empty;

    [ObservableProperty]
    private string _tile3Label = string.Empty;

    [ObservableProperty]
    private string _tile3Text = string.Empty;

    [ObservableProperty]
    private string _changeText = string.Empty;

    [ObservableProperty]
    private string _changeCaption = string.Empty;

    [ObservableProperty]
    private Brush _changeBrush = SameBrush;

    [ObservableProperty]
    private bool _hasChange;

    // ---- Empty states ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    private string _emptyTitle = string.Empty;

    [ObservableProperty]
    private string _emptyMessage = string.Empty;

    [ObservableProperty]
    private string _rowsNote = string.Empty;

    public AnalyticsViewModel(MainViewModel main)
    {
        _main = main;
        ViewChoices = new[]
        {
            new ViewChoice(AnalyticsView.Day, "Day"),
            new ViewChoice(AnalyticsView.Week, "Week"),
            new ViewChoice(AnalyticsView.Month, "Month"),
        };
        _selectedViewChoice = ViewChoices[1];
        _anchor = main.CurrentBudgetDay;
        Refresh();
    }

    public IReadOnlyList<ViewChoice> ViewChoices { get; }

    /// <summary>The groups that can be picked. All are picked to begin with.</summary>
    public ObservableCollection<GroupChoice> Choices { get; } = new();

    /// <summary>The bars of the horizontal chart.</summary>
    public ObservableCollection<UsageRow> Rows { get; } = new();

    public AnalyticsView View => SelectedViewChoice.View;

    public bool IsDayView => View == AnalyticsView.Day;

    public bool IsWeekView => View == AnalyticsView.Week;

    public bool IsMonthView => View == AnalyticsView.Month;

    // ---- What the charts draw ----

    /// <summary>The days of the period: seven for a week, the whole month for a month, one for a day.</summary>
    public IReadOnlyList<DayUsageSlice> Days => _report?.Days ?? Array.Empty<DayUsageSlice>();

    /// <summary>
    /// The columns of the stacked bar chart: the seven days of a week, or the 24 hours of a day. (Empty for a month, which
    /// has its own calendar.)
    /// </summary>
    public IReadOnlyList<ChartColumn> ChartColumns { get; private set; } = Array.Empty<ChartColumn>();

    /// <summary>Today's date, for the calendar to mark and to know which days have happened.</summary>
    public DateOnly Today => _main.CurrentBudgetDay;

    /// <summary>A note under the chart, for example that hourly detail only exists for days since it was first recorded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChartNote))]
    private string _chartNote = string.Empty;

    public bool HasChartNote => ChartNote.Length > 0;

    /// <summary>True for the views that use the stacked bar chart (a day by hour, a week by day). A month has its calendar instead.</summary>
    public bool ShowBarChart => !IsMonthView;

    /// <summary>The line above the chart, saying what its columns are.</summary>
    public string ChartHint => IsDayView
        ? "Time in each hour, split by group; point at a colour to see its apps."
        : "Hours each day, split by group; point at a colour to see its apps, or click a day to open it.";

    // ---- Selection ----

    /// <summary>True when every group is picked.</summary>
    public bool AllSelected => Choices.Count > 0 && Choices.All(c => c.IsSelected);

    /// <summary>True when no group is picked.</summary>
    public bool NoneSelected => Choices.All(c => !c.IsSelected);

    /// <summary>"Select all", or "Deselect all" once every group is already picked.</summary>
    public string SelectAllLabel => AllSelected ? "Deselect all" : "Select all";

    public IReadOnlyList<GroupChoice> Legend => Choices.Where(c => c.IsSelected).ToList();

    public bool HasGroups => Choices.Count > 0;

    public bool HasNoGroups => Choices.Count == 0;

    /// <summary>True when there is something to chart: groups are picked and they have time in the period.</summary>
    public bool ShowContent => string.IsNullOrEmpty(EmptyTitle);

    public bool ShowGroups
    {
        get => !ShowApps;
        set => ShowApps = !value;
    }

    // ---- Moving around ----

    public bool IsCurrentPeriod => _report?.IsCurrent ?? true;

    public bool CanGoForward => AnalyticsPeriod.CanStepForward(View, Anchor, Today);

    private bool CanGoCurrent => !IsCurrentPeriod;

    [RelayCommand]
    private void Previous() => Anchor = AnalyticsPeriod.Step(View, Anchor, -1);

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void Next() => Anchor = AnalyticsPeriod.Step(View, Anchor, 1);

    [RelayCommand(CanExecute = nameof(CanGoCurrent))]
    private void Current() => Anchor = Today;

    /// <summary>Shows a view that many periods back from now (0 is the current one). Used by the development screenshot mode.</summary>
    public void Navigate(AnalyticsView view, int periodsBack) =>
        Show(view, AnalyticsPeriod.Step(view, Today, -Math.Max(0, periodsBack)));

    /// <summary>Switches to the day view for that day. (A click on a day in the week chart or the month calendar.)</summary>
    public void OpenDay(DateOnly day) => Show(AnalyticsView.Day, day);

    /// <summary>Switches to the week view for the week starting on that Monday. (A click on a week number in the month calendar.)</summary>
    public void OpenWeek(DateOnly weekStart) => Show(AnalyticsView.Week, weekStart);

    private void Show(AnalyticsView view, DateOnly anchor)
    {
        // Change both, then refresh once, so the page never shows the new view around the old date.
        _isMoving = true;
        Anchor = anchor;
        SelectedViewChoice = ViewChoices.First(c => c.View == view);
        _isMoving = false;
        Refresh();
    }

    partial void OnSelectedViewChoiceChanged(ViewChoice value)
    {
        if (!_isMoving)
        {
            Refresh();
        }
    }

    partial void OnAnchorChanged(DateOnly value)
    {
        if (!_isMoving)
        {
            Refresh();
        }
    }

    partial void OnShowAppsChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowGroups));
        RebuildRows();
    }

    /// <summary>Picks every group, or, if every group is already picked, none.</summary>
    [RelayCommand]
    private void ToggleAll()
    {
        var pickAll = !AllSelected;

        _isRebuildingChoices = true;
        foreach (var choice in Choices)
        {
            choice.IsSelected = pickAll;
        }

        _isRebuildingChoices = false;
        OnSelectionChanged();
    }

    /// <summary>Re-reads the usage record and updates everything on the page. Called when the page opens and after each check.</summary>
    public void Refresh()
    {
        SyncChoicesWithGroups();
        var selected = Choices.Where(c => c.IsSelected).Select(c => c.Id).ToHashSet();
        _report = _main.BuildUsageReport(selected, View, Anchor);

        var (title, range) = AnalyticsPeriod.Describe(View, Anchor, Today);
        PeriodTitle = title;
        PeriodRange = range;
        CurrentButtonText = AnalyticsPeriod.CurrentButtonText(View);
        ChartTitle = $"{title} · {range}";

        DescribeNumbers(_report);
        DescribeChange(_report);
        DescribeEmptyState(_report);
        RebuildRows();
        RebuildColumns(_report);

        OnPropertyChanged(nameof(View));
        OnPropertyChanged(nameof(IsDayView));
        OnPropertyChanged(nameof(IsWeekView));
        OnPropertyChanged(nameof(IsMonthView));
        OnPropertyChanged(nameof(ShowBarChart));
        OnPropertyChanged(nameof(ChartHint));
        OnPropertyChanged(nameof(Days));
        OnPropertyChanged(nameof(ChartColumns));
        OnPropertyChanged(nameof(Today));
        OnPropertyChanged(nameof(IsCurrentPeriod));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(Legend));
        NextCommand.NotifyCanExecuteChanged();
        CurrentCommand.NotifyCanExecuteChanged();
    }

    private void OnSelectionChanged()
    {
        if (_isRebuildingChoices)
        {
            return;
        }

        OnPropertyChanged(nameof(AllSelected));
        OnPropertyChanged(nameof(NoneSelected));
        OnPropertyChanged(nameof(SelectAllLabel));
        Refresh();
    }

    /// <summary>
    /// Keeps the selector in step with the real groups (one may have been added, renamed or removed). A new group is picked
    /// only if every other group was, so leaving everything unpicked isn't undone by adding a group.
    /// </summary>
    private void SyncChoicesWithGroups()
    {
        var groups = _main.Settings.Groups;
        var unchanged = groups.Count == Choices.Count
            && groups.Zip(Choices, (group, choice) => group.Id == choice.Id && group.Name == choice.Name).All(same => same);
        if (unchanged)
        {
            return;
        }

        var wasEverythingPicked = Choices.Count == 0 || Choices.All(c => c.IsSelected);
        var previous = Choices.ToDictionary(c => c.Id, c => c.IsSelected);

        _isRebuildingChoices = true;
        Choices.Clear();
        foreach (var group in groups)
        {
            var isPicked = previous.TryGetValue(group.Id, out var was) ? was : wasEverythingPicked;
            Choices.Add(new GroupChoice(group, OnSelectionChanged) { IsSelected = isPicked });
        }

        _isRebuildingChoices = false;
        OnPropertyChanged(nameof(AllSelected));
        OnPropertyChanged(nameof(NoneSelected));
        OnPropertyChanged(nameof(SelectAllLabel));
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(HasNoGroups));
    }

    // ---- Wording ----

    /// <summary>The headline and the three smaller figures, worded for the view.</summary>
    private void DescribeNumbers(PeriodReport report)
    {
        switch (report.View)
        {
            case AnalyticsView.Day:
                HeroLabel = report.IsCurrent ? "Today so far" : "Total";
                HeroText = Duration(report.TotalSeconds);

                var top = report.Groups.FirstOrDefault(g => g.Seconds > 0);
                Tile1Label = top is null ? "Busiest group" : $"Busiest: {top.Name}";
                Tile1Text = top is null ? "—" : Duration(top.Seconds);

                var weekContainsToday = AnalyticsPeriod.Contains(AnalyticsView.Week, report.Start, report.Today);
                Tile2Label = weekContainsToday ? "This week so far" : "That week's total";
                Tile2Text = Duration(report.ContainingWeekSeconds);

                Tile3Label = "Typical day";
                Tile3Text = report.PreviousDailyAverageSeconds is double typicalDay ? Duration(typicalDay) : "—";
                break;

            case AnalyticsView.Week:
                HeroLabel = "Daily average";
                HeroText = Duration(report.DailyAverageSeconds);

                if (report.IsCurrent)
                {
                    Tile1Label = "Today";
                    Tile1Text = Duration(report.TodaySeconds);
                    Tile2Label = "This week so far";
                }
                else
                {
                    Tile1Label = "Busiest day";
                    Tile1Text = BusiestDayText(report, "ddd");
                    Tile2Label = "Week total";
                }

                Tile2Text = Duration(report.TotalSeconds);
                Tile3Label = "Average week";
                Tile3Text = report.PreviousWeeklyAverageSeconds is double typicalWeek ? Duration(typicalWeek) : "—";
                break;

            default:
                HeroLabel = "Daily average";
                HeroText = Duration(report.DailyAverageSeconds);

                Tile1Label = "Busiest day";
                Tile1Text = BusiestDayText(report, "MMM d");
                Tile2Label = report.IsCurrent ? "This month so far" : "Month total";
                Tile2Text = Duration(report.TotalSeconds);
                Tile3Label = "Average week";
                Tile3Text = Duration(report.DailyAverageSeconds * 7);
                break;
        }
    }

    private static string BusiestDayText(PeriodReport report, string dateFormat) =>
        report.BusiestDay is DateOnly day
            ? $"{Duration(report.BusiestDaySeconds)} ({day.ToString(dateFormat, CultureInfo.InvariantCulture)})"
            : "—";

    /// <summary>Words and colour for the small "+12%" indicator.</summary>
    private void DescribeChange(PeriodReport report)
    {
        if (report.ChangePercent is not double percent || report.PreviousDailyAverageSeconds is not double before)
        {
            HasChange = false;
            ChangeText = "—";
            ChangeCaption = "Not enough earlier history to compare with yet.";
            ChangeBrush = SameBrush;
            return;
        }

        HasChange = true;
        var rounded = Math.Round(percent);
        if (rounded == 0)
        {
            ChangeText = "No change";
            ChangeBrush = SameBrush;
        }
        else
        {
            ChangeText = $"{(rounded > 0 ? "▲" : "▼")} {Math.Abs(rounded):0}%";
            ChangeBrush = rounded > 0 ? UpBrush : DownBrush;
        }

        ChangeCaption = $"vs. your {Duration(before)} a day average over earlier weeks";
    }

    /// <summary>The message shown instead of the charts when there is nothing to chart.</summary>
    private void DescribeEmptyState(PeriodReport report)
    {
        if (NoneSelected && HasGroups)
        {
            EmptyTitle = "No groups selected";
            EmptyMessage = "Pick one or more groups above to see their time.";
            return;
        }

        if (report.TotalSeconds <= 0)
        {
            var noun = report.View switch { AnalyticsView.Day => "today", AnalyticsView.Week => "this week", _ => "this month" };
            var what = report.IsCurrent ? noun : PeriodRange;
            EmptyTitle = $"No data for {what}";
            EmptyMessage = report.IsCurrent
                ? "Nothing has been recorded yet; time appears once an app in the selected groups has been running."
                : "Nothing was recorded for the selected groups then; use the arrows to look at another period.";
            return;
        }

        EmptyTitle = string.Empty;
        EmptyMessage = string.Empty;
    }

    /// <summary>Builds the stacked chart's columns for the view, and the note under it.</summary>
    private void RebuildColumns(PeriodReport report)
    {
        var culture = CultureInfo.InvariantCulture;
        ChartNote = string.Empty;

        switch (report.View)
        {
            case AnalyticsView.Day:
                var thisHour = DateTime.Now.Hour;
                ChartColumns = report.Hours
                    .Select(hour => new ChartColumn(
                        HourLabel(hour.ClockHour), HourTitle(hour.ClockHour), hour.Groups, report.IsCurrent && hour.ClockHour == thisHour, null))
                    .ToList();

                if (report.TotalSeconds > 0 && report.Hours.All(h => h.TotalSeconds == 0))
                {
                    ChartNote = "Hour-by-hour detail is only recorded from now on; earlier days just have totals.";
                }

                break;

            case AnalyticsView.Week:
                ChartColumns = report.Days
                    .Select(day => new ChartColumn(
                        day.Date.ToString("ddd", culture), day.Date.ToString("ddd MMM d", culture), day.Groups, day.Date == report.Today,
                        day.Date <= report.Today ? day.Date : null))
                    .ToList();
                break;

            default:
                ChartColumns = Array.Empty<ChartColumn>();
                break;
        }
    }

    /// <summary>"12a", "3a", "12p", "3p": the short label under an hour's column.</summary>
    private static string HourLabel(int clockHour) => $"{HourOnClock(clockHour)}{(clockHour < 12 ? 'a' : 'p')}";

    /// <summary>"3 PM to 4 PM": an hour in words, for the hover card.</summary>
    private static string HourTitle(int clockHour) =>
        $"{HourOnClock(clockHour)} {(clockHour < 12 ? "AM" : "PM")} to {HourOnClock((clockHour + 1) % 24)} {((clockHour + 1) % 24 < 12 ? "AM" : "PM")}";

    private static int HourOnClock(int clockHour) => clockHour % 12 == 0 ? 12 : clockHour % 12;

    // ---- The horizontal bars ----

    /// <summary>Builds the horizontal chart for the period and kind of bar.</summary>
    private void RebuildRows()
    {
        Rows.Clear();
        RowsNote = string.Empty;
        if (_report is null)
        {
            return;
        }

        var groups = _report.Groups;
        var rows = ShowApps ? AppRows(groups) : GroupRows(groups);

        var longest = rows.Count == 0 ? 0 : rows.Max(r => r.Seconds);
        foreach (var (row, seconds) in rows)
        {
            Rows.Add(WithFraction(row, longest == 0 ? 0 : seconds / longest));
        }

        var groupTime = groups.Sum(g => g.Seconds);
        var appTime = groups.Sum(g => g.Apps.Sum(a => a.Seconds));
        if (groupTime > 0 && appTime == 0)
        {
            RowsNote = "App detail is only recorded from now on; earlier days just have totals.";
        }
    }

    private static List<(UsageRow Row, double Seconds)> GroupRows(IReadOnlyList<GroupUsage> groups) =>
        groups.Select(group => (
            Row: new UsageRow
            {
                Name = group.Name,
                ColorBrush = GroupViewModel.Swatch(group.Color),
                TimeText = Duration(group.Seconds),
                Apps = AppsOf(group),
            },
            Seconds: group.Seconds)).ToList();

    private static List<(UsageRow Row, double Seconds)> AppRows(IReadOnlyList<GroupUsage> groups) =>
        groups.SelectMany(group => group.Apps.Select(app => (
                Row: new UsageRow
                {
                    Name = app.Name,
                    Subtitle = group.Name,
                    ColorBrush = GroupViewModel.Swatch(group.Color),
                    TimeText = Duration(app.Seconds),
                },
                Seconds: app.Seconds)))
            .OrderByDescending(x => x.Seconds)
            .ThenBy(x => x.Row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The apps of one group, shown when it is pointed at. Apps with no time are left out.</summary>
    private static List<UsageRow> AppsOf(GroupUsage group)
    {
        var brush = GroupViewModel.Swatch(group.Color);
        var used = group.Apps.Where(a => a.Seconds > 0).ToList();
        if (used.Count == 0)
        {
            var note = group.Seconds > 0 ? "No app detail recorded for these days" : "Nothing recorded";
            return new List<UsageRow> { new() { Name = note, ColorBrush = brush, TimeText = string.Empty } };
        }

        var longest = used.Max(a => a.Seconds);
        return used
            .Select(app => new UsageRow { Name = app.Name, ColorBrush = brush, Fraction = app.Seconds / longest, TimeText = Duration(app.Seconds) })
            .ToList();
    }

    private static UsageRow WithFraction(UsageRow row, double fraction) => new()
    {
        Name = row.Name,
        Subtitle = row.Subtitle,
        ColorBrush = row.ColorBrush,
        Fraction = fraction,
        TimeText = row.TimeText,
        Apps = row.Apps,
    };

    /// <summary>"2h 30m", "45m", "&lt;1m", "0m".</summary>
    public static string Duration(double seconds) => TimeText.Compact(TimeSpan.FromSeconds(seconds));
}
