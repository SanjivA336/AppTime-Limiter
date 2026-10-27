using CommunityToolkit.Mvvm.ComponentModel;
using HardCap.Core.Models;
using HardCap.Core.Settings;

namespace HardCap.App.ViewModels;

/// <summary>
/// The Settings page: things that belong to the whole app rather than one group. Everything here takes effect as soon as
/// it is changed. Because several of these settings can loosen the rules (the reset time, the wait and open hours,
/// importing), the whole page sits behind the general wait, shown as a countdown each time the page is opened.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;

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
        Wait = new DurationInput(() => Settings.WaitSeconds, SetWaitSeconds, "No wait");
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.StartWithWindows))
            {
                OnPropertyChanged(nameof(StartWithWindows));
            }
        };
    }

    private AppSettings Settings => _main.Settings;

    public bool IsUnlocked => !IsLocked;

    /// <summary>The countdown for opening this page, or null when there is no wait.</summary>
    public WaitCountdown? Countdown { get; private set; }

    /// <summary>Starts the wait for opening the page (none if no wait applies right now). Called each time the page is shown.</summary>
    public void BeginWait()
    {
        EndWait();
        Message = null;

        var seconds = WaitRules.SettingsWaitSeconds(Settings, DateTime.Now);
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
        get => _main.StartWithWindows;
        set => _main.StartWithWindows = value;
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

    /// <summary>The general wait before the settings, a group or quitting (up to 5 minutes), as minutes and seconds boxes.</summary>
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
                return "No general wait is set, so only groups with their own lock make you wait.";
            }

            var wait = LockTime.Describe(Settings.WaitSeconds);
            if (!Settings.OpenHoursEnabled)
            {
                return $"Opening a group, the settings, or quitting waits {wait}, at any time of day.";
            }

            var from = BudgetDay.Describe(Settings.OpenHoursStartMinutes);
            var to = BudgetDay.Describe(Settings.OpenHoursEndMinutes);
            return Settings.OpenHoursStartMinutes == Settings.OpenHoursEndMinutes
                ? "Open hours start and end at the same time, so they are empty."
                : $"No wait between {from} and {to}; at other times, opening a group, the settings or quitting waits {wait} (a group's own lock still applies, and the longer wait wins).";
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
            new SettingsStore(path).Save(Settings);
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
        Wait.Refresh();
        OnPropertyChanged(string.Empty); // every setting on the page may have changed

        var groupCount = loaded.Settings.Groups.Count;
        var repairCount = loaded.Repairs.Count;
        var fixedNote = repairCount > 0 ? $", correcting {repairCount} out-of-range {(repairCount == 1 ? "value" : "values")}" : string.Empty;
        Message = $"Imported {groupCount} {(groupCount == 1 ? "group" : "groups")} from {System.IO.Path.GetFileName(path)}{fixedNote}.";
        return true;
    }

    private void Changed() => _main.RequestSave();
}
