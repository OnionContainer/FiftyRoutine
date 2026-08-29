using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PersonalManagement.Desktop;

public partial class FavoriteAddWindow : Window
{
    public string FavTitle { get; private set; } = "";
    public string Kind { get; private set; } = "image";
    public string Source { get; private set; } = "";
    public string Tags { get; private set; } = "";
    public bool IsPrivate { get; private set; }
    public string? FilePath { get; private set; }
    public string? ThumbPath { get; private set; }
    public string? OriginalPath { get; private set; }
    public string? CropJson { get; private set; }
    public bool IsEdit { get; private set; }

    private BitmapImage? _existingPreview;

    public FavoriteAddWindow()
    {
        InitializeComponent();
        KindBox.Items.Add(new ComboBoxItem { Content = "图片", Tag = "image" });
        KindBox.Items.Add(new ComboBoxItem { Content = "链接/文字", Tag = "link" });
        KindBox.Items.Add(new ComboBoxItem { Content = "视频", Tag = "video" });
        KindBox.SelectedIndex = 0;
        Theme.Tint(this);
        RefreshPreview();
    }

    internal void PrefillCreate(string? filePath = null, string? title = null, string kind = "image", string? source = null)
    {
        SelectKind(kind);
        if (!string.IsNullOrWhiteSpace(title))
            TitleBox.Text = title;
        if (!string.IsNullOrWhiteSpace(source))
            SourceBox.Text = source;
        if (filePath is not null)
        {
            SetFile(filePath, fillTitleIfEmpty: string.IsNullOrWhiteSpace(TitleBox.Text));
            TryCropThumb(filePath);
        }
        RefreshPreview();
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    internal void PrefillEdit(FavoriteItem item)
    {
        IsEdit = true;
        Title = "编辑收藏";
        OkButton.Content = "保存";
        _existingPreview = item.Preview;
        SelectKind(string.IsNullOrWhiteSpace(item.Kind) ? "image" : item.Kind);
        TitleBox.Text = item.Title;
        SourceBox.Text = item.Source;
        TagsBox.Text = item.TagsRaw;
        PrivateBox.IsChecked = item.IsPrivate;
        FileLabel.Text = "（保留已有文件，可重新选择）";
        ThumbLabel.Text = "（保留已有缩略图，可重新选择）";
        OriginalPath = item.OriginalPath;
        CropJson = item.CropJson;
        RefreshPreview();
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void SelectKind(string kind)
    {
        foreach (ComboBoxItem item in KindBox.Items)
        {
            if ((item.Tag as string) == kind)
            {
                KindBox.SelectedItem = item;
                return;
            }
        }
    }

    private string CurrentKind =>
        (KindBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "image";

    private void KindBox_Changed(object sender, SelectionChangedEventArgs e) => RefreshPreview();

    private void SetFile(string path, bool fillTitleIfEmpty)
    {
        FilePath = path;
        FileLabel.Text = Path.GetFileName(path);
        if (fillTitleIfEmpty)
            TitleBox.Text = Path.GetFileNameWithoutExtension(path);
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (PreviewImage is null || PreviewPlaceholder is null) return;

        BitmapImage? bmp = null;
        if (ThumbPath is not null)
            bmp = FavoriteService.LoadLocalPreview(ThumbPath);
        if (bmp is null && FilePath is not null)
            bmp = FavoriteService.LoadLocalPreview(FilePath);
        bmp ??= _existingPreview;

        PreviewImage.Source = bmp;
        PreviewPlaceholder.Visibility = bmp is null ? Visibility.Visible : Visibility.Collapsed;
        PreviewPlaceholder.Text = CurrentKind switch
        {
            "link" => "链接",
            "video" => "视频",
            _ => "图片"
        };
    }

    private void PickFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "媒体|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.mp4;*.webm;*.mkv|所有文件|*.*" };
        if (dlg.ShowDialog() == true)
        {
            SetFile(dlg.FileName, fillTitleIfEmpty: string.IsNullOrWhiteSpace(TitleBox.Text));
            TryCropThumb(dlg.FileName);
        }
    }

    private void PickThumb_Click(object sender, RoutedEventArgs e)
    {
        var result = ThumbCropWindow.PickFromFileFull(this, "fav");
        if (result is null) return;
        ApplyCrop(result);
    }

    private void RecropThumb_Click(object sender, RoutedEventArgs e)
    {
        var result = ThumbCropWindow.RecropExistingFull(this, "fav",
            PreviewImage.Source as BitmapSource, OriginalPath, ThumbPath, CropViewState.FromJson(CropJson));
        if (result is null) return;
        ApplyCrop(result);
    }

    private void ApplyCrop(CropResult result)
    {
        ThumbPath = result.ThumbPath;
        OriginalPath = result.SourcePath;
        CropJson = result.Crop?.ToJson();
        ThumbLabel.Text = Path.GetFileName(result.ThumbPath);
        RefreshPreview();
    }

    private void TryCropThumb(string imagePath)
    {
        if (!FavoriteService.IsImagePath(imagePath)) return;
        var original = ThumbCropWindow.PersistOriginalCopy(imagePath);
        var result = ThumbCropWindow.AskFull(Owner ?? this, original, "fav");
        if (result is null) return;
        ApplyCrop(result);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Kind = CurrentKind;
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            MessageBox.Show("请填写名称。");
            return;
        }
        if (Kind == "image" && FilePath is null && !IsEdit)
        {
            MessageBox.Show("图片收藏需要选一个文件。");
            return;
        }
        FavTitle = TitleBox.Text.Trim();
        Source = SourceBox.Text.Trim();
        Tags = TagsBox.Text.Trim();
        IsPrivate = PrivateBox.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
