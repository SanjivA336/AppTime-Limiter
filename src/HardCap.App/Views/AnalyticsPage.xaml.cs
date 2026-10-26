using System.Windows.Controls;
using HardCap.App.ViewModels;
using HardCap.Core.Usage;

namespace HardCap.App.Views;

/// <summary>
/// The Analytics page. It is rebuilt each time it is opened, and while it is showing it refreshes after every
/// enforcement check, so today's numbers grow as time is recorded. Clicking a day or a week number in a chart opens it.
/// </summary>
public partial class AnalyticsPage : Page
{
    /// <summary>Which view and how far back the page opens on. Development aids for screenshots (--analytics-view, --analytics-back).</summary>
    internal static AnalyticsView? InitialView { get; set; }

    internal static int InitialBack { get; set; }

    private readonly MainViewModel _main;
    private readonly AnalyticsViewModel _viewModel;

    public AnalyticsPage(MainViewModel main)
    {
        _main = main;
        _viewModel = new AnalyticsViewModel(main);
        DataContext = _viewModel;
        InitializeComponent();

        if (InitialView is { } view)
        {
            _viewModel.Navigate(view, InitialBack);
        }

        // A click on a day opens it in the day view; a click on a week number opens that week.
        BarChart.ColumnClicked += _viewModel.OpenDay;
        MonthChart.DayClicked += _viewModel.OpenDay;
        MonthChart.WeekClicked += _viewModel.OpenWeek;

        Loaded += (_, _) =>
        {
            PageHeightFix.Apply(this);
            _viewModel.Refresh();
            _main.Ticked += _viewModel.Refresh;
        };
        Unloaded += (_, _) => _main.Ticked -= _viewModel.Refresh;
    }
}
