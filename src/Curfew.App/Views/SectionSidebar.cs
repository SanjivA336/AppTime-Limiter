using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace Curfew.App.Views;

/// <summary>One entry in a settings sidebar: its name and icon, and the card of settings it shows.</summary>
/// <param name="Tab">For a screen with tabs: which tab the card sits in. Screens without tabs leave it at 0.</param>
internal sealed record SectionItem(string Title, SymbolRegular Symbol, FrameworkElement Card, int Tab = 0);

/// <summary>
/// Turns a column of setting cards into a layout like a system settings app, when there is room: a list of sections on the left
/// and only the chosen section on the right. When the space is narrow it does nothing, so every card stays exactly where the
/// screen put it (stacked, or inside its tab). The cards are never moved or copied, only shown or hidden, so everything bound to
/// them keeps working.
/// </summary>
internal sealed class SectionSidebar
{
    /// <summary>The width, in device-independent pixels, from which the sidebar appears.</summary>
    public const double WideEnough = 840;

    private readonly ListBox _list;
    private readonly IReadOnlyList<SectionItem> _sections;
    private readonly FrameworkElement _measured;
    private readonly Action<bool>? _wideChanged;
    private readonly Action<SectionItem>? _shown;

    // What each card looked like before: two cards that sit side by side are stretched across the full width when shown alone.
    private readonly Dictionary<FrameworkElement, (int Column, int Span, Thickness Margin)> _original = new();

    /// <param name="list">The list to fill with the sections (it starts hidden and is only shown when there is room).</param>
    /// <param name="measured">The element whose width decides whether there is room.</param>
    /// <param name="wideChanged">Called when the layout switches between wide and narrow (for example, to hide a tab strip).</param>
    /// <param name="shown">Called when the wide layout shows a section (for example, to select the tab it is in).</param>
    public SectionSidebar(
        ListBox list, IReadOnlyList<SectionItem> sections, FrameworkElement measured,
        Action<bool>? wideChanged = null, Action<SectionItem>? shown = null)
    {
        _list = list;
        _sections = sections;
        _measured = measured;
        _wideChanged = wideChanged;
        _shown = shown;

        foreach (var section in sections)
        {
            _original[section.Card] = (Grid.GetColumn(section.Card), Grid.GetColumnSpan(section.Card), section.Card.Margin);
        }

        _list.ItemsSource = sections;
        _list.SelectedIndex = 0;
        _list.SelectionChanged += (_, _) => Apply();
        _measured.SizeChanged += (_, _) => Apply();
        Apply();
    }

    /// <summary>True while the sidebar is showing.</summary>
    public bool IsWide { get; private set; }

    public SectionItem Selected => _list.SelectedItem as SectionItem ?? _sections[0];

    /// <summary>Chooses a section by its name. Does nothing for a name that isn't there.</summary>
    public void Select(string title)
    {
        var section = _sections.FirstOrDefault(s => s.Title == title);
        if (section is not null)
        {
            _list.SelectedItem = section;
        }
    }

    /// <summary>Chooses the first section of a tab.</summary>
    public void SelectTab(int tab)
    {
        var section = _sections.FirstOrDefault(s => s.Tab == tab);
        if (section is not null)
        {
            _list.SelectedItem = section;
        }
    }

    private void Apply()
    {
        var wide = _measured.ActualWidth >= WideEnough;
        if (wide != IsWide)
        {
            IsWide = wide;
            _list.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
            _wideChanged?.Invoke(wide);
        }

        var selected = Selected;
        foreach (var section in _sections)
        {
            var card = section.Card;
            card.Visibility = !wide || section == selected ? Visibility.Visible : Visibility.Collapsed;

            // Shown alone, a card that shared a row with another takes the whole row.
            var (column, span, margin) = _original[card];
            if (wide && card.Parent is Grid { ColumnDefinitions.Count: > 1 } row)
            {
                Grid.SetColumn(card, 0);
                Grid.SetColumnSpan(card, row.ColumnDefinitions.Count);
                card.Margin = new Thickness(0, 0, 0, margin.Bottom);
            }
            else if (card.Parent is Grid)
            {
                Grid.SetColumn(card, column);
                Grid.SetColumnSpan(card, span);
                card.Margin = margin;
            }
        }

        if (wide)
        {
            _shown?.Invoke(selected);
        }
    }
}
