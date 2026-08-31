using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PersonalManagement.Desktop;

/// <summary>缩略图卡片壳：圆角图区 + 下方说明。只持临时视觉，不写盘。</summary>
internal static class ThumbCard
{
    public sealed class Options
    {
        public double Width { get; init; }
        public double Height { get; init; }
        public double ThumbHeight { get; init; }
        public double CornerRadius { get; init; }
        public BitmapImage? Preview { get; init; }
        /// <summary>无 Preview 时图区内容（占位字等）。</summary>
        public UIElement? Placeholder { get; init; }
        public Brush? ThumbBackground { get; init; }
        /// <summary>叠在图区之上（勾选框、按钮等）。</summary>
        public UIElement? ThumbOverlay { get; init; }
        public string Caption { get; init; } = "";
        public TextWrapping CaptionWrapping { get; init; } = TextWrapping.Wrap;
        public TextTrimming CaptionTrimming { get; init; } = TextTrimming.None;
        public object? CaptionToolTip { get; init; }
        public Brush? CardBackground { get; init; }
        public Brush? BorderBrush { get; init; }
        public double BorderThickness { get; init; } = 1;
        public double Opacity { get; init; } = 1;
        public object? Tag { get; init; }
        public Action? OnSelect { get; init; }
        public Func<Task>? OnDoubleClick { get; init; }
    }

    public static Border Build(Options o)
    {
        var thumbRadius = Math.Max(0, o.CornerRadius - 2);
        UIElement? inner = null;
        if (o.Preview is not null)
        {
            inner = new Image
            {
                Height = o.ThumbHeight,
                Stretch = Stretch.UniformToFill,
                Source = o.Preview
            };
        }
        else if (o.Placeholder is not null)
        {
            inner = o.Placeholder;
        }

        var icon = new Border
        {
            Height = o.ThumbHeight,
            Background = o.ThumbBackground ?? (o.Preview is null
                ? Theme.Brush("SurfaceBackgroundBrush")
                : Brushes.Transparent),
            Child = inner,
            CornerRadius = new CornerRadius(thumbRadius),
            ClipToBounds = true
        };
        UiShapes.RoundClip(icon, thumbRadius);

        var thumbGrid = new Grid { Height = o.ThumbHeight };
        thumbGrid.Children.Add(icon);
        if (o.ThumbOverlay is not null)
            thumbGrid.Children.Add(o.ThumbOverlay);

        var caption = new TextBlock
        {
            Text = o.Caption,
            TextWrapping = o.CaptionWrapping,
            TextTrimming = o.CaptionTrimming,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = Theme.Brush("TextPrimaryBrush"),
            FontSize = Theme.Current.FontSizeBody,
            ToolTip = o.CaptionToolTip
        };

        var stack = new StackPanel();
        stack.Children.Add(thumbGrid);
        stack.Children.Add(caption);

        var border = new Border
        {
            Width = o.Width,
            Height = o.Height,
            Margin = new Thickness(6),
            Padding = new Thickness(10),
            Background = o.CardBackground ?? Theme.Brush("SurfaceBackgroundBrush"),
            BorderBrush = o.BorderBrush ?? Theme.Brush("BorderSubtleBrush"),
            BorderThickness = new Thickness(o.BorderThickness),
            CornerRadius = new CornerRadius(o.CornerRadius),
            Cursor = Cursors.Hand,
            Opacity = o.Opacity,
            Child = stack,
            Tag = o.Tag
        };

        if (o.OnSelect is not null || o.OnDoubleClick is not null)
        {
            border.MouseLeftButtonDown += async (_, e) =>
            {
                o.OnSelect?.Invoke();
                if (e.ClickCount == 2 && o.OnDoubleClick is not null)
                    await o.OnDoubleClick();
            };
        }

        return border;
    }

    public static TextBlock Hint(string text) => new()
    {
        Text = text,
        Margin = new Thickness(12),
        Foreground = Theme.Brush("TextSecondaryBrush"),
        TextWrapping = TextWrapping.Wrap
    };
}
