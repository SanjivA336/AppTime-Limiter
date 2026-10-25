using CommunityToolkit.Mvvm.ComponentModel;

namespace HardCap.App.ViewModels;

/// <summary>
/// The budget box above one day's column in the schedule grid. The number is this group's own budget for that day,
/// in the draft being edited; the box is only usable when the group has Limited time on that day.
/// </summary>
public sealed class DayBudgetViewModel : ObservableObject
{
    private readonly GroupEditorViewModel _editor;

    public DayBudgetViewModel(DayOfWeek day, GroupEditorViewModel editor)
    {
        Day = day;
        _editor = editor;
    }

    public DayOfWeek Day { get; }

    /// <summary>Minutes of Limited time allowed on this day. It is a double because that's what the number box uses.</summary>
    public double Minutes
    {
        get => _editor.GetBudgetMinutes(Day);
        set
        {
            _editor.SetBudgetMinutes(Day, (int)Math.Round(value));
            OnPropertyChanged();
        }
    }

    /// <summary>A budget only means something on a day where this group has Limited time.</summary>
    public bool IsEnabled => _editor.Draft.Schedule.HasLimitedCells(Day);

    public string ToolTipText => IsEnabled
        ? $"Minutes of Limited time this group may use on {Day}."
        : $"{Day} has no Limited time in this group. Paint some Limited cells to set a budget.";

    /// <summary>Re-reads everything. Called after the schedule changes, a budget is copied, or the draft is reverted.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Minutes));
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(ToolTipText));
    }
}
