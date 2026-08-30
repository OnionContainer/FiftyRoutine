using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PersonalManagement.Desktop;

public partial class GadgetsPage : UserControl
{
    private IAppHost? _host;
    private WriteableBitmap? _mirrorBmp;

    public GadgetsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshMirrorPreview();
    }

    public void Attach(IAppHost host) => _host = host;

    private void MirrorInput_TextChanged(object sender, TextChangedEventArgs e) =>
        RefreshMirrorPreview();

    private void MirrorPad_TextChanged(object sender, TextChangedEventArgs e) =>
        RefreshMirrorPreview();

    private void MirrorCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_mirrorBmp is null)
        {
            MessageBox.Show("没有可复制的图像。");
            return;
        }
        try
        {
            Clipboard.SetImage(_mirrorBmp);
            if (_host is not null)
                _host.StatusText = "已复制镜像文字图到剪贴板";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "复制失败");
        }
    }

    private void RefreshMirrorPreview()
    {
        var user = MirrorInput?.Text ?? "";
        var text = "？！" + user;
        var padL = ParsePad(MirrorPadLeft?.Text, 1);
        var padR = ParsePad(MirrorPadRight?.Text, 1);
        _mirrorBmp = RenderMirrorStrip(text, padL, padR);
        if (MirrorPreview is not null)
            MirrorPreview.Source = _mirrorBmp;
    }

    private static int ParsePad(string? raw, int fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return fallback;
        return Math.Clamp(n, 0, 64);
    }

    /// <summary>白底：左侧正向文字，右侧为其水平镜像。第一步左右留白由 padL/padR 控制。</summary>
    private static WriteableBitmap RenderMirrorStrip(string text, int padL, int padR)
    {
        const double dpi = 96;
        var pixelsPerDip = 1.0;
        if (Application.Current?.MainWindow is { } mw)
            pixelsPerDip = VisualTreeHelper.GetDpi(mw).PixelsPerDip;

        var typeface = new Typeface(
            new FontFamily("Microsoft YaHei UI"),
            FontStyles.Normal,
            FontWeights.SemiBold,
            FontStretches.Normal);
        // 字号与功能行 UI 高度无关；行高只影响控件，不影响导出清晰度
        var fontSize = Math.Max(18, Theme.Current.FontSizeBody + 6);
        var ft = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            pixelsPerDip);

        const double padY = 1.0;
        var textW = Math.Ceiling(ft.WidthIncludingTrailingWhitespace);
        var textH = Math.Ceiling(ft.Height);
        var halfW = Math.Max(1, (int)(textW + padL + padR));
        var h = Math.Max(1, (int)(textH + padY * 2));
        var fullW = halfW * 2;

        var leftDv = new DrawingVisual();
        using (var dc = leftDv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, halfW, h));
            dc.DrawText(ft, new Point(padL, padY));
        }
        var leftRtb = new RenderTargetBitmap(halfW, h, dpi, dpi, PixelFormats.Pbgra32);
        leftRtb.Render(leftDv);

        var outDv = new DrawingVisual();
        using (var dc = outDv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, fullW, h));
            dc.DrawImage(leftRtb, new Rect(0, 0, halfW, h));
            var flip = new TransformGroup();
            flip.Children.Add(new ScaleTransform(-1, 1));
            flip.Children.Add(new TranslateTransform(fullW, 0));
            dc.PushTransform(flip);
            dc.DrawImage(leftRtb, new Rect(0, 0, halfW, h));
            dc.Pop();
        }

        var rtb = new RenderTargetBitmap(fullW, h, dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(outDv);
        var conv = new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
        var wb = new WriteableBitmap(conv);
        wb.Freeze();
        return wb;
    }
}
