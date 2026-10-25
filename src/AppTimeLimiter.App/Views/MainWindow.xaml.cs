using AppTimeLimiter.App.ViewModels;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace AppTimeLimiter.App.Views;

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

    private sealed class PageProvider : INavigationViewPageProvider
    {
        private readonly MainViewModel _viewModel;
        private LimitsPage? _limits;

        public PageProvider(MainViewModel viewModel)
        {
            _viewModel = viewModel;
        }

        public object? GetPage(Type pageType) =>
            pageType == typeof(LimitsPage) ? _limits ??= new LimitsPage(_viewModel) : null;
    }
}
