using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PersonalManagement.Desktop;

/// <summary>HLS：外圈色相环 + 内三角（饱和度/亮度）。支持点击与长按拖动即时取色。</summary>
public sealed class HlsColorPicker : FrameworkElement
{
    public static readonly DependencyProperty SelectedColorProperty =
        DependencyProperty.Register(nameof(SelectedColor), typeof(Color), typeof(HlsColorPicker),
            new FrameworkPropertyMetadata(Colors.Red, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedColorChanged));

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    public event Action<Color>? ColorChanged;

    private double _h = 0, _l = 0.5, _s = 1;
    private bool _internal;
    private bool _dragging;
    private enum DragTarget { None, Wheel, Triangle }
    private DragTarget _target;
    private DispatcherTimer? _pressTimer;
    private Point _pressPoint;
    private WriteableBitmap? _wheelBmp;
    private WriteableBitmap? _triBmp;
    private int _bmpSize;

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var p = (HlsColorPicker)d;
        if (p._internal) return;
        p.FromColor((Color)e.NewValue);
        p.InvalidateVisual();
    }

    public HlsColorPicker()
    {
        Width = 220;
        Height = 220;
        SnapsToDevicePixels = true;
        Focusable = true;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        RebuildBitmaps();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = (int)Math.Min(ActualWidth, ActualHeight);
        if (size < 40) return;
        if (_wheelBmp is null || _bmpSize != size) RebuildBitmaps();

        var cx = ActualWidth / 2;
        var cy = ActualHeight / 2;
        var outer = size / 2.0;
        if (_wheelBmp is not null)
            dc.DrawImage(_wheelBmp, new Rect(cx - outer, cy - outer, size, size));
        if (_triBmp is not null)
        {
            var tri = outer * 0.62;
            dc.DrawImage(_triBmp, new Rect(cx - tri, cy - tri, tri * 2, tri * 2));
        }

        // 指示器
        var (wx, wy) = HueToPoint(cx, cy, outer * 0.88, _h);
        dc.DrawEllipse(null, new Pen(Brushes.White, 2), new Point(wx, wy), 5, 5);
        dc.DrawEllipse(null, new Pen(Brushes.Black, 1), new Point(wx, wy), 5, 5);

        var (tx, ty) = SlToTrianglePoint(cx, cy, outer * 0.62, _s, _l);
        dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, 1.5), new Point(tx, ty), 4, 4);
    }

    private void RebuildBitmaps()
    {
        var size = (int)Math.Min(ActualWidth, ActualHeight);
        if (size < 40) return;
        _bmpSize = size;
        _wheelBmp = BuildWheel(size);
        var tri = (int)(size * 0.62);
        if (tri % 2 == 1) tri++;
        _triBmp = BuildTriangle(Math.Max(40, tri));
    }

    private WriteableBitmap BuildWheel(int size)
    {
        var bmp = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[size * size * 4];
        var cx = (size - 1) / 2.0;
        var cy = (size - 1) / 2.0;
        var outer = size / 2.0 - 1;
        var inner = outer * 0.72;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var dx = x - cx;
            var dy = y - cy;
            var r = Math.Sqrt(dx * dx + dy * dy);
            var i = (y * size + x) * 4;
            if (r > outer || r < inner) continue;
            var hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
            var c = HlsToRgb(hue, 0.5, 1);
            pixels[i] = c.B;
            pixels[i + 1] = c.G;
            pixels[i + 2] = c.R;
            pixels[i + 3] = 255;
        }
        bmp.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        bmp.Freeze();
        return bmp;
    }

    private WriteableBitmap BuildTriangle(int size)
    {
        var bmp = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[size * size * 4];
        // 等边三角：顶=纯色相，左下=黑，右下=白（HLS 简化）
        var pH = new Point(size * 0.5, size * 0.08);
        var pK = new Point(size * 0.10, size * 0.92);
        var pW = new Point(size * 0.90, size * 0.92);
        var pure = HlsToRgb(_h, 0.5, 1);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            if (!Barycentric(new Point(x, y), pH, pK, pW, out var a, out var b, out var c))
                continue;
            // a→hue, b→black, c→white
            var r = a * pure.R + b * 0 + c * 255;
            var g = a * pure.G + b * 0 + c * 255;
            var bl = a * pure.B + b * 0 + c * 255;
            var i = (y * size + x) * 4;
            pixels[i] = (byte)Math.Clamp(bl, 0, 255);
            pixels[i + 1] = (byte)Math.Clamp(g, 0, 255);
            pixels[i + 2] = (byte)Math.Clamp(r, 0, 255);
            pixels[i + 3] = 255;
        }
        bmp.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        bmp.Freeze();
        return bmp;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        CaptureMouse();
        _pressPoint = e.GetPosition(this);
        _pressTimer?.Stop();
        _pressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _pressTimer.Tick += (_, _) =>
        {
            _pressTimer.Stop();
            BeginDrag(_pressPoint);
        };
        _pressTimer.Start();
        // 短点也立即取色
        BeginDrag(_pressPoint);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        ApplyAt(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _pressTimer?.Stop();
        _dragging = false;
        _target = DragTarget.None;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    private void BeginDrag(Point p)
    {
        _dragging = true;
        var cx = ActualWidth / 2;
        var cy = ActualHeight / 2;
        var size = Math.Min(ActualWidth, ActualHeight);
        var outer = size / 2;
        var inner = outer * 0.72;
        var dx = p.X - cx;
        var dy = p.Y - cy;
        var r = Math.Sqrt(dx * dx + dy * dy);
        _target = r >= inner && r <= outer + 4 ? DragTarget.Wheel : DragTarget.Triangle;
        ApplyAt(p);
    }

    private void ApplyAt(Point p)
    {
        var cx = ActualWidth / 2;
        var cy = ActualHeight / 2;
        var size = Math.Min(ActualWidth, ActualHeight);
        var outer = size / 2;
        if (_target == DragTarget.Wheel)
        {
            _h = (Math.Atan2(p.Y - cy, p.X - cx) * 180 / Math.PI + 360) % 360;
            RebuildBitmaps();
        }
        else
        {
            var tri = outer * 0.62;
            var local = new Point(
                (p.X - (cx - tri)) / (tri * 2) * (_triBmp?.PixelWidth ?? 1),
                (p.Y - (cy - tri)) / (tri * 2) * (_triBmp?.PixelHeight ?? 1));
            var dim = _triBmp?.PixelWidth ?? 100;
            var pH = new Point(dim * 0.5, dim * 0.08);
            var pK = new Point(dim * 0.10, dim * 0.92);
            var pW = new Point(dim * 0.90, dim * 0.92);
            if (Barycentric(local, pH, pK, pW, out var a, out var b, out var c))
            {
                // 从混合色反推近似 S/L
                var rgb = HlsToRgb(_h, 0.5, 1);
                var R = a * rgb.R + c * 255;
                var G = a * rgb.G + c * 255;
                var B = a * rgb.B + c * 255;
                RgbToHls(Color.FromRgb(
                    (byte)Math.Clamp(R, 0, 255),
                    (byte)Math.Clamp(G, 0, 255),
                    (byte)Math.Clamp(B, 0, 255)), out _, out _l, out _s);
                // 黑白顶点：b 大则暗
                _l = Math.Clamp(0.5 * a + 0 * b + 1 * c, 0, 1);
                _s = Math.Clamp(a / Math.Max(0.05, a + c), 0, 1);
                if (b > 0.6) { _l = Math.Clamp(_l * (1 - b), 0, 1); }
            }
        }
        PushColor();
        InvalidateVisual();
    }

    private void PushColor()
    {
        var c = HlsToRgb(_h, _l, _s);
        _internal = true;
        SelectedColor = c;
        _internal = false;
        ColorChanged?.Invoke(c);
    }

    private void FromColor(Color c)
    {
        RgbToHls(c, out _h, out _l, out _s);
        RebuildBitmaps();
    }

    private static (double x, double y) HueToPoint(double cx, double cy, double r, double h)
    {
        var rad = h * Math.PI / 180;
        return (cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
    }

    private static (double x, double y) SlToTrianglePoint(double cx, double cy, double tri, double s, double l)
    {
        // 粗略映射到三角内
        var pH = new Point(cx, cy - tri * 0.85);
        var pK = new Point(cx - tri * 0.85, cy + tri * 0.85);
        var pW = new Point(cx + tri * 0.85, cy + tri * 0.85);
        var a = s * (1 - Math.Abs(l - 0.5) * 1.2);
        a = Math.Clamp(a, 0, 1);
        var rest = 1 - a;
        var b = rest * (1 - l);
        var c = rest * l;
        var sum = a + b + c;
        if (sum < 1e-6) return (cx, cy);
        a /= sum; b /= sum; c /= sum;
        return (a * pH.X + b * pK.X + c * pW.X, a * pH.Y + b * pK.Y + c * pW.Y);
    }

    private static bool Barycentric(Point p, Point a, Point b, Point c, out double u, out double v, out double w)
    {
        var den = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        if (Math.Abs(den) < 1e-9) { u = v = w = 0; return false; }
        u = ((b.Y - c.Y) * (p.X - c.X) + (c.X - b.X) * (p.Y - c.Y)) / den;
        v = ((c.Y - a.Y) * (p.X - c.X) + (a.X - c.X) * (p.Y - c.Y)) / den;
        w = 1 - u - v;
        return u >= -0.01 && v >= -0.01 && w >= -0.01;
    }

    public static Color HlsToRgb(double h, double l, double s)
    {
        h = (h % 360 + 360) % 360;
        l = Math.Clamp(l, 0, 1);
        s = Math.Clamp(s, 0, 1);
        if (s == 0)
        {
            var g = (byte)Math.Round(l * 255);
            return Color.FromRgb(g, g, g);
        }
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        double hk = h / 360.0;
        double tr = hk + 1.0 / 3, tg = hk, tb = hk - 1.0 / 3;
        return Color.FromRgb(Channel(tr), Channel(tg), Channel(tb));

        byte Channel(double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            double v;
            if (t < 1.0 / 6) v = p + (q - p) * 6 * t;
            else if (t < 0.5) v = q;
            else if (t < 2.0 / 3) v = p + (q - p) * (2.0 / 3 - t) * 6;
            else v = p;
            return (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        }
    }

    public static void RgbToHls(Color c, out double h, out double l, out double s)
    {
        var r = c.R / 255.0;
        var g = c.G / 255.0;
        var b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2;
        if (Math.Abs(max - min) < 1e-9) { h = 0; s = 0; return; }
        var d = max - min;
        s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        if (max == r) h = ((g - b) / d + (g < b ? 6 : 0)) * 60;
        else if (max == g) h = ((b - r) / d + 2) * 60;
        else h = ((r - g) / d + 4) * 60;
    }
}
