using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PersonalManagement.Desktop;

/// <summary>日程块统计折线图：画到给定 Canvas。临时视觉，不写盘。</summary>
internal static class ScheduleStatsChart
{
    public static void Render(
        Canvas? canvas,
        bool bodyVisible,
        IReadOnlyDictionary<DateTime, double> directHours,
        IReadOnlyDictionary<DateTime, double> otherHours)
    {
        if (canvas is null || !bodyVisible) return;
        canvas.Children.Clear();
        var w = canvas.ActualWidth;
        var h = canvas.ActualHeight;
        if (w < 40 || h < 40) return;

        const double padL = 28, padR = 8, padT = 8, padB = 22;
        var plotW = Math.Max(10, w - padL - padR);
        var plotH = Math.Max(10, h - padT - padB);
        const double maxH = 16;

        var gridBrush = Theme.Brush("GridLineBrush");
        for (var hour = 0; hour <= 16; hour += 4)
        {
            var y = padT + plotH * (1 - hour / maxH);
            canvas.Children.Add(new Line
            {
                X1 = padL,
                X2 = padL + plotW,
                Y1 = y,
                Y2 = y,
                Stroke = gridBrush,
                StrokeThickness = 1,
                StrokeDashArray = hour == 0 || hour == 16 ? null : new DoubleCollection { 2, 2 },
                IsHitTestVisible = false
            });
            canvas.Children.Add(new TextBlock
            {
                Text = $"{hour}h",
                FontSize = 10,
                Foreground = Theme.Brush("TextSecondaryBrush"),
                Margin = new Thickness(2, y - 7, 0, 0)
            });
        }

        var days = directHours.Keys.OrderBy(d => d).ToList();
        if (days.Count == 0)
        {
            var tip = new TextBlock
            {
                Text = "暂无数据",
                Foreground = Theme.Brush("TextSecondaryBrush"),
                FontSize = 12
            };
            Canvas.SetLeft(tip, padL + 8);
            Canvas.SetTop(tip, padT + 8);
            canvas.Children.Add(tip);
            return;
        }

        Point Map(int i, double hours)
        {
            var x = padL + (days.Count == 1 ? plotW / 2 : plotW * i / (days.Count - 1));
            var y = padT + plotH * (1 - Math.Clamp(hours, 0, maxH) / maxH);
            return new Point(x, y);
        }

        void DrawSeries(IReadOnlyDictionary<DateTime, double> series, Brush stroke)
        {
            var poly = new Polyline
            {
                Stroke = stroke,
                StrokeThickness = 2,
                IsHitTestVisible = false
            };
            for (var i = 0; i < days.Count; i++)
                poly.Points.Add(Map(i, series.GetValueOrDefault(days[i])));
            canvas.Children.Add(poly);
        }

        DrawSeries(otherHours, new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B)));
        DrawSeries(directHours, new SolidColorBrush(Color.FromRgb(0x5B, 0x9B, 0xD5)));

        var labelEvery = days.Count > 14 ? 3 : days.Count > 7 ? 2 : 1;
        for (var i = 0; i < days.Count; i++)
        {
            if (i % labelEvery != 0 && i != days.Count - 1) continue;
            var p = Map(i, 0);
            var label = new TextBlock
            {
                Text = days[i].ToString("M/d"),
                FontSize = 10,
                Foreground = Theme.Brush("TextSecondaryBrush")
            };
            Canvas.SetLeft(label, p.X - 10);
            Canvas.SetTop(label, padT + plotH + 4);
            canvas.Children.Add(label);
        }
    }
}
