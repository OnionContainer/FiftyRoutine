using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PersonalManagement.Desktop;
using IOPath = System.IO.Path;

namespace PersonalManagement.Probes;

/// <summary>
/// 缩略图探针：选区窗 → 分析导出缩略图是否纯灰 → 用 CropJson 等价参数还原原图矩形，
/// 比对「原图该块」与「导出文件」是否一致，并弹出对照窗。
/// </summary>
internal static class ThumbPixelsProbe
{
    public const string DefaultPath = @"D:\Pasttence\School Work\Draw\sai2\2026\3-12-1.png";

    /// <returns>0 = pass, 2 = fail</returns>
    public static int Run(string? pathArg)
    {
        var path = string.IsNullOrWhiteSpace(pathArg) ? DefaultPath : pathArg.Trim().Trim('"');
        Console.WriteLine("Thumb crop / gray-trace probe");
        Console.WriteLine("Path: " + path);

        if (!File.Exists(path))
        {
            Console.WriteLine("FAIL  file not found");
            return 2;
        }

        Console.WriteLine($"File: {new FileInfo(path).Length} bytes");

        var exit = 2;
        Exception? uiEx = null;

        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                Theme.LoadAndApply();

                var original = FavoriteService.LoadLocalBitmap(path);
                if (original is null)
                {
                    Console.WriteLine("FAIL  FavoriteService.LoadLocalBitmap(original) returned null");
                    exit = 2;
                    app.Shutdown();
                    return;
                }

                Console.WriteLine(
                    $"Original decoded: {original.PixelWidth}x{original.PixelHeight} format={original.Format}");
                Console.WriteLine(DescribePixels("original(full)", original));

                Console.WriteLine();
                Console.WriteLine("Opening ThumbCropWindow… 确定后分析；取消则退出。");
                var cropResult = ThumbCropWindow.AskFull(owner: null, imagePath: path, wall: "task");
                if (cropResult is null || cropResult.Crop is null)
                {
                    Console.WriteLine("Crop window: cancelled / no crop state");
                    exit = 2;
                    app.Shutdown();
                    return;
                }

                Console.WriteLine($"thumb file → {cropResult.ThumbPath}");
                Console.WriteLine(
                    $"crop state: scale={cropResult.Crop.Scale:0.######} tx={cropResult.Crop.Tx:0.###} ty={cropResult.Crop.Ty:0.###} view={cropResult.Crop.ViewW:0.#}x{cropResult.Crop.ViewH:0.#}");

                // 与 Ok_Click 相同公式：选区 → 原图像素矩形
                var rect = SourceRectFromCrop(cropResult.Crop, original.PixelWidth, original.PixelHeight);
                Console.WriteLine($"mapped source Int32Rect = ({rect.X},{rect.Y},{rect.Width},{rect.Height})  in original {original.PixelWidth}x{original.PixelHeight}");

                BitmapSource region = new CroppedBitmap(original, rect);
                region.Freeze();
                Console.WriteLine(DescribePixels("original[crop-rect]", region));

                var thumbFile = FavoriteService.LoadLocalBitmap(cropResult.ThumbPath);
                if (thumbFile is null)
                {
                    Console.WriteLine("FAIL  cannot reload exported thumb file");
                    exit = 2;
                    app.Shutdown();
                    return;
                }

                Console.WriteLine(
                    $"exported thumb file: {thumbFile.PixelWidth}x{thumbFile.PixelHeight} format={thumbFile.Format}");
                Console.WriteLine(DescribePixels("thumb-file", thumbFile));

                var thumbFlat = IsNearFlatGray(thumbFile, out var thumbMean, out var thumbStdSum);
                var regionFlat = IsNearFlatGray(region, out var regionMean, out var regionStdSum);
                Console.WriteLine();
                Console.WriteLine($"thumb-file flat-gray? {thumbFlat}  meanRGB=({thumbMean.R:0.#},{thumbMean.G:0.#},{thumbMean.B:0.#}) stdSum={thumbStdSum:0.#}");
                Console.WriteLine($"crop-rect  flat-gray? {regionFlat}  meanRGB=({regionMean.R:0.#},{regionMean.G:0.#},{regionMean.B:0.#}) stdSum={regionStdSum:0.#}");

                // 原图该块缩放到与导出缩略图同尺寸后比对
                var regionScaled = ScaleTo(region, thumbFile.PixelWidth, thumbFile.PixelHeight);
                var mse = MeanSquareError(regionScaled, thumbFile);
                var match = mse < 25.0; // 约均方差 <5/通道
                Console.WriteLine();
                Console.WriteLine($"compare scaled(crop-rect) vs thumb-file: MSE={mse:0.##}  {(match ? "MATCH (thumb content comes from this rect)" : "MISMATCH (thumb ≠ that rect — bug or different pipeline)")}");

                if (thumbFlat)
                {
                    Console.WriteLine();
                    Console.WriteLine("Tracing flat-gray in original…");
                    if (regionFlat)
                    {
                        Console.WriteLine(
                            $"  → 导出缩略图的灰来自选区本身：原图矩形 ({rect.X},{rect.Y})-{rect.Width}x{rect.Height} 就是近纯灰。");
                        Console.WriteLine("  → 内部对应：ThumbCropWindow.Ok_Click 裁切的 CroppedBitmap + 可选缩小后的 Png 文件；不是整图解码失败。");
                    }
                    else
                    {
                        Console.WriteLine("  → 选区在原图里并非纯灰，但导出文件接近纯灰 → 问题更可能在导出/缩放/写盘，而不是选区落点。");
                    }

                    var hits = FindFlatGrayTiles(original, thumbMean, rect, maxHits: 8);
                    Console.WriteLine($"  other near-gray tiles in original (mean≈thumb, low variance): {hits.Count}");
                    foreach (var h in hits)
                        Console.WriteLine($"    rect=({h.X},{h.Y},{h.Width},{h.Height}) mean=({h.MeanR:0.#},{h.MeanG:0.#},{h.MeanB:0.#}) stdSum={h.StdSum:0.#} overlapCrop={h.OverlapsCrop}");
                }

                var diagDir = IOPath.Combine(IOPath.GetTempPath(), "pm-thumb-probe-" + DateTime.Now.ToString("HHmmss"));
                Directory.CreateDirectory(diagDir);
                var regionPath = IOPath.Combine(diagDir, "01-original-crop-rect.png");
                var thumbPathCopy = IOPath.Combine(diagDir, "02-exported-thumb.png");
                var scaledPath = IOPath.Combine(diagDir, "03-crop-rect-scaled-to-thumb.png");
                SavePng(region, regionPath);
                File.Copy(cropResult.ThumbPath, thumbPathCopy, overwrite: true);
                SavePng(regionScaled, scaledPath);
                Console.WriteLine();
                Console.WriteLine("Diagnostic files: " + diagDir);

                Console.WriteLine();
                Console.WriteLine("Showing compare window (close it to finish)…");
                ShowCompareWindow(region, thumbFile, regionScaled, rect, mse, match, thumbFlat, regionFlat, diagDir);

                exit = match && !thumbFlat ? 0 : (match ? 0 : 2);
                // 纯灰但能对上原图选区：数据链路正确，exit 0 并标 WARN
                if (match && thumbFlat)
                {
                    Console.WriteLine("WARN  thumb is flat gray but matches original crop-rect — selection landed on gray area (or canvas).");
                    exit = 0;
                }

                app.Shutdown();
            }
            catch (Exception ex)
            {
                uiEx = ex;
                try { Application.Current?.Shutdown(); } catch { /* ignore */ }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (uiEx is not null)
        {
            Console.WriteLine("FAIL  " + uiEx);
            return 2;
        }

        return exit;
    }

    private static Int32Rect SourceRectFromCrop(CropViewState crop, int bmpW, int bmpH)
    {
        var srcX = -crop.Tx / crop.Scale;
        var srcY = -crop.Ty / crop.Scale;
        var srcW = crop.ViewW / crop.Scale;
        var srcH = crop.ViewH / crop.Scale;
        var x = (int)Math.Round(srcX);
        var y = (int)Math.Round(srcY);
        var w = (int)Math.Round(srcW);
        var h = (int)Math.Round(srcH);
        x = Math.Clamp(x, 0, Math.Max(0, bmpW - 1));
        y = Math.Clamp(y, 0, Math.Max(0, bmpH - 1));
        w = Math.Clamp(w, 1, bmpW - x);
        h = Math.Clamp(h, 1, bmpH - y);
        return new Int32Rect(x, y, w, h);
    }

    private static BitmapSource ScaleTo(BitmapSource src, int outW, int outH)
    {
        if (src.PixelWidth == outW && src.PixelHeight == outH) return src;
        var sx = outW / (double)src.PixelWidth;
        var sy = outH / (double)src.PixelHeight;
        var scaled = new TransformedBitmap(src, new ScaleTransform(sx, sy));
        scaled.Freeze();
        return scaled;
    }

    private static double MeanSquareError(BitmapSource a, BitmapSource b)
    {
        var wa = ToBgra(a);
        var wb = ToBgra(b);
        if (wa.W != wb.W || wa.H != wb.H)
            throw new InvalidOperationException($"size mismatch {wa.W}x{wa.H} vs {wb.W}x{wb.H}");
        double sum = 0;
        var n = wa.Pixels.Length;
        for (var i = 0; i < n; i++)
        {
            var d = wa.Pixels[i] - wb.Pixels[i];
            sum += d * d;
        }
        return sum / n;
    }

    private static bool IsNearFlatGray(BitmapSource src, out (double R, double G, double B) mean, out double stdSum)
    {
        var s = Stats(src);
        mean = (s.MeanR, s.MeanG, s.MeanB);
        stdSum = s.StdR + s.StdG + s.StdB;
        var range = Math.Max(s.MaxR - s.MinR, Math.Max(s.MaxG - s.MinG, s.MaxB - s.MinB));
        var grayish = Math.Abs(s.MeanR - s.MeanG) < 12 && Math.Abs(s.MeanG - s.MeanB) < 12;
        return stdSum < 12 && range < 20 && grayish;
    }

    private sealed record GrayHit(int X, int Y, int Width, int Height, double MeanR, double MeanG, double MeanB, double StdSum, bool OverlapsCrop);

    private static List<GrayHit> FindFlatGrayTiles(BitmapSource original, (double R, double G, double B) target, Int32Rect crop, int maxHits)
    {
        var bgra = ToBgra(original);
        const int tile = 64;
        var hits = new List<GrayHit>();
        for (var y = 0; y + tile <= bgra.H; y += tile)
        {
            for (var x = 0; x + tile <= bgra.W; x += tile)
            {
                AccTile(bgra, x, y, tile, tile, out var meanR, out var meanG, out var meanB, out var stdSum, out var range);
                var grayish = Math.Abs(meanR - meanG) < 12 && Math.Abs(meanG - meanB) < 12;
                if (stdSum > 12 || range > 24 || !grayish) continue;
                var dist = Math.Abs(meanR - target.R) + Math.Abs(meanG - target.G) + Math.Abs(meanB - target.B);
                if (dist > 40) continue;
                var overlaps = x < crop.X + crop.Width && x + tile > crop.X && y < crop.Y + crop.Height && y + tile > crop.Y;
                hits.Add(new GrayHit(x, y, tile, tile, meanR, meanG, meanB, stdSum, overlaps));
            }
        }
        return hits.OrderBy(h => Math.Abs(h.MeanR - target.R) + Math.Abs(h.MeanG - target.G) + Math.Abs(h.MeanB - target.B))
            .Take(maxHits)
            .ToList();
    }

    private static void AccTile(BgraBuf buf, int x0, int y0, int tw, int th,
        out double meanR, out double meanG, out double meanB, out double stdSum, out int range)
    {
        long sumR = 0, sumG = 0, sumB = 0, sumR2 = 0, sumG2 = 0, sumB2 = 0;
        byte minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
        var n = 0;
        for (var y = y0; y < y0 + th; y += 2)
        for (var x = x0; x < x0 + tw; x += 2)
        {
            var i = y * buf.Stride + x * 4;
            var b = buf.Pixels[i];
            var g = buf.Pixels[i + 1];
            var r = buf.Pixels[i + 2];
            sumR += r; sumG += g; sumB += b;
            sumR2 += r * r; sumG2 += g * g; sumB2 += b * b;
            if (r < minR) minR = r; if (r > maxR) maxR = r;
            if (g < minG) minG = g; if (g > maxG) maxG = g;
            if (b < minB) minB = b; if (b > maxB) maxB = b;
            n++;
        }
        n = Math.Max(1, n);
        meanR = sumR / (double)n;
        meanG = sumG / (double)n;
        meanB = sumB / (double)n;
        var stdR = Math.Sqrt(Math.Max(0, sumR2 / (double)n - meanR * meanR));
        var stdG = Math.Sqrt(Math.Max(0, sumG2 / (double)n - meanG * meanG));
        var stdB = Math.Sqrt(Math.Max(0, sumB2 / (double)n - meanB * meanB));
        stdSum = stdR + stdG + stdB;
        range = Math.Max(maxR - minR, Math.Max(maxG - minG, maxB - minB));
    }

    private static string DescribePixels(string label, BitmapSource source)
    {
        var s = Stats(source);
        var flat = s.StdR + s.StdG + s.StdB < 12 &&
                   Math.Max(s.MaxR - s.MinR, Math.Max(s.MaxG - s.MinG, s.MaxB - s.MinB)) < 20;
        var sb = new StringBuilder();
        sb.AppendLine($"[{label}] {s.W}x{s.H} unique≈{s.Unique} flatGray≈{flat}");
        sb.AppendLine($"  mean RGB=({s.MeanR:0.#},{s.MeanG:0.#},{s.MeanB:0.#}) α={s.MeanA:0.#}");
        sb.AppendLine($"  std RGB=({s.StdR:0.#},{s.StdG:0.#},{s.StdB:0.#}) range R[{s.MinR}-{s.MaxR}] G[{s.MinG}-{s.MaxG}] B[{s.MinB}-{s.MaxB}]");
        sb.Append($"  corners: {string.Join(" ", s.Corners)}");
        return sb.ToString();
    }

    private sealed class PixStats
    {
        public int W, H, Unique;
        public double MeanR, MeanG, MeanB, MeanA, StdR, StdG, StdB;
        public byte MinR, MaxR, MinG, MaxG, MinB, MaxB;
        public List<string> Corners { get; } = [];
    }

    private static PixStats Stats(BitmapSource source)
    {
        var buf = ToBgra(source);
        var stepX = Math.Max(1, buf.W / 64);
        var stepY = Math.Max(1, buf.H / 64);
        var unique = new HashSet<int>();
        long sumR = 0, sumG = 0, sumB = 0, sumA = 0, sumR2 = 0, sumG2 = 0, sumB2 = 0;
        byte minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
        var count = 0;

        void Take(int x, int y)
        {
            x = Math.Clamp(x, 0, buf.W - 1);
            y = Math.Clamp(y, 0, buf.H - 1);
            var i = y * buf.Stride + x * 4;
            var b = buf.Pixels[i];
            var g = buf.Pixels[i + 1];
            var r = buf.Pixels[i + 2];
            var a = buf.Pixels[i + 3];
            unique.Add((a << 24) | (r << 16) | (g << 8) | b);
            sumR += r; sumG += g; sumB += b; sumA += a;
            sumR2 += r * r; sumG2 += g * g; sumB2 += b * b;
            if (r < minR) minR = r; if (r > maxR) maxR = r;
            if (g < minG) minG = g; if (g > maxG) maxG = g;
            if (b < minB) minB = b; if (b > maxB) maxB = b;
            count++;
        }

        for (var y = 0; y < buf.H; y += stepY)
        for (var x = 0; x < buf.W; x += stepX)
            Take(x, y);
        Take(0, 0); Take(buf.W - 1, 0); Take(0, buf.H - 1); Take(buf.W - 1, buf.H - 1); Take(buf.W / 2, buf.H / 2);

        var n = Math.Max(1, count);
        var meanR = sumR / (double)n;
        var meanG = sumG / (double)n;
        var meanB = sumB / (double)n;
        var s = new PixStats
        {
            W = buf.W, H = buf.H, Unique = unique.Count,
            MeanR = meanR, MeanG = meanG, MeanB = meanB, MeanA = sumA / (double)n,
            StdR = Math.Sqrt(Math.Max(0, sumR2 / (double)n - meanR * meanR)),
            StdG = Math.Sqrt(Math.Max(0, sumG2 / (double)n - meanG * meanG)),
            StdB = Math.Sqrt(Math.Max(0, sumB2 / (double)n - meanB * meanB)),
            MinR = minR, MaxR = maxR, MinG = minG, MaxG = maxG, MinB = minB, MaxB = maxB
        };
        foreach (var (sx, sy, label) in new (int, int, string)[]
                 {
                     (0, 0, "TL"), (buf.W - 1, 0, "TR"), (0, buf.H - 1, "BL"), (buf.W - 1, buf.H - 1, "BR"),
                     (buf.W / 2, buf.H / 2, "C")
                 })
        {
            var i = sy * buf.Stride + sx * 4;
            s.Corners.Add($"{label}=#{buf.Pixels[i + 2]:X2}{buf.Pixels[i + 1]:X2}{buf.Pixels[i]:X2}");
        }
        return s;
    }

    private sealed record BgraBuf(byte[] Pixels, int W, int H, int Stride);

    private static BgraBuf ToBgra(BitmapSource source)
    {
        var converted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        if (converted is FormatConvertedBitmap fcb) fcb.Freeze();
        var w = converted.PixelWidth;
        var h = converted.PixelHeight;
        var stride = w * 4;
        var pixels = new byte[stride * h];
        converted.CopyPixels(pixels, stride, 0);
        return new BgraBuf(pixels, w, h, stride);
    }

    private static void SavePng(BitmapSource src, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    private static void ShowCompareWindow(
        BitmapSource region, BitmapSource thumb, BitmapSource regionScaled,
        Int32Rect rect, double mse, bool match, bool thumbFlat, bool regionFlat, string diagDir)
    {
        Image Mk(BitmapSource src) => new()
        {
            Source = src,
            Stretch = Stretch.Uniform,
            MaxWidth = 320,
            MaxHeight = 320,
            Margin = new Thickness(8)
        };

        var summary = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12),
            FontSize = 13,
            Text =
                $"原图选区 Int32Rect=({rect.X},{rect.Y},{rect.Width},{rect.Height})\n" +
                $"thumb flat-gray={thumbFlat}  crop-rect flat-gray={regionFlat}\n" +
                $"scaled(crop-rect) vs thumb-file  MSE={mse:0.##}  {(match ? "内容一致（灰若出现，来自该选区）" : "内容不一致")}\n" +
                $"诊断文件: {diagDir}\n" +
                "左：原图裁切块  中：导出缩略图文件  右：裁切块缩放到缩略图尺寸"
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        void Col(string title, BitmapSource src)
        {
            var sp = new StackPanel { Margin = new Thickness(4) };
            sp.Children.Add(new TextBlock { Text = title, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) });
            sp.Children.Add(Mk(src));
            row.Children.Add(sp);
        }
        Col("① 原图[crop-rect]", region);
        Col("② 导出 thumb 文件", thumb);
        Col("③ ①缩放到②尺寸", regionScaled);

        var root = new DockPanel();
        DockPanel.SetDock(summary, Dock.Top);
        root.Children.Add(summary);
        root.Children.Add(new ScrollViewer { Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });

        var win = new Window
        {
            Title = "缩略图对照（原图选区 vs 导出文件）",
            Content = root,
            Width = 1100,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        Theme.Tint(win);
        win.ShowDialog();
    }
}
