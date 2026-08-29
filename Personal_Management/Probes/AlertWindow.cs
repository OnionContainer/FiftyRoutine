using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace PersonalManagement.Probes;

internal static class AlertWindowProbe
{
    public static void ShowBlocking(int secondsVisible = 12)
    {
        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };

        var remaining = secondsVisible;
        var status = new TextBlock
        {
            Text = $"置顶提醒探针。窗口应盖在其他程序上面。{remaining} 秒后自动关闭。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
            FontSize = 16,
        };

        var close = new Button
        {
            Content = "我看到了",
            Width = 120,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var root = new DockPanel { Margin = new Thickness(20) };
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(status);

        var window = new Window
        {
            Title = "Personal_Management 提醒探针",
            Content = root,
            Width = 460,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true,
            ShowActivated = true,
            ShowInTaskbar = true,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.WhiteSmoke,
        };

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            remaining--;
            if (remaining <= 0)
            {
                timer.Stop();
                window.Close();
                return;
            }
            status.Text = $"置顶提醒探针。窗口应盖在其他程序上面。{remaining} 秒后自动关闭。";
        };

        close.Click += (_, _) =>
        {
            timer.Stop();
            window.Close();
        };

        window.Loaded += (_, _) =>
        {
            Native.ForceToForeground(window);
            Console.Beep(880, 180);
            timer.Start();
        };

        window.Closed += (_, _) => app.Shutdown();
        app.Run(window);
    }
}

internal static class Native
{
    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    public static void ForceToForeground(Window window)
    {
        window.WindowState = WindowState.Normal;
        window.Topmost = false;
        window.Topmost = true;
        window.Activate();
        window.Focus();

        var helper = new WindowInteropHelper(window);
        helper.EnsureHandle();
        var hwnd = helper.Handle;
        ShowWindow(hwnd, SwRestore);
        BringWindowToTop(hwnd);

        var foreground = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foreground, IntPtr.Zero);
        var thisThread = GetCurrentThreadId();
        if (foregroundThread != thisThread)
            AttachThreadInput(foregroundThread, thisThread, true);
        SetForegroundWindow(hwnd);
        if (foregroundThread != thisThread)
            AttachThreadInput(foregroundThread, thisThread, false);
    }
}
