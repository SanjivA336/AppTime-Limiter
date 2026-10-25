using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AppTimeLimiter.Core.Models;
using AppTimeLimiter.Core.Processes;
using Wpf.Ui.Controls;

namespace AppTimeLimiter.App.Views;

public partial class ProcessPickerWindow : FluentWindow
{
    private readonly AppSettings _settings;
    private readonly AppGroup _target;
    private List<Entry> _all = new();

    public ProcessPickerWindow(AppSettings settings, AppGroup target)
    {
        _settings = settings;
        _target = target;
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    /// <summary>Names the user chose. Set when the dialog returns true.</summary>
    public IReadOnlyList<string> SelectedNames { get; private set; } = Array.Empty<string>();

    /// <summary>One running app. Apps already in a group are shown but can't be selected.</summary>
    private sealed record Entry(string Name, string? WindowTitle, bool HasWindow, string? OwnerGroup)
    {
        public bool IsSelectable => OwnerGroup is null;

        public string Detail => OwnerGroup is not null
            ? $"Already in the group \"{OwnerGroup}\""
            : string.IsNullOrWhiteSpace(WindowTitle) ? "No window" : WindowTitle;
    }

    private void Reload()
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
                if (byName.TryGetValue(process.ProcessName, out var existing) && (existing.HasWindow || !hasWindow))
                {
                    continue;
                }

                byName[process.ProcessName] = new Entry(process.ProcessName, title, hasWindow, OwnerOf(process.ProcessName));
            }
        }

        _all = byName.Values.OrderBy(e => e.Name, ProcessNames.Comparer).ToList();
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
        var onlyWindows = OnlyWindows.IsChecked == true;

        List.ItemsSource = _all
            .Where(e => !onlyWindows || e.HasWindow)
            .Where(e => search.Length == 0
                || e.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (e.WindowTitle?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
    }

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

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

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
