using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace PersonalManagement.Desktop;

public partial class RewardEditWindow : Window
{
    public string RewardTitle { get; private set; } = "";
    public string Kind { get; private set; } = "item";
    public int QuotaAmount { get; private set; }
    public double Probability { get; private set; }
    public bool IsBase { get; private set; }
    public string? ThumbPath { get; private set; }
    public string? OriginalPath { get; private set; }
    public string? CropJson { get; private set; }
    public bool Archived { get; private set; }

    public RewardEditWindow()
    {
        InitializeComponent();
        Theme.Tint(this);
        foreach (var (id, label) in RewardKinds.All)
            KindBox.Items.Add(new ComboBoxItem { Content = label, Tag = id });
        KindBox.SelectedIndex = 0;
        BaseBox_Changed(this, new RoutedEventArgs());
        ApplyKindFields(resetValues: true);
    }

    internal void PrefillCreate()
    {
        Title = "添加奖励";
        OkButton.Content = "添加";
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    internal void PrefillEdit(RewardRow row)
    {
        Title = "编辑奖励";
        OkButton.Content = "保存";
        TitleBox.Text = row.Title;
        foreach (ComboBoxItem item in KindBox.Items)
        {
            if ((item.Tag as string) == row.Kind)
            {
                KindBox.SelectedItem = item;
                break;
            }
        }
        ApplyKindFields(resetValues: false);
        if (row.Kind == "ticket")
        {
            TicketBox.Text = Math.Max(1, row.QuotaAmount).ToString();
            QuotaBox.Text = "0";
        }
        else if (row.Kind == "quota")
        {
            QuotaBox.Text = row.QuotaAmount.ToString();
            TicketBox.Text = "1";
        }
        else
        {
            QuotaBox.Text = "0";
            TicketBox.Text = "1";
        }
        ProbBox.Text = row.Probability.ToString("0.##");
        BaseBox.IsChecked = row.IsBase;
        if (row.Preview is not null)
            ThumbPreview.Source = row.Preview;
        OriginalPath = row.OriginalPath;
        CropJson = row.CropJson;
        ArchivedBox.IsChecked = row.Archived;
        BaseBox_Changed(this, new RoutedEventArgs());
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private string CurrentKind =>
        (KindBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "item";

    private void KindBox_Changed(object sender, SelectionChangedEventArgs e) =>
        ApplyKindFields(resetValues: true);

    private void ApplyKindFields(bool resetValues)
    {
        if (QuotaBox is null || TicketBox is null) return;
        var kind = CurrentKind;
        QuotaBox.IsEnabled = kind == "quota";
        TicketBox.IsEnabled = kind == "ticket";
        if (!resetValues) return;
        if (kind != "quota")
            QuotaBox.Text = "0";
        if (kind != "ticket")
            TicketBox.Text = "1";
        else if (string.IsNullOrWhiteSpace(TicketBox.Text) || TicketBox.Text == "0")
            TicketBox.Text = "1";
    }

    private void BaseBox_Changed(object sender, RoutedEventArgs e)
    {
        var isBase = BaseBox.IsChecked == true;
        ProbBox.IsEnabled = !isBase;
        if (isBase)
            ProbBox.Text = "0";
    }

    private void PickThumb_Click(object sender, RoutedEventArgs e)
    {
        var result = ThumbCropWindow.PickFromFileFull(this, "reward");
        if (result is null) return;
        ApplyCrop(result);
    }

    private void RecropThumb_Click(object sender, RoutedEventArgs e)
    {
        var result = ThumbCropWindow.RecropExistingFull(this, "reward",
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
        var kind = CurrentKind;
        var amount = 0;
        if (kind == "quota")
        {
            if (!int.TryParse(QuotaBox.Text.Trim(), out amount) || amount < 0)
            {
                MessageBox.Show("额度必须是 ≥ 0 的整数。");
                return;
            }
        }
        else if (kind == "ticket")
        {
            if (!int.TryParse(TicketBox.Text.Trim(), out amount) || amount < 1)
            {
                MessageBox.Show("奖券数量必须是 ≥ 1 的整数。");
                return;
            }
        }
        var isBase = BaseBox.IsChecked == true;
        double prob = 0;
        if (!isBase)
        {
            if (!double.TryParse(ProbBox.Text.Trim(), out prob) || prob < 0 || prob > 100)
            {
                MessageBox.Show("概率必须是 0–100 的数字。");
                return;
            }
        }
        RewardTitle = TitleBox.Text.Trim();
        Kind = kind;
        QuotaAmount = amount;
        Probability = prob;
        IsBase = isBase;
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
