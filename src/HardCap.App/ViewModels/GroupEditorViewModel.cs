using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using HardCap.Core.Models;
using HardCap.Core.Processes;
using HardCap.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HardCap.App.ViewModels;

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
    // A locked group first shows only a countdown, and every time the group is opened the wait starts again from the
    // full length. The countdown only runs while this window is the active one (see WaitCountdown).

    /// <summary>True while the countdown is running. The window shows only the countdown, not the group.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnlocked))]
    private bool _isLocked;

    public GroupEditorViewModel(MainViewModel main, GroupViewModel card)
    {
        _main = main;
        Card = card;
        _session = new GroupEditSession(main.Settings, card.Model);

        Processes = new ObservableCollection<string>();
        Warnings = new ObservableCollection<WarningViewModel>();
        DayBudgets = new ObservableCollection<DayBudgetViewModel>(
            ScheduleGridLayout.ColumnDays.Select(day => new DayBudgetViewModel(day, this)));

        Lock = new DurationInput(() => Draft.LockSeconds, SetLockSeconds, "No lock");
        Grace = new DurationInput(() => Draft.GraceSeconds, SetGraceSeconds, "No grace period");
        BuildPauseChoices();

        RebuildListsFromDraft();
        LoadPauseFromDraft();
        RefreshDerivedValues();

        // The wait is the longer of the group's own lock and the general wait (which doesn't apply in open hours).
        // A brand-new group has nothing to protect yet, so it doesn't wait.
        var waitSeconds = _session.IsNew ? 0 : WaitRules.GroupWaitSeconds(main.Settings, card.Model, DateTime.Now);
        if (waitSeconds > 0)
        {
            IsLocked = true;
            Countdown = new WaitCountdown(waitSeconds);
            Countdown.Finished += () => IsLocked = false; // the wait is over: show the group
        }
    }

    /// <summary>The wait before the group opens, or null if it opened straight away.</summary>
    public WaitCountdown? Countdown { get; }

    /// <summary>What the countdown reads. Empty-handed "0:00" once the wait is over or if there never was one.</summary>
    public string LockRemainingText => Countdown?.RemainingText ?? "0:00";

    /// <summary>How much of the wait is left, from 1 down to 0.</summary>
    public double LockProgress => Countdown?.Progress ?? 0;

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

    // ---- The lock, and the grace period (both are "minutes and seconds" boxes) ----

    /// <summary>
    /// The lock: how long the wait is the NEXT time this group is opened. It never affects the window you are in.
    /// </summary>
    public DurationInput Lock { get; }

    /// <summary>
    /// The grace period: how long an app that is already running may carry on after a limit starts (the budget runs out,
    /// or a Blocked time begins), so there is time to save. An app opened during a block never gets one.
    /// </summary>
    public DurationInput Grace { get; }

    private void SetLockSeconds(int seconds)
    {
        Draft.LockSeconds = seconds;
        NotifyEdited();
    }

    private void SetGraceSeconds(int seconds)
    {
        Draft.GraceSeconds = seconds;
        NotifyEdited();
    }

    /// <summary>Stops the countdown timer. Called when the window closes.</summary>
    public void StopCountdown() => Countdown?.Stop();

    // ---- Pause ----
    // Pausing treats the group as Free for a while. It is part of the draft, so (like everything here) it needs the
    // group's lock to be waited out first, and takes effect only on "Save changes".

    private readonly List<PauseOption> _pauseOptions = new();
    private PauseOption? _keepCurrentOption;
    private DateTime? _keepCurrentUntil;
    private bool _loadingPause;

    /// <summary>What the pause box offers: not paused, until resumed, for a while, or until a chosen day and time.</summary>
    public IReadOnlyList<PauseOption> PauseOptions => _pauseOptions;

    /// <summary>The days the "until a day and time" choice offers: today and the next 90 days.</summary>
    public IReadOnlyList<DayChoice> PauseDays { get; private set; } = Array.Empty<DayChoice>();

    public IReadOnlyList<TimeChoice> PauseTimes => TimeChoice.All;

    [ObservableProperty]
    private PauseOption? _selectedPauseOption;

    [ObservableProperty]
    private DayChoice? _selectedPauseDay;

    [ObservableProperty]
    private TimeChoice? _selectedPauseTime;

    /// <summary>True while "until a day and time" is chosen, which is when the day and time boxes are shown.</summary>
    public bool IsDateTimePause => SelectedPauseOption?.Kind == PauseKind.UntilDateTime;

    /// <summary>The pause in words: "Not paused", "Paused until tomorrow at 8:00 AM", "Paused until you resume".</summary>
    public string PauseSummary
    {
        get
        {
            if (!Draft.IsPaused)
            {
                return "Not paused: this group's limits apply as normal.";
            }

            return Draft.PausedUntil is DateTime until
                ? $"Paused until {TimeText.When(until, DateTime.Now)}."
                : "Paused until you resume it here.";
        }
    }

    partial void OnSelectedPauseOptionChanged(PauseOption? value)
    {
        OnPropertyChanged(nameof(IsDateTimePause));
        ApplyPauseToDraft();
    }

    partial void OnSelectedPauseDayChanged(DayChoice? value) => ApplyPauseToDraft();

    partial void OnSelectedPauseTimeChanged(TimeChoice? value) => ApplyPauseToDraft();

    private void BuildPauseChoices()
    {
        var today = DateTime.Today;
        PauseDays = Enumerable.Range(0, 91)
            .Select(offset => today.AddDays(offset))
            .Select(date => new DayChoice(date, DayLabel(date, today)))
            .ToList();

        // If the group is paused until a particular time when the window opens, that pause can be kept as it is.
        if (Draft.IsPausedAt(DateTime.Now) && Draft.PausedUntil is DateTime until)
        {
            _keepCurrentUntil = until;
            _keepCurrentOption = new PauseOption($"Keep it paused until {TimeText.When(until, DateTime.Now)}", PauseKind.KeepCurrent);
            _pauseOptions.Add(_keepCurrentOption);
        }

        _pauseOptions.Add(new PauseOption("Not paused", PauseKind.NotPaused));
        _pauseOptions.Add(new PauseOption("Until I resume it", PauseKind.UntilResumed));
        _pauseOptions.Add(new PauseOption("For 30 minutes", PauseKind.ForDuration, TimeSpan.FromMinutes(30)));
        _pauseOptions.Add(new PauseOption("For 1 hour", PauseKind.ForDuration, TimeSpan.FromHours(1)));
        _pauseOptions.Add(new PauseOption("For 2 hours", PauseKind.ForDuration, TimeSpan.FromHours(2)));
        _pauseOptions.Add(new PauseOption("For 4 hours", PauseKind.ForDuration, TimeSpan.FromHours(4)));
        _pauseOptions.Add(new PauseOption("For 8 hours", PauseKind.ForDuration, TimeSpan.FromHours(8)));
        _pauseOptions.Add(new PauseOption("Until a day and time…", PauseKind.UntilDateTime));
    }

    private static string DayLabel(DateTime date, DateTime today) => (date - today).Days switch
    {
        0 => "Today",
        1 => "Tomorrow",
        _ => date.ToString("ddd, MMM d", System.Globalization.CultureInfo.CurrentCulture),
    };

    /// <summary>Shows the draft's pause in the boxes, without that counting as the user changing it.</summary>
    private void LoadPauseFromDraft()
    {
        _loadingPause = true;

        var now = DateTime.Now;
        SelectedPauseOption = !Draft.IsPausedAt(now)
            ? _pauseOptions.First(o => o.Kind == PauseKind.NotPaused)
            : Draft.PausedUntil is null
                ? _pauseOptions.First(o => o.Kind == PauseKind.UntilResumed)
                : _keepCurrentOption ?? _pauseOptions.First(o => o.Kind == PauseKind.UntilDateTime);

        // A sensible start for the day and time boxes: when the pause ends, or else tomorrow morning.
        var suggested = Draft.PausedUntil is DateTime until && until > now ? until : now.Date.AddDays(1).AddHours(8);
        SelectedPauseDay = PauseDays.FirstOrDefault(d => d.Date == suggested.Date) ?? PauseDays[1];
        SelectedPauseTime = TimeChoice.Nearest((int)suggested.TimeOfDay.TotalMinutes);

        _loadingPause = false;
        OnPropertyChanged(nameof(PauseSummary));
    }

    /// <summary>Copies what the pause boxes say into the draft.</summary>
    private void ApplyPauseToDraft()
    {
        if (_loadingPause || SelectedPauseOption is null)
        {
            return;
        }

        var now = DateTime.Now;
        switch (SelectedPauseOption.Kind)
        {
            case PauseKind.NotPaused:
                Draft.IsPaused = false;
                Draft.PausedUntil = null;
                break;

            case PauseKind.UntilResumed:
                Draft.IsPaused = true;
                Draft.PausedUntil = null;
                break;

            case PauseKind.KeepCurrent:
                Draft.IsPaused = true;
                Draft.PausedUntil = _keepCurrentUntil;
                break;

            case PauseKind.ForDuration:
                // Counted from now for the preview; counted again from the moment of saving (see Save).
                Draft.IsPaused = true;
                Draft.PausedUntil = now + SelectedPauseOption.Duration;
                break;

            case PauseKind.UntilDateTime:
                ApplyChosenDayAndTime(now);
                break;
        }

        OnPropertyChanged(nameof(PauseSummary));
        NotifyEdited();
    }

    private void ApplyChosenDayAndTime(DateTime now)
    {
        if (SelectedPauseDay is null || SelectedPauseTime is null)
        {
            return;
        }

        var until = SelectedPauseDay.Date.AddMinutes(SelectedPauseTime.Minutes);
        if (until <= now)
        {
            Draft.IsPaused = false;
            Draft.PausedUntil = null;
            Message = "That time has already passed, so the group isn't paused: pick a later day or time.";
            return;
        }

        Draft.IsPaused = true;
        Draft.PausedUntil = until;
    }

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

    private const string DuplicateWarningMessage =
        "Each warning needs a different number of minutes, so change the ones marked \"same as another\".";

    /// <summary>True while two warnings have the same number of minutes, which can't be saved.</summary>
    public bool HasDuplicateWarnings => Warnings.Any(w => w.IsDuplicate);

    /// <summary>
    /// Copies the warnings on screen into the draft. While you are still editing, two boxes may briefly hold the same
    /// number (stepping 2 up to 5 passes through every number, and 5 may already be taken), so nothing is removed here:
    /// the repeated rows are only marked. The draft keeps one of each number, and the check that really matters happens
    /// when saving (see <see cref="ValidateForSave"/>).
    /// </summary>
    private void OnWarningsChanged()
    {
        var shown = Warnings.Select(w => (int)w.Minutes).ToList();
        foreach (var warning in Warnings)
        {
            warning.IsDuplicate = shown.Count(minutes => minutes == (int)warning.Minutes) > 1;
        }

        Draft.WarningMinutes = SettingsRules.SanitizeWarnings(shown);

        // The reminder about repeats goes away by itself once none are left.
        if (!HasDuplicateWarnings && Message == DuplicateWarningMessage)
        {
            Message = null;
        }

        OnPropertyChanged(nameof(CanAddWarning));
        OnPropertyChanged(nameof(HasDuplicateWarnings));
        AddWarningCommand.NotifyCanExecuteChanged();
        NotifyEdited();
    }

    /// <summary>
    /// Checks that the draft can be saved. Returns false, with a message saying why, if two warnings share a number. The
    /// window calls this before <see cref="Save"/> and stays open when it fails.
    /// </summary>
    public bool ValidateForSave()
    {
        if (HasDuplicateWarnings)
        {
            Message = DuplicateWarningMessage;
            return false;
        }

        return true;
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

        // "Pause for 1 hour" means an hour from the moment it is saved, not from when it was picked in the box.
        if (SelectedPauseOption is { Kind: PauseKind.ForDuration, Duration: TimeSpan pauseLength })
        {
            Draft.PausedUntil = DateTime.Now + pauseLength;
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
        Lock.Refresh();
        Grace.Refresh();
        LoadPauseFromDraft();
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

        var budgetDay = _main.BudgetDayOfWeek(DateTime.Now);
        UsedTodayText = $"Used today ({budgetDay}): {_main.BudgetMinutesUsedToday(Draft.Id)} of {Draft.BudgetMinutesFor(budgetDay)} min";
    }

    /// <summary>The note under the budget boxes, including when budgets refill ("midnight", or a time such as "4:00 AM").</summary>
    public string BudgetNoteText
    {
        get
        {
            var resetTime = _main.Settings.ResetMinutes == 0 ? "midnight" : BudgetDay.Describe(_main.Settings.ResetMinutes);
            return $"These budgets are this group's own and refill at {resetTime} (change that in Settings).";
        }
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

    /// <summary>True while another warning has the same number of minutes. The row is marked, and saving is refused.</summary>
    [ObservableProperty]
    private bool _isDuplicate;

    public WarningViewModel(int minutes, Action changed)
    {
        _minutes = minutes;
        _changed = changed;
    }

    partial void OnMinutesChanged(double value) => _changed();
}
