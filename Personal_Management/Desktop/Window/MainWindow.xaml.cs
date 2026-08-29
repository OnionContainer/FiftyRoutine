using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PersonalManagement.Desktop;

public partial class MainWindow : Window, IAppHost
{
    private readonly AppSession _session;

    AppSession IAppHost.Session => _session;
    Window IAppHost.OwnerWindow => this;

    string IAppHost.StatusText
    {
        get => StatusText.Text;
        set => StatusText.Text = value;
    }

    public MainWindow(AppSession session)
    {
        _session = session;
        InitializeComponent();
        WindowBounds.Restore(this);
        WindowBounds.Attach(this);
        Theme.Tint(this);

        SchedulePageHost.Attach(this);
        FavoritesPageHost.Attach(this);
        WeightPageHost.Attach(this);
        AppearancePageHost.Attach(this);
        SettingsPageHost.Attach(this);

        Theme.Changed += (_, _) => Dispatcher.Invoke(OnThemeChanged);
        Loaded += async (_, _) => await ReloadAllAsync();
    }

    public async Task ReloadAllAsync()
    {
        try
        {
            SchedulePageHost.UpdateOfflineOverlay();
            FavoritesPageHost.UpdateOfflineOverlay();
            WeightPageHost.UpdateOfflineOverlay();

            if (_session.BusinessReady)
                await SchedulePageHost.ReloadAsync();
            else
                SchedulePageHost.ClearBusinessUi();

            if (_session.FavoritesReady)
                await FavoritesPageHost.ReloadAsync();
            else
                FavoritesPageHost.ClearUi();

            if (_session.WeightReady)
                await WeightPageHost.ReloadAsync();
            else
                WeightPageHost.ClearUi();

            await SchedulePageHost.ReloadRewardWishIfOpenAsync();

            StatusText.Text = "已同步 " + DateTime.Now.ToString("HH:mm:ss");
        }
        catch (Exception ex)
        {
            StatusText.Text = "同步失败";
            MessageBox.Show(ex.Message, "同步失败");
        }
    }

    public async Task TryConnectNocoAsync()
    {
        StatusText.Text = "正在连接 NocoDB…";
        var ok = await _session.TryConnectAsync(msg => Dispatcher.Invoke(() => StatusText.Text = msg));
        if (!ok)
        {
            MessageBox.Show(
                "连接失败：\n" + (_session.LastConnectError ?? "未知错误"),
                "NocoDB");
            SchedulePageHost.UpdateOfflineOverlay();
            FavoritesPageHost.UpdateOfflineOverlay();
            WeightPageHost.UpdateOfflineOverlay();
            return;
        }
        await ReloadAllAsync();
        SettingsPageHost.Rebuild();
    }

    private void OnThemeChanged()
    {
        Theme.Tint(this);
        AppearancePageHost.OnHostThemeChanged();
        SettingsPageHost.OnHostThemeChanged();
        SchedulePageHost.OnHostThemeChanged();
        FavoritesPageHost.OnHostThemeChanged();
        WeightPageHost.OnHostThemeChanged();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            e.Handled = true;
            if (WeekTab?.IsSelected == true)
                SchedulePageHost.ToggleFocusFromHost();
            return;
        }
        if (e.Key == Key.F5)
        {
            e.Handled = true;
            _ = ReloadAllAsync();
            return;
        }
        if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.OriginalSource is TextBox) return;
        if (FavTab?.IsSelected == true)
        {
            e.Handled = true;
            _ = FavoritesPageHost.ImportClipboardFromHostAsync();
            return;
        }
        if (WeekTab?.IsSelected == true)
        {
            e.Handled = true;
            _ = SchedulePageHost.ImportClipboardFromHostAsync();
        }
    }

    public Task AbandonRunningAsync() => SchedulePageHost.AbandonRunningAsync();
}
