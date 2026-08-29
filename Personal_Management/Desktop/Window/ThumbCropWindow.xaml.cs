using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PersonalManagement.Desktop;

public sealed class CropViewState
{
    public double Scale { get; set; }
    public double Tx { get; set; }
    public double Ty { get; set; }
    public double ViewW { get; set; }
    public double ViewH { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static CropViewState? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;
        try { return JsonSerializer.Deserialize<CropViewState>(json); }
        catch { return null; }
    }
}

public partial class ThumbCropWindow : Window
{
    public string? ResultPath { get; private set; }
    public CropViewState? ResultCrop { get; private set; }

    private readonly double _boxW;
    private readonly double _boxH;
    private readonly BitmapSource _bmp;
    private readonly CropViewState? _initial;
    private double _imgW;
    private double _imgH;
    private double _viewW;
    private double _viewH;
    private double _scale;
    private double _minScale;
    private double _tx;
    private double _ty;
    private bool _dragging;
    private Point _dragStart;
    private double _dragTx;
    private double _dragTy;
    private readonly ScaleTransform _scaleTx = new();
    private readonly TranslateTransform _translateTx = new();

    public ThumbCropWindow(Size box, BitmapSource bmp, CropViewState? initial = null)
    {
        // 非 96 DPI 时 Stretch=None 只按 DIP 画在控件一角，选区变换却按 Pixel 尺寸居中 → 视口常落在空白上只见灰底。
        _bmp = NormalizeTo96Dpi(bmp);
        _initial = initial;
        _boxW = Math.Max(32, box.Width);
        _boxH = Math.Max(32, box.Height);
        InitializeComponent();
        Theme.Tint(this);
        var display = DisplaySize(_boxW, _boxH);
        _viewW = display.Width;
        _viewH = display.Height;
        Viewport.Width = _viewW;
        Viewport.Height = _viewH;
        Frame.Width = _viewW;
        Frame.Height = _viewH;
        Loaded += OnLoaded;
    }

    /// <summary>使布局 DIP 与 Pixel 一致，避免高 DPI PNG 在选区窗里「只见灰底」。</summary>
    private static BitmapSource NormalizeTo96Dpi(BitmapSource src)
    {
        if (Math.Abs(src.DpiX - 96) < 0.05 && Math.Abs(src.DpiY - 96) < 0.05)
            return src;
        var conv = src.Format == PixelFormats.Bgra32
            ? src
            : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        var w = conv.PixelWidth;
        var h = conv.PixelHeight;
        var stride = w * 4;
        var pixels = new byte[stride * h];
        conv.CopyPixels(pixels, stride, 0);
        var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
        wb.Freeze();
        return wb;
    }

    public static string? Ask(Window? owner, string imagePath, string wall, CropViewState? initial = null)
    {
        var result = AskFull(owner, imagePath, wall, initial);
        return result?.ThumbPath;
    }

    public static CropResult? AskFull(Window? owner, string imagePath, string wall, CropViewState? initial = null)
    {
        if (!File.Exists(imagePath)) return null;
        var ext = Path.GetExtension(imagePath).ToLowerInvariant();
        if (ext is ".webp")
        {
            MessageBox.Show("不支持 WebP。请改用 PNG 或 JPEG。");
            return null;
        }
        if (!FavoriteService.IsImagePath(imagePath)) return null;
        var bmp = FavoriteService.LoadLocalBitmap(imagePath);
        if (bmp is null)
        {
            MessageBox.Show("无法读取这张图片。");
            return null;
        }
        var box = Theme.ThumbDisplaySize(wall, owner);
        var win = new ThumbCropWindow(box, bmp, initial) { Owner = owner };
        if (win.ShowDialog() != true || win.ResultPath is null) return null;
        return new CropResult(win.ResultPath, imagePath, win.ResultCrop);
    }

    public static CropResult? PickFromFileFull(Window owner, string wall)
    {
        var dlg = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.gif;*.bmp|所有文件|*.*" };
        if (dlg.ShowDialog(owner) != true) return null;
        // 保留用户所选原图：拷到临时文件，避免源文件被移走
        var original = PersistOriginalCopy(dlg.FileName);
        return AskFull(owner, original, wall);
    }

    public static string? PickFromFile(Window owner, string wall) =>
        PickFromFileFull(owner, wall)?.ThumbPath;

    /// <summary>对已有原图（优先）或缩略图再次打开选区，可恢复上次缩放/位置。</summary>
    public static CropResult? RecropExistingFull(
        Window owner, string wall, BitmapSource? preview,
        string? originalPath = null, string? thumbPath = null, CropViewState? initial = null)
    {
        string? path = originalPath;
        if (path is null || !File.Exists(path))
            path = thumbPath;
        if (path is null || !File.Exists(path))
        {
            if (preview is null)
            {
                MessageBox.Show("还没有可截取的缩略图。请先浏览选图。");
                return null;
            }
            path = SaveBitmapTempPng(preview);
            initial = null; // 预览已是裁切结果，变换无意义
        }
        var result = AskFull(owner, path, wall, initial);
        if (result is null) return null;
        // 若本次是从原图裁的，SourcePath 即为原图；否则保留传入的 originalPath
        var keepOriginal = originalPath is not null && File.Exists(originalPath)
            ? originalPath
            : result.SourcePath;
        return result with { SourcePath = keepOriginal };
    }

    public static string? RecropExisting(Window owner, string wall, BitmapSource? preview, string? localPath = null) =>
        RecropExistingFull(owner, wall, preview, originalPath: localPath, thumbPath: localPath)?.ThumbPath;

    public static string PersistOriginalCopy(string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
        var dest = Path.Combine(Path.GetTempPath(), "pm-orig-" + Guid.NewGuid().ToString("N") + ext);
        File.Copy(sourcePath, dest, overwrite: true);
        return dest;
    }

    public static string SaveBitmapTempPng(BitmapSource source)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(source));
        var path = Path.Combine(Path.GetTempPath(), "pm-recrop-" + Guid.NewGuid().ToString("N")[..10] + ".png");
        using var fs = File.Create(path);
        enc.Save(fs);
        return path;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _imgW = _bmp.PixelWidth;
        _imgH = _bmp.PixelHeight;
        Photo.Source = _bmp;
        Photo.Width = _imgW;
        Photo.Height = _imgH;
        Photo.RenderTransform = new TransformGroup { Children = { _scaleTx, _translateTx } };
        _minScale = Math.Max(_viewW / Math.Max(1, _imgW), _viewH / Math.Max(1, _imgH));
        if (_initial is not null && _initial.ViewW > 0 && _initial.ViewH > 0)
        {
            // 按上次视口比例映射到当前视口
            var sx = _viewW / _initial.ViewW;
            var sy = _viewH / _initial.ViewH;
            var map = Math.Min(sx, sy);
            _scale = Math.Clamp(_initial.Scale * map, _minScale, _minScale * 8);
            _tx = _initial.Tx * map + (_viewW - _initial.ViewW * map) / 2;
            _ty = _initial.Ty * map + (_viewH - _initial.ViewH * map) / 2;
        }
        else
        {
            _scale = _minScale;
            _tx = (_viewW - _imgW * _scale) / 2;
            _ty = (_viewH - _imgH * _scale) / 2;
        }
        ApplyTransform();
    }

    private static Size DisplaySize(double boxW, double boxH)
    {
        var longSide = Math.Max(boxW, boxH);
        var scale = 320 / longSide;
        if (scale < 1) scale = 1;
        if (longSide * scale > 480) scale = 480 / longSide;
        return new Size(Math.Round(boxW * scale), Math.Round(boxH * scale));
    }

    private void ApplyTransform()
    {
        ClampPan();
        _scaleTx.ScaleX = _scale;
        _scaleTx.ScaleY = _scale;
        _translateTx.X = _tx;
        _translateTx.Y = _ty;
    }

    private void ClampPan()
    {
        var w = _imgW * _scale;
        var h = _imgH * _scale;
        if (w <= _viewW) _tx = (_viewW - w) / 2;
        else
        {
            if (_tx > 0) _tx = 0;
            if (_tx + w < _viewW) _tx = _viewW - w;
        }
        if (h <= _viewH) _ty = (_viewH - h) / 2;
        else
        {
            if (_ty > 0) _ty = 0;
            if (_ty + h < _viewH) _ty = _viewH - h;
        }
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var mouse = e.GetPosition(Viewport);
        var imgX = (mouse.X - _tx) / _scale;
        var imgY = (mouse.Y - _ty) / _scale;
        var factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
        _scale = Math.Clamp(_scale * factor, _minScale, _minScale * 8);
        _tx = mouse.X - imgX * _scale;
        _ty = mouse.Y - imgY * _scale;
        ApplyTransform();
        e.Handled = true;
    }

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragStart = e.GetPosition(Viewport);
        _dragTx = _tx;
        _dragTy = _ty;
        Viewport.CaptureMouse();
    }

    private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        Viewport.ReleaseMouseCapture();
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(Viewport);
        _tx = _dragTx + (p.X - _dragStart.X);
        _ty = _dragTy + (p.Y - _dragStart.Y);
        ApplyTransform();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var srcX = -_tx / _scale;
        var srcY = -_ty / _scale;
        var srcW = _viewW / _scale;
        var srcH = _viewH / _scale;
        var x = (int)Math.Round(srcX);
        var y = (int)Math.Round(srcY);
        var w = (int)Math.Round(srcW);
        var h = (int)Math.Round(srcH);
        x = Math.Clamp(x, 0, Math.Max(0, _bmp.PixelWidth - 1));
        y = Math.Clamp(y, 0, Math.Max(0, _bmp.PixelHeight - 1));
        w = Math.Clamp(w, 1, _bmp.PixelWidth - x);
        h = Math.Clamp(h, 1, _bmp.PixelHeight - y);
        BitmapSource cropped = new CroppedBitmap(_bmp, new Int32Rect(x, y, w, h));
        var outW = Math.Max(32, (int)Math.Round(_boxW * 2));
        if (cropped.PixelWidth > outW)
        {
            var s = outW / (double)cropped.PixelWidth;
            cropped = new TransformedBitmap(cropped, new ScaleTransform(s, s));
        }
        cropped.Freeze();
        var path = Path.Combine(Path.GetTempPath(), "pm-thumb-" + Guid.NewGuid().ToString("N") + ".png");
        using (var fs = File.Create(path))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(cropped));
            encoder.Save(fs);
        }
        ResultPath = path;
        ResultCrop = new CropViewState
        {
            Scale = _scale,
            Tx = _tx,
            Ty = _ty,
            ViewW = _viewW,
            ViewH = _viewH
        };
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public sealed record CropResult(string ThumbPath, string SourcePath, CropViewState? Crop);
