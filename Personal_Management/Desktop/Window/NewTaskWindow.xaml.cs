using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace PersonalManagement.Desktop;

public partial class NewTaskWindow : Window
{
    public string TaskTitle { get; private set; } = "";
    public string TaskType { get; private set; } = "daily";
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
    public bool IsDirectProductivity { get; private set; }
    public double OverflowSeconds { get; private set; }

    private int _originalMinutes = 30;
    private bool _originalAllow;
    private double _originalOverflow;

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
        RefreshBlockVisuals();
        DueBox.Text = task.DueAt?.ToString("yyyy-MM-dd HH:mm") ?? "";
        RemindBox.Text = task.ReminderAt?.ToString("yyyy-MM-dd HH:mm") ?? "";
        MinutesBox.Text = task.RewardMinutes.ToString();
        OverflowBox.IsChecked = task.AllowOverflow;
        ArchivedBox.IsChecked = task.Archived;
        ProductivityBox.IsChecked = task.IsDirectProductivity;
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

    private void RefreshBlockVisuals()
    {
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
        RefreshBlockVisuals();
    }

    private void TypeBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        var id = (TypeBox.SelectedItem as ComboBoxItem)?.Tag as string;
        LevelBox.IsEnabled = id != "daily";
        if (id == "daily") SelectLevel(1);
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
        TaskType = (TypeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "daily";
        RewardLevel = (LevelBox.SelectedItem as ComboBoxItem)?.Tag is int n ? n : 1;
        if (TaskType == "daily") RewardLevel = 1;
        DueAt = ParseOptional(DueBox.Text);
        ReminderAt = ParseOptional(RemindBox.Text);
        RewardMinutes = minutes;
        AllowOverflow = allow;
        Archived = ArchivedBox.IsChecked == true;
        IsDirectProductivity = ProductivityBox.IsChecked == true;
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
