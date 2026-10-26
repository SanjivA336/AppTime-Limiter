using HardCap.App.Controls;
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

    /// <summary>Opens the Analytics page. Used by the development screenshot mode.</summary>
    public void ShowAnalytics() => RootNavigation.Navigate(typeof(AnalyticsPage));

    /// <summary>
    /// Shows part of the Analytics chart as if it were hovered. Used by the development screenshot mode, which has no mouse.
    /// The text depends on the view: "column,group" for a day or a week, "dayOfMonth" for a month.
    /// </summary>
    public void PreviewAnalyticsHover(string spec)
    {
        var numbers = spec.Split(',').Select(part => int.TryParse(part, out var n) ? n : -1).ToArray();

        if (FindVisible<StackedBarChart>(this) is { } week && numbers.Length == 2)
        {
            week.PreviewHover(numbers[0], numbers[1]);
        }
        else if (FindVisible<MonthCalendarChart>(this) is { } month && numbers.Length == 1)
        {
            month.PreviewHover(numbers[0]);
        }
    }

    /// <summary>The first element of this type in the window that is actually showing (the page keeps the other views collapsed).</summary>
    private static T? FindVisible<T>(System.Windows.DependencyObject parent) where T : System.Windows.FrameworkElement
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match && match.IsVisible)
            {
                return match;
            }

            if (FindVisible<T>(child) is { } inner)
            {
                return inner;
            }
        }

        return null;
    }

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

        // The Limits page is kept. The Settings page is built fresh each time, so it always starts with its wait; so is Analytics, so it reads fresh numbers.
        public object? GetPage(Type pageType) =>
            pageType == typeof(LimitsPage) ? _limits ??= new LimitsPage(_viewModel)
            : pageType == typeof(SettingsPage) ? new SettingsPage(_viewModel)
            : pageType == typeof(AnalyticsPage) ? new AnalyticsPage(_viewModel)
            : null;
    }
}
