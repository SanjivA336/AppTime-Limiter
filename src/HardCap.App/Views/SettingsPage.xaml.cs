using System.Windows;
using System.Windows.Controls;
using HardCap.App.ViewModels;
using Microsoft.Win32;

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
    }

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
