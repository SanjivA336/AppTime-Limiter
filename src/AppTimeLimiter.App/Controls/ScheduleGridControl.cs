using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AppTimeLimiter.Core.Models;
using static AppTimeLimiter.Core.Models.ScheduleGridLayout;

namespace AppTimeLimiter.App.Controls;

/// <summary>What the mouse is over: a line of text, plus the cell's state when it is over a cell.</summary>
public sealed record GridHover(string Description, CellState? State);

/// <summary>
/// Draws the weekly schedule and lets the user paint it. Days run across the top (Monday first) and the time
/// of day runs down the left, from 12 AM to 11:30 PM, one row per 30 minutes.
///
/// Press and drag over cells to set them to <see cref="PaintState"/>. Click a day's name to fill that whole day.
/// Which cell sits under the mouse is worked out by <see cref="ScheduleGridLayout"/>; this class only draws
/// and reacts to the mouse.
/// </summary>
public sealed class ScheduleGridControl : Control
{
    public static readonly DependencyProperty ScheduleProperty = DependencyProperty.Register(
        nameof(Schedule), typeof(WeeklySchedule), typeof(ScheduleGridControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PaintStateProperty = DependencyProperty.Register(
        nameof(PaintState), typeof(CellState), typeof(ScheduleGridControl), new PropertyMetadata(CellState.Limited));

    // The state colors used on the group cards (the bar, its glow, the status dot): green, blue, red.
    private static readonly Brush FreeBrush = Frozen(Color.FromRgb(0x3F, 0xA6, 0x6B));
    private static readonly Brush LimitedBrush = Frozen(Color.FromRgb(0x3B, 0x82, 0xF6));
    private static readonly Brush BlockedBrush = Frozen(Color.FromRgb(0xD6, 0x45, 0x45));

    // On the schedule grid itself, Free is a quiet grey so the Limited and Blocked cells stand out.
    private static readonly Brush FreeCellBrush = Frozen(Color.FromRgb(0x6B, 0x72, 0x80));

    // A very faint dark line across a column marks each half hour, without competing with the cell colors.
    private static readonly Pen HalfHourLine = FrozenPen(Color.FromArgb(38, 0, 0, 0), 1);

    // Each hour boundary is a clear gap this many pixels wide (half of it is taken from each neighbouring cell).
    private const double HourGap = 3;

    private bool _painting;
    private int _lastColumn = -1;
    private int _lastSlot = -1;

    public ScheduleGridControl()
    {
        Focusable = false;
        MinWidth = 600;
        Cursor = Cursors.Pen;
    }

    public WeeklySchedule? Schedule
    {
        get => (WeeklySchedule?)GetValue(ScheduleProperty);
        set => SetValue(ScheduleProperty, value);
    }

    /// <summary>The state applied to cells the user paints.</summary>
    public CellState PaintState
    {
        get => (CellState)GetValue(PaintStateProperty);
        set => SetValue(PaintStateProperty, value);
    }

    /// <summary>Raised once per drag (when the mouse is released), so the owner can save.</summary>
    public event EventHandler? ScheduleEdited;

    /// <summary>
    /// Raised as the mouse moves over the grid, describing what is under it, or null when it isn't over
    /// anything. The page shows this in a readout above the grid.
    /// </summary>
    public event Action<GridHover?>? HoverChanged;

    /// <summary>
    /// The width of the time strip on the left, as a Grid column width. Anything that must line up with
    /// the day columns (such as the budget boxes above them) starts with a column this wide.
    /// </summary>
    public static GridLength TimeColumnLength => new(TimeColumnWidth);

    /// <summary>The color that shows a state on the group cards: green, blue or red.</summary>
    public static Brush BrushFor(CellState state) => state switch
    {
        CellState.Free => FreeBrush,
        CellState.Limited => LimitedBrush,
        _ => BlockedBrush,
    };

    /// <summary>The color of a cell on the schedule grid: the same as <see cref="BrushFor"/>, except that Free is grey.</summary>
    public static Brush CellBrushFor(CellState state) => state == CellState.Free ? FreeCellBrush : BrushFor(state);

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? MinWidth : availableSize.Width, TotalHeight);

    // ---- Drawing ----

    protected override void OnRender(DrawingContext dc)
    {
        // A see-through fill over the whole area makes every pixel respond to the mouse, even the gaps.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var schedule = Schedule;
        if (schedule is null)
        {
            return;
        }

        var columnWidth = ColumnWidth(ActualWidth);
        DrawDayNames(dc, columnWidth);
        DrawTimeAxis(dc);

        for (var column = 0; column < ColumnDays.Length; column++)
        {
            DrawDayColumn(dc, schedule, column, columnWidth);
        }
    }

    private void DrawDayNames(DrawingContext dc, double columnWidth)
    {
        for (var column = 0; column < ColumnDays.Length; column++)
        {
            var name = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(ColumnDays[column]);
            var text = MakeText(name, 13, FontWeights.SemiBold, Foreground);

            var columnCentre = TimeColumnWidth + column * columnWidth + columnWidth / 2;
            dc.DrawText(text, new Point(columnCentre - text.Width / 2, (DayHeaderHeight - text.Height) / 2));
        }
    }

    /// <summary>
    /// The times down the left. Every hour gets a label and a long, bold tick;
    /// every half hour in between gets a short, faint tick.
    /// </summary>
    private void DrawTimeAxis(DrawingContext dc)
    {
        var hourTick = new Pen(Fade(Foreground, 0.9), 1.5);
        var halfHourTick = new Pen(Fade(Foreground, 0.35), 1);
        var labelBrush = Fade(Foreground, 1);

        for (var slot = 0; slot <= WeeklySchedule.SlotsPerDay; slot++)
        {
            var y = SlotTop(slot);
            var isOnTheHour = slot % 2 == 0;

            if (isOnTheHour)
            {
                dc.DrawLine(hourTick, new Point(TimeColumnWidth - 10, y), new Point(TimeColumnWidth - 3, y));

                var isLastLine = slot == WeeklySchedule.SlotsPerDay;
                if (!isLastLine)
                {
                    // Right-aligned against the tick, centred on the hour line.
                    var label = MakeText(HourLabel(slot / 2), 11, FontWeights.Normal, labelBrush);
                    dc.DrawText(label, new Point(TimeColumnWidth - 16 - label.Width, y - label.Height / 2));
                }
            }
            else
            {
                dc.DrawLine(halfHourTick, new Point(TimeColumnWidth - 6, y), new Point(TimeColumnWidth - 3, y));
            }
        }
    }

    /// <summary>
    /// One day: 48 colored cells stacked top to bottom. Cells touch each other, except at each hour where
    /// a small gap is left, and at each half hour where a faint line is drawn.
    /// </summary>
    private void DrawDayColumn(DrawingContext dc, WeeklySchedule schedule, int column, double columnWidth)
    {
        var day = ColumnDays[column];
        var left = TimeColumnWidth + column * columnWidth + ColumnGap / 2;
        var width = Math.Max(1, columnWidth - ColumnGap);

        for (var slot = 0; slot < WeeklySchedule.SlotsPerDay; slot++)
        {
            var startsAnHour = slot % 2 == 0 && slot > 0;
            var endsAnHour = slot % 2 == 1 && slot < WeeklySchedule.SlotsPerDay - 1;

            var top = SlotTop(slot) + (startsAnHour ? HourGap / 2 : 0);
            var bottom = SlotTop(slot + 1) - (endsAnHour ? HourGap / 2 : 0);
            dc.DrawRectangle(CellBrushFor(schedule.GetState(day, slot)), null, new Rect(left, top, width, bottom - top));

            var isHalfHourBoundary = slot % 2 == 1;
            if (isHalfHourBoundary)
            {
                dc.DrawLine(HalfHourLine, new Point(left, SlotTop(slot)), new Point(left + width, SlotTop(slot)));
            }
        }
    }

    // ---- Mouse ----

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var schedule = Schedule;
        if (schedule is null)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (!TryGetColumn(point.X, ActualWidth, out var column))
        {
            return;
        }

        if (IsInDayHeader(point.Y))
        {
            FillWholeDay(schedule, column);
            return;
        }

        if (TryGetSlot(point.Y, out var slot))
        {
            _painting = true;
            CaptureMouse();
            Paint(schedule, column, slot);
            ReportHover(schedule, point);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var schedule = Schedule;
        if (schedule is null)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (_painting && e.LeftButton == MouseButtonState.Pressed
            && TryGetColumn(point.X, ActualWidth, out var column) && TryGetSlot(point.Y, out var slot))
        {
            Paint(schedule, column, slot);
        }

        ReportHover(schedule, point);
    }

    /// <summary>
    /// Tells the page what is under the mouse. Called after any painting, so the readout always shows
    /// what the cell has just become, whether the mouse moved or only clicked.
    /// </summary>
    private void ReportHover(WeeklySchedule schedule, Point point)
    {
        if (!TryGetColumn(point.X, ActualWidth, out var column))
        {
            HoverChanged?.Invoke(null);
            return;
        }

        if (IsInDayHeader(point.Y))
        {
            HoverChanged?.Invoke(new GridHover($"Click to fill all of {ColumnDays[column]}", null));
            return;
        }

        if (!TryGetSlot(point.Y, out var slot))
        {
            HoverChanged?.Invoke(null);
            return;
        }

        var day = ColumnDays[column];
        HoverChanged?.Invoke(new GridHover($"{day} · {SlotRangeLabel(slot)}", schedule.GetState(day, slot)));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_painting)
        {
            HoverChanged?.Invoke(null);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        EndPaint();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        EndPaint();
    }

    private void Paint(WeeklySchedule schedule, int column, int slot)
    {
        // A fast drag can skip cells. If we're still in the same day as last time, fill every cell in between.
        var stayedInSameDay = _lastColumn == column;
        var from = stayedInSameDay ? Math.Min(_lastSlot, slot) : slot;
        var to = stayedInSameDay ? Math.Max(_lastSlot, slot) : slot;

        for (var s = from; s <= to; s++)
        {
            schedule.SetState(ColumnDays[column], s, PaintState);
        }

        _lastColumn = column;
        _lastSlot = slot;
        InvalidateVisual();
    }

    private void FillWholeDay(WeeklySchedule schedule, int column)
    {
        for (var slot = 0; slot < WeeklySchedule.SlotsPerDay; slot++)
        {
            schedule.SetState(ColumnDays[column], slot, PaintState);
        }

        InvalidateVisual();
        ScheduleEdited?.Invoke(this, EventArgs.Empty);
    }

    private void EndPaint()
    {
        if (!_painting)
        {
            return;
        }

        _painting = false;
        _lastColumn = _lastSlot = -1;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        if (!IsMouseOver)
        {
            HoverChanged?.Invoke(null); // The drag ended outside the grid, so there's nothing to describe.
        }

        ScheduleEdited?.Invoke(this, EventArgs.Empty);
    }

    // ---- Small helpers ----

    private FormattedText MakeText(string text, double size, FontWeight weight, Brush? brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size, brush ?? Brushes.Gray, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>A copy of <paramref name="brush"/> at the given opacity (the original may be shared and read-only).</summary>
    private static Brush Fade(Brush? brush, double opacity)
    {
        var copy = (brush ?? Brushes.Gray).CloneCurrentValue();
        copy.Opacity = opacity;
        copy.Freeze();
        return copy;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(Frozen(color), thickness);
        pen.Freeze();
        return pen;
    }
}
