using CommunityToolkit.Mvvm.ComponentModel;
using Curfew.Core.Models;
using Curfew.Core.Settings;

namespace Curfew.App.ViewModels;

/// <summary>
/// The Settings page: things that belong to the whole app rather than one group. Like the group editor, it edits a draft: nothing
/// takes effect until "Save changes", "Revert" puts the draft back to what is saved, and leaving the page throws the draft away.
/// Because several of these settings can loosen the rules (the reset time, the wait and frictionless hours, importing), the whole
/// page sits behind the global friction, shown as a countdown each time the page is opened.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    // What the page edits. Only the app-wide settings are in it (no groups); Save copies them into the real settings.
    private readonly AppSettings _draft = new();

    /// <summary>The result of the last export or import, or a problem with it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMessageOpen))]
    private string? _message;

    /// <summary>True while the countdown is showing. The page shows only the countdown until it ends.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnlocked))]
    private bool _isLocked;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        LoadDraft();
        Wait = new DurationInput(() => Settings.WaitSeconds, SetWaitSeconds, "No wait");
    }

    /// <summary>The draft: what every box and switch on the page reads and writes.</summary>
    private AppSettings Settings => _draft;

    /// <summary>Starts the draft over from what is saved.</summary>
    private void LoadDraft()
    {
        _draft.CopyGeneralSettingsFrom(_main.Settings);
        _draft.StartWithWindows = _main.Settings.StartWithWindows;
    }

    /// <summary>True when something on the page differs from what is saved. It enables Save and Revert.</summary>
    public bool HasChanges =>
        !_draft.HasSameGeneralSettingsAs(_main.Settings) || _draft.StartWithWindows != _main.Settings.StartWithWindows;

    /// <summary>The opposite, for the things that need everything saved first (export and import).</summary>
    public bool NoChanges => !HasChanges;

    /// <summary>
    /// Makes the draft real: copies it into the settings, applies Start with Windows to Windows, and saves. If Windows refuses
    /// the startup change, the setting stays as it was and a notice says why.
    /// </summary>
    public void Save()
    {
        if (!HasChanges)
        {
            return;
        }

        _main.Settings.CopyGeneralSettingsFrom(_draft);
        if (_draft.StartWithWindows != _main.Settings.StartWithWindows)
        {
            _main.StartWithWindows = _draft.StartWithWindows; // writes to Windows; on a problem it shows a notice and leaves the setting alone
        }

        _main.RequestSave();
        Reload();
        Message = "Saved your settings.";
    }

    /// <summary>Puts every box and switch back to what is saved, without leaving the page.</summary>
    public void Revert()
    {
        Reload();
        Message = null;
    }

    /// <summary>Starts the draft over from what is saved and refreshes everything on the page.</summary>
    private void Reload()
    {
        LoadDraft();
        Wait.Refresh();
        OnPropertyChanged(string.Empty); // every setting on the page may have changed
    }

    public bool IsUnlocked => !IsLocked;

    /// <summary>The countdown for opening this page, or null when there is no wait.</summary>
    public WaitCountdown? Countdown { get; private set; }

    /// <summary>Starts the wait for opening the page (none if no wait applies right now). Called each time the page is shown.</summary>
    public void BeginWait()
    {
        EndWait();
        Message = null;

        var seconds = WaitRules.SettingsWaitSeconds(_main.Settings, DateTime.Now); // what is saved, not the draft
        if (seconds > 0)
        {
            Countdown = new WaitCountdown(seconds);
            Countdown.Finished += () => IsLocked = false;
            IsLocked = true;
        }
        else
        {
            Countdown = null;
            IsLocked = false;
        }

        OnPropertyChanged(nameof(Countdown));
    }

    /// <summary>Stops the countdown timer. Called when the page is closed or left.</summary>
    public void EndWait() => Countdown?.Stop();

    // ---- General ----

    public bool StartWithWindows
    {
        get => _draft.StartWithWindows;
        set
        {
            if (value == _draft.StartWithWindows)
            {
                return;
            }

            _draft.StartWithWindows = value;
            OnPropertyChanged();
            Changed();
        }
    }

    // ---- When budgets refill ----

    public IReadOnlyList<TimeChoice> TimeChoices => TimeChoice.All;

    public TimeChoice SelectedReset
    {
        get => TimeChoice.For(Settings.ResetMinutes);
        set
        {
            if (value is null || value.Minutes == Settings.ResetMinutes)
            {
                return;
            }

            Settings.ResetMinutes = BudgetDay.Tidy(value.Minutes);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ResetSummary));
            Changed();
        }
    }

    public string ResetSummary => Settings.ResetMinutes == 0
        ? "Budgets refill at midnight."
        : $"Budgets refill at {BudgetDay.Describe(Settings.ResetMinutes)}, so play after midnight until then counts toward the day before.";

    // ---- Waiting ----

    /// <summary>The general wait before the settings or quitting (up to 5 minutes), as minutes and seconds boxes.</summary>
    public DurationInput Wait { get; }

    private void SetWaitSeconds(int seconds)
    {
        Settings.WaitSeconds = seconds;
        OnPropertyChanged(nameof(OpenHoursSummary));
        Changed();
    }

    public bool OpenHoursEnabled
    {
        get => Settings.OpenHoursEnabled;
        set
        {
            if (value == Settings.OpenHoursEnabled)
            {
                return;
            }

            Settings.OpenHoursEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OpenHoursSummary));
            Changed();
        }
    }

    public TimeChoice OpenHoursStart
    {
        get => TimeChoice.For(Settings.OpenHoursStartMinutes);
        set
        {
            if (value is not null)
            {
                Settings.OpenHoursStartMinutes = value.Minutes;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OpenHoursSummary));
                Changed();
            }
        }
    }

    public TimeChoice OpenHoursEnd
    {
        get => TimeChoice.For(Settings.OpenHoursEndMinutes);
        set
        {
            if (value is not null)
            {
                Settings.OpenHoursEndMinutes = value.Minutes;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OpenHoursSummary));
                Changed();
            }
        }
    }

    /// <summary>What the wait and the open hours add up to, in words.</summary>
    public string OpenHoursSummary
    {
        get
        {
            if (Settings.WaitSeconds == 0)
            {
                return "No global friction is set, so the settings open straight away (quitting still waits for your longest Edit friction).";
            }

            var wait = LockTime.Describe(Settings.WaitSeconds);
            if (!Settings.OpenHoursEnabled)
            {
                return $"Opening the settings or quitting waits {wait}, at any time of day.";
            }

            var from = BudgetDay.Describe(Settings.OpenHoursStartMinutes);
            var to = BudgetDay.Describe(Settings.OpenHoursEndMinutes);
            return Settings.OpenHoursStartMinutes == Settings.OpenHoursEndMinutes
                ? "Frictionless hours start and end at the same time, so they are empty."
                : $"No friction between {from} and {to}; at other times, opening the settings or quitting waits {wait}.";
        }
    }

    // ---- Away from the computer ----

    /// <summary>When on, time isn't counted while there has been no keyboard or mouse input for <see cref="IdleMinutes"/>. When off, the computer is never asked.</summary>
    public bool IdleDetectionEnabled
    {
        get => Settings.IdleDetectionEnabled;
        set
        {
            if (value == Settings.IdleDetectionEnabled)
            {
                return;
            }

            Settings.IdleDetectionEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IdleSummary));
            Changed();
        }
    }

    /// <summary>Minutes without input before the computer counts as idle (1 to 60), as the number box's value.</summary>
    public double IdleMinutes
    {
        get => Settings.IdleMinutes;
        set
        {
            if (!double.IsNaN(value))
            {
                var minutes = Math.Clamp((int)Math.Round(value), 1, AppSettings.MaxIdleMinutes);
                if (minutes != Settings.IdleMinutes)
                {
                    Settings.IdleMinutes = minutes;
                    OnPropertyChanged(nameof(IdleSummary));
                    Changed();
                }
            }

            OnPropertyChanged(); // so the box shows the tidied number, even when it was out of range
        }
    }

    public string IdleSummary => Settings.IdleDetectionEnabled
        ? $"After {Settings.IdleMinutes} {(Settings.IdleMinutes == 1 ? "minute" : "minutes")} without keyboard or mouse input, time stops counting until you are back."
        : "Off: time counts whenever a group's apps are running.";

    // ---- Backup ----

    public bool IsMessageOpen
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

    /// <summary>Writes the groups and settings to a file the user can keep. Returns true if it worked.</summary>
    public bool Export(string path)
    {
        try
        {
            _main.SaveNow();
            new SettingsStore(path).Save(_main.Settings); // what is saved, with the groups
            Message = $"Saved your settings to {System.IO.Path.GetFileName(path)}.";
            return true;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Message = "Couldn't write that file: " + ex.Message;
            return false;
        }
    }

    /// <summary>Replaces the groups and settings with those in a file. Returns true if it worked.</summary>
    public bool Import(string path)
    {
        var loaded = new SettingsStore(path).Load();
        if (loaded.Error is not null)
        {
            Message = "That file couldn't be used: " + loaded.Error;
            return false;
        }

        _main.ReplaceSettings(loaded.Settings);
        Reload();

        var groupCount = loaded.Settings.Groups.Count;
        var repairCount = loaded.Repairs.Count;
        var fixedNote = repairCount > 0 ? $", correcting {repairCount} out-of-range {(repairCount == 1 ? "value" : "values")}" : string.Empty;
        Message = $"Imported {groupCount} {(groupCount == 1 ? "group" : "groups")} from {System.IO.Path.GetFileName(path)}{fixedNote}.";
        return true;
    }

    /// <summary>Called after any edit: the draft may now differ from what is saved, which enables Save and Revert.</summary>
    private void Changed() => OnPropertyChanged(nameof(HasChanges));
}
