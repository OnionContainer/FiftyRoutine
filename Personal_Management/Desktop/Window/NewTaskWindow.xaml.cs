using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PersonalManagement.Desktop;

public partial class NewTaskWindow : Window
{
    public string TaskTitle { get; private set; } = "";
    public string TaskType { get; private set; } = "flexible";
    public int RewardLevel { get; private set; } = 1;
    public DateTime? DueAt { get; private set; }
    public DateTime? ReminderAt { get; private set; }
    public string ColorHex { get; private set; } = TaskVisual.DefaultColor;
    public string BlockPattern { get; private set; } = BlockPatterns.None;
    public string BlockPatternColor { get; private set; } = BlockPatterns.DefaultPatternColor;
    public string? BlockStyleJson { get; private set; }
    public string? ThumbPath { get; private set; }
    public string? OriginalPath { get; private set; }
    public string? CropJson { get; private set; }
    /// <summary>用户点了「取消缩略图」，保存时应清空 Thumb / Original / CropJson。</summary>
    public bool ClearThumb { get; private set; }
    public int RewardMinutes { get; private set; } = 30;
    public bool AllowOverflow { get; private set; }
    public bool Archived { get; private set; }
    public double OverflowSeconds { get; private set; }

    private int _originalMinutes = 30;
    private bool _originalAllow;
    private double _originalOverflow;
    private bool _patternUiReady;
    /// <summary>程序化改 PatternBox 时不覆盖 BlockStyleJson（否则高级编辑结果会被 FromLegacy 冲掉）。</summary>
    private bool _syncingPatternUi;

    public NewTaskWindow()
    {
        InitializeComponent();
        Theme.Tint(this);
        foreach (var (id, label) in TaskKinds.All)
            TypeBox.Items.Add(new ComboBoxItem { Content = label, Tag = id });
        TypeBox.SelectedIndex = 0;
        for (var i = 0; i <= 5; i++)
            LevelBox.Items.Add(new ComboBoxItem { Content = i == 0 ? "L0 无奖励" : "L" + i, Tag = i });
        SelectLevel(1);
        foreach (var (id, label) in BlockPatterns.All)
            PatternBox.Items.Add(new ComboBoxItem { Content = label, Tag = id });
        SelectPattern(BlockPatterns.None);
        _patternUiReady = true;
        RefreshBlockVisuals();
    }

    internal void PrefillFromCrop(CropResult result, string? title = null)
    {
        ApplyCropResult(result);
        PrefillTitle(title);
    }

    internal void PrefillTitle(string? title = null)
    {
        if (!string.IsNullOrWhiteSpace(title))
            TitleBox.Text = title;
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    internal void PrefillEdit(TaskRow task)
    {
        Title = "编辑任务";
        OkButton.Content = "保存";
        TitleBox.Text = task.Title;
        foreach (ComboBoxItem item in TypeBox.Items)
        {
            if ((item.Tag as string) == task.Type)
            {
                TypeBox.SelectedItem = item;
                break;
            }
        }
        SelectLevel(task.RewardLevel);
        ColorHex = task.ColorHex;
        BlockPattern = BlockPatterns.Normalize(task.BlockPattern);
        BlockPatternColor = string.IsNullOrWhiteSpace(task.BlockPatternColor)
            ? BlockPatterns.DefaultPatternColor
            : task.BlockPatternColor;
        BlockStyleJson = task.BlockStyleJson;
        SelectPattern(BlockPattern);
        RefreshBlockVisuals();
        DueBox.Text = task.DueAt?.ToString("yyyy-MM-dd HH:mm") ?? "";
        RemindBox.Text = task.ReminderAt?.ToString("yyyy-MM-dd HH:mm") ?? "";
        MinutesBox.Text = task.RewardMinutes.ToString();
        OverflowBox.IsChecked = task.AllowOverflow;
        ArchivedBox.IsChecked = task.Archived;
        _originalMinutes = task.RewardMinutes;
        _originalAllow = task.AllowOverflow;
        _originalOverflow = task.OverflowSeconds;
        OverflowSeconds = task.OverflowSeconds;
        if (task.Preview is not null)
            ThumbPreview.Source = task.Preview;
        UpdateClearThumbEnabled();
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void SelectLevel(int level)
    {
        foreach (ComboBoxItem item in LevelBox.Items)
        {
            if (item.Tag is int n && n == level)
            {
                LevelBox.SelectedItem = item;
                return;
            }
        }
    }

    private void SelectPattern(string id)
    {
        var want = BlockPatterns.Normalize(id);
        _syncingPatternUi = true;
        try
        {
            foreach (ComboBoxItem item in PatternBox.Items)
            {
                if ((item.Tag as string) == want)
                {
                    PatternBox.SelectedItem = item;
                    return;
                }
            }
            PatternBox.SelectedIndex = 0;
        }
        finally
        {
            _syncingPatternUi = false;
        }
    }

    private void RefreshBlockVisuals()
    {
        ColorSwatch.Background = TaskVisual.BrushOf(ColorHex);
        PatternColorSwatch.Background = TaskVisual.BrushOf(BlockPatternColor);
        var spec = BlockStyleSpec.FromJson(BlockStyleJson)
                   ?? BlockStyleSpec.FromLegacy(ColorHex, BlockPattern, BlockPatternColor);
        spec.BaseColor = ColorHex;
        PatternPreview.Background = BlockPatterns.CreateBrush(spec);
    }

    private void OpenStyleEditor_Click(object sender, RoutedEventArgs e)
    {
        var initial = BlockStyleSpec.FromJson(BlockStyleJson)
                      ?? BlockStyleSpec.FromLegacy(ColorHex, BlockPattern, BlockPatternColor);
        initial.BaseColor = ColorHex;
        var dlg = new BlockStyleEditorWindow(initial) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var spec = dlg.ResultSpec;
        ColorHex = spec.BaseColor;
        BlockStyleJson = spec.ToJson();
        if (spec.Layers.Count == 0)
        {
            BlockPattern = BlockPatterns.None;
            BlockPatternColor = BlockPatterns.DefaultPatternColor;
        }
        else
        {
            var layer = spec.Layers[0];
            BlockPatternColor = layer.Color;
            BlockPattern = layer.Kind switch
            {
                "stripe" when layer.Angle is > 90 and < 180 => BlockPatterns.StripeLeft,
                "stripe" => BlockPatterns.StripeRight,
                "diamond" => BlockPatterns.Diamond,
                "star" => BlockPatterns.Star,
                "dot" => BlockPatterns.Dot,
                "moon" => BlockPatterns.Moon,
                "sine" => BlockPatterns.StripeRight,
                _ => BlockPatterns.None
            };
        }
        SelectPattern(BlockPattern);
        RefreshBlockVisuals();
    }

    private void Pattern_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_patternUiReady || _syncingPatternUi) return;
        BlockPattern = BlockPatterns.Normalize((PatternBox.SelectedItem as ComboBoxItem)?.Tag as string);
        // 仅用户改简易下拉时，用旧单层覆盖 JSON；高级编辑写回后的同步不得走这里
        var spec = BlockStyleSpec.FromLegacy(ColorHex, BlockPattern, BlockPatternColor);
        BlockStyleJson = spec.Layers.Count == 0 ? null : spec.ToJson();
        RefreshBlockVisuals();
    }

    private void TypeBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        var id = (TypeBox.SelectedItem as ComboBoxItem)?.Tag as string;
        LevelBox.IsEnabled = id != "daily";
        if (id == "daily") SelectLevel(1);
    }

    private void PickColor_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true };
        var current = TaskVisual.ParseColor(ColorHex);
        dlg.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        ColorHex = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
        if (BlockStyleSpec.FromJson(BlockStyleJson) is { } spec)
        {
            spec.BaseColor = ColorHex;
            BlockStyleJson = spec.ToJson();
        }
        RefreshBlockVisuals();
    }

    private void PickPatternColor_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true };
        var current = TaskVisual.ParseColor(BlockPatternColor);
        dlg.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        BlockPatternColor = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
        var spec = BlockStyleSpec.FromJson(BlockStyleJson)
                   ?? BlockStyleSpec.FromLegacy(ColorHex, BlockPattern, BlockPatternColor);
        if (spec.Layers.Count > 0)
            spec.Layers[0].Color = BlockPatternColor;
        else if (BlockPattern != BlockPatterns.None)
            spec = BlockStyleSpec.FromLegacy(ColorHex, BlockPattern, BlockPatternColor);
        BlockStyleJson = spec.Layers.Count == 0 ? null : spec.ToJson();
        RefreshBlockVisuals();
    }

    private void ClearThumb_Click(object sender, RoutedEventArgs e)
    {
        ThumbPath = null;
        OriginalPath = null;
        CropJson = null;
        ThumbPreview.Source = null;
        ClearThumb = true;
        UpdateClearThumbEnabled();
    }

    private void UpdateClearThumbEnabled()
    {
        ClearThumbButton.IsEnabled = ThumbPreview.Source is not null || ThumbPath is not null;
    }

    private void PickThumb_Click(object sender, RoutedEventArgs e)
    {
        var result = ThumbCropWindow.PickFromFileFull(this, "task");
        if (result is not null)
            ApplyCropResult(result);
    }

    private void RecropThumb_Click(object sender, RoutedEventArgs e)
    {
        var result = ThumbCropWindow.RecropExistingFull(this, "task",
            ThumbPreview.Source as BitmapSource,
            originalPath: OriginalPath,
            thumbPath: ThumbPath,
            initial: CropViewState.FromJson(CropJson));
        if (result is not null)
            ApplyCropResult(result);
    }

    private void ApplyCropResult(CropResult result)
    {
        ClearThumb = false;
        ThumbPath = result.ThumbPath;
        OriginalPath = result.SourcePath;
        CropJson = result.Crop?.ToJson();
        ThumbPreview.Source = FavoriteService.LoadLocalPreview(result.ThumbPath, 96);
        UpdateClearThumbEnabled();
    }

    private void SetThumb(string path)
    {
        ClearThumb = false;
        ThumbPath = path;
        ThumbPreview.Source = FavoriteService.LoadLocalPreview(path, 96);
        UpdateClearThumbEnabled();
    }

    internal void PrefillCropState(string? originalPath, string? cropJson)
    {
        OriginalPath = originalPath;
        CropJson = cropJson;
        UpdateClearThumbEnabled();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            MessageBox.Show("请填写名称。");
            return;
        }
        if (!int.TryParse(MinutesBox.Text.Trim(), out var minutes) || minutes < 1 || minutes > 1440)
        {
            MessageBox.Show("输入的数据不符合要求");
            return;
        }
        var allow = OverflowBox.IsChecked == true;
        var overflow = _originalOverflow;
        var minutesChanged = minutes != _originalMinutes;
        var overflowOff = _originalAllow && !allow;
        if (_originalOverflow > 0 && (minutesChanged || overflowOff))
        {
            var ok = MessageBox.Show(
                "改动奖励要求时长将会使溢出进度清零，请问是否确定？",
                "个人管理",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (ok != MessageBoxResult.Yes) return;
            overflow = 0;
        }
        if (!allow) overflow = 0;
        TaskTitle = TitleBox.Text.Trim();
        TaskType = (TypeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "flexible";
        RewardLevel = (LevelBox.SelectedItem as ComboBoxItem)?.Tag is int n ? n : 1;
        if (TaskType == "daily") RewardLevel = 1;
        BlockPattern = BlockPatterns.Normalize((PatternBox.SelectedItem as ComboBoxItem)?.Tag as string);
        DueAt = ParseOptional(DueBox.Text);
        ReminderAt = ParseOptional(RemindBox.Text);
        if (DueBox.Text.Trim().Length > 0 && DueAt is null)
        {
            MessageBox.Show("截止时间格式不对。");
            return;
        }
        if (RemindBox.Text.Trim().Length > 0 && ReminderAt is null)
        {
            MessageBox.Show("提醒时间格式不对。");
            return;
        }
        RewardMinutes = minutes;
        AllowOverflow = allow;
        Archived = ArchivedBox.IsChecked == true;
        OverflowSeconds = overflow;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static DateTime? ParseOptional(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return DateTime.TryParse(text.Trim(), out var d) ? d : null;
    }
}
