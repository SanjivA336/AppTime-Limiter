using System.Globalization;
using System.Windows;
using System.Windows.Media;
using HardCap.Core.Usage;

namespace HardCap.App.Controls;

/// <summary>
/// The small drawing jobs the hand-drawn charts share: text, colours, pie slices and the floating information card. The
/// charts are drawn in code (not with a chart library) so they follow the app's colours and need nothing extra.
/// </summary>
internal static class ChartDrawing
{
    /// <summary>How much lighter (positive) or darker (negative) each app's slice is than its group's colour, in turn.</summary>
    public static readonly double[] AppShades = { 0.0, 0.30, -0.25, 0.50, -0.40 };

    public static Color ColorOf(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// <summary>Lightens (positive) or darkens (negative) a colour by mixing it with white or black.</summary>
    public static Color Shade(Color color, double amount)
    {
        var target = amount >= 0 ? Colors.White : Colors.Black;
        var t = Math.Abs(amount);
        byte Mix(byte from, byte to) => (byte)Math.Round(from + (to - from) * t);
        return Color.FromRgb(Mix(color.R, target.R), Mix(color.G, target.G), Mix(color.B, target.B));
    }

    public static FormattedText Text(Visual owner, string text, double size, Brush brush, FontWeight? weight = null) => new(
        text,
        CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        new Typeface(new FontFamily("Segoe UI Variable, Segoe UI"), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal),
        size,
        brush,
        VisualTreeHelper.GetDpi(owner).PixelsPerDip);

    /// <summary>
    /// One slice of a pie or ring, starting at <paramref name="startDegrees"/> (0 is straight up, going clockwise) and
    /// sweeping <paramref name="sweepDegrees"/>. A full circle can't be drawn as one arc, so it is drawn as two halves.
    /// </summary>
    public static Geometry Slice(Point center, double outerRadius, double innerRadius, double startDegrees, double sweepDegrees)
    {
        sweepDegrees = Math.Clamp(sweepDegrees, 0, 360);
        var isFull = sweepDegrees >= 359.999;
        var sweep = isFull ? 359.99 : sweepDegrees;

        Point PointAt(double radius, double degrees)
        {
            var radians = (degrees - 90) * Math.PI / 180;
            return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
        }

        var end = startDegrees + sweep;
        var large = sweep > 180;
        var figure = new PathFigure { StartPoint = PointAt(outerRadius, startDegrees), IsClosed = true };
        figure.Segments.Add(new ArcSegment(PointAt(outerRadius, end), new Size(outerRadius, outerRadius), 0, large, SweepDirection.Clockwise, true));

        if (innerRadius > 0)
        {
            figure.Segments.Add(new LineSegment(PointAt(innerRadius, end), true));
            figure.Segments.Add(new ArcSegment(PointAt(innerRadius, startDegrees), new Size(innerRadius, innerRadius), 0, large, SweepDirection.Counterclockwise, true));
        }
        else
        {
            figure.Segments.Add(new LineSegment(center, true));
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// Draws a pie (or, with an inner radius, a ring) with one slice for each group that has time, in the group's colour.
    /// Returns where each slice went (the group's index in <paramref name="groups"/>, and its start and sweep in degrees), so
    /// a caller can find the slice under the pointer or split a slice into its apps.
    /// </summary>
    public static List<(int Group, double Start, double Sweep)> DrawPie(
        DrawingContext dc, Point center, double outerRadius, double innerRadius, IReadOnlyList<GroupUsage> groups,
        Func<int, double> opacityOf)
    {
        var total = groups.Sum(g => g.Seconds);
        var placed = new List<(int, double, double)>();
        if (total <= 0)
        {
            return placed;
        }

        double angle = 0;
        for (var i = 0; i < groups.Count; i++)
        {
            if (groups[i].Seconds <= 0)
            {
                continue;
            }

            var sweep = 360 * groups[i].Seconds / total;
            placed.Add((i, angle, sweep));
            var opacity = opacityOf(i);
            if (opacity > 0)
            {
                dc.PushOpacity(opacity);
                dc.DrawGeometry(new SolidColorBrush(ColorOf(groups[i].Color)), null, Slice(center, outerRadius, innerRadius, angle, sweep));
                dc.Pop();
            }

            angle += sweep;
        }

        return placed;
    }

    /// <summary>
    /// The little card that floats beside whatever is pointed at: a title, a line under it, and rows of text with a
    /// right-hand value. It sits to the right of <paramref name="anchor"/>, or to its left when there is no room.
    /// </summary>
    public static void DrawInfoCard(
        DrawingContext dc, Visual owner, Size surface, Rect anchor, double opacity,
        string title, string subtitle, IReadOnlyList<(string Name, string Value)> rows,
        Brush textBrush, Brush mutedBrush, Brush cardBrush, Brush cardBorderBrush)
    {
        if (opacity <= 0.01)
        {
            return;
        }

        const double padding = 10;
        var titleText = Text(owner, title, 12, textBrush, FontWeights.SemiBold);
        var subtitleText = Text(owner, subtitle, 11, mutedBrush);
        var lines = rows.Select(r => (Name: Text(owner, r.Name, 11, textBrush), Value: Text(owner, r.Value, 11, mutedBrush))).ToList();

        var nameWidth = lines.Count == 0 ? 0 : lines.Max(l => l.Name.Width);
        var valueWidth = lines.Count == 0 ? 0 : lines.Max(l => l.Value.Width);
        var width = Math.Max(Math.Max(titleText.Width, subtitleText.Width), nameWidth + 18 + valueWidth) + padding * 2;
        var height = padding * 2 + titleText.Height + subtitleText.Height + (lines.Count > 0 ? 6 : 0) + lines.Sum(l => l.Name.Height + 2);

        var x = anchor.Right + 12;
        if (x + width > surface.Width - 4)
        {
            x = anchor.Left - 12 - width;
        }

        x = Math.Max(4, x);
        var y = Math.Clamp(anchor.Top + anchor.Height / 2 - height / 2, 4, Math.Max(4, surface.Height - height - 4));

        dc.PushOpacity(opacity);
        dc.DrawRoundedRectangle(cardBrush, new Pen(cardBorderBrush, 1), new Rect(x, y, width, height), 6, 6);

        var top = y + padding;
        dc.DrawText(titleText, new Point(x + padding, top));
        top += titleText.Height;
        dc.DrawText(subtitleText, new Point(x + padding, top));
        top += subtitleText.Height + 6;

        foreach (var (name, value) in lines)
        {
            dc.DrawText(name, new Point(x + padding, top));
            dc.DrawText(value, new Point(x + width - padding - value.Width, top));
            top += name.Height + 2;
        }

        dc.Pop();
    }

    /// <summary>The rows of an app breakdown for a card: each app and its time, with "+N more" after the first few.</summary>
    public static List<(string Name, string Value)> AppRows(GroupUsage group, int maxApps, Func<double, string> formatTime)
    {
        var apps = group.Apps.Where(a => a.Seconds > 0).ToList();
        var rows = apps.Take(maxApps).Select(a => (a.Name, formatTime(a.Seconds))).ToList();

        if (apps.Count > maxApps)
        {
            rows.Add(($"+{apps.Count - maxApps} more", string.Empty));
        }

        if (apps.Count == 0)
        {
            rows.Add(("No app detail recorded", string.Empty));
        }

        return rows;
    }
}
