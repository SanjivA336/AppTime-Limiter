using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using AppTimeLimiter.App.Services;
using AppTimeLimiter.Core.Enforcement;
using AppTimeLimiter.Core.Models;
using AppTimeLimiter.Core.Settings;
using AppTimeLimiter.Core.Usage;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AppTimeLimiter.App.ViewModels;

/// <summary>The whole app: the settings, the list of groups, and live status. Saves settings after edits.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly SettingsStore _store;
    private readonly UsageLedger _ledger;
    private readonly AutostartService _autostart;
    private readonly DispatcherTimer _saveTimer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNoticeOpen))]
    private string? _notice;

    public MainViewModel(
        AppSettings settings,
        SettingsStore store,
        UsageLedger ledger,
        AutostartService autostart,
        AppIconService icons,
        IEnumerable<string> startupNotices)
    {
        Settings = settings;
        Icons = icons;
        _store = store;
        _ledger = ledger;
        _autostart = autostart;

        // Save icons for any of these apps that happen to be running now, before the cards are built.
        icons.CaptureIfRunning(settings.Groups.SelectMany(g => g.Processes));
        Groups = new ObservableCollection<GroupViewModel>(settings.Groups.Select(g => new GroupViewModel(g, this)));
        Groups.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasGroups));
            OnPropertyChanged(nameof(HasNoGroups));
        };

        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => SaveNow();

        Notice = string.Join(" ", startupNotices);
        if (string.IsNullOrEmpty(Notice))
        {
            Notice = null;
        }

        RefreshStatuses();
    }

    public AppSettings Settings { get; }

    public AppIconService Icons { get; }

    public ObservableCollection<GroupViewModel> Groups { get; }

    public bool HasGroups => Groups.Count > 0;

    public bool HasNoGroups => Groups.Count == 0;

    /// <summary>Shows the warning bar while there is a message; the bar's close button clears it.</summary>
    public bool IsNoticeOpen
    {
        get => !string.IsNullOrEmpty(Notice);
        set
        {
            if (!value)
            {
                Notice = null;
            }
        }
    }

    public bool StartWithWindows
    {
        get => Settings.StartWithWindows;
        set
        {
            if (value == Settings.StartWithWindows)
            {
                return;
            }

            var error = _autostart.SetEnabled(value);
            if (error is not null)
            {
                ShowNotice(error);
                OnPropertyChanged(); // snap the switch back
                return;
            }

            Settings.StartWithWindows = value;
            OnPropertyChanged();
            RequestSave();
        }
    }

    public void ShowNotice(string message) => Notice = message;

    /// <summary>
    /// Makes a new, empty group (all Free, so nothing is limited until the user paints something) for the editor to
    /// fill in. It is NOT added to the app yet: if the editor is closed without saving, it simply disappears.
    /// </summary>
    public GroupViewModel CreateGroup()
    {
        var existing = Groups.Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = "New group";
        for (var n = 2; existing.Contains(name); n++)
        {
            name = $"New group {n}";
        }

        var group = new AppGroup
        {
            Name = name,
            Color = GroupViewModel.Palette[Groups.Count % GroupViewModel.Palette.Length],
        };
        return new GroupViewModel(group, this);
    }

    /// <summary>
    /// Shows a new group's card, once it has been saved. (Saving already put the group itself into the settings.)
    /// </summary>
    public void AddGroupCard(GroupViewModel card)
    {
        Groups.Add(card);
    }

    public void RemoveGroup(GroupViewModel group)
    {
        Settings.Groups.Remove(group.Model);
        Groups.Remove(group);
        RequestSave();
    }

    /// <summary>Saves shortly after the last edit, so dragging across the schedule grid isn't one disk write per cell.</summary>
    public void RequestSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            _store.Save(Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Could not save settings", ex);
            ShowNotice("Couldn't save your settings: " + ex.Message);
        }
    }

    /// <summary>Called after each enforcement check: updates the "running" marks and every card's status.</summary>
    public void OnTick(TickResult result)
    {
        foreach (var status in result.Statuses)
        {
            var group = Groups.FirstOrDefault(g => g.Model.Id == status.GroupId);
            if (group is null)
            {
                continue;
            }

            group.IsRunning = status.IsRunning;

            // A running app is the chance to save its icon, if we don't have it yet.
            if (status.IsRunning && Icons.CaptureIfRunning(group.Model.Processes))
            {
                group.RebuildAppChips();
            }
        }

        RefreshStatuses();
    }

    /// <summary>Recomputes every card's status: Free, Limited with time left, or Blocked.</summary>
    public void RefreshStatuses()
    {
        var now = DateTime.Now;

        foreach (var group in Groups)
        {
            group.RefreshStatus(now, BudgetSecondsLeft(group.Model, now));
        }
    }

    /// <summary>Whole minutes of today's budget that this one group has used so far.</summary>
    public int BudgetMinutesUsedToday(Guid groupId) =>
        (int)(_ledger.GetBudgetSecondsUsed(DateOnly.FromDateTime(DateTime.Now), groupId) / 60);

    /// <summary>What is left of this group's own budget for today. Other groups' time doesn't count against it.</summary>
    private double BudgetSecondsLeft(AppGroup group, DateTime now)
    {
        var budgetSeconds = group.BudgetMinutesFor(now.DayOfWeek) * 60.0; // today's budget; each day can differ
        var usedSeconds = _ledger.GetBudgetSecondsUsed(DateOnly.FromDateTime(now), group.Id);
        return Math.Max(0, budgetSeconds - usedSeconds);
    }
}
