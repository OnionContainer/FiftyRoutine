using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace PersonalManagement.Desktop;

internal static class ReminderWindow
{
    public static void ShowFor(string taskTitle, string detail, string? extraButton = null, Action? extraClick = null)
    {
        var window = new Window
        {
            Title = "任务提醒",
            Width = 420,
            Height = extraButton is null ? 180 : 200,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true,
            ShowActivated = true,
            ResizeMode = ResizeMode.NoResize
        };
        Theme.Tint(window);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        if (extraButton is not null)
        {
            var extra = new Button { Content = extraButton, Width = 100, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
            extra.Click += (_, _) =>
            {
                extraClick?.Invoke();
                window.Close();
            };
            buttons.Children.Add(extra);
        }
        var close = new Button { Content = "知道了", Width = 100, Height = 32 };
        close.Click += (_, _) => window.Close();
        buttons.Children.Add(close);

        var root = new DockPanel { Margin = new Thickness(20) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new TextBlock
        {
            Text = taskTitle + "\n\n" + detail,
            TextWrapping = TextWrapping.Wrap,
            FontSize = Theme.Current.FontSizeTitle
        });
        window.Content = root;
        window.Loaded += (_, _) => ForceForeground(window);
        Console.Beep(880, 160);
        window.Show();
        window.Activate();
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private static void ForceForeground(Window window)
    {
        window.Topmost = false;
        window.Topmost = true;
        window.Activate();
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        ShowWindow(hwnd, 9);
        BringWindowToTop(hwnd);
        SetForegroundWindow(hwnd);
    }
}
