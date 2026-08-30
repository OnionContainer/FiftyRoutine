using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

    /// <param name="pixelWidth">含非普通混合时建议传入目标宽（DIP）；≤0 用默认。</param>
    /// <param name="pixelHeight">含非普通混合时建议传入目标高（DIP）；≤0 用默认。</param>
    public static Brush CreateBrush(BlockStyleSpec? spec, double pixelWidth = 0, double pixelHeight = 0)
    {
        spec ??= new BlockStyleSpec();
        spec.Normalize();
        if (spec.Layers.Count == 0)
            return TaskVisual.BrushOf(spec.BaseColor);

        if (spec.NeedsPixelBlend())
        {
            var w = Math.Max(1, (int)Math.Ceiling(pixelWidth > 0 ? pixelWidth : 480));
            var h = Math.Max(1, (int)Math.Ceiling(pixelHeight > 0 ? pixelHeight : 160));
            // 上限避免周板超高块一次栅格过大
            w = Math.Min(w, 1024);
            h = Math.Min(h, 4096);
            return CreateBlendedImageBrush(spec, w, h);
        }

        return CreateVectorBrush(spec);
    }

    public static FrameworkElement BuildVisual(BlockStyleSpec spec, double width, double height)
    {
        spec.Normalize();
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (spec.Layers.Count == 0 || !spec.NeedsPixelBlend())
        {
            var grid = new System.Windows.Controls.Grid { Width = width, Height = height, ClipToBounds = true };
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

        var imgBrush = CreateBlendedImageBrush(spec,
            Math.Max(1, (int)Math.Ceiling(width)),
            Math.Max(1, (int)Math.Ceiling(height)));
        return new System.Windows.Shapes.Rectangle
        {
            Width = width,
            Height = height,
            Fill = imgBrush,
            IsHitTestVisible = false
        };
    }

    private static Brush CreateVectorBrush(BlockStyleSpec spec)
    {
        // 绝对 DIP 平铺底色+纹样；DrawingBrush 不拉伸，色块只裁切可见部分
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

    /// <summary>绘制时像素合成（不烘焙样式参数）；普通/正片叠底/叠加。</summary>
    private static ImageBrush CreateBlendedImageBrush(BlockStyleSpec spec, int w, int h)
    {
        var baseC = TaskVisual.ParseColor(spec.BaseColor);
        var dst = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++)
        {
            var o = i * 4;
            dst[o] = baseC.B;
            dst[o + 1] = baseC.G;
            dst[o + 2] = baseC.R;
            dst[o + 3] = 255;
        }

        foreach (var layer in spec.Layers)
        {
            var src = RenderLayerPixels(layer, w, h);
            BlendOnto(dst, src, w * h, BlockStyleLayer.NormalizeBlend(layer.BlendMode), layer.Opacity);
        }

        var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, w, h), dst, w * 4, 0);
        bmp.Freeze();

        var brush = new ImageBrush(bmp)
        {
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            TileMode = TileMode.None
        };
        brush.Freeze();
        return brush;
    }

    private static byte[] RenderLayerPixels(BlockStyleLayer layer, int w, int h)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(CreateLayerTileBrush(layer), null, new Rect(0, 0, w, h));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var conv = new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[w * h * 4];
        conv.CopyPixels(pixels, w * 4, 0);
        return pixels;
    }

    private static void BlendOnto(byte[] dst, byte[] src, int pixelCount, string mode, double opacity)
    {
        var op = (float)Math.Clamp(opacity, 0, 1);
        for (var i = 0; i < pixelCount; i++)
        {
            var o = i * 4;
            var a = src[o + 3] / 255f * op;
            if (a < 0.001f) continue;

            var cbB = dst[o] / 255f;
            var cbG = dst[o + 1] / 255f;
            var cbR = dst[o + 2] / 255f;
            var ctB = src[o] / 255f;
            var ctG = src[o + 1] / 255f;
            var ctR = src[o + 2] / 255f;

            float csR, csG, csB;
            if (mode == "multiply")
            {
                csR = cbR * ctR;
                csG = cbG * ctG;
                csB = cbB * ctB;
            }
            else if (mode == "overlay")
            {
                csR = OverlayChannel(cbR, ctR);
                csG = OverlayChannel(cbG, ctG);
                csB = OverlayChannel(cbB, ctB);
            }
            else
            {
                csR = ctR;
                csG = ctG;
                csB = ctB;
            }

            dst[o] = (byte)Math.Clamp(((1 - a) * cbB + a * csB) * 255f + 0.5f, 0, 255);
            dst[o + 1] = (byte)Math.Clamp(((1 - a) * cbG + a * csG) * 255f + 0.5f, 0, 255);
            dst[o + 2] = (byte)Math.Clamp(((1 - a) * cbR + a * csR) * 255f + 0.5f, 0, 255);
            dst[o + 3] = 255;
        }
    }

    private static float OverlayChannel(float cb, float ct) =>
        cb < 0.5f ? 2f * cb * ct : 1f - 2f * (1f - cb) * (1f - ct);

    public static Brush CreateLayerTileBrush(BlockStyleLayer layer)
    {
        layer.Normalize();
        var color = TaskVisual.ParseColor(layer.Color);
        // 纹样本体不透明；层透明度由外层 Opacity / 像素合成控制
        var pat = new SolidColorBrush(Color.FromArgb(255, color.R, color.G, color.B));
        pat.Freeze();

        if (layer.Kind == "solid")
            return pat;

        var spacing = layer.Spacing;
        var unit = layer.Kind switch
        {
            "sine" => SineTile(pat, spacing, layer.Thickness, layer.Size),
            "stripe" => StripeTile(pat, spacing, layer.Thickness),
            "diamond" => MotifTile(pat, spacing, DiamondGeom(spacing / 2, spacing / 2, layer.Size, layer.Size)),
            "star" => MotifTile(pat, spacing, StarGeom(spacing / 2, spacing / 2, layer.Size)),
            "dot" => MotifTile(pat, spacing, new EllipseGeometry(new Point(spacing / 2, spacing / 2), layer.Size, layer.Size)),
            "moon" => MotifTile(pat, spacing, MoonGeom(spacing / 2, spacing / 2, layer.Size)),
            _ => StripeTile(pat, spacing, layer.Thickness)
        };

        Drawing drawing = unit;
        DrawingBrush brush;
        if (Math.Abs(layer.CumulativeOffsetX) > 0.01 || Math.Abs(layer.CumulativeOffsetY) > 0.01)
        {
            drawing = ExpandWithCumulative(unit, spacing, layer.OffsetX, layer.OffsetY,
                layer.CumulativeOffsetX, layer.CumulativeOffsetY);
            brush = new DrawingBrush(drawing)
            {
                TileMode = TileMode.None,
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top
            };
        }
        else
        {
            brush = new DrawingBrush(drawing)
            {
                TileMode = TileMode.Tile,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(layer.OffsetX, layer.OffsetY, spacing, spacing),
                Stretch = Stretch.None
            };
        }
        if (Math.Abs(layer.Angle) > 0.01)
            brush.Transform = new RotateTransform(layer.Angle);
        brush.Freeze();
        return brush;
    }

    /// <summary>铺满约 8k DIP：第 n 行 X+=n·cumX，第 n 列 Y+=n·cumY（n 从 1 起）。</summary>
    private static Drawing ExpandWithCumulative(
        Drawing unit, double spacing, double offX, double offY, double cumX, double cumY)
    {
        const double extent = 8192;
        var n = (int)Math.Ceiling(extent / spacing) + 2;
        var g = new DrawingGroup();
        for (var iy = -1; iy < n; iy++)
        {
            for (var ix = -1; ix < n; ix++)
            {
                var row = iy + 1; // 1-based when iy=0
                var col = ix + 1;
                if (row < 1) row = 1;
                if (col < 1) col = 1;
                var x = ix * spacing + offX + row * cumX;
                var y = iy * spacing + offY + col * cumY;
                var cell = new DrawingGroup { Transform = new TranslateTransform(x, y) };
                cell.Children.Add(unit);
                g.Children.Add(cell);
            }
        }
        g.Freeze();
        return g;
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
