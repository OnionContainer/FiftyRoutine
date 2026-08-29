using System.IO;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace PersonalManagement.Desktop;

public partial class App : System.Windows.Application
{
    private WinForms.NotifyIcon? _tray;
    private bool _exitFromTray;
    private MainWindow? _main;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        Theme.LoadAndApply();

        var splash = new System.Windows.Window
        {
            Title = "个人管理",
            Width = 420,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Content = new System.Windows.Controls.TextBlock
            {
                Text = "正在启动…",
                Margin = new Thickness(24),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 16
            }
        };
        splash.Show();
        Theme.Tint(splash);

        try
        {
            if (Paths.FindWorkspaceRoot() is null)
                throw new DirectoryNotFoundException("找不到工作区（个人管理工具需求.md / nocodb-admin.txt）。");

            var status = (System.Windows.Controls.TextBlock)splash.Content;
            var session = AppSession.Create();

            if (session.Settings.UseNocoBusiness || session.Settings.UseNocoFavorites || session.Settings.UseNocoWeight)
            {
                status.Text = "正在尝试连接 NocoDB…";
                await session.TryConnectAsync(msg => Dispatcher.Invoke(() => status.Text = msg));
                // 连不上也不退出：主窗口里对依赖页灰显
            }

            splash.Close();
            _main = new MainWindow(session);
            _main.Closing += MainOnClosing;
            SetupTray(_main);
            _main.Show();
        }
        catch (Exception ex)
        {
            splash.Close();
            System.Windows.MessageBox.Show(
                "启动失败：\n" + ex.Message,
                "个人管理",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void SetupTray(MainWindow main)
    {
        _tray = new WinForms.NotifyIcon
        {
            Text = "个人管理",
            Visible = true,
            Icon = AppIcon.Tray() ?? System.Drawing.SystemIcons.Application
        };
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("打开主窗口", null, (_, _) => ShowMain());
        menu.Items.Add("退出", null, async (_, _) =>
        {
            _exitFromTray = true;
            if (_main is not null)
                await _main.AbandonRunningAsync();
            Shutdown();
        });
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowMain();
    }

    private void ShowMain()
    {
        if (_main is null) return;
        WindowBounds.RestoreFromTray(_main);
    }

    private void MainOnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exitFromTray)
        {
            WindowBounds.Save(_main);
            return;
        }
        WindowBounds.Save(_main);
        e.Cancel = true;
        _main?.Hide();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        WindowBounds.Save(_main);
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.OnExit(e);
    }
}
