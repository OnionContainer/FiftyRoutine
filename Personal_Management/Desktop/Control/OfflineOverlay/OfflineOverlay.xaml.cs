using System.Windows;
using System.Windows.Controls;

namespace PersonalManagement.Desktop;

public partial class OfflineOverlay : UserControl
{
    public OfflineOverlay()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? ConnectClick;

    /// <summary>显示/隐藏遮罩，并启用或禁用被遮内容（半透明）。</summary>
    public void SetActive(UIElement? content, bool show)
    {
        Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (content is not null)
        {
            content.IsEnabled = !show;
            content.Opacity = show ? 0.35 : 1;
        }
    }

    private void Connect_Click(object sender, RoutedEventArgs e) =>
        ConnectClick?.Invoke(this, e);
}
