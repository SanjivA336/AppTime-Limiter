using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HardCap.App.Controls;
using HardCap.App.ViewModels;
using HardCap.Core.Models;
using Wpf.Ui.Controls;

namespace HardCap.App.Views;

/// <summary>
/// The window for editing one group. Everything here changes a draft copy, not the real group, so nothing takes effect
/// until "Save changes" is pressed. Closing the window any other way (the X, Esc) discards the changes.
/// </summary>
public partial class GroupEditorWindow : FluentWindow
{
    private const string HoverHint = "Hover over the grid to see a time slot";

    private readonly MainViewModel _main;
    private readonly GroupViewModel _card;
    private readonly GroupEditorViewModel _editor;
    private readonly List<RadioButton> _swatches = new();

    public GroupEditorWindow(MainViewModel main, GroupViewModel card)
    {
        _main = main;
        _card = card;
        _editor = new GroupEditorViewModel(main, card);
        DataContext = _editor;

        InitializeComponent();
        BuildColorSwatches();
        BuildCopyChoices();

        ScheduleGrid.HoverChanged += ScheduleGrid_HoverChanged;
        _editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GroupEditorViewModel.ColorHex))
            {
                SelectMatchingSwatch();
            }
        };

        // The lock countdown only runs while this window is the active one; switching to another window pauses it.
        _editor.Countdown?.FollowActivityOf(this);

        // Whatever way the window closes, stop the lock countdown's timer.
        Closed += (_, _) => _editor.StopCountdown();

        _tabsBorder = Tabs.BorderThickness;

        // When the window is wide, the cards are listed in a sidebar and the tabs step aside.
        _sidebar = new SectionSidebar(
            Sidebar,
            new SectionItem[]
            {
                new("Group", SymbolRegular.Tag24, GroupCard),
                new("Apps", SymbolRegular.Apps24, AppsCard),
                new("Warnings", SymbolRegular.Alert24, WarningsCard),
                new("Grace period", SymbolRegular.Timer24, GraceCard),
                new("Pause", SymbolRegular.PauseCircle24, PauseCard),
                new("What counts", SymbolRegular.Eye24, CountCard),
                new("Edit friction", SymbolRegular.LockClosed24, FrictionCard),
                new("Launch friction", SymbolRegular.PlayCircle24, LaunchCard),
                new("Schedule", SymbolRegular.CalendarLtr24, ScheduleCard, Tab: 1),
            },
            Body,
            wideChanged: wide =>
            {
                // The list takes over from the tabs, so their strip (and the line under it) is hidden too.
                foreach (var tab in Tabs.Items.OfType<TabItem>())
                {
                    tab.Visibility = wide ? Visibility.Collapsed : Visibility.Visible;
                }

                Tabs.BorderThickness = wide ? new Thickness(0) : _tabsBorder;
            },
            shown: section => Tabs.SelectedIndex = section.Tab);

        // Esc means "never mind": close without saving.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !e.Handled)
            {
                e.Handled = true;
                Close();
            }
        };
    }

    private readonly SectionSidebar _sidebar;
    private readonly Thickness _tabsBorder;

    /// <summary>Shows the tab at this position (0 is General, 1 is Schedule). Used by the development screenshot mode.</summary>
    public void SelectTab(int index)
    {
        index = Math.Clamp(index, 0, Tabs.Items.Count - 1);
        Tabs.SelectedIndex = index;
        _sidebar.SelectTab(index);
    }

    /// <summary>Shows one card by its name, such as "Grace period" (in the sidebar layout, and its tab in the narrow one).</summary>
    public void SelectSection(string title)
    {
        _sidebar.Select(title);
        if (!_sidebar.IsWide)
        {
            Tabs.SelectedIndex = _sidebar.Selected.Tab;
        }
    }

    /// <summary>True while the window is wide enough to show the section list.</summary>
    public bool IsSidebarShowing => _sidebar.IsWide;

    // ---- Color swatches ----

    /// <summary>One round choice per palette color. They act as a group, so exactly one shows as selected.</summary>
    private void BuildColorSwatches()
    {
        foreach (var hex in GroupViewModel.Palette)
        {
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("SwatchRadio"),
                GroupName = "groupColor",
                Background = GroupViewModel.Swatch(hex),
                Foreground = CheckmarkBrushFor(hex),
                ToolTip = hex,
                Tag = hex,
                IsChecked = string.Equals(hex, _editor.ColorHex, StringComparison.OrdinalIgnoreCase),
            };

            // Hooked up after the starting selection is set, so building the list doesn't count as a change.
            swatch.Checked += (_, _) => _editor.ColorHex = hex;
            _swatches.Add(swatch);
            SwatchPanel.Children.Add(swatch);
        }
    }

    /// <summary>After a revert the color may be different, so move the selection ring to match.</summary>
    private void SelectMatchingSwatch()
    {
        foreach (var swatch in _swatches)
        {
            swatch.IsChecked = string.Equals((string)swatch.Tag, _editor.ColorHex, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Black on light colors, white on dark ones, so the checkmark is readable on every swatch.</summary>
    private static Brush CheckmarkBrushFor(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var brightness = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
        return brightness > 150 ? Brushes.Black : Brushes.White;
    }

    // ---- Schedule painting ----

    private void Paint_Checked(object sender, RoutedEventArgs e)
    {
        // Fires once while the window is still being built, before the grid exists.
        if (ScheduleGrid is not null && sender is RadioButton { Tag: string tag })
        {
            ScheduleGrid.PaintState = Enum.Parse<CellState>(tag);
        }
    }

    private void Grid_ScheduleEdited(object? sender, EventArgs e) => _editor.NotifyScheduleEdited();

    /// <summary>Fills the readout above the grid: for a cell, its day, its time span and its state.</summary>
    private void ScheduleGrid_HoverChanged(GridHover? hover)
    {
        if (hover is null)
        {
            HoverText.Text = HoverHint;
            HoverSwatch.Visibility = HoverState.Visibility = Visibility.Collapsed;
            return;
        }

        HoverText.Text = hover.Description;

        var hasState = hover.State is not null;
        HoverSwatch.Visibility = HoverState.Visibility = hasState ? Visibility.Visible : Visibility.Collapsed;
        if (hover.State is CellState state)
        {
            HoverSwatch.Background = ScheduleGridControl.CellBrushFor(state); // it describes a grid cell, so it matches the grid
            HoverState.Text = state.ToString();
        }
    }

    // ---- Copy a day to other days ----

    private sealed record CopyChoice(string Label, Func<DayOfWeek, IEnumerable<DayOfWeek>> Targets)
    {
        public override string ToString() => Label;
    }

    private static readonly DayOfWeek[] Weekdays =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday,
    };

    private static readonly DayOfWeek[] Weekend = { DayOfWeek.Saturday, DayOfWeek.Sunday };

    private static readonly DayOfWeek[] MondayFirst = Weekdays.Concat(Weekend).ToArray();

    private void BuildCopyChoices()
    {
        foreach (var day in MondayFirst)
        {
            CopyFrom.Items.Add(day);
        }

        CopyFrom.SelectedIndex = 0;

        CopyTo.Items.Add(new CopyChoice("All other days", from => MondayFirst.Where(d => d != from)));
        CopyTo.Items.Add(new CopyChoice("Weekdays (Mon–Fri)", from => Weekdays.Where(d => d != from)));
        CopyTo.Items.Add(new CopyChoice("Weekend (Sat, Sun)", from => Weekend.Where(d => d != from)));
        foreach (var day in MondayFirst)
        {
            var target = day;
            var dayName = CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(day);
            CopyTo.Items.Add(new CopyChoice(dayName, from => from == target ? Array.Empty<DayOfWeek>() : new[] { target }));
        }

        CopyTo.SelectedIndex = 0;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (CopyFrom.SelectedItem is not DayOfWeek from || CopyTo.SelectedItem is not CopyChoice choice)
        {
            return;
        }

        var targets = choice.Targets(from).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        _editor.CopyDay(from, targets);
        ScheduleGrid.InvalidateVisual();
    }

    // ---- Apps ----

    private void AddApps_Click(object sender, RoutedEventArgs e)
    {
        // The picker checks against the draft, so it knows about apps added here that aren't saved yet.
        var picker = new ProcessPickerWindow(_editor.DraftSettings, _editor.Draft) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            _editor.AddProcesses(picker.SelectedNames);
        }
    }

    // ---- Footer and lock screen ----

    /// <summary>The Cancel button on the lock screen. It just closes, like Esc.</summary>
    private void CancelLock_Click(object sender, RoutedEventArgs e) => Close();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Typing in a box only counts once the box loses focus. Moving focus to this button first makes
        // any half-finished entry (a warning being typed, a renamed group) land in the draft before saving it.
        SaveButton.Focus();

        // Stay open (with a message) if something can't be saved yet, such as two warnings with the same number.
        if (!_editor.ValidateForSave())
        {
            return;
        }

        _editor.Save();
        Close();
    }

    private void Revert_Click(object sender, RoutedEventArgs e)
    {
        _editor.Revert();
        ScheduleGrid.InvalidateVisual();
        ScheduleGrid_HoverChanged(null);
    }

    private void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        var answer = System.Windows.MessageBox.Show(
            this,
            $"Delete the group \"{_card.Name}\" and stop limiting its apps?",
            "Delete group",
            System.Windows.MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer == System.Windows.MessageBoxResult.Yes)
        {
            _main.RemoveGroup(_card);
            Close();
        }
    }
}
