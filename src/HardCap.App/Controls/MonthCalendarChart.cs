using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HardCap.App.ViewModels;
using HardCap.Core.Usage;

namespace HardCap.App.Controls;

/// <summary>
/// A month as a calendar. Each day that has usage shows a small pie of how it was split between groups, with the day's
/// total time under it. The ISO week number sits on the left of every row. Point at a day for a text summary of its
/// groups and their percentages; click a day to open it, or a week number to open that week.
/// </summary>
public sealed class MonthCalendarChart : FrameworkElement
{
    private const double WeekColumnWidth = 48;
    private const double HeaderHeight = 28;
    private const double RowHeight = 100;
    private const double PieRadius = 27;

    public static readonly DependencyProperty DaysProperty = DependencyProperty.Register(
        nameof(Days), typeof(IReadOnlyList<DayUsageSlice>), typeof(MonthCalendarChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TodayProperty = DependencyProperty.Register(
        nameof(Today), typeof(DateOnly), typeof(MonthCalendarChart),
        new FrameworkPropertyMetadata(default(DateOnly), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextBrushProperty = BrushProperty(nameof(TextBrush), Brushes.White);
    public static readonly DependencyProperty MutedBrushProperty = BrushProperty(nameof(MutedBrush), Brushes.Gray);
    public static readonly DependencyProperty GridBrushProperty = BrushProperty(nameof(GridBrush), Brushes.DimGray);
    public static readonly DependencyProperty AccentBrushProperty = BrushProperty(nameof(AccentBrush), Brushes.DodgerBlue);
    public static readonly DependencyProperty CardBrushProperty = BrushProperty(nameof(CardBrush), Brushes.Black);
    public static readonly DependencyProperty CardBorderBrushProperty = BrushProperty(nameof(CardBorderBrush), Brushes.Gray);

    /// <summary>How visible the summary card is, from 0 to 1 (it fades in when a day is pointed at).</summary>
    public static readonly DependencyProperty HoverProgressProperty = DependencyProperty.Register(
        nameof(HoverProgress), typeof(double), typeof(MonthCalendarChart),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private DateOnly? _shownDay;      // the day whose card is showing (kept while it fades out)
    private DateOnly? _pointerDay;    // the day or week the pointer is over now, for the highlight
    private DateOnly? _pointerWeek;
    private bool _pointerIsOver;

    // True once a hover has been previewed for a screenshot: the real mouse is then ignored, so it can't cancel the preview.
    private bool _isPreview;

    /// <summary>Raised when a day is clicked.</summary>
    public event Action<DateOnly>? DayClicked;

    /// <summary>Raised when a week number is clicked, with that week's Monday.</summary>
    public event Action<DateOnly>? WeekClicked;

    /// <summary>The days of the month shown, each with its groups' use.</summary>
    public IReadOnlyList<DayUsageSlice>? Days
    {
        get => (IReadOnlyList<DayUsageSlice>?)GetValue(DaysProperty);
        set => SetValue(DaysProperty, value);
    }

    public DateOnly Today
    {
        get => (DateOnly)GetValue(TodayProperty);
        set => SetValue(TodayProperty, value);
    }

    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }

    public Brush MutedBrush { get => (Brush)GetValue(MutedBrushProperty); set => SetValue(MutedBrushProperty, value); }

    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }

    public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    public Brush CardBrush { get => (Brush)GetValue(CardBrushProperty); set => SetValue(CardBrushProperty, value); }

    public Brush CardBorderBrush { get => (Brush)GetValue(CardBorderBrushProperty); set => SetValue(CardBorderBrushProperty, value); }

    public double HoverProgress
    {
        get => (double)GetValue(HoverProgressProperty);
        set => SetValue(HoverProgressProperty, value);
    }

    private static DependencyProperty BrushProperty(string name, Brush fallback) => DependencyProperty.Register(
        name, typeof(Brush), typeof(MonthCalendarChart), new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender));

    // ---- Layout ----

    /// <summary>The Mondays of the weeks that show, from the one holding the 1st to the one holding the last day.</summary>
    private List<DateOnly> WeekStarts()
    {
        if (Days is not { Count: > 0 } days)
        {
            return new List<DateOnly>();
        }

        var first = UsageAnalytics.WeekStartOf(days[0].Date);
        var last = UsageAnalytics.WeekStartOf(days[^1].Date);
        var weeks = new List<DateOnly>();
        for (var monday = first; monday <= last; monday = monday.AddDays(7))
        {
            weeks.Add(monday);
        }

        return weeks;
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 700 : availableSize.Width, HeaderHeight + RowHeight * Math.Max(1, WeekStarts().Count));

    private double DayWidth => (ActualWidth - WeekColumnWidth) / 7;

    private Rect DayCell(DateOnly monday, int dayOfWeek, int weekIndex) =>
        new(WeekColumnWidth + DayWidth * dayOfWeek, HeaderHeight + RowHeight * weekIndex, DayWidth, RowHeight);

    private Rect WeekCell(int weekIndex) => new(0, HeaderHeight + RowHeight * weekIndex, WeekColumnWidth, RowHeight);

    /// <summary>What the pointer is over: a day of the month that has happened, or a week number.</summary>
    private (DateOnly? Day, DateOnly? Week) Hit(Point position)
    {
        var weeks = WeekStarts();
        if (position.Y < HeaderHeight || weeks.Count == 0)
        {
            return (null, null);
        }

        var row = (int)((position.Y - HeaderHeight) / RowHeight);
        if (row < 0 || row >= weeks.Count)
        {
            return (null, null);
        }

        if (position.X < WeekColumnWidth)
        {
            return weeks[row] <= Today ? (null, weeks[row]) : (null, null);
        }

        var column = (int)((position.X - WeekColumnWidth) / DayWidth);
        if (column < 0 || column > 6)
        {
            return (null, null);
        }

        var day = weeks[row].AddDays(column);
        return IsInMonth(day) && day <= Today ? (day, null) : (null, null);
    }

    private bool IsInMonth(DateOnly day) => Days is { Count: > 0 } days && day >= days[0].Date && day <= days[^1].Date;

    private DayUsageSlice? SliceOf(DateOnly day) => Days?.FirstOrDefault(d => d.Date == day);

    // ---- Pointer ----

    /// <summary>The same, for the day of the month with this number.</summary>
    public void PreviewHover(int dayOfMonth)
    {
        if (Days is { } days && dayOfMonth >= 1 && dayOfMonth <= days.Count)
        {
            PreviewHover(days[dayOfMonth - 1].Date);
        }
    }

    /// <summary>Shows a day's summary card as if the pointer were on it. Development aid for screenshots (there is no mouse in them).</summary>
    public void PreviewHover(DateOnly day)
    {
        _shownDay = day;
        _pointerDay = day;
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

        var (day, week) = Hit(e.GetPosition(this));
        if (day != _pointerDay || week != _pointerWeek)
        {
            _pointerDay = day;
            _pointerWeek = week;
            Cursor = day is not null || week is not null ? Cursors.Hand : Cursors.Arrow;
            InvalidateVisual();
        }

        var hasUsage = day is DateOnly d && SliceOf(d) is { TotalSeconds: > 0 };
        if (hasUsage)
        {
            if (day != _shownDay)
            {
                _shownDay = day;
                AnimateHover(0, 1, 200);
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
        _pointerDay = null;
        _pointerWeek = null;
        Cursor = Cursors.Arrow;
        FadeOut();
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        var (day, week) = Hit(e.GetPosition(this));
        if (day is DateOnly clickedDay)
        {
            DayClicked?.Invoke(clickedDay);
        }
        else if (week is DateOnly clickedWeek)
        {
            WeekClicked?.Invoke(clickedWeek);
        }
    }

    private void FadeOut()
    {
        if (_shownDay is not null && HoverProgress > 0)
        {
            AnimateHover(HoverProgress, 0, 140, forgetWhenDone: true);
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
                    _shownDay = null;
                    InvalidateVisual();
                }
            };
        }

        BeginAnimation(HoverProgressProperty, animation);
    }

    // ---- Drawing ----

    protected override void OnRender(DrawingContext dc)
    {
        var weeks = WeekStarts();
        if (weeks.Count == 0 || ActualWidth < 200)
        {
            return;
        }

        DrawHeader(dc);

        var gridPen = new Pen(GridBrush, 1);
        for (var w = 0; w < weeks.Count; w++)
        {
            var rowTop = HeaderHeight + RowHeight * w;
            dc.DrawLine(gridPen, new Point(0, Math.Round(rowTop) + 0.5), new Point(ActualWidth, Math.Round(rowTop) + 0.5));

            DrawWeekNumber(dc, weeks[w], w);
            for (var d = 0; d < 7; d++)
            {
                DrawDay(dc, weeks[w].AddDays(d), DayCell(weeks[w], d, w));
            }
        }

        if (_shownDay is DateOnly shownDay && SliceOf(shownDay) is { TotalSeconds: > 0 } slice)
        {
            DrawSummaryCard(dc, weeks, shownDay, slice);
        }
    }

    private void DrawHeader(DrawingContext dc)
    {
        var week = ChartDrawing.Text(this, "Wk", 11, MutedBrush);
        dc.DrawText(week, new Point((WeekColumnWidth - week.Width) / 2, (HeaderHeight - week.Height) / 2));

        for (var d = 0; d < 7; d++)
        {
            var name = new DateOnly(2026, 10, 19).AddDays(d).ToString("ddd", CultureInfo.CurrentCulture); // 19 Oct 2026 is a Monday
            var label = ChartDrawing.Text(this, name, 12, MutedBrush);
            dc.DrawText(label, new Point(WeekColumnWidth + DayWidth * d + (DayWidth - label.Width) / 2, (HeaderHeight - label.Height) / 2));
        }
    }

    private void DrawWeekNumber(DrawingContext dc, DateOnly monday, int weekIndex)
    {
        var cell = WeekCell(weekIndex);
        var isPointedAt = _pointerWeek == monday;
        if (isPointedAt)
        {
            dc.PushOpacity(0.10);
            dc.DrawRoundedRectangle(MutedBrush, null, new Rect(cell.X + 4, cell.Y + 6, cell.Width - 8, cell.Height - 12), 6, 6);
            dc.Pop();
        }

        var clickable = monday <= Today;
        var number = ChartDrawing.Text(this, AnalyticsPeriod.WeekNumber(monday).ToString(CultureInfo.InvariantCulture), 13,
            isPointedAt ? TextBrush : MutedBrush, isPointedAt ? FontWeights.SemiBold : FontWeights.Normal);
        dc.PushOpacity(clickable ? 1 : 0.5);
        dc.DrawText(number, new Point(cell.X + (cell.Width - number.Width) / 2, cell.Y + (cell.Height - number.Height) / 2));
        dc.Pop();
    }

    private void DrawDay(DrawingContext dc, DateOnly day, Rect cell)
    {
        var inMonth = IsInMonth(day);
        var isFuture = day > Today;
        var isPointedAt = _pointerDay == day;

        if (isPointedAt)
        {
            dc.PushOpacity(0.08);
            dc.DrawRoundedRectangle(MutedBrush, null, new Rect(cell.X + 3, cell.Y + 3, cell.Width - 6, cell.Height - 6), 8, 8);
            dc.Pop();
        }

        if (day == Today)
        {
            dc.DrawRoundedRectangle(null, new Pen(AccentBrush, 1.5), new Rect(cell.X + 3, cell.Y + 3, cell.Width - 6, cell.Height - 6), 8, 8);
        }

        // Days from the months either side are only there to fill the week, so they are faint and empty.
        dc.PushOpacity(inMonth ? (isFuture ? 0.55 : 1) : 0.3);
        var number = ChartDrawing.Text(this, day.Day.ToString(CultureInfo.InvariantCulture), 12, day == Today ? TextBrush : MutedBrush,
            day == Today ? FontWeights.SemiBold : FontWeights.Normal);
        dc.DrawText(number, new Point(cell.X + 10, cell.Y + 7));
        dc.Pop();

        if (!inMonth || SliceOf(day) is not { TotalSeconds: > 0 } slice)
        {
            return;
        }

        var center = new Point(cell.X + cell.Width / 2, cell.Y + 24 + PieRadius);
        ChartDrawing.DrawPie(dc, center, PieRadius, 0, slice.Groups, _ => 1.0);

        var total = ChartDrawing.Text(this, AnalyticsViewModel.Duration(slice.TotalSeconds), 11, TextBrush);
        dc.DrawText(total, new Point(center.X - total.Width / 2, center.Y + PieRadius + 5));
    }

    /// <summary>The summary beside the day: its total, then each group with its share and time.</summary>
    private void DrawSummaryCard(DrawingContext dc, List<DateOnly> weeks, DateOnly day, DayUsageSlice slice)
    {
        var week = weeks.FindIndex(monday => day >= monday && day <= monday.AddDays(6));
        if (week < 0)
        {
            return;
        }

        var cell = DayCell(weeks[week], (day.DayNumber - weeks[week].DayNumber), week);
        var total = slice.TotalSeconds;
        var rows = slice.Groups
            .Where(g => g.Seconds > 0)
            .OrderByDescending(g => g.Seconds)
            .Select(g => (g.Name, $"{g.Seconds / total * 100:0}%  ·  {AnalyticsViewModel.Duration(g.Seconds)}"))
            .ToList();

        ChartDrawing.DrawInfoCard(
            dc, this, new Size(ActualWidth, ActualHeight), cell, HoverProgress,
            day.ToString("dddd, MMM d", CultureInfo.CurrentCulture), AnalyticsViewModel.Duration(total) + " in total", rows,
            TextBrush, MutedBrush, CardBrush, CardBorderBrush);
    }
}
