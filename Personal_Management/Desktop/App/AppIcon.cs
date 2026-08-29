using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace PersonalManagement.Desktop;

internal static class AppIcon
{
    public static string? FilePath
    {
        get
        {
            var local = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(local)) return local;
            var root = Paths.FindWorkspaceRoot();
            if (root is not null)
            {
                var src = Path.Combine(root, "Personal_Management", "Desktop", "Assets", "app.ico");
                if (File.Exists(src)) return src;
            }
            return null;
        }
    }

    public static ImageSource? Wpf()
    {
        var path = FilePath;
        if (path is null) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public static Drawing.Icon? Tray()
    {
        var path = FilePath;
        if (path is null) return null;
        try
        {
            return new Drawing.Icon(path);
        }
        catch
        {
            return null;
        }
    }
}
