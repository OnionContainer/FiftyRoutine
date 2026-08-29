using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PersonalManagement.Desktop;

public partial class RewardWishWindow : Window
{
    private readonly AppSession _session;
    private readonly ObservableCollection<WishRow> _wishes = [];
    private readonly List<RewardRow> _rewards = [];
    private readonly Random _rng = new();
    private bool _showArchivedRewards;
    private bool _showArchivedWishes;
    private string? _selectedRewardId;
    private string? _selectedWishId;

    public event Action? WalletChanged;

    public RewardWishWindow(AppSession session)
    {
        _session = session;
        InitializeComponent();
        Theme.Tint(this);
        Loaded += async (_, _) => await ReloadAsync();
    }

    private WishRow? SelectedWish => _wishes.FirstOrDefault(w => w.Id == _selectedWishId);
    private RewardRow? SelectedReward => _rewards.FirstOrDefault(r => r.Id == _selectedRewardId);

    public async Task ReloadAsync()
    {
        try
        {
            UpdateOfflineOverlay();
            if (_session.BusinessReady)
            {
                await LoadWalletAsync();
                await LoadRewardsAsync();
                await LoadWishesAsync();
            }
            else
            {
                WalletText.Text = "当前奖券数量：— · 愿望单额度：—";
                _rewards.Clear();
                RenderRewardCards();
                _wishes.Clear();
                RenderWishCards();
            }

            StatusText.Text = "已同步 " + DateTime.Now.ToString("HH:mm:ss");
        }
        catch (Exception ex)
        {
            StatusText.Text = "同步失败";
            MessageBox.Show(this, ex.Message, "同步失败");
        }
    }

    private void UpdateOfflineOverlay()
    {
        var show = !_session.BusinessReady;
        OfflineOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ContentRoot.IsEnabled = !show;
        ContentRoot.Opacity = show ? 0.35 : 1;
    }

    private async void TryConnectNoco_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "正在连接 NocoDB…";
        var ok = await _session.TryConnectAsync(msg => Dispatcher.Invoke(() => StatusText.Text = msg));
        if (!ok)
        {
            MessageBox.Show(
                this,
                "连接失败：\n" + (_session.LastConnectError ?? "未知错误"),
                "NocoDB");
            UpdateOfflineOverlay();
            return;
        }
        await ReloadAsync();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F5) return;
        e.Handled = true;
        _ = ReloadAsync();
    }

    private async Task LoadWalletAsync()
    {
        var (tickets, quota) = await ReadWalletAsync();
        WalletText.Text = $"当前奖券数量：{tickets} · 愿望单额度：{quota}";
        WalletChanged?.Invoke();
    }

    private async void WalletText_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_session.BusinessReady) return;
        try
        {
            var (tickets, quota) = await ReadWalletAsync();
            var edited = WalletPrompt.Ask(this, tickets, quota);
            if (edited is null) return;
            await WriteWalletAsync(edited.Value.Tickets, edited.Value.Quota);
            StatusText.Text = "已调整钱包";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "调整失败");
        }
    }

    private async Task<(int Tickets, int Quota)> ReadWalletAsync()
    {
        var rows = await _session.Business.ListRecordsAsync(StoreTables.State);
        var row = rows.FirstOrDefault();
        var tickets = NocoClient.ReadInt(row, "DrawTickets");
        var quota = NocoClient.ReadInt(row, "WishlistQuota");
        return (tickets, quota);
    }

    private async Task WriteWalletAsync(int tickets, int quota)
    {
        var rows = await _session.Business.ListRecordsAsync(StoreTables.State);
        var id = NocoClient.ReadId(rows.FirstOrDefault()) ?? throw new InvalidOperationException("没有 app_state 行");
        await _session.Business.PatchRecordAsync(StoreTables.State, new Dictionary<string, object?>
        {
            ["Id"] = id,
            ["DrawTickets"] = tickets,
            ["WishlistQuota"] = quota
        });
        await LoadWalletAsync();
    }

    private async Task<BitmapImage?> LoadPreviewAsync(JsonNode? thumbField)
    {
        var url = NocoClient.FirstFileUrl(thumbField);
        if (url is null) return null;
        try
        {
            var bytes = await _session.Business.DownloadBytesAsync(url);
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 160;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private async Task LoadRewardsAsync()
    {
        var rows = await _session.Business.ListRecordsAsync(StoreTables.Rewards);
        _rewards.Clear();
        foreach (var n in rows)
        {
            var row = new RewardRow
            {
                Id = NocoClient.ReadId(n) ?? "",
                Title = NocoClient.ReadString(n, "Title") ?? "",
                Kind = NocoClient.ReadString(n, "Kind") ?? "item",
                QuotaAmount = NocoClient.ReadInt(n, "QuotaAmount"),
                Probability = Math.Clamp(NocoClient.ReadDouble(n, "Probability"), 0, 100),
                IsBase = NocoClient.ReadBool(n, "IsBase"),
                Archived = NocoClient.ReadBool(n, "Archived"),
                Preview = await LoadPreviewAsync(NocoClient.FileField(n, "Thumb")),
                OriginalField = NocoClient.FileField(n, "Original"),
                CropJson = NocoClient.ReadString(n, "CropJson")
            };
            _rewards.Add(row);
        }
        RefreshRewardDisplayProbabilities();
        if (_selectedRewardId is not null && _rewards.All(r => r.Id != _selectedRewardId))
            _selectedRewardId = null;
        RenderRewardCards();
    }

    private void RefreshRewardDisplayProbabilities()
    {
        var fixedSum = _rewards.Where(r => !r.Archived && !r.IsBase).Sum(r => r.Probability);
        foreach (var r in _rewards)
        {
            if (r.IsBase && !r.Archived)
                r.DisplayProbability = Math.Max(0, 100 - fixedSum);
            else
                r.DisplayProbability = r.Probability;
        }
    }

    private async Task LoadWishesAsync()
    {
        var rows = await _session.Business.ListRecordsAsync(StoreTables.Wishlist);
        _wishes.Clear();
        foreach (var n in rows)
        {
            _wishes.Add(new WishRow
            {
                Id = NocoClient.ReadId(n) ?? "",
                Title = NocoClient.ReadString(n, "Title") ?? "",
                Cost = NocoClient.ReadInt(n, "Cost", 1),
                Archived = NocoClient.ReadBool(n, "Archived"),
                Preview = await LoadPreviewAsync(NocoClient.FileField(n, "Thumb")),
                OriginalField = NocoClient.FileField(n, "Original"),
                CropJson = NocoClient.ReadString(n, "CropJson")
            });
        }
        if (_selectedWishId is not null && _wishes.All(w => w.Id != _selectedWishId))
            _selectedWishId = null;
        RenderWishCards();
    }

    private void RewardArchived_Changed(object sender, RoutedEventArgs e)
    {
        if (RewardArchivedBox is null || RewardWrap is null) return;
        _showArchivedRewards = RewardArchivedBox.IsChecked == true;
        RenderRewardCards();
    }

    private void WishArchived_Changed(object sender, RoutedEventArgs e)
    {
        if (WishArchivedBox is null || WishWrap is null) return;
        _showArchivedWishes = WishArchivedBox.IsChecked == true;
        RenderWishCards();
    }

    private TextBlock HintBlock(string text) => new()
    {
        Text = text,
        Margin = new Thickness(8),
        Foreground = Theme.Brush("TextSecondaryBrush"),
        TextWrapping = TextWrapping.Wrap
    };

    private void RenderRewardCards()
    {
        RewardWrap.Children.Clear();
        var visible = _rewards.Where(r => _showArchivedRewards || !r.Archived).ToList();
        if (visible.Count == 0)
        {
            var hint = _rewards.Count == 0
                ? "奖池是空的。"
                : "没有可显示的奖励。勾选「显示已归档内容」可查看已归档项。";
            RewardWrap.Children.Add(HintBlock(hint));
            return;
        }
        foreach (var row in visible)
            RewardWrap.Children.Add(CreateRewardCard(row));
    }

    private UIElement CreateRewardCard(RewardRow row)
    {
        var extra = row.Kind switch
        {
            "quota" => $" · 额度 {row.QuotaAmount}",
            "ticket" => $" · 奖券 {row.QuotaAmount}",
            _ => ""
        };
        var prob = row.IsBase
            ? $"基础 · 当前 {row.DisplayProbability:0.##}%"
            : $"概率 {row.DisplayProbability:0.##}%";
        return CreateSimpleCard(
            row.Id == _selectedRewardId,
            row.Preview,
            row.KindLabel,
            $"{row.Title}\n{row.KindLabel} · {prob}{extra}",
            row.Archived,
            () => { _selectedRewardId = row.Id; RenderRewardCards(); },
            () => EditRewardAsync(row));
    }

    private void RenderWishCards()
    {
        WishWrap.Children.Clear();
        var visible = _wishes.Where(w => _showArchivedWishes || !w.Archived).ToList();
        if (visible.Count == 0)
        {
            var hint = _wishes.Count == 0
                ? "还没有愿望。点「添加」。"
                : "没有可显示的愿望。勾选「显示已归档内容」可查看已归档项。";
            WishWrap.Children.Add(HintBlock(hint));
            return;
        }
        foreach (var row in visible)
            WishWrap.Children.Add(CreateWishCard(row));
    }

    private UIElement CreateWishCard(WishRow row) =>
        CreateSimpleCard(
            row.Id == _selectedWishId,
            row.Preview,
            "愿望",
            $"{row.Title}\n所需额度 {row.Cost}",
            row.Archived,
            () => { _selectedWishId = row.Id; RenderWishCards(); },
            () => EditWishAsync(row));

    private UIElement CreateSimpleCard(
        bool selected, BitmapImage? preview, string placeholder,
        string info, bool archived, Action onSelect, Func<Task> onEdit)
    {
        var t = Theme.Current;
        var w = t.CardWidth;
        var thumbH = t.CardThumbHeight;
        UIElement thumbChild;
        if (preview is not null)
        {
            thumbChild = new System.Windows.Controls.Image
            {
                Height = thumbH,
                Stretch = Stretch.UniformToFill,
                Source = preview
            };
        }
        else
        {
            thumbChild = new TextBlock
            {
                Text = placeholder,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Theme.Brush("TextSecondaryBrush")
            };
        }
        var thumbRadius = Math.Max(0, t.CardCornerRadius - 2);
        var icon = new Border
        {
            Height = thumbH,
            Background = preview is null
                ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(50,
                    TaskVisual.ParseColor(Theme.Current.Accent).R,
                    TaskVisual.ParseColor(Theme.Current.Accent).G,
                    TaskVisual.ParseColor(Theme.Current.Accent).B))
                : Brushes.Transparent,
            Child = thumbChild,
            CornerRadius = new CornerRadius(thumbRadius),
            ClipToBounds = true
        };
        UiShapes.RoundClip(icon, thumbRadius);
        var infoBlock = new TextBlock
        {
            Text = info,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = Theme.Brush("TextPrimaryBrush"),
            FontSize = t.FontSizeBody
        };
        var stack = new StackPanel();
        stack.Children.Add(icon);
        stack.Children.Add(infoBlock);
        var border = new Border
        {
            Width = w,
            Height = t.CardHeight,
            Margin = new Thickness(6),
            Padding = new Thickness(10),
            Background = Theme.Brush("SurfaceBackgroundBrush"),
            BorderBrush = selected ? Theme.Brush("AccentBrush") : Theme.Brush("BorderSubtleBrush"),
            BorderThickness = new Thickness(selected ? 3 : 1),
            CornerRadius = new CornerRadius(t.CardCornerRadius),
            Cursor = Cursors.Hand,
            Opacity = archived ? 0.55 : 1,
            Child = stack
        };
        border.MouseLeftButtonDown += async (_, e) =>
        {
            onSelect();
            if (e.ClickCount == 2)
                await onEdit();
        };
        return border;
    }

    private async Task<string?> DownloadAttachmentTempAsync(IRecordStore store, JsonNode? field, string prefix)
    {
        var url = NocoClient.FirstFileUrl(field);
        if (url is null) return null;
        try
        {
            var bytes = await store.DownloadBytesAsync(url);
            var ext = System.IO.Path.GetExtension(url.Split('?')[0]);
            if (string.IsNullOrWhiteSpace(ext) || ext.Length > 5) ext = ".png";
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N")[..10] + ext);
            await File.WriteAllBytesAsync(path, bytes);
            return path;
        }
        catch { return null; }
    }

    private async Task EditRewardAsync(RewardRow row)
    {
        var dlg = new RewardEditWindow { Owner = this };
        row.OriginalPath = await DownloadAttachmentTempAsync(_session.Business, row.OriginalField, "pm-rew-orig-");
        dlg.PrefillEdit(row);
        if (dlg.ShowDialog() != true) return;
        try
        {
            if (!await TryPersistRewardAsync(dlg, row.Id)) return;
            await LoadRewardsAsync();
            StatusText.Text = "已保存奖励「" + dlg.RewardTitle + "」";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存失败");
        }
    }

    private async Task EditWishAsync(WishRow wish)
    {
        var dlg = new WishEditWindow { Owner = this };
        wish.OriginalPath = await DownloadAttachmentTempAsync(_session.Business, wish.OriginalField, "pm-wish-orig-");
        dlg.PrefillEdit(wish);
        if (dlg.ShowDialog() != true) return;
        try
        {
            var fields = new Dictionary<string, object?>
            {
                ["Id"] = wish.Id,
                ["Title"] = dlg.WishTitle,
                ["Cost"] = dlg.Cost,
                ["Archived"] = dlg.Archived
            };
            if (dlg.ThumbPath is not null)
                fields["Thumb"] = await _session.Business.UploadAsync(System.IO.Path.GetFileName(dlg.ThumbPath), await File.ReadAllBytesAsync(dlg.ThumbPath), MimeOf(dlg.ThumbPath));
            if (dlg.OriginalPath is not null)
                fields["Original"] = await _session.Business.UploadAsync(System.IO.Path.GetFileName(dlg.OriginalPath), await File.ReadAllBytesAsync(dlg.OriginalPath), MimeOf(dlg.OriginalPath));
            if (dlg.CropJson is not null)
                fields["CropJson"] = dlg.CropJson;
            await _session.Business.PatchRecordAsync(StoreTables.Wishlist, fields);
            await LoadWishesAsync();
            StatusText.Text = "已保存愿望「" + dlg.WishTitle + "」";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存失败");
        }
    }

    private async void Draw_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (tickets, quota) = await ReadWalletAsync();
            if (tickets <= 0)
            {
                DrawResult.Text = "没有奖券。完成 L1–L3 或每日任务可获得。";
                return;
            }
            await LoadRewardsAsync();
            var active = _rewards.Where(r => !r.Archived).ToList();
            if (active.Count == 0)
            {
                DrawResult.Text = "奖池是空的。";
                return;
            }

            var baseReward = active.FirstOrDefault(r => r.IsBase);
            var fixedSum = active.Where(r => !r.IsBase).Sum(r => r.Probability);
            RewardRow? hit;
            if (baseReward is null && fixedSum < 100)
            {
                MessageBox.Show(
                    this,
                    "当前没有基础奖励。本次抽取会将各奖励的设定概率视为权重来生成随机结果。",
                    "个人管理");
                var weighted = active.Where(r => r.Probability > 0).ToList();
                if (weighted.Count == 0)
                {
                    DrawResult.Text = "可抽奖励的概率均为 0。";
                    return;
                }
                var totalW = weighted.Sum(r => r.Probability);
                var rollW = _rng.NextDouble() * totalW;
                hit = null;
                var accW = 0.0;
                foreach (var r in weighted)
                {
                    accW += r.Probability;
                    if (rollW < accW)
                    {
                        hit = r;
                        break;
                    }
                }
                hit ??= weighted[^1];
            }
            else
            {
                var roll = _rng.NextDouble() * 100;
                hit = null;
                var acc = 0.0;
                foreach (var r in active.Where(x => !x.IsBase))
                {
                    acc += r.Probability;
                    if (roll < acc)
                    {
                        hit = r;
                        break;
                    }
                }
                if (hit is null)
                    hit = baseReward;
                if (hit is null)
                {
                    DrawResult.Text = "未抽中奖励。";
                    tickets -= 1;
                    await WriteWalletAsync(tickets, quota);
                    return;
                }
            }

            tickets -= 1;
            if (hit.Kind == "quota")
                quota += hit.QuotaAmount;
            else if (hit.Kind == "ticket")
                tickets += hit.QuotaAmount;
            await WriteWalletAsync(tickets, quota);
            DrawResult.Text = "抽到：" + hit.Title;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "抽奖失败");
        }
    }

    private async void AddReward_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new RewardEditWindow { Owner = this };
        dlg.PrefillCreate();
        if (dlg.ShowDialog() != true) return;
        try
        {
            if (!await TryPersistRewardAsync(dlg, null)) return;
            await LoadRewardsAsync();
            StatusText.Text = "已添加奖励「" + dlg.RewardTitle + "」";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "添加失败");
        }
    }

    private async Task<bool> TryPersistRewardAsync(RewardEditWindow dlg, string? existingId)
    {
        if (!dlg.Archived && !dlg.IsBase)
        {
            var others = _rewards
                .Where(r => !r.Archived && !r.IsBase && r.Id != existingId)
                .Sum(r => r.Probability);
            if (others + dlg.Probability > 100)
            {
                MessageBox.Show(this, $"未归档非基础奖励的概率合计不能超过 100%（当前其余合计 {others:0.##}%，本次 {dlg.Probability:0.##}%）。");
                return false;
            }
        }

        if (dlg.IsBase && !dlg.Archived)
        {
            foreach (var old in _rewards.Where(r => r.IsBase && !r.Archived && r.Id != existingId))
            {
                await _session.Business.PatchRecordAsync(StoreTables.Rewards, new Dictionary<string, object?>
                {
                    ["Id"] = old.Id,
                    ["IsBase"] = false,
                    ["Probability"] = 0
                });
                MessageBox.Show(this, $"{old.Title}不再设为基础奖励，抽取概率已设为0%。", "个人管理");
            }
        }

        var fields = new Dictionary<string, object?>
        {
            ["Title"] = dlg.RewardTitle,
            ["Kind"] = dlg.Kind,
            ["QuotaAmount"] = dlg.QuotaAmount,
            ["Probability"] = dlg.IsBase ? 0 : dlg.Probability,
            ["IsBase"] = dlg.IsBase,
            ["Archived"] = dlg.Archived
        };
        if (dlg.ThumbPath is not null)
            fields["Thumb"] = await _session.Business.UploadAsync(System.IO.Path.GetFileName(dlg.ThumbPath), await File.ReadAllBytesAsync(dlg.ThumbPath), MimeOf(dlg.ThumbPath));
        if (dlg.OriginalPath is not null)
            fields["Original"] = await _session.Business.UploadAsync(System.IO.Path.GetFileName(dlg.OriginalPath), await File.ReadAllBytesAsync(dlg.OriginalPath), MimeOf(dlg.OriginalPath));
        if (dlg.CropJson is not null)
            fields["CropJson"] = dlg.CropJson;
        if (existingId is null)
            await _session.Business.CreateRecordAsync(StoreTables.Rewards, fields);
        else
        {
            fields["Id"] = existingId;
            await _session.Business.PatchRecordAsync(StoreTables.Rewards, fields);
        }
        return true;
    }

    private async void AddWish_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new WishEditWindow { Owner = this };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var fields = new Dictionary<string, object?>
            {
                ["Title"] = dlg.WishTitle,
                ["Cost"] = dlg.Cost,
                ["Archived"] = dlg.Archived
            };
            if (dlg.ThumbPath is not null)
                fields["Thumb"] = await _session.Business.UploadAsync(System.IO.Path.GetFileName(dlg.ThumbPath), await File.ReadAllBytesAsync(dlg.ThumbPath), MimeOf(dlg.ThumbPath));
            if (dlg.OriginalPath is not null)
                fields["Original"] = await _session.Business.UploadAsync(System.IO.Path.GetFileName(dlg.OriginalPath), await File.ReadAllBytesAsync(dlg.OriginalPath), MimeOf(dlg.OriginalPath));
            if (dlg.CropJson is not null)
                fields["CropJson"] = dlg.CropJson;
            await _session.Business.CreateRecordAsync(StoreTables.Wishlist, fields);
            await LoadWishesAsync();
            StatusText.Text = "已添加愿望「" + dlg.WishTitle + "」";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "添加失败");
        }
    }

    private async void Redeem_Click(object sender, RoutedEventArgs e)
    {
        var wish = SelectedWish;
        if (wish is null)
        {
            MessageBox.Show(this, "请先选一项愿望。");
            return;
        }
        try
        {
            var (tickets, quota) = await ReadWalletAsync();
            if (quota < wish.Cost)
            {
                MessageBox.Show(this, $"额度不够（现有 {quota}，需要 {wish.Cost}）。");
                return;
            }
            await WriteWalletAsync(tickets, quota - wish.Cost);
            MessageBox.Show(this, $"已兑换「{wish.Title}」，扣 {wish.Cost} 额度。条目还留在清单里，方便你自己删。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "兑换失败");
        }
    }

    private static string MimeOf(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".mkv" => "video/x-matroska",
        _ => "application/octet-stream"
    };
}
