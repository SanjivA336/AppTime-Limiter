using System.Windows;
using System.Windows.Controls;
using HardCap.App.ViewModels;
using Microsoft.Win32;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace HardCap.App.Views;

/// <summary>
/// The Settings page. Each time it is shown it starts the general wait over, and the countdown follows the main
/// window: it only runs while that window is the active one.
/// </summary>
public partial class SettingsPage : Page
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(MainViewModel main)
    {
        _viewModel = new SettingsViewModel(main);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += (_, _) =>
        {
            PageHeightFix.Apply(this);
            _viewModel.BeginWait();
            if (Window.GetWindow(this) is { } window)
            {
                _viewModel.Countdown?.FollowActivityOf(window);
            }
        };

        // Leaving the page (or closing the window) ends the wait; coming back starts it again from the full length.
        Unloaded += (_, _) => _viewModel.EndWait();

        // When the page is wide, the cards are listed in a sidebar and one is shown at a time.
        _sidebar = new SectionSidebar(
            Sidebar,
            new SectionItem[]
            {
                new("General", SymbolRegular.Settings24, GeneralCard),
                new("Daily reset", SymbolRegular.Clock24, ResetCard),
                new("Global friction", SymbolRegular.LockClosed24, FrictionCard),
                new("Away", SymbolRegular.Hourglass24, AwayCard),
                new("Back up", SymbolRegular.ArrowUpload24, BackupCard),
            },
            Body);
    }

    private readonly SectionSidebar _sidebar;

    /// <summary>Shows one card by its name, such as "Global friction" (only matters in the sidebar layout).</summary>
    public void SelectSection(string title) => _sidebar.Select(title);

    /// <summary>True while the page is wide enough to show the section list.</summary>
    public bool IsSidebarShowing => _sidebar.IsWide;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveButton.Focus(); // a box that is still being typed in commits its value first
        _viewModel.Save();
    }

    private void Revert_Click(object sender, RoutedEventArgs e) => _viewModel.Revert();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow main)
        {
            main.ShowLimits();
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export HardCap settings",
            Filter = "HardCap settings (*.json)|*.json",
            FileName = "hardcap-settings.json",
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            _viewModel.Export(dialog.FileName);
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import HardCap settings",
            Filter = "HardCap settings (*.json)|*.json|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            Window.GetWindow(this),
            "Importing replaces every group and the settings on this page with the ones in the file. Continue?",
            "Import settings",
            System.Windows.MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer == System.Windows.MessageBoxResult.Yes)
        {
            _viewModel.Import(dialog.FileName);
        }
    }
}
