using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PersonalManagement.Desktop;

internal sealed class FavoriteItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "image";
    public string Source { get; set; } = "";
    public string TagsRaw { get; set; } = "";
    public bool IsPrivate { get; set; }
    public JsonNode? File { get; set; }
    public JsonNode? Thumb { get; set; }
    public JsonNode? Original { get; set; }
    public string? CropJson { get; set; }
    public bool Selected { get; set; }
    public BitmapImage? Preview { get; set; }
    public string? LocalPath { get; set; }
    public string? OriginalPath { get; set; }

    public IReadOnlyList<string> Tags =>
        TagsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public bool IsUntagged => Tags.Count == 0;
    public bool HasPrivateTag => IsPrivate || Tags.Any(t => t.StartsWith('*'));
    public string KindLabel => Kind switch
    {
        "link" => "链接",
        "video" => "视频",
        _ => "图片"
    };
}

internal static class FavoriteService
{
    public static FavoriteItem FromRecord(JsonNode node) => new()
    {
        Id = NocoClient.ReadId(node) ?? "",
        Title = NocoClient.ReadString(node, "Title") ?? "",
        Kind = NocoClient.ReadString(node, "Kind") ?? "image",
        Source = NocoClient.ReadString(node, "Source") ?? "",
        TagsRaw = NocoClient.ReadString(node, "Tags") ?? "",
        IsPrivate = NocoClient.ReadBool(node, "IsPrivate"),
        File = NocoClient.FileField(node, "File"),
        Thumb = NocoClient.FileField(node, "Thumb"),
        Original = NocoClient.FileField(node, "Original"),
        CropJson = NocoClient.ReadString(node, "CropJson")
    };

    public static string HashPin(string pin) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("pm-fav|" + pin)));

    public static async Task<string> DownloadBestAsync(IRecordStore store, FavoriteItem item)
    {
        if (item.LocalPath is not null && File.Exists(item.LocalPath))
            return item.LocalPath;
        var url = NocoClient.FirstFileUrl(item.Kind == "image" ? item.File : item.Thumb)
                  ?? NocoClient.FirstFileUrl(item.File)
                  ?? NocoClient.FirstFileUrl(item.Thumb)
                  ?? throw new InvalidOperationException("这条收藏没有可下载的文件。");
        var ext = GuessExt(url, item.Kind);
        var path = Path.Combine(Path.GetTempPath(), "pm-fav-" + item.Id + ext);
        var bytes = await store.DownloadBytesAsync(url);
        await File.WriteAllBytesAsync(path, bytes);
        item.LocalPath = path;
        return path;
    }

    public static async Task<BitmapImage?> LoadPreviewAsync(IRecordStore store, FavoriteItem item)
    {
        try
        {
            var url = NocoClient.FirstFileUrl(item.Thumb)
                      ?? (item.Kind == "image" ? NocoClient.FirstFileUrl(item.File) : null);
            if (url is null) return null;
            var bytes = await store.DownloadBytesAsync(url);
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 200;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            item.Preview = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public static void OpenHoneyView(string honeyView, string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = honeyView,
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
    }

    public static bool IsImagePath(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp";
    }

    public static BitmapImage? LoadLocalPreview(string path, int decodeWidth = 240)
    {
        if (!File.Exists(path) || !IsImagePath(path)) return null;
        try
        {
            using var fs = File.OpenRead(path);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = decodeWidth;
            bmp.StreamSource = fs;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public static BitmapSource? LoadLocalBitmap(string path, int maxEdge = 4096)
    {
        if (!File.Exists(path) || !IsImagePath(path)) return null;
        try
        {
            var bytes = File.ReadAllBytes(path);
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            var w = bmp.PixelWidth;
            var h = bmp.PixelHeight;
            if (w <= 0 || h <= 0) return null;
            var edge = Math.Max(w, h);
            if (edge <= maxEdge) return bmp;
            var scale = maxEdge / (double)edge;
            var scaled = new TransformedBitmap(bmp, new System.Windows.Media.ScaleTransform(scale, scale));
            scaled.Freeze();
            return scaled;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("LoadLocalBitmap: " + ex);
            return null;
        }
    }

    public static IReadOnlyList<string> ImageFilesFromData(System.Windows.IDataObject data)
    {
        var list = new List<string>();
        if (data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            && data.GetData(System.Windows.DataFormats.FileDrop) is string[] files)
        {
            foreach (var file in files)
            {
                if (File.Exists(file) && IsImagePath(file))
                    list.Add(file);
            }
        }
        return list;
    }

    public static IReadOnlyList<string> ImageFilesFromClipboard()
    {
        if (!System.Windows.Clipboard.ContainsFileDropList())
            return [];
        var list = new List<string>();
        foreach (var file in System.Windows.Clipboard.GetFileDropList())
        {
            if (!string.IsNullOrEmpty(file) && File.Exists(file) && IsImagePath(file))
                list.Add(file);
        }
        return list;
    }

    public static string? ClipboardHttpUrl()
    {
        if (!System.Windows.Clipboard.ContainsText()) return null;
        var text = System.Windows.Clipboard.GetText().Trim();
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return uri.ToString();
        return null;
    }

    public static string? SaveImageFromData(System.Windows.IDataObject data)
    {
        try
        {
            if (data.GetDataPresent("PNG") && data.GetData("PNG") is Stream png)
            {
                using (png)
                    return WriteTempImage(png, ".png");
            }
        }
        catch { /* try next format */ }

        try
        {
            if (data.GetDataPresent(System.Windows.DataFormats.Bitmap)
                && data.GetData(System.Windows.DataFormats.Bitmap) is BitmapSource bmp)
                return EncodePng(bmp);
        }
        catch { /* try next format */ }

        return null;
    }

    public static string? SaveImageFromClipboard()
    {
        try
        {
            if (System.Windows.Clipboard.GetDataObject() is { } data)
            {
                var fromData = SaveImageFromData(data);
                if (fromData is not null) return fromData;
            }
        }
        catch { /* clipboard can be locked */ }

        try
        {
            if (System.Windows.Clipboard.ContainsImage())
            {
                var bmp = System.Windows.Clipboard.GetImage();
                if (bmp is not null) return EncodePng(bmp);
            }
        }
        catch { /* ignore */ }

        try
        {
            if (System.Windows.Forms.Clipboard.ContainsImage())
            {
                using var img = System.Windows.Forms.Clipboard.GetImage();
                if (img is not null)
                {
                    var path = TempImagePath(".png");
                    img.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    return path;
                }
            }
        }
        catch { /* ignore */ }

        return null;
    }

    public static bool DataHasImage(System.Windows.IDataObject data) =>
        ImageFilesFromData(data).Count > 0
        || data.GetDataPresent("PNG")
        || data.GetDataPresent(System.Windows.DataFormats.Bitmap);

    private static string EncodePng(BitmapSource bmp)
    {
        var path = TempImagePath(".png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        encoder.Save(fs);
        return path;
    }

    private static string WriteTempImage(Stream src, string ext)
    {
        var path = TempImagePath(ext);
        using var fs = File.Create(path);
        src.CopyTo(fs);
        return path;
    }

    private static string TempImagePath(string ext) =>
        Path.Combine(Path.GetTempPath(), "pm-fav-in-" + Guid.NewGuid().ToString("N") + ext);

    public static void CopyImage(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        System.Windows.Clipboard.SetImage(bmp);
    }

    public static async Task ExportAsync(IRecordStore store, IEnumerable<FavoriteItem> items, string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var item in items)
        {
            var src = await DownloadBestAsync(store, item);
            var name = Sanitize(item.Title);
            if (string.IsNullOrWhiteSpace(name)) name = item.Id;
            var dest = Path.Combine(folder, name + Path.GetExtension(src));
            File.Copy(src, dest, overwrite: true);
        }
    }

    private static string GuessExt(string url, string kind)
    {
        try
        {
            var ext = Path.GetExtension(new Uri(url, UriKind.RelativeOrAbsolute).AbsolutePath);
            if (!string.IsNullOrWhiteSpace(ext) && ext.Length <= 5) return ext;
        }
        catch { /* ignore */ }
        return kind == "video" ? ".mp4" : ".png";
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }
}
