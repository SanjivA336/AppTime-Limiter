using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HardCap.App.ViewModels;
using HardCap.Core.Usage;

namespace HardCap.App.Controls;

/// <summary>
/// A stacked column chart: a column for each day of a week, or each hour of a day, split into one coloured piece for each
/// group. Point at a piece and it fades into the apps that make it up, with a small card listing them. Click a column that
/// has somewhere to go (a day) to open it. It is drawn by hand so that it follows the app's colours (dark or light) and
/// needs no chart library.
/// </summary>
public sealed class StackedBarChart : FrameworkElement
{
    private const double LeftMargin = 46;
    private const double RightMargin = 8;
    private const double TopMargin = 24;
    private const double BottomMargin = 28;
    private const double MaxBarWidth = 56;
    private const double CornerRadius = 4;

    /// <summary>The least width a column label needs, so labels are skipped when columns are narrower than this.</summary>
    private const double LabelRoom = 34;

    // Gridline spacing in hours, for the tallest column. Each row is (the axis tops out at, a line every). The small ones
    // are for hours, which never pass one hour; the larger ones are for days.
    private static readonly (double AxisHours, double StepHours)[] Axes =
    {
        (0.25, 1.0 / 12), (0.5, 0.25), (1, 0.5), (2, 0.5), (4, 1), (6, 2), (8, 2), (12, 4), (16, 4), (24, 6),
    };

    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(IReadOnlyList<ChartColumn>), typeof(StackedBarChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextBrushProperty = BrushProperty(nameof(TextBrush), Brushes.White);
    public static readonly DependencyProperty MutedBrushProperty = BrushProperty(nameof(MutedBrush), Brushes.Gray);
    public static readonly DependencyProperty GridBrushProperty = BrushProperty(nameof(GridBrush), Brushes.DimGray);
    public static readonly DependencyProperty CardBrushProperty = BrushProperty(nameof(CardBrush), Brushes.Black);
    public static readonly DependencyProperty CardBorderBrushProperty = BrushProperty(nameof(CardBorderBrush), Brushes.Gray);

    /// <summary>How far the hovered piece has faded into its apps: 0 is the plain piece, 1 is fully the apps.</summary>
    public static readonly DependencyProperty HoverProgressProperty = DependencyProperty.Register(
        nameof(HoverProgress), typeof(double), typeof(StackedBarChart),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    // The piece being shown faded into its apps (kept while it fades back out).
    private int _shownColumn = -1;
    private int _shownGroup = -1;
    private int _pointerColumn = -1;
    private bool _pointerIsOver;

    // True once a hover has been previewed for a screenshot: the real mouse is then ignored, so it can't cancel the preview.
    private bool _isPreview;

    /// <summary>Raised when a column that can be opened is clicked, with that column's date.</summary>
    public event Action<DateOnly>? ColumnClicked;

    /// <summary>The columns to draw, left to right.</summary>
    public IReadOnlyList<ChartColumn>? Columns
    {
        get => (IReadOnlyList<ChartColumn>?)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }

    public Brush MutedBrush { get => (Brush)GetValue(MutedBrushProperty); set => SetValue(MutedBrushProperty, value); }

    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }

    public Brush CardBrush { get => (Brush)GetValue(CardBrushProperty); set => SetValue(CardBrushProperty, value); }

    public Brush CardBorderBrush { get => (Brush)GetValue(CardBorderBrushProperty); set => SetValue(CardBorderBrushProperty, value); }

    public double HoverProgress
    {
        get => (double)GetValue(HoverProgressProperty);
        set => SetValue(HoverProgressProperty, value);
    }

    private static DependencyProperty BrushProperty(string name, Brush fallback) => DependencyProperty.Register(
        name, typeof(Brush), typeof(StackedBarChart), new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender));

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 260 : availableSize.Height);

    // ---- Layout (shared by drawing and by pointer hit-testing) ----

    /// <summary>One piece of a column: which column and group it is, and where it sits.</summary>
    private readonly record struct Piece(int Column, int Group, Rect Bounds);

    private sealed record Layout(Rect Plot, double AxisHours, double StepHours, double Slot, IReadOnlyList<Rect> Bars, IReadOnlyList<Piece> Pieces);

    private Layout? ComputeLayout()
    {
        var columns = Columns;
        if (columns is null || columns.Count == 0 || ActualWidth < 100 || ActualHeight < 80)
        {
            return null;
        }

        var plot = new Rect(LeftMargin, TopMargin, ActualWidth - LeftMargin - RightMargin, ActualHeight - TopMargin - BottomMargin);
        var tallestHours = columns.Max(c => c.TotalSeconds) / 3600;
        var (axisHours, stepHours) = Axes.FirstOrDefault(a => tallestHours <= a.AxisHours, Axes[^1]);

        var slot = plot.Width / columns.Count;
        var barWidth = Math.Min(slot * (columns.Count > 10 ? 0.66 : 0.52), MaxBarWidth);
        var bars = new List<Rect>();
        var pieces = new List<Piece>();

        for (var c = 0; c < columns.Count; c++)
        {
            var left = plot.Left + slot * c + (slot - barWidth) / 2;
            var bottom = plot.Bottom;
            var topOfStack = bottom;

            for (var g = 0; g < columns[c].Groups.Count; g++)
            {
                var seconds = columns[c].Groups[g].Seconds;
                if (seconds <= 0)
                {
                    continue;
                }

                var height = Math.Max(2, plot.Height * (seconds / 3600) / axisHours);
                pieces.Add(new Piece(c, g, new Rect(left, bottom - height, barWidth, height)));
                bottom -= height;
                topOfStack = bottom;
            }

            bars.Add(new Rect(left, topOfStack, barWidth, plot.Bottom - topOfStack));
        }

        return new Layout(plot, axisHours, stepHours, slot, bars, pieces);
    }

    /// <summary>The column the pointer is in (anywhere from the top of the plot to the labels) if it can be opened, or -1.</summary>
    private int ClickableColumnAt(Point position, Layout layout)
    {
        var columns = Columns;
        if (columns is null || position.X < layout.Plot.Left || position.X >= layout.Plot.Right
            || position.Y < layout.Plot.Top - TopMargin / 2 || position.Y > ActualHeight)
        {
            return -1;
        }

        var column = (int)((position.X - layout.Plot.Left) / layout.Slot);
        return column >= 0 && column < columns.Count && columns[column].ClickDate is not null ? column : -1;
    }

    // ---- Pointer ----

    /// <summary>Shows a piece as if the pointer were on it. Development aid for screenshots (there is no mouse in them).</summary>
    public void PreviewHover(int column, int group)
    {
        _shownColumn = column;
        _shownGroup = group;
        _pointerIsOver = true;
        _isPreview = true;
        HoverProgress = 1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_isPreview)
        {
            return;
        }

        _pointerIsOver = true;

        var layout = ComputeLayout();
        var position = e.GetPosition(this);

        var clickable = layout is null ? -1 : ClickableColumnAt(position, layout);
        if (clickable != _pointerColumn)
        {
            _pointerColumn = clickable;
            Cursor = clickable >= 0 ? Cursors.Hand : Cursors.Arrow;
            InvalidateVisual();
        }

        var hit = layout?.Pieces.Cast<Piece?>().FirstOrDefault(p => p!.Value.Bounds.Contains(position));
        if (hit is Piece piece)
        {
            if (piece.Column != _shownColumn || piece.Group != _shownGroup)
            {
                _shownColumn = piece.Column;
                _shownGroup = piece.Group;
                AnimateHover(from: 0, to: 1, milliseconds: 220);
            }
        }
        else
        {
            FadeOut();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_isPreview)
        {
            return;
        }

        _pointerIsOver = false;
        _pointerColumn = -1;
        Cursor = Cursors.Arrow;
        FadeOut();
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        var layout = ComputeLayout();
        var column = layout is null ? -1 : ClickableColumnAt(e.GetPosition(this), layout);
        if (column >= 0 && Columns is { } columns && columns[column].ClickDate is DateOnly date)
        {
            ColumnClicked?.Invoke(date);
        }
    }

    private void FadeOut()
    {
        if (_shownColumn >= 0 && HoverProgress > 0)
        {
            AnimateHover(from: HoverProgress, to: 0, milliseconds: 160, forgetWhenDone: true);
        }
    }

    private void AnimateHover(double from, double to, int milliseconds, bool forgetWhenDone = false)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };

        if (forgetWhenDone)
        {
            animation.Completed += (_, _) =>
            {
                if (!_pointerIsOver || HoverProgress <= 0)
                {
                    _shownColumn = _shownGroup = -1;
                    InvalidateVisual();
                }
            };
        }

        BeginAnimation(HoverProgressProperty, animation);
    }

    // ---- Drawing ----

    protected override void OnRender(DrawingContext dc)
    {
        var layout = ComputeLayout();
        if (layout is null || Columns is not { } columns)
        {
            return;
        }

        DrawGrid(dc, layout);
        DrawPointerColumn(dc, layout, columns);
        DrawBars(dc, layout, columns);
        DrawLabels(dc, layout, columns);

        if (_shownColumn >= 0 && _shownColumn < columns.Count && _shownGroup >= 0 && _shownGroup < columns[_shownColumn].Groups.Count)
        {
            var piece = layout.Pieces.Cast<Piece?>().FirstOrDefault(p => p!.Value.Column == _shownColumn && p.Value.Group == _shownGroup);
            if (piece is Piece shown)
            {
                var group = columns[_shownColumn].Groups[_shownGroup];
                ChartDrawing.DrawInfoCard(
                    dc, this, new Size(ActualWidth, ActualHeight), shown.Bounds, HoverProgress,
                    $"{group.Name}, {columns[_shownColumn].Title}", AnalyticsViewModel.Duration(group.Seconds) + " in total",
                    ChartDrawing.AppRows(group, 6, AnalyticsViewModel.Duration),
                    TextBrush, MutedBrush, CardBrush, CardBorderBrush);
            }
        }
    }

    private void DrawGrid(DrawingContext dc, Layout layout)
    {
        var gridPen = new Pen(GridBrush, 1);
        for (var hours = 0.0; hours <= layout.AxisHours + 0.001; hours += layout.StepHours)
        {
            var y = Math.Round(layout.Plot.Bottom - layout.Plot.Height * hours / layout.AxisHours) + 0.5;
            dc.DrawLine(gridPen, new Point(layout.Plot.Left, y), new Point(layout.Plot.Right, y));

            var label = ChartDrawing.Text(this, TickLabel(hours), 11, MutedBrush);
            dc.DrawText(label, new Point(layout.Plot.Left - 8 - label.Width, y - label.Height / 2));
        }
    }

    /// <summary>A faint highlight behind the column the pointer is over, to show it can be clicked.</summary>
    private void DrawPointerColumn(DrawingContext dc, Layout layout, IReadOnlyList<ChartColumn> columns)
    {
        if (_pointerColumn < 0 || _pointerColumn >= columns.Count)
        {
            return;
        }

        var area = new Rect(layout.Plot.Left + layout.Slot * _pointerColumn + 2, layout.Plot.Top - 8, layout.Slot - 4, layout.Plot.Height + BottomMargin + 8);
        dc.PushOpacity(0.07);
        dc.DrawRoundedRectangle(MutedBrush, null, area, 6, 6);
        dc.Pop();
    }

    private void DrawBars(DrawingContext dc, Layout layout, IReadOnlyList<ChartColumn> columns)
    {
        var hovering = _shownColumn >= 0 && HoverProgress > 0;
        var dimOthers = hovering ? 1 - 0.55 * HoverProgress : 1.0;

        for (var c = 0; c < columns.Count; c++)
        {
            var bar = layout.Bars[c];
            if (bar.Height <= 0)
            {
                continue;
            }

            // Rounded top, flat bottom: the clip reaches below the baseline, and nothing is drawn down there.
            var clip = new RectangleGeometry(new Rect(bar.X, bar.Y, bar.Width, bar.Height + CornerRadius * 2), CornerRadius, CornerRadius);
            clip.Freeze();
            dc.PushClip(clip);

            foreach (var piece in layout.Pieces.Where(p => p.Column == c))
            {
                var group = columns[c].Groups[piece.Group];
                var color = ChartDrawing.ColorOf(group.Color);
                var isShown = hovering && piece.Column == _shownColumn && piece.Group == _shownGroup;
                var rect = SeparatedRect(piece.Bounds);

                if (!isShown)
                {
                    dc.PushOpacity(dimOthers);
                    dc.DrawRectangle(new SolidColorBrush(color), null, rect);
                    dc.Pop();
                    continue;
                }

                // The piece you are pointing at: the plain colour fades out as its apps fade in.
                dc.PushOpacity(1 - HoverProgress);
                dc.DrawRectangle(new SolidColorBrush(color), null, rect);
                dc.Pop();

                dc.PushOpacity(HoverProgress);
                DrawAppPieces(dc, rect, group, color);
                dc.Pop();
            }

            dc.Pop();
        }
    }

    /// <summary>Splits a piece into one slice for each app, in shades of the group's colour.</summary>
    private static void DrawAppPieces(DrawingContext dc, Rect rect, GroupUsage group, Color color)
    {
        var apps = group.Apps.Where(a => a.Seconds > 0).ToList();
        var total = apps.Sum(a => a.Seconds);
        if (apps.Count == 0 || total <= 0)
        {
            dc.DrawRectangle(new SolidColorBrush(ChartDrawing.Shade(color, -0.2)), null, rect);
            return;
        }

        var y = rect.Bottom;
        for (var i = 0; i < apps.Count; i++)
        {
            var height = rect.Height * apps[i].Seconds / total;
            var slice = new Rect(rect.X, y - height, rect.Width, height);
            var shade = ChartDrawing.AppShades[i % ChartDrawing.AppShades.Length];
            dc.DrawRectangle(new SolidColorBrush(ChartDrawing.Shade(color, shade)), null, slice);
            y -= height;
        }
    }

    /// <summary>The label under each column, and the total above it. Labels are thinned out when the columns are narrow.</summary>
    private void DrawLabels(DrawingContext dc, Layout layout, IReadOnlyList<ChartColumn> columns)
    {
        var every = Math.Max(1, (int)Math.Ceiling(LabelRoom / layout.Slot));

        for (var c = 0; c < columns.Count; c++)
        {
            var column = columns[c];
            var centre = layout.Plot.Left + layout.Slot * c + layout.Slot / 2;

            if (c % every == 0 || column.IsHighlighted)
            {
                var label = ChartDrawing.Text(this, column.Label, 12, column.IsHighlighted ? TextBrush : MutedBrush,
                    column.IsHighlighted ? FontWeights.SemiBold : FontWeights.Normal);
                dc.DrawText(label, new Point(centre - label.Width / 2, layout.Plot.Bottom + 7));
            }

            var total = column.TotalSeconds;
            if (total > 0)
            {
                var amount = ChartDrawing.Text(this, AnalyticsViewModel.Duration(total), 11, MutedBrush);
                if (amount.Width <= layout.Slot)
                {
                    dc.DrawText(amount, new Point(centre - amount.Width / 2, layout.Bars[c].Top - amount.Height - 3));
                }
            }
        }
    }

    // ---- Small helpers ----

    /// <summary>A hairline gap between stacked pieces, only where the piece is tall enough to spare it.</summary>
    private static Rect SeparatedRect(Rect piece) =>
        piece.Height > 4 ? new Rect(piece.X, piece.Y + 1, piece.Width, piece.Height - 1) : piece;

    private static string TickLabel(double hours)
    {
        if (hours == 0)
        {
            return "0";
        }

        var whole = (int)hours;
        var minutes = (int)Math.Round((hours - whole) * 60);
        return whole == 0 ? $"{minutes}m" : minutes == 0 ? $"{whole}h" : $"{whole}h {minutes}m";
    }
}
