using HardCap.App.ViewModels;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace HardCap.App.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        SystemThemeWatcher.Watch(this);

        // The navigation view builds pages by asking us; ours need the view model passed in.
        RootNavigation.SetPageProviderService(new PageProvider(viewModel));
        Loaded += (_, _) => RootNavigation.Navigate(typeof(LimitsPage));
    }

    /// <summary>Goes back to the Limits page (the "back" button of the settings wait).</summary>
    public void ShowLimits() => RootNavigation.Navigate(typeof(LimitsPage));

    /// <summary>Opens the Settings page. Used by the development screenshot mode.</summary>
    public void ShowSettings() => RootNavigation.Navigate(typeof(SettingsPage));

    private sealed class PageProvider : INavigationViewPageProvider
    {
        private readonly MainViewModel _viewModel;
        private LimitsPage? _limits;

        public PageProvider(MainViewModel viewModel)
        {
            _viewModel = viewModel;
        }

        // The Limits page is kept. The Settings page is built fresh each time, so it always starts with its wait.
        public object? GetPage(Type pageType) =>
            pageType == typeof(LimitsPage) ? _limits ??= new LimitsPage(_viewModel)
            : pageType == typeof(SettingsPage) ? new SettingsPage(_viewModel)
            : null;
    }
}
