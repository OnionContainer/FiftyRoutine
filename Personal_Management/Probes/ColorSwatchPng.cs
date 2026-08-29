using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PersonalManagement.Probes;

internal static class ColorSwatchPng
{
    public const int Size = 200;

    public static byte[] Create()
    {
        var pixels = new byte[Size * Size * 4];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var i = (y * Size + x) * 4;
                pixels[i + 0] = (byte)(x * 255 / (Size - 1));       // B
                pixels[i + 1] = (byte)(y * 255 / (Size - 1));       // G
                pixels[i + 2] = (byte)(255 - x * 255 / (Size - 1)); // R
                pixels[i + 3] = 255;
            }
        }

        Fill(pixels, 0, 0, 40, 40, 255, 0, 0);
        Fill(pixels, Size - 40, 0, 40, 40, 0, 255, 0);
        Fill(pixels, 0, Size - 40, 40, 40, 0, 0, 255);
        Fill(pixels, Size - 40, Size - 40, 40, 40, 255, 255, 0);
        Fill(pixels, 90, 90, 20, 20, 255, 0, 255);

        var bitmap = new WriteableBitmap(Size, Size, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, Size, Size), pixels, Size * 4, 0);
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    public static string SaveTemp()
    {
        var path = Path.Combine(Path.GetTempPath(), "pm-honeyview-200x200.png");
        File.WriteAllBytes(path, Create());
        return path;
    }

    private static void Fill(byte[] pixels, int left, int top, int width, int height, byte r, byte g, byte b)
    {
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                var i = (y * Size + x) * 4;
                pixels[i + 0] = b;
                pixels[i + 1] = g;
                pixels[i + 2] = r;
                pixels[i + 3] = 255;
            }
        }
    }
}
