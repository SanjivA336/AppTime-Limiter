using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using AppTimeLimiter.Core.Models;
using AppTimeLimiter.Core.Processes;
using AppTimeLimiter.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AppTimeLimiter.App.ViewModels;

/// <summary>
/// Everything the group editor window shows and changes. It works on a <b>draft</b> copy of the group (see
/// <see cref="GroupEditSession"/>), so nothing here reaches the real settings, or the enforcement loop, until
/// <see cref="Save"/> is called. Closing the window without saving simply drops this object.
/// </summary>
public sealed partial class GroupEditorViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly GroupEditSession _session;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _hasLimitedTime;

    [ObservableProperty]
    private string _usedTodayText = string.Empty;

    // ---- The lock screen ----
    // A locked group first shows only a countdown. The stopwatch (not the clock) measures it, so changing the
    // computer's clock can't skip ahead, and every time the group is opened the wait starts again from the full length.
    private readonly int _lockTotalSeconds;
    private readonly Stopwatch? _lockStopwatch;
    private readonly DispatcherTimer? _lockTimer;

    /// <summary>True while the countdown is running. The window shows only the countdown, not the group.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnlocked))]
    private bool _isLocked;

    /// <summary>What the countdown reads, such as "0:45".</summary>
    [ObservableProperty]
    private string _lockRemainingText = string.Empty;

    /// <summary>How much of the wait is left, from 1 (all of it) down to 0. It sizes the bar under the countdown.</summary>
    [ObservableProperty]
    private double _lockProgress = 1;

    public GroupEditorViewModel(MainViewModel main, GroupViewModel card)
    {
        _main = main;
        Card = card;
        _session = new GroupEditSession(main.Settings, card.Model);

        Processes = new ObservableCollection<string>();
        Warnings = new ObservableCollection<WarningViewModel>();
        DayBudgets = new ObservableCollection<DayBudgetViewModel>(
            ScheduleGridLayout.ColumnDays.Select(day => new DayBudgetViewModel(day, this)));
        RebuildListsFromDraft();
        RefreshDerivedValues();

        // The wait is the lock the group had when this window opened. (A brand-new group has no lock yet.)
        _lockTotalSeconds = _session.IsNew ? 0 : card.Model.LockSeconds;
        if (_lockTotalSeconds > 0)
        {
            IsLocked = true;
            _lockStopwatch = Stopwatch.StartNew();
            UpdateCountdown();

            _lockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _lockTimer.Tick += (_, _) => UpdateCountdown();
            _lockTimer.Start();
        }
    }

    /// <summary>The card this edits. After a save it picks up the changes.</summary>
    public GroupViewModel Card { get; }

    /// <summary>The group being edited: the draft copy, not the real one.</summary>
    public AppGroup Draft => _session.Draft;

    /// <summary>The draft's schedule. A new object after <see cref="Revert"/>, hence the change notification.</summary>
    public WeeklySchedule Schedule => Draft.Schedule;

    /// <summary>Settings with the draft standing in for the real group. Used for checks such as "is this name taken".</summary>
    public AppSettings DraftSettings => _session.DraftSettings;

    public ObservableCollection<string> Processes { get; }

    public ObservableCollection<WarningViewModel> Warnings { get; }

    /// <summary>One budget box per day, in the same order as the schedule grid's columns (Monday first).</summary>
    public ObservableCollection<DayBudgetViewModel> DayBudgets { get; }

    /// <summary>The opposite of <see cref="IsLocked"/>: the group's settings can be seen and changed.</summary>
    public bool IsUnlocked => !IsLocked;

    /// <summary>True when something differs from how the group was when the window opened. It enables Revert.</summary>
    public bool HasChanges => _session.HasChanges;

    /// <summary>A brand-new group has nothing to delete yet.</summary>
    public bool CanDelete => !_session.IsNew;

    public bool IsNoticeOpen
    {
        get => !string.IsNullOrEmpty(Message);
        set
        {
            if (!value)
            {
                Message = null;
            }
        }
    }

    public bool CanAddWarning => Warnings.Count < AppGroup.MaxWarnings;

    public string Name
    {
        get => Draft.Name;
        set
        {
            var error = SettingsRules.CanRename(DraftSettings, Draft, value);
            if (error is not null)
            {
                Message = error;

                // Put the old text back in the box. Done after the binding finishes, or WPF ignores it.
                Application.Current.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(Name)));
                return;
            }

            if (Draft.Name != value.Trim())
            {
                Draft.Name = value.Trim();
                OnPropertyChanged();
                NotifyEdited();
            }
        }
    }

    public string ColorHex
    {
        get => Draft.Color;
        set
        {
            if (Draft.Color == value)
            {
                return;
            }

            Draft.Color = value;
            OnPropertyChanged();
            NotifyEdited();
        }
    }

    partial void OnMessageChanged(string? value) => OnPropertyChanged(nameof(IsNoticeOpen));

    // ---- The lock (the length of the wait the NEXT time this group is opened) ----

    /// <summary>The minutes box. It is a double because that is what a number box uses.</summary>
    public double LockMinutes
    {
        get => LockTime.ToParts(Draft.LockSeconds).Minutes;
        set => SetLock(value, LockSecondsPart);
    }

    /// <summary>The seconds box. It steps by 5, and going past 59 or below 0 carries into the minutes.</summary>
    public double LockSecondsPart
    {
        get => LockTime.ToParts(Draft.LockSeconds).Seconds;
        set => SetLock(LockMinutes, value);
    }

    /// <summary>The chosen length in words, like "1 minute 15 seconds" or "No lock".</summary>
    public string LockSummaryText => LockTime.Describe(Draft.LockSeconds);

    /// <summary>
    /// Turns what is in the two boxes into one total (carrying and borrowing between them), stores it in the draft,
    /// and shows the tidied-up result back in both boxes.
    /// </summary>
    private void SetLock(double minutes, double seconds)
    {
        // (A box that is empty or holds something that isn't a number counts as 0.)
        static int Whole(double value) => double.IsFinite(value) ? (int)Math.Round(value) : 0;

        var total = LockTime.FromParts(Whole(minutes), Whole(seconds));
        if (total != Draft.LockSeconds)
        {
            Draft.LockSeconds = total;
            NotifyEdited();
        }

        RefreshLockBoxes();

        // A number box that was just typed into keeps what was typed unless it is told again after its own update
        // has finished (for example when "5:05" was tidied to "5:00", or "0:60" to "1:00").
        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, RefreshLockBoxes);
    }

    private void RefreshLockBoxes()
    {
        OnPropertyChanged(nameof(LockMinutes));
        OnPropertyChanged(nameof(LockSecondsPart));
        OnPropertyChanged(nameof(LockSummaryText));
    }

    private void UpdateCountdown()
    {
        var secondsLeft = _lockTotalSeconds - (_lockStopwatch?.Elapsed.TotalSeconds ?? _lockTotalSeconds);
        if (secondsLeft <= 0)
        {
            StopCountdown();
            LockRemainingText = LockTime.Format(0);
            LockProgress = 0;
            IsLocked = false; // the wait is over: show the group
            return;
        }

        // Rounded up, so it reads 1:00 at the start and 0:01 for the final second, and only hits 0:00 when it opens.
        LockRemainingText = LockTime.Format((int)Math.Ceiling(secondsLeft));
        LockProgress = secondsLeft / _lockTotalSeconds;
    }

    /// <summary>Stops the countdown timer. Called when the countdown ends and when the window closes.</summary>
    public void StopCountdown() => _lockTimer?.Stop();

    // ---- Budgets (this group's own, part of the draft like everything else) ----

    public int GetBudgetMinutes(DayOfWeek day) => Draft.BudgetMinutesFor(day);

    public void SetBudgetMinutes(DayOfWeek day, int minutes)
    {
        Draft.SetBudgetMinutes(day, minutes);
        NotifyEdited();
    }

    // ---- Apps ----

    /// <summary>Adds each name to the draft, with a message if any were refused.</summary>
    public void AddProcesses(IEnumerable<string> names)
    {
        var errors = new List<string>();
        foreach (var name in names)
        {
            var error = SettingsRules.CanAddProcess(DraftSettings, Draft, name);
            if (error is not null)
            {
                errors.Add(error);
                continue;
            }

            var normalized = ProcessNames.Normalize(name);
            Draft.Processes.Add(normalized);
            Processes.Add(normalized);
        }

        // The apps were running when picked, so this is the best moment to save their icons.
        _main.Icons.CaptureIfRunning(Draft.Processes);

        if (errors.Count > 0)
        {
            Message = string.Join(" ", errors);
        }

        NotifyEdited();
    }

    [RelayCommand]
    private void RemoveProcess(string name)
    {
        Draft.Processes.RemoveAll(p => ProcessNames.Comparer.Equals(p, name));
        Processes.Remove(name);
        NotifyEdited();
    }

    // ---- Warnings ----

    [RelayCommand(CanExecute = nameof(CanAddWarning))]
    private void AddWarning()
    {
        Warnings.Add(new WarningViewModel(SuggestNewWarningMinutes(), OnWarningsChanged));
        OnWarningsChanged();
    }

    [RelayCommand]
    private void RemoveWarning(WarningViewModel warning)
    {
        Warnings.Remove(warning);
        OnWarningsChanged();
    }

    /// <summary>Picks a sensible starting value for a new warning: the first common choice not already in use.</summary>
    private int SuggestNewWarningMinutes()
    {
        var commonChoices = new[] { 5, 10, 15, 2, 1, 30 };
        var alreadyUsed = Warnings.Select(w => (int)w.Minutes).ToHashSet();

        foreach (var minutes in commonChoices)
        {
            if (!alreadyUsed.Contains(minutes))
            {
                return minutes;
            }
        }

        return 3; // Every common choice is taken, which is only possible if there were more than three warnings.
    }

    /// <summary>
    /// Copies the warnings on screen into the draft. The draft keeps only distinct times, so if two boxes were
    /// given the same number, one of them can't be kept. Rather than leave the window showing a warning that
    /// won't be saved, the duplicate is removed from the list and a message explains why.
    /// </summary>
    private void OnWarningsChanged()
    {
        var shown = Warnings.Select(w => (int)w.Minutes).ToList();
        var kept = SettingsRules.SanitizeWarnings(shown);
        Draft.WarningMinutes = kept;

        OnPropertyChanged(nameof(CanAddWarning));
        AddWarningCommand.NotifyCanExecuteChanged();
        NotifyEdited();

        if (kept.Count != shown.Count)
        {
            Message = "Each warning needs a different number of minutes, so the repeat was removed.";

            // The list can't be changed in the middle of the edit that caused this, so do it right after.
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                RebuildWarningsFromDraft();
                OnPropertyChanged(nameof(CanAddWarning));
                AddWarningCommand.NotifyCanExecuteChanged();
            });
        }
    }

    // ---- Schedule ----

    /// <summary>Called after the schedule is painted or filled.</summary>
    public void NotifyScheduleEdited() => NotifyEdited();

    /// <summary>Copies one day onto others. The day's budget goes with it, but only if the day uses a budget.</summary>
    public void CopyDay(DayOfWeek from, IReadOnlyList<DayOfWeek> targets)
    {
        var copyBudgetToo = Draft.Schedule.HasLimitedCells(from);

        Draft.Schedule.CopyDay(from, targets);
        if (copyBudgetToo)
        {
            var minutes = Draft.BudgetMinutesFor(from);
            foreach (var day in targets)
            {
                Draft.SetBudgetMinutes(day, minutes);
            }
        }

        NotifyEdited();
    }

    // ---- Saving and reverting ----

    /// <summary>
    /// Makes the draft real and writes it to disk, once. Returns false if there was nothing to save.
    /// </summary>
    public bool Save()
    {
        if (!HasChanges)
        {
            return false;
        }

        var isNew = !CanDelete;
        _session.Commit();

        if (isNew)
        {
            _main.AddGroupCard(Card);
        }

        Card.RefreshFromModel();
        _main.RefreshStatuses();
        _main.SaveNow();
        return true;
    }

    /// <summary>Puts everything back to how it was when the window opened, without closing it.</summary>
    public void Revert()
    {
        _session.Revert();
        Message = null;

        RebuildListsFromDraft();
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ColorHex));
        OnPropertyChanged(nameof(Schedule));
        RefreshLockBoxes();
        NotifyEdited();
    }

    // ---- Keeping the screen in step with the draft ----

    /// <summary>Called after any change to the draft: refreshes everything that depends on it.</summary>
    private void NotifyEdited()
    {
        RefreshDerivedValues();
        OnPropertyChanged(nameof(HasChanges));
    }

    private void RefreshDerivedValues()
    {
        HasLimitedTime = Draft.Schedule.HasLimitedCells();
        foreach (var dayBudget in DayBudgets)
        {
            dayBudget.Refresh();
        }

        var today = DateTime.Now.DayOfWeek;
        UsedTodayText = $"Used today ({today}): {_main.BudgetMinutesUsedToday(Draft.Id)} of {Draft.BudgetMinutesFor(today)} min";
    }

    private void RebuildListsFromDraft()
    {
        Processes.Clear();
        foreach (var name in Draft.Processes)
        {
            Processes.Add(name);
        }

        RebuildWarningsFromDraft();
    }

    private void RebuildWarningsFromDraft()
    {
        Warnings.Clear();
        foreach (var minutes in Draft.WarningMinutes)
        {
            Warnings.Add(new WarningViewModel(minutes, OnWarningsChanged));
        }
    }
}

/// <summary>One "warn me N minutes before" entry.</summary>
public sealed partial class WarningViewModel : ObservableObject
{
    private readonly Action _changed;

    [ObservableProperty]
    private double _minutes;

    public WarningViewModel(int minutes, Action changed)
    {
        _minutes = minutes;
        _changed = changed;
    }

    partial void OnMinutesChanged(double value) => _changed();
}
