using System.Windows;
using System.Windows.Controls;
using HardCap.App.ViewModels;

namespace HardCap.App.Views;

/// <summary>The Limits page: one card per group. Clicking a card opens the editor for that group.</summary>
public partial class LimitsPage : Page
{
    private readonly MainViewModel _viewModel;

    public LimitsPage(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) => PageHeightFix.Apply(this);
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
}
