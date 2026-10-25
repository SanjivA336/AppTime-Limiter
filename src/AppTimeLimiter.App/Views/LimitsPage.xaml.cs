using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AppTimeLimiter.App.ViewModels;

namespace AppTimeLimiter.App.Views;

/// <summary>The Limits page: one card per group. Clicking a card opens the editor for that group.</summary>
public partial class LimitsPage : Page
{
    private readonly MainViewModel _viewModel;

    public LimitsPage(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) => FitHeightToHostScrollViewer();
    }

    private void Card_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GroupViewModel group })
        {
            OpenEditor(group);
        }
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        // Open straight away so the new group can be named and filled in. It only becomes a real group if saved.
        OpenEditor(_viewModel.CreateGroup());
    }

    private void OpenEditor(GroupViewModel group)
    {
        var editor = new GroupEditorWindow(_viewModel, group) { Owner = Window.GetWindow(this) };
        editor.ShowDialog();

        // The card should show whatever changed (new apps, a different schedule, a deleted group).
        _viewModel.RefreshStatuses();
    }

    /// <summary>
    /// The navigation area wraps every page in its own ScrollViewer, which offers the page unlimited height.
    /// Then this page's own list grows to fit all its cards, has nothing to scroll, and still swallows the
    /// mouse wheel, so scrolling over the cards did nothing. Pinning the page to the visible height gives
    /// the list a real limit, so it scrolls properly.
    /// </summary>
    private void FitHeightToHostScrollViewer()
    {
        DependencyObject? ancestor = VisualTreeHelper.GetParent(this);
        while (ancestor is not null and not ScrollViewer)
        {
            ancestor = VisualTreeHelper.GetParent(ancestor);
        }

        if (ancestor is ScrollViewer host)
        {
            SetBinding(HeightProperty, new Binding(nameof(ScrollViewer.ViewportHeight)) { Source = host });
        }
    }
}
