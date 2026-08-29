using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PersonalManagement.Desktop;

/// <summary>日程块纹样：旧枚举兼容 + 多层 BlockStyleSpec 渲染。</summary>
internal static class BlockPatterns
{
    public const string None = "none";
    public const string StripeRight = "stripe-right";
    public const string StripeLeft = "stripe-left";
    public const string Diamond = "diamond";
    public const string Star = "star";
    public const string Dot = "dot";
    public const string Moon = "moon";

    public const string DefaultPatternColor = "#2C2C2C";

    public static readonly (string Id, string Label)[] All =
    [
        (None, "无（纯色）"),
        (StripeRight, "右斜纹"),
        (StripeLeft, "左斜纹"),
        (Diamond, "棱形散布"),
        (Star, "星形散布"),
        (Dot, "圆点散布"),
        (Moon, "月亮散布")
    ];

    public static string Normalize(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return None;
        foreach (var (key, _) in All)
            if (key.Equals(id, StringComparison.OrdinalIgnoreCase))
                return key;
        return None;
    }

    public static string Label(string? id)
    {
        var n = Normalize(id);
        foreach (var (key, label) in All)
            if (key == n) return label;
        return n;
    }

    public static Brush CreateBrush(string? baseHex, string? patternId, string? patternHex) =>
        CreateBrush(BlockStyleSpec.FromLegacy(baseHex, patternId, patternHex));

    public static Brush CreateBrush(BlockStyleSpec? spec)
    {
        spec ??= new BlockStyleSpec();
        spec.Normalize();
        if (spec.Layers.Count == 0)
            return TaskVisual.BrushOf(spec.BaseColor);

        // 绝对 DIP 平铺底色+纹样；DrawingBrush 不拉伸，色块只裁切可见部分
        // （旧 VisualBrush+Stretch.Fill 会随色块宽高把纹样拉变形）
        const double extent = 8192;
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            TaskVisual.BrushOf(spec.BaseColor),
            null,
            new RectangleGeometry(new Rect(0, 0, extent, extent))));

        foreach (var layer in spec.Layers)
        {
            var layerDraw = new DrawingGroup { Opacity = Math.Clamp(layer.Opacity, 0, 1) };
            layerDraw.Children.Add(new GeometryDrawing(
                CreateLayerTileBrush(layer),
                null,
                new RectangleGeometry(new Rect(0, 0, extent, extent))));
            group.Children.Add(layerDraw);
        }
        group.Freeze();

        var brush = new DrawingBrush(group)
        {
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            TileMode = TileMode.None
        };
        brush.Freeze();
        return brush;
    }

    public static FrameworkElement BuildVisual(BlockStyleSpec spec, double width, double height)
    {
        spec.Normalize();
        var grid = new Grid { Width = width, Height = height, ClipToBounds = true };
        grid.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Fill = TaskVisual.BrushOf(spec.BaseColor),
            Width = width,
            Height = height
        });
        foreach (var layer in spec.Layers)
        {
            var brush = CreateLayerTileBrush(layer);
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = width,
                Height = height,
                Fill = brush,
                Opacity = layer.Opacity,
                IsHitTestVisible = false
            };
            grid.Children.Add(rect);
        }
        return grid;
    }

    public static Brush CreateLayerTileBrush(BlockStyleLayer layer)
    {
        layer.Normalize();
        var spacing = layer.Spacing;
        var color = TaskVisual.ParseColor(layer.Color);
        // 纹样本体不透明；层透明度由外层 Opacity 控制
        var pat = new SolidColorBrush(Color.FromArgb(255, color.R, color.G, color.B));
        pat.Freeze();

        var drawing = layer.Kind switch
        {
            "sine" => SineTile(pat, spacing, layer.Thickness, layer.Size),
            "stripe" => StripeTile(pat, spacing, layer.Thickness),
            "diamond" => MotifTile(pat, spacing, DiamondGeom(spacing / 2, spacing / 2, layer.Size, layer.Size)),
            "star" => MotifTile(pat, spacing, StarGeom(spacing / 2, spacing / 2, layer.Size)),
            "dot" => MotifTile(pat, spacing, new EllipseGeometry(new Point(spacing / 2, spacing / 2), layer.Size, layer.Size)),
            "moon" => MotifTile(pat, spacing, MoonGeom(spacing / 2, spacing / 2, layer.Size)),
            _ => StripeTile(pat, spacing, layer.Thickness)
        };

        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(layer.OffsetX, layer.OffsetY, spacing, spacing),
            Stretch = Stretch.None
        };
        if (Math.Abs(layer.Angle) > 0.01)
        {
            brush.Transform = new RotateTransform(layer.Angle);
        }
        brush.Freeze();
        return brush;
    }

    private static Drawing StripeTile(Brush pat, double spacing, double thickness)
    {
        var t = Math.Min(thickness, spacing);
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(pat, null, new RectangleGeometry(new Rect(0, 0, spacing, t))));
        g.Freeze();
        return g;
    }

    private static Drawing SineTile(Brush pat, double spacing, double thickness, double amplitude)
    {
        var amp = Math.Min(amplitude, spacing * 0.45);
        var mid = spacing / 2;
        var fig = new PathFigure { StartPoint = new Point(0, mid) };
        const int steps = 16;
        for (var i = 1; i <= steps; i++)
        {
            var x = spacing * i / steps;
            var y = mid + amp * Math.Sin(2 * Math.PI * i / steps);
            fig.Segments.Add(new LineSegment(new Point(x, y), true));
        }
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        geo.Freeze();
        var pen = new Pen(pat, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(null, pen, geo));
        g.Freeze();
        return g;
    }

    private static Drawing MotifTile(Brush pat, double spacing, Geometry motif)
    {
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(pat, null, motif));
        g.Freeze();
        return g;
    }

    private static Geometry DiamondGeom(double cx, double cy, double rx, double ry)
    {
        var fig = new PathFigure { StartPoint = new Point(cx, cy - ry), IsClosed = true };
        fig.Segments.Add(new LineSegment(new Point(cx + rx, cy), true));
        fig.Segments.Add(new LineSegment(new Point(cx, cy + ry), true));
        fig.Segments.Add(new LineSegment(new Point(cx - rx, cy), true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        geo.Freeze();
        return geo;
    }

    private static Geometry StarGeom(double cx, double cy, double r)
    {
        var fig = new PathFigure { IsClosed = true };
        for (var i = 0; i < 5; i++)
        {
            var a = -Math.PI / 2 + i * 2 * Math.PI / 5;
            var b = a + Math.PI / 5;
            var outer = new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            var inner = new Point(cx + r * 0.42 * Math.Cos(b), cy + r * 0.42 * Math.Sin(b));
            if (i == 0) fig.StartPoint = outer;
            else fig.Segments.Add(new LineSegment(outer, true));
            fig.Segments.Add(new LineSegment(inner, true));
        }
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        geo.Freeze();
        return geo;
    }

    private static Geometry MoonGeom(double cx, double cy, double r)
    {
        var full = new EllipseGeometry(new Point(cx, cy), r, r);
        var cut = new EllipseGeometry(new Point(cx + r * 0.45, cy - r * 0.1), r * 0.78, r * 0.78);
        var combined = new CombinedGeometry(GeometryCombineMode.Exclude, full, cut);
        combined.Freeze();
        return combined;
    }
}
