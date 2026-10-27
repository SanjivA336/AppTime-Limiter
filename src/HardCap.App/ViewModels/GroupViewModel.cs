using System.Collections.ObjectModel;
using System.Windows.Media;
using HardCap.App.Controls;
using HardCap.App.Services;
using HardCap.Core.Enforcement;
using HardCap.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HardCap.App.ViewModels;

/// <summary>
/// One group as its card shows it: name, color, first few apps, and live status. It only displays. Changing a
/// group happens in <see cref="GroupEditorViewModel"/>, on a copy, and reaches the real group only when saved.
/// </summary>
public sealed partial class GroupViewModel : ObservableObject
{
    public static readonly string[] Palette =
    {
        "#4F8CFF", "#F2994A", "#EB5757", "#27AE60", "#BB6BD9", "#2D9CDB", "#F2C94C", "#56CCF2",
    };

    /// <summary>How many apps a card lists by name before it falls back to "+N".</summary>
    private const int AppsShownOnCard = 3;

    /// <summary>
    /// How quickly the bar settles on a new length: about two thirds of the way there after this many seconds, and
    /// nearly all of it after three times as long. It is close to the 3-second check interval, so the bar keeps
    /// gliding between checks and doesn't move in separate little steps.
    /// </summary>
    private const double BarSettleSeconds = 1.5;

    /// <summary>The bar is drawn at its goal once it is this close (a fraction of the bar, so well under one pixel).</summary>
    private const double CloseEnough = 0.0005;

    private readonly MainViewModel _main;

    // False until the first status is worked out. The very first length is shown at once, not glided to from empty.
    private bool _barHasBeenShown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunningSuffix))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    private CellState _currentState;

    /// <summary>"Free", "Blocked", or "Limited · 1h 12m left".</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>True when any cell in this group's schedule is Limited, so the card gets a budget bar.</summary>
    [ObservableProperty]
    private bool _hasLimitedTime;

    [ObservableProperty]
    private string _moreAppsText = string.Empty;

    /// <summary>
    /// How full the card's bar is, from 0 to 1. It only goes down while the group is Limited, as the day's budget is
    /// spent. In Free and Blocked nothing is being spent, so it stays full. Once the budget is used up it is empty.
    /// </summary>
    [ObservableProperty]
    private double _budgetFraction;

    /// <summary>
    /// The length the bar is actually drawn at. It glides toward <see cref="BudgetFraction"/> rather than jumping to it,
    /// so the bar eases down smoothly. See <see cref="StepDisplayedFraction"/>.
    /// </summary>
    [ObservableProperty]
    private double _displayedFraction;

    /// <summary>What the card's bar means, shown when you hover over it.</summary>
    [ObservableProperty]
    private string _budgetBarTooltip = string.Empty;

    public GroupViewModel(AppGroup model, MainViewModel main)
    {
        Model = model;
        _main = main;
        HasLimitedTime = model.Schedule.HasLimitedCells();
        RebuildAppChips();
    }

    public AppGroup Model { get; }

    /// <summary>The first few apps (alphabetically) shown on the group's card.</summary>
    public ObservableCollection<AppChip> AppChips { get; } = new();

    /// <summary>
    /// True while the daily budget is actually going down, as the engine reports it on each check: a Limited cell, not
    /// paused, budget left, and time that counts under the group's count mode. The card's bar shows moving stripes and a
    /// pulsing glow only then.
    /// </summary>
    [ObservableProperty]
    private bool _isBudgetDraining;

    /// <summary>" · Running" while one of the group's apps is open, otherwise nothing. It is added after the status text.</summary>
    public string RunningSuffix => IsRunning ? " · Running" : string.Empty;

    public bool HasMoreApps => MoreAppsText.Length > 0;

    public bool HasNoApps => Model.Processes.Count == 0;

    public string Name => Model.Name;

    public Brush ColorBrush => Swatch(Model.Color);

    /// <summary>The color that goes with the current state: green, blue or red.</summary>
    public Brush StatusBrush => ScheduleGridControl.BrushFor(CurrentState);

    public static Brush Swatch(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Picks up changes made to the real group (after the editor saved): name, color, apps, and whether it has
    /// Limited time. The caller then refreshes the status.
    /// </summary>
    public void RefreshFromModel()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ColorBrush));
        HasLimitedTime = Model.Schedule.HasLimitedCells();
        RebuildAppChips();
    }

    /// <summary>
    /// Works out what the card should say right now.
    ///   Free: when that ends ("Free until today at 6:00 PM").
    ///   Limited: how much of today's budget is left.
    ///   Blocked: when the apps can run again ("Blocked until tomorrow at 8:00 PM").
    /// A Limited cell with no budget left counts as Blocked, because the app is closed.
    /// </summary>
    public void RefreshStatus(DateTime now, double budgetSecondsLeft)
    {
        // A paused group is treated as Free: nothing is limited, and nothing is charged to the budget.
        var isPaused = Model.IsPausedAt(now);
        var scheduledState = isPaused ? CellState.Free : Model.Schedule.GetState(now);
        var budgetIsUsedUp = scheduledState == CellState.Limited && budgetSecondsLeft <= 0;

        CurrentState = budgetIsUsedUp ? CellState.Blocked : scheduledState;

        var budgetTodaySeconds = Model.BudgetMinutesFor(_main.BudgetDayOfWeek(now)) * 60.0; // this group's own budget
        var budgetLeftFraction = budgetTodaySeconds <= 0 ? 0 : Math.Clamp(budgetSecondsLeft / budgetTodaySeconds, 0, 1);
        BudgetFraction = CurrentState switch
        {
            CellState.Limited => budgetLeftFraction,      // draining: the bar shows what's left
            _ when budgetIsUsedUp => 0,                   // it drained and ran out
            _ => 1,                                       // Free or Blocked by the schedule: nothing is being spent
        };

        if (!_barHasBeenShown)
        {
            DisplayedFraction = BudgetFraction;
            _barHasBeenShown = true;
        }

        BudgetBarTooltip = budgetTodaySeconds <= 0
            ? "No budget set for today"
            : $"{TimeText.Compact(TimeSpan.FromSeconds(budgetSecondsLeft))} of today's {TimeText.Compact(TimeSpan.FromSeconds(budgetTodaySeconds))} budget left";

        StatusText = isPaused
            ? PausedStatusText(now)
            : CurrentState switch
            {
                CellState.Limited => $"Limited · {TimeText.Compact(TimeSpan.FromSeconds(budgetSecondsLeft))} left",
                CellState.Blocked => BlockedStatusText(now, budgetSecondsLeft, budgetIsUsedUp),
                _ => FreeStatusText(now),
            };
    }

    /// <summary>"Paused until tomorrow at 8:00 AM", or "Paused until you resume" when there is no end time.</summary>
    private string PausedStatusText(DateTime now) =>
        Model.PausedUntil is DateTime until ? $"Paused until {TimeText.When(until, now)}" : "Paused until you resume";

    /// <summary>Whenever the bar's goal changes, start gliding toward it (except the very first time, which just appears).</summary>
    partial void OnBudgetFractionChanged(double value)
    {
        if (_barHasBeenShown)
        {
            BarAnimator.Start(this);
        }
    }

    /// <summary>
    /// Moves the drawn bar a little closer to its goal to show <paramref name="seconds"/> passing. The further away it
    /// is, the faster it moves, and it slows to a stop as it arrives. Returns true while it still has further to go.
    /// </summary>
    public bool StepDisplayedFraction(double seconds)
    {
        var next = Smoothing.Approach(DisplayedFraction, BudgetFraction, seconds, BarSettleSeconds);
        if (Math.Abs(next - BudgetFraction) < CloseEnough)
        {
            next = BudgetFraction;
        }

        DisplayedFraction = next;
        return next != BudgetFraction;
    }

    /// <summary>"Free", or "Free until tomorrow at 8:00 PM" when the schedule is going to change.</summary>
    private string FreeStatusText(DateTime now)
    {
        var change = Model.Schedule.NextChange(now);
        return change is null ? "Free" : $"Free until {TimeText.When(change.At, now)}";
    }

    /// <summary>
    /// "Blocked until ..." with the next moment the apps are allowed to run. That is not always the next
    /// change in the schedule: a Limited cell is no use if the budget is already spent, so this waits for the
    /// next Free cell, or for a Limited cell on a day whose budget has reset.
    /// </summary>
    private string BlockedStatusText(DateTime now, double budgetSecondsLeft, bool budgetIsUsedUp)
    {
        var allowedAt = LimitForecast.NextAllowedTime(
            Model.Schedule, now, budgetSecondsLeft, Model.BudgetMinutesFor, _main.Settings.ResetMinutes);
        var text = allowedAt is DateTime when ? $"Blocked until {TimeText.When(when, now)}" : "Blocked";
        return budgetIsUsedUp ? $"{text} · budget used up" : text;
    }

    /// <summary>Rebuilds the "first three apps, then +N" strip on the card.</summary>
    public void RebuildAppChips()
    {
        var namesAlphabetically = Model.Processes.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();

        AppChips.Clear();
        foreach (var name in namesAlphabetically.Take(AppsShownOnCard))
        {
            AppChips.Add(new AppChip(name, _main.Icons.GetIcon(name)));
        }

        var hiddenCount = namesAlphabetically.Count - AppChips.Count;
        MoreAppsText = hiddenCount > 0 ? $"+{hiddenCount}" : string.Empty;
        OnPropertyChanged(nameof(HasMoreApps));
        OnPropertyChanged(nameof(HasNoApps));
    }
}
