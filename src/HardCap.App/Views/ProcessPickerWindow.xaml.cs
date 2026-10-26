using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HardCap.App.Services;
using HardCap.Core.Models;
using HardCap.Core.Processes;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace HardCap.App.Views;

/// <summary>
/// Lets the user choose apps for a group. Running apps come first, then programs found on the computer (so an app
/// can be added without starting it), and "Browse" takes any program file. Programs that probably run in the background
/// all day carry a warning, because they would use up the budget even when nobody is playing.
/// </summary>
public partial class ProcessPickerWindow : FluentWindow
{
    private readonly AppSettings _settings;
    private readonly AppGroup _target;
    private List<Entry> _running = new();
    private readonly Dictionary<string, Entry> _installed = new(ProcessNames.Comparer);
    private readonly Dictionary<string, Entry> _browsed = new(ProcessNames.Comparer);
    private List<Entry> _all = new();
    private bool _isLookingForInstalledApps;

    public ProcessPickerWindow(AppSettings settings, AppGroup target)
    {
        _settings = settings;
        _target = target;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ReloadRunning();
            _ = LoadInstalledAppsAsync();
        };
    }

    /// <summary>Names the user chose. Set when the dialog returns true.</summary>
    public IReadOnlyList<string> SelectedNames { get; private set; } = Array.Empty<string>();

    /// <summary>How an entry got into the list. The order here is the order they are ranked in.</summary>
    private enum EntryKind
    {
        RunningWithWindow,
        RunningInBackground,
        Installed,
    }

    /// <summary>One app in the list. Apps already in a group are shown but can't be selected.</summary>
    private sealed record Entry(string Name, EntryKind Kind, string? WindowTitle, string? Path, string? OwnerGroup)
    {
        public bool IsSelectable => OwnerGroup is null;

        /// <summary>The note shown for programs that probably run in the background all day.</summary>
        public string? Warning => ProcessNames.BackgroundWarning(Name);

        public bool HasWarning => Warning is not null;

        public string Tag => Kind switch
        {
            EntryKind.RunningWithWindow => "Running",
            EntryKind.RunningInBackground => "Background",
            _ => "Not running",
        };

        public string Detail => OwnerGroup is not null
            ? $"Already in the group \"{OwnerGroup}\""
            : Kind switch
            {
                EntryKind.RunningWithWindow => string.IsNullOrWhiteSpace(WindowTitle) ? "Running" : WindowTitle,
                EntryKind.RunningInBackground => "No window, runs in the background",
                _ => Path ?? string.Empty,
            };
    }

    // ---- Building the list ----

    private void ReloadRunning()
    {
        var byName = new Dictionary<string, Entry>(ProcessNames.Comparer);
        var ownId = Environment.ProcessId;

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == ownId || ProcessNames.IsProtected(process.ProcessName))
                {
                    continue;
                }

                var title = SafeWindowTitle(process);
                var hasWindow = !string.IsNullOrWhiteSpace(title);
                if (byName.TryGetValue(process.ProcessName, out var existing) && (existing.Kind == EntryKind.RunningWithWindow || !hasWindow))
                {
                    continue;
                }

                var kind = hasWindow ? EntryKind.RunningWithWindow : EntryKind.RunningInBackground;
                byName[process.ProcessName] = new Entry(process.ProcessName, kind, title, null, OwnerOf(process.ProcessName));
            }
        }

        _running = byName.Values.ToList();
        Rebuild();
    }

    private async Task LoadInstalledAppsAsync()
    {
        if (_isLookingForInstalledApps)
        {
            return;
        }

        _isLookingForInstalledApps = true;
        Status.Text = "Looking for installed apps…";
        var apps = await InstalledAppFinder.FindAsync();

        foreach (var app in apps)
        {
            _installed[app.ProcessName] = new Entry(app.ProcessName, EntryKind.Installed, null, app.Path, OwnerOf(app.ProcessName));
        }

        _isLookingForInstalledApps = false;
        Rebuild();
    }

    /// <summary>Joins the running apps, the installed ones and the browsed ones into one ranked list (running apps win a clash).</summary>
    private void Rebuild()
    {
        var merged = new Dictionary<string, Entry>(ProcessNames.Comparer);
        foreach (var entry in _running.Concat(_browsed.Values).Concat(_installed.Values))
        {
            merged.TryAdd(entry.Name, entry);
        }

        // Running apps with a window first, then other running programs, then everything else; by name inside each group.
        _all = merged.Values
            .OrderBy(e => e.Kind)
            .ThenBy(e => e.Name, ProcessNames.Comparer)
            .ToList();
        ApplyFilter();
    }

    private string? OwnerOf(string processName)
    {
        var owner = _settings.Groups.FirstOrDefault(g => g.Processes.Contains(processName, ProcessNames.Comparer));
        return owner?.Name;
    }

    private static string? SafeWindowTitle(Process process)
    {
        try
        {
            return process.MainWindowTitle;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null; // the process exited, or we aren't allowed to look at it
        }
    }

    private void ApplyFilter()
    {
        var search = SearchBox.Text.Trim();
        var includeNotRunning = ShowNotRunning.IsChecked == true;
        var includeBackground = ShowBackground.IsChecked == true;

        var shown = _all
            .Where(e => e.Kind != EntryKind.RunningInBackground || includeBackground)
            .Where(e => e.Kind != EntryKind.Installed || includeNotRunning || _browsed.ContainsKey(e.Name))
            .Where(e => search.Length == 0
                || e.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (e.WindowTitle?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (e.Path?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        List.ItemsSource = shown;
        Status.Text = _isLookingForInstalledApps
            ? "Looking for installed apps…"
            : $"{shown.Count} {(shown.Count == 1 ? "app" : "apps")}; Steam and Epic games often aren't listed, so use Browse to pick the game's .exe file.";
    }

    // ---- Events ----

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded)
        {
            ApplyFilter();
        }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            ApplyFilter();
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        ReloadRunning();
        _ = LoadInstalledAppsAsync();
    }

    /// <summary>Opens a file dialog for a program file, adds it to the list and selects it.</summary>
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose the program to add",
            Filter = "Programs (*.exe)|*.exe",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var name = ProcessNames.Normalize(Path.GetFileName(dialog.FileName));
        if (ProcessNames.IsProtected(name))
        {
            Status.Text = $"\"{name}\" is a core Windows process (or this app) and can't be limited.";
            return;
        }

        _browsed[name] = new Entry(name, EntryKind.Installed, null, dialog.FileName, OwnerOf(name));
        SearchBox.Text = string.Empty;
        ShowNotRunning.IsChecked = true;
        Rebuild();

        // Show it, selected, wherever it landed in the list.
        var entry = (List.ItemsSource as IEnumerable<Entry>)?.FirstOrDefault(x => ProcessNames.Comparer.Equals(x.Name, name));
        if (entry is not null)
        {
            List.SelectedItem = entry;
            List.ScrollIntoView(entry);
        }
    }

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (List.SelectedItem is Entry { IsSelectable: true })
        {
            Add_Click(sender, e);
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        SelectedNames = List.SelectedItems.OfType<Entry>().Where(x => x.IsSelectable).Select(x => x.Name).ToList();
        if (SelectedNames.Count == 0)
        {
            return;
        }

        DialogResult = true;
    }
}
