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

public partial class AppearancePage : UserControl
{
    public AppearancePage()
    {
        InitializeComponent();
    }


    private IAppHost _host = null!;

    public void Attach(IAppHost host)
    {
        _host = host;
        FillThemeBox();
        BuildSettingsEditor();

    }

    private bool _themeBoxSilent;
    private bool _settingsBuilding;
    private TextBlock HintBlock(string text) => new()
    {
        Text = text,
        Margin = new Thickness(8),
        Foreground = Theme.Brush("TextSecondaryBrush"),
        TextWrapping = TextWrapping.Wrap
    };

    private void FillThemeBox()
    {
        _themeBoxSilent = true;
        ThemeBox.Items.Clear();
        foreach (var style in Theme.All)
        {
            var item = new ComboBoxItem { Content = style.Name, Tag = style.Id };
            ThemeBox.Items.Add(item);
            if (style.Id == Theme.Current.Id)
                ThemeBox.SelectedItem = item;
        }
        _themeBoxSilent = false;
    }

    private void ThemeBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_themeBoxSilent) return;
        var id = (ThemeBox.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.IsNullOrWhiteSpace(id) || id == Theme.Current.Id) return;
        Theme.Activate(id);
    }

    private void SaveThemeCopy_Click(object sender, RoutedEventArgs e)
    {
        var name = TextPrompt.Ask(_host.OwnerWindow, "另存为副本", "副本名称", Theme.Current.Name + " 副本");
        if (name is null) return;
        var copy = Theme.Current.Clone();
        copy.Id = "u-" + Guid.NewGuid().ToString("N")[..8];
        copy.Name = name;
        copy.Builtin = false;
        Theme.SaveCopy(copy);
        FillThemeBox();
        BuildSettingsEditor();
        _host.StatusText = "已保存样式副本「" + name + "」";
    }

    private void DeleteThemeCopy_Click(object sender, RoutedEventArgs e)
    {
        if (Theme.Current.Builtin)
        {
            MessageBox.Show("预设不能删。");
            return;
        }
        Theme.DeleteCopy(Theme.Current.Id);
        FillThemeBox();
        BuildSettingsEditor();
        _host.StatusText = "已删除样式副本";
    }

    private void BuildSettingsEditor()
    {
        if (SettingsHost is null) return;
        _settingsBuilding = true;
        SettingsHost.Children.Clear();
        var t = Theme.Current;
        var locked = t.Builtin;
        if (locked)
            SettingsHost.Children.Add(HintBlock("这是预设，不能直接改。请先「另存为副本」。"));

        void Sec(string title) => SettingsHost.Children.Add(SectionHeader(title));

        Sec("通用");
        SettingsHost.Children.Add(ThemeRows.ColorRow("窗口背景", t.WindowBackground, v => t.WindowBackground = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("表面", t.SurfaceBackground, v => t.SurfaceBackground = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("主文字", t.TextPrimary, v => t.TextPrimary = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("次要文字", t.TextSecondary, v => t.TextSecondary = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("强调色", t.Accent, v => t.Accent = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("边框", t.BorderSubtle, v => t.BorderSubtle = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("参考线", t.GridLine, v => t.GridLine = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("输入框底", t.ControlBackground, v => t.ControlBackground = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("危险", t.Danger, v => t.Danger = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("滚动条槽", t.ScrollbarTrack, v => t.ScrollbarTrack = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("滚动条滑块", t.ScrollbarThumb, v => t.ScrollbarThumb = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("页签文字", t.TabText, v => t.TabText = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("页签底", t.TabBackground, v => t.TabBackground = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("页签悬停底", t.TabHoverBackground, v => t.TabHoverBackground = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("页签选中底", t.TabSelectedBackground, v => t.TabSelectedBackground = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("选项栏底", t.ComboBackground, v => t.ComboBackground = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("选项栏文字", t.ComboText, v => t.ComboText = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("选项栏悬停底", t.ComboHoverBackground, v => t.ComboHoverBackground = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("选项栏悬停字", t.ComboHoverText, v => t.ComboHoverText = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("选项栏选中底", t.ComboSelectedBackground, v => t.ComboSelectedBackground = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("选项栏选中字", t.ComboSelectedText, v => t.ComboSelectedText = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("悬浮提示底", t.TooltipBackground, v => t.TooltipBackground = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("悬浮提示字", t.TooltipText, v => t.TooltipText = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("右键菜单底", t.MenuBackground, v => t.MenuBackground = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("右键菜单字", t.MenuText, v => t.MenuText = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("右键菜单悬停底", t.MenuHoverBackground, v => t.MenuHoverBackground = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("右键菜单悬停字", t.MenuHoverText, v => t.MenuHoverText = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("按钮底", t.ButtonBackground, v => t.ButtonBackground = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("按钮字", t.ButtonText, v => t.ButtonText = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("按钮悬停底", t.ButtonHoverBackground, v => t.ButtonHoverBackground = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("按钮悬停字", t.ButtonHoverText, v => t.ButtonHoverText = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.NumberRow("正文字号", t.FontSizeBody, 10, 22, v => t.FontSizeBody = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.NumberRow("标题字号", t.FontSizeTitle, 12, 28, v => t.FontSizeTitle = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.NumberRow("卡片圆角", t.CardCornerRadius, 0, 24, v => t.CardCornerRadius = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));

        Sec("日程记录");
        SettingsHost.Children.Add(ThemeRows.ColorRow("本周列底", t.WeekColumnBackground, v => t.WeekColumnBackground = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("二四六列底", t.WeekAltColumnBackground, v => t.WeekAltColumnBackground = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("暂停块", t.PauseOverlay, v => t.PauseOverlay = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("溢出条底", t.OverflowTrack, v => t.OverflowTrack = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("溢出条进度", t.OverflowFill, v => t.OverflowFill = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.NumberRow("每小时高度（常态）", t.WeekPxPerHour, 8, 150, v => t.WeekPxPerHour = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.NumberRow("每小时高度（聚焦）", t.WeekFocusPxPerHour, 8, 200, v => t.WeekFocusPxPerHour = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("当前时间线颜色", t.WeekNowLine, v => t.WeekNowLine = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.NumberRow("当前时间线粗细", t.WeekNowLineThickness, 0.5, 8, v => t.WeekNowLineThickness = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.NumberRow("当前时间线透明度", t.WeekNowLineOpacity, 0.05, 1, v => t.WeekNowLineOpacity = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(new TextBlock
        {
            Text = "每日仅显示一部分时间（整点，0–24；填 0 与 24 则显示全日。默认 6–23 即显示到 23:00）",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Theme.Brush("TextSecondaryBrush"),
            Margin = new Thickness(0, 4, 0, 4)
        });
        SettingsHost.Children.Add(ThemeRows.NumberRow("显示起始时", t.WeekHourStart, 0, 24, v =>
        {
            t.WeekHourStart = (int)Math.Round(v);
            if (t.WeekHourEnd <= t.WeekHourStart)
                t.WeekHourEnd = Math.Min(24, t.WeekHourStart + 1);
        }, () => _settingsBuilding, GuardEditable, CommitThemeEdits, integer: true));
        SettingsHost.Children.Add(ThemeRows.NumberRow("显示结束时", t.WeekHourEnd, 0, 24, v =>
        {
            t.WeekHourEnd = (int)Math.Round(v);
            if (t.WeekHourEnd <= t.WeekHourStart)
                t.WeekHourStart = Math.Max(0, t.WeekHourEnd - 1);
        }, () => _settingsBuilding, GuardEditable, CommitThemeEdits, integer: true));
        SettingsHost.Children.Add(ThemeRows.NumberRow("任务卡宽度", t.ScheduleCardWidth, 80, 360, v => t.ScheduleCardWidth = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.NumberRow("任务卡高度", t.ScheduleCardHeight, 120, 400, v => t.ScheduleCardHeight = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.NumberRow("任务图区高度", t.ScheduleCardThumbHeight, 48, 240, v => t.ScheduleCardThumbHeight = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.NumberRow("开始按钮尺寸", t.StartTaskButtonSize, 16, 64, v => t.StartTaskButtonSize = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.NumberRow("开始按钮圆角", t.StartTaskButtonCornerRadius, 0, 32, v => t.StartTaskButtonCornerRadius = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(IconRow());

        Sec("奖励 / 愿望单");
        SettingsHost.Children.Add(ThemeRows.NumberRow("卡片宽度", t.CardWidth, 80, 360, v => t.CardWidth = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.NumberRow("卡片高度", t.CardHeight, 120, 400, v => t.CardHeight = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.NumberRow("图区高度", t.CardThumbHeight, 48, 240, v => t.CardThumbHeight = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));

        Sec("收藏夹");
        SettingsHost.Children.Add(ThemeRows.NumberRow("收藏卡宽度", t.FavCardWidth, 80, 360, v => t.FavCardWidth = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.NumberRow("收藏卡高度", t.FavCardHeight, 80, 360, v => t.FavCardHeight = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.NumberRow("收藏图区高度", t.FavCardThumbHeight, 40, 320, v => t.FavCardThumbHeight = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));

        Sec("体重");
        SettingsHost.Children.Add(ThemeRows.ColorRow("折线颜色", t.WeightChartLine, v => t.WeightChartLine = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("网格线颜色", t.WeightChartGrid, v => t.WeightChartGrid = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.ColorRow("图区背景", t.WeightChartPlotBackground, v => t.WeightChartPlotBackground = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.NumberRow("折线粗细", t.WeightChartLineThickness, 1, 8, v => t.WeightChartLineThickness = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.NumberRow("数据点大小", t.WeightChartPointSize, 2, 16, v => t.WeightChartPointSize = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));
        SettingsHost.Children.Add(ThemeRows.NumberRow("图区高度", t.WeightChartHeight, 120, 800, v => t.WeightChartHeight = v, () => _settingsBuilding, GuardEditable, CommitThemeEdits));

        SettingsHost.IsEnabled = !locked;
        _settingsBuilding = false;
    }

    private static TextBlock SectionHeader(string title) => new()
    {
        Text = title,
        FontSize = Theme.Current.FontSizeTitle,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 16, 0, 8),
        Foreground = Theme.Brush("TextPrimaryBrush")
    };

    private UIElement IconRow()
    {
        var preview = new System.Windows.Controls.Image
        {
            Width = 36,
            Height = 36,
            Source = Theme.LoadStartIcon(),
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var browse = new Button { Content = "浏览…" };
        var reset = new Button { Content = "恢复默认" };
        browse.Click += (_, _) =>
        {
            if (!GuardEditable()) return;
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp" };
            if (dlg.ShowDialog() != true) return;
            Theme.Current.StartTaskIcon = Theme.ImportUserIcon(dlg.FileName);
            preview.Source = Theme.LoadStartIcon();
            CommitThemeEdits();
        };
        reset.Click += (_, _) =>
        {
            if (!GuardEditable()) return;
            Theme.Current.StartTaskIcon = "";
            preview.Source = Theme.LoadStartIcon();
            CommitThemeEdits();
        };
        var row = new DockPanel { Margin = new Thickness(0, 8, 0, 8) };
        row.Children.Add(new TextBlock { Text = "开始任务图标", Width = 120, VerticalAlignment = VerticalAlignment.Center });
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(preview);
        right.Children.Add(browse);
        right.Children.Add(reset);
        row.Children.Add(right);
        return row;
    }

    private bool GuardEditable()
    {
        if (!Theme.Current.Builtin) return true;
        MessageBox.Show("预设不能改，请另存为副本。");
        return false;
    }

    private void CommitThemeEdits()
    {
        if (_settingsBuilding) return;
        _settingsBuilding = true;
        try
        {
            Theme.PersistCurrentIfCopy();
        }
        finally
        {
            _settingsBuilding = false;
        }
    }



    public void OnHostThemeChanged()
    {
        if (!_settingsBuilding)
        {
            FillThemeBox();
            BuildSettingsEditor();
        }
    }
}
