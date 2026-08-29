using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PersonalManagement.Desktop;

public partial class WishEditWindow : Window
{
    public string WishTitle { get; private set; } = "";
    public int Cost { get; private set; } = 1;
    public string? ThumbPath { get; private set; }
    public string? OriginalPath { get; private set; }
    public string? CropJson { get; private set; }
    public bool Archived { get; private set; }

    public WishEditWindow()
    {
        InitializeComponent();
        Theme.Tint(this);
    }

    internal void PrefillEdit(WishRow wish)
    {
        Title = "编辑愿望";
        OkButton.Content = "保存";
        TitleBox.Text = wish.Title;
        CostBox.Text = wish.Cost.ToString();
        if (wish.Preview is not null)
            ThumbPreview.Source = wish.Preview;
        OriginalPath = wish.OriginalPath;
        CropJson = wish.CropJson;
        ArchivedBox.IsChecked = wish.Archived;
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void PickThumb_Click(object sender, RoutedEventArgs e)
    {
        var result = ThumbCropWindow.PickFromFileFull(this, "wish");
        if (result is null) return;
        ApplyCrop(result);
    }

    private void RecropThumb_Click(object sender, RoutedEventArgs e)
    {
        var result = ThumbCropWindow.RecropExistingFull(this, "wish",
            ThumbPreview.Source as BitmapSource, OriginalPath, ThumbPath, CropViewState.FromJson(CropJson));
        if (result is null) return;
        ApplyCrop(result);
    }

    private void ApplyCrop(CropResult result)
    {
        ThumbPath = result.ThumbPath;
        OriginalPath = result.SourcePath;
        CropJson = result.Crop?.ToJson();
        ThumbPreview.Source = FavoriteService.LoadLocalPreview(result.ThumbPath, 96);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            MessageBox.Show("请填写名称。");
            return;
        }
        if (!int.TryParse(CostBox.Text.Trim(), out var cost) || cost < 1)
        {
            MessageBox.Show("额度必须是正整数。");
            return;
        }
        WishTitle = TitleBox.Text.Trim();
        Cost = cost;
        Archived = ArchivedBox.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
