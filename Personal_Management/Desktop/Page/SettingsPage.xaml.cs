using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PersonalManagement.Desktop;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private IAppHost _host = null!;

    public void Attach(IAppHost host)
    {
        _host = host;
        BuildConfigEditor();
    }

    public void Rebuild() => BuildConfigEditor();

    public void OnHostThemeChanged() => BuildConfigEditor();

    private void BuildConfigEditor()
    {
        if (ConfigHost is null) return;
        ConfigHost.Children.Clear();
        var s = _host.Session.Settings;
        var programCfg = ProgramConfig.Load();

        ConfigHost.Children.Add(new TextBlock
        {
            Text = "程序",
            FontSize = Theme.Current.FontSizeTitle,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = Theme.Brush("TextPrimaryBrush")
        });
        ConfigHost.Children.Add(HintBlock(
            "当前用户：" + _host.Session.UserName + "。关闭「直接登录」后，下次启动会先显示登录页选用户（暂无登出按钮）。"));
        var directLoginBox = new CheckBox
        {
            Content = "直接登录上次用户（程序级，默认开）",
            IsChecked = programCfg.DirectLogin,
            Margin = new Thickness(0, 4, 0, 16)
        };
        directLoginBox.Checked += (_, _) =>
        {
            var cfg = ProgramConfig.Load();
            cfg.DirectLogin = true;
            cfg.Save();
        };
        directLoginBox.Unchecked += (_, _) =>
        {
            var cfg = ProgramConfig.Load();
            cfg.DirectLogin = false;
            cfg.Save();
        };
        ConfigHost.Children.Add(directLoginBox);

        ConfigHost.Children.Add(new TextBlock
        {
            Text = "NocoDB",
            FontSize = Theme.Current.FontSizeTitle,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = Theme.Brush("TextPrimaryBrush")
        });
        ConfigHost.Children.Add(HintBlock(
            _host.Session.NocoConnected
                ? "当前已连接 NocoDB。"
                : "当前未连接。依赖云端的页会灰显；也可关掉下方开关改用本地。连接信息仅保存在本用户 settings.json，缺项即未配置。"));

        var bizBox = new CheckBox
        {
            Content = "业务数据使用 NocoDB（任务 / 日程 / 奖励 / 愿望 / 钱包）",
            IsChecked = s.UseNocoBusiness,
            Margin = new Thickness(0, 8, 0, 4)
        };
        var favBox = new CheckBox
        {
            Content = "收藏夹使用 NocoDB（含二级密码）",
            IsChecked = s.UseNocoFavorites,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var weightBox = new CheckBox
        {
            Content = "体重使用 NocoDB",
            IsChecked = s.UseNocoWeight,
            Margin = new Thickness(0, 0, 0, 12)
        };
        ConfigHost.Children.Add(bizBox);
        ConfigHost.Children.Add(favBox);
        ConfigHost.Children.Add(weightBox);

        var urlBox = FieldBox(s.Url ?? "");
        var emailBox = FieldBox(s.Email ?? "");
        var passBox = FieldBox(s.Password ?? "");
        var tokenBox = FieldBox(s.ApiToken ?? "");
        var containerBox = FieldBox(s.Container ?? "nocodb-vibecoding");
        var honeyBox = FieldBox(s.HoneyView ?? "");
        var llmKeyBox = FieldBox(s.LlmApiKey ?? "");
        var llmUrlBox = FieldBox(s.LlmBaseUrl ?? "");
        var llmModelBox = FieldBox(s.LlmModel ?? "");

        ConfigHost.Children.Add(Labeled("服务地址", urlBox));
        ConfigHost.Children.Add(Labeled("Email", emailBox));
        ConfigHost.Children.Add(SecretLabeled("Password", passBox));
        ConfigHost.Children.Add(SecretLabeled("ApiToken（可空）", tokenBox));
        ConfigHost.Children.Add(Labeled("Docker 容器名", containerBox));
        ConfigHost.Children.Add(Labeled("HoneyView 路径", honeyBox));

        ConfigHost.Children.Add(new TextBlock
        {
            Text = "LLM",
            FontSize = Theme.Current.FontSizeTitle,
            Margin = new Thickness(0, 16, 0, 8),
            Foreground = Theme.Brush("TextPrimaryBrush")
        });
        ConfigHost.Children.Add(HintBlock("供未来 LLM 功能使用；ApiKey 可点「显示」查看。"));
        ConfigHost.Children.Add(SecretLabeled("ApiKey", llmKeyBox));
        ConfigHost.Children.Add(Labeled("BaseUrl", llmUrlBox));
        ConfigHost.Children.Add(Labeled("Model", llmModelBox));

        ConfigHost.Children.Add(new TextBlock
        {
            Text = "尝试连接方式",
            Margin = new Thickness(0, 12, 0, 4),
            Foreground = Theme.Brush("TextPrimaryBrush")
        });
        var mode = new ComboBox { Width = 420, Margin = new Thickness(0, 0, 0, 8) };
        mode.Items.Add("访问指定链接上的 NocoDB");
        mode.Items.Add("命令行拉起本地 NocoDB 后再访问指定链接");
        mode.SelectedIndex = s.ConnectMode == NocoConnectMode.UrlOnly ? 0 : 1;
        ConfigHost.Children.Add(mode);

        var btnRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var saveBtn = new Button { Content = "保存连接设置", Margin = new Thickness(0, 0, 8, 0) };
        var tryBtn = new Button { Content = "尝试连接 NocoDB", Margin = new Thickness(0, 0, 8, 0) };
        var applySwitches = new Button { Content = "应用存储开关（会迁移数据）" };
        btnRow.Children.Add(saveBtn);
        btnRow.Children.Add(tryBtn);
        btnRow.Children.Add(applySwitches);
        ConfigHost.Children.Add(btnRow);

        saveBtn.Click += (_, _) =>
        {
            PersistConnectionFields(mode, urlBox, emailBox, passBox, tokenBox, containerBox, honeyBox, llmKeyBox, llmUrlBox, llmModelBox);
            MessageBox.Show("已保存连接设置。", "设置");
        };
        tryBtn.Click += async (_, _) =>
        {
            PersistConnectionFields(mode, urlBox, emailBox, passBox, tokenBox, containerBox, honeyBox, llmKeyBox, llmUrlBox, llmModelBox);
            _host.StatusText = "正在连接…";
            var ok = await _host.Session.TryConnectAsync(msg => Dispatcher.Invoke(() => _host.StatusText = msg));
            MessageBox.Show(ok ? "连接成功。" : ("连接失败：\n" + _host.Session.LastConnectError), "NocoDB");
            BuildConfigEditor();
            await _host.ReloadAllAsync();
        };
        applySwitches.Click += async (_, _) =>
        {
            PersistConnectionFields(mode, urlBox, emailBox, passBox, tokenBox, containerBox, honeyBox, llmKeyBox, llmUrlBox, llmModelBox);
            var wantBiz = bizBox.IsChecked == true;
            var wantFav = favBox.IsChecked == true;
            var wantWeight = weightBox.IsChecked == true;
            try
            {
                _host.StatusText = "正在迁移存储…";
                if (wantBiz != s.UseNocoBusiness)
                    await _host.Session.SetUseNocoBusinessAsync(wantBiz, msg => Dispatcher.Invoke(() => _host.StatusText = msg));
                if (wantFav != _host.Session.Settings.UseNocoFavorites)
                    await _host.Session.SetUseNocoFavoritesAsync(wantFav, msg => Dispatcher.Invoke(() => _host.StatusText = msg));
                if (wantWeight != _host.Session.Settings.UseNocoWeight)
                    await _host.Session.SetUseNocoWeightAsync(wantWeight, msg => Dispatcher.Invoke(() => _host.StatusText = msg));
                MessageBox.Show("存储开关已更新。", "设置");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "迁移失败");
            }
            BuildConfigEditor();
            await _host.ReloadAllAsync();
        };

        ConfigHost.Children.Add(new TextBlock
        {
            Text = "快捷键",
            FontSize = Theme.Current.FontSizeTitle,
            Margin = new Thickness(0, 24, 0, 8),
            Foreground = Theme.Brush("TextPrimaryBrush")
        });
        var hot = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        hot.Children.Add(new TextBlock { Text = "刷新数据", Width = 120, VerticalAlignment = VerticalAlignment.Center });
        hot.Children.Add(new Button { Content = "F5", IsEnabled = false, Width = 72 });
        hot.Children.Add(new TextBlock
        {
            Text = "日程聚焦",
            Width = 120,
            Margin = new Thickness(24, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        hot.Children.Add(new Button { Content = "F1", IsEnabled = false, Width = 72 });
        hot.Children.Add(new TextBlock
        {
            Text = "（本轮仅展示，不可改绑）",
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Theme.Brush("TextSecondaryBrush")
        });
        ConfigHost.Children.Add(hot);
    }

    private void PersistConnectionFields(
        ComboBox mode, TextBox url, TextBox email, TextBox pass, TextBox token, TextBox container, TextBox honey,
        TextBox llmKey, TextBox llmUrl, TextBox llmModel)
    {
        _host.Session.SaveSettingsFromUi(
            _host.Session.Settings.UseNocoBusiness,
            _host.Session.Settings.UseNocoFavorites,
            _host.Session.Settings.UseNocoWeight,
            mode.SelectedIndex == 0 ? NocoConnectMode.UrlOnly : NocoConnectMode.DockerThenUrl,
            url.Text, email.Text, FieldValue(pass), FieldValue(token), container.Text, honey.Text,
            FieldValue(llmKey), llmUrl.Text, llmModel.Text);
    }

    private static TextBox FieldBox(string text) =>
        new() { Text = text, MinWidth = 360, Margin = new Thickness(0, 0, 0, 0) };

    private static UIElement SecretLabeled(string label, TextBox box)
    {
        // WPF TextBox 无 PasswordChar，用 ● 遮罩并把明文放在 Tag 供读取。
        box.FontFamily = new FontFamily("Consolas");
        box.Tag = box.Text ?? "";
        var showing = false;
        var syncing = false;

        void Apply()
        {
            syncing = true;
            try
            {
                var real = box.Tag as string ?? "";
                if (showing)
                {
                    box.IsReadOnly = false;
                    box.Text = real;
                }
                else
                {
                    if (!box.IsReadOnly)
                        box.Tag = box.Text ?? "";
                    real = box.Tag as string ?? "";
                    box.Text = new string('\u25CF', real.Length);
                    box.IsReadOnly = true;
                }
            }
            finally
            {
                syncing = false;
            }
        }

        box.TextChanged += (_, _) =>
        {
            if (syncing || !showing) return;
            box.Tag = box.Text ?? "";
        };

        Apply();
        var btn = new Button { Content = "显示", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        btn.Click += (_, _) =>
        {
            if (showing)
                box.Tag = box.Text ?? "";
            showing = !showing;
            btn.Content = showing ? "隐藏" : "显示";
            Apply();
        };
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(btn, Dock.Right);
        row.Children.Add(btn);
        row.Children.Add(box);
        return Labeled(label, row);
    }

    private static string FieldValue(TextBox box) =>
        box.Tag as string ?? box.Text;

    private static DockPanel Labeled(string label, UIElement field)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        row.Children.Add(new TextBlock
        {
            Text = label,
            Width = 140,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Theme.Brush("TextPrimaryBrush")
        });
        row.Children.Add(field);
        return row;
    }

    private TextBlock HintBlock(string text) => new()
    {
        Text = text,
        Margin = new Thickness(8),
        Foreground = Theme.Brush("TextSecondaryBrush"),
        TextWrapping = TextWrapping.Wrap
    };

}
