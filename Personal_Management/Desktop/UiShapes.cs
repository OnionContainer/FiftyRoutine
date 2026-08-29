using System.Windows;
using System.Windows.Media;

namespace PersonalManagement.Desktop;

internal static class UiShapes
{
    /// <summary>WPF 的 CornerRadius 不裁剪子元素；给含 Image 的图区补圆角 Clip。</summary>
    public static void RoundClip(FrameworkElement element, double radius)
    {
        void Apply()
        {
            if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return;
            var r = Math.Max(0, radius);
            element.Clip = new RectangleGeometry(
                new Rect(0, 0, element.ActualWidth, element.ActualHeight), r, r);
        }

        element.Loaded += (_, _) => Apply();
        element.SizeChanged += (_, _) => Apply();
        Apply();
    }
}
