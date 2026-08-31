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

public partial class FavoritesPage : UserControl
{
    public FavoritesPage()
    {
        InitializeComponent();
    }


    private IAppHost _host = null!;

    public void Attach(IAppHost host)
    {
        _host = host;

    }

    private readonly List<FavoriteItem> _favs = [];
    private bool _privateUnlocked;
    private bool _favBusy;
    private void CardHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 卡片宽度已固定，窗口拉宽拉窄不必重算。
    }

    private async Task LoadFavoritesAsync()
    {
        var rows = await _host.Session.Favorites.ListRecordsAsync(StoreTables.Favorites);
        _favs.Clear();
        foreach (var n in rows)
        {
            if (n is not null)
                _favs.Add(FavoriteService.FromRecord(n));
        }
        await Task.WhenAll(_favs.Select(i => FavoriteService.LoadPreviewAsync(_host.Session.Favorites, i)));
        FillFavTags();
        RenderFavGrid();
    }

    private void FillFavTags()
    {
        var keep = (FavTagBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
        FavTagBox.Items.Clear();
        FavTagBox.Items.Add(new ComboBoxItem { Content = "全部", Tag = "all" });
        FavTagBox.Items.Add(new ComboBoxItem { Content = "untagged", Tag = "untagged" });
        foreach (var tag in _favs.SelectMany(f => f.Tags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t))
        {
            if (tag.StartsWith('*') && (FavPrivateBox?.IsChecked != true || !_privateUnlocked)) continue;
            FavTagBox.Items.Add(new ComboBoxItem { Content = tag, Tag = "tag:" + tag });
        }
        foreach (ComboBoxItem item in FavTagBox.Items)
        {
            if ((item.Tag as string) == keep)
            {
                FavTagBox.SelectedItem = item;
                return;
            }
        }
        FavTagBox.SelectedIndex = 0;
    }

    private IEnumerable<FavoriteItem> VisibleFavorites()
    {
        var filter = (FavTagBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
        foreach (var item in _favs)
        {
            if (item.HasPrivateTag && (FavPrivateBox?.IsChecked != true || !_privateUnlocked)) continue;
            if (filter == "untagged" && !item.IsUntagged) continue;
            if (filter.StartsWith("tag:") && !item.Tags.Contains(filter[4..], StringComparer.OrdinalIgnoreCase))
                continue;
            yield return item;
        }
    }

    private void RenderFavGrid()
    {
        FavWrap.Children.Clear();
        foreach (var item in VisibleFavorites())
            FavWrap.Children.Add(CreateFavCard(item));
        if (FavWrap.Children.Count == 0)
        {
            var hint = _favs.Count == 0
                ? "把图片拖进来，或按 Ctrl+V 粘贴。"
                : "没有符合筛选的收藏。";
            FavWrap.Children.Add(ThumbCard.Hint(hint));
        }
    }

    private UIElement CreateFavCard(FavoriteItem item)
    {
        var t = Theme.Current;
        var check = new CheckBox
        {
            IsChecked = item.Selected,
            Margin = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        check.Checked += (_, _) => item.Selected = true;
        check.Unchecked += (_, _) => item.Selected = false;
        return ThumbCard.Build(new ThumbCard.Options
        {
            Width = t.FavCardWidth,
            Height = t.FavCardHeight,
            ThumbHeight = t.FavCardThumbHeight,
            CornerRadius = t.CardCornerRadius,
            Preview = item.Preview,
            ThumbOverlay = check,
            Caption = item.Title,
            CaptionWrapping = TextWrapping.NoWrap,
            CaptionTrimming = TextTrimming.CharacterEllipsis,
            CaptionToolTip = item.KindLabel + (string.IsNullOrWhiteSpace(item.Source) ? "" : "\n" + item.Source),
            Tag = item,
            OnDoubleClick = () => EditFavoriteAsync(item)
        });
    }

    private List<FavoriteItem> SelectedFavorites() => _favs.Where(f => f.Selected).ToList();

    private async Task OpenFavoriteAsync(FavoriteItem item)
    {
        try
        {
            if (item.Kind == "link" && Uri.TryCreate(item.Source, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = uri.ToString(),
                    UseShellExecute = true
                });
                return;
            }
            var path = await FavoriteService.DownloadBestAsync(_host.Session.Favorites, item);
            var honey = _host.Session.HoneyViewPath;
            if (item.Kind != "image" || string.IsNullOrWhiteSpace(honey) || !File.Exists(honey))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
                return;
            }
            FavoriteService.OpenHoneyView(honey, path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "打开失败");
        }
    }

    private async void AddFavorite_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new FavoriteAddWindow { Owner = _host.OwnerWindow };
        if (dlg.ShowDialog() != true) return;
        try
        {
            await CreateFavoriteAsync(dlg);
            await LoadFavoritesAsync();
            _host.StatusText = "已添加收藏「" + dlg.FavTitle + "」";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "添加失败");
        }
    }

    private void FavDock_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = FavoriteService.DataHasImage(e.Data)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void FavDock_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var files = FavoriteService.ImageFilesFromData(e.Data).ToList();
        if (files.Count == 0)
        {
            var saved = FavoriteService.SaveImageFromData(e.Data);
            if (saved is not null) files.Add(saved);
        }
        if (files.Count == 0)
        {
            _host.StatusText = "拖入的不是图片。";
            return;
        }
        await ImportFavoriteFilesAsync(files);
    }

    private async Task ImportFromClipboardAsync()
    {
        try
        {
            var files = FavoriteService.ImageFilesFromClipboard().ToList();
            if (files.Count == 0)
            {
                var saved = FavoriteService.SaveImageFromClipboard();
                if (saved is not null) files.Add(saved);
            }
            if (files.Count > 0)
            {
                await ImportFavoriteFilesAsync(files);
                return;
            }
            var url = FavoriteService.ClipboardHttpUrl();
            if (url is not null)
            {
                await ImportFavoriteLinkAsync(url);
                return;
            }
            _host.StatusText = "剪贴板里没有图片。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "粘贴失败");
        }
    }

    private async Task ImportFavoriteFilesAsync(IReadOnlyList<string> paths)
    {
        if (_favBusy) return;
        _favBusy = true;
        try
        {
            var added = 0;
            string? lastTitle = null;
            foreach (var path in paths)
            {
                var title = System.IO.Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrWhiteSpace(title) || title.StartsWith("pm-fav-in-", StringComparison.OrdinalIgnoreCase))
                    title = "粘贴 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var dlg = new FavoriteAddWindow { Owner = _host.OwnerWindow };
                dlg.PrefillCreate(filePath: path, title: title, kind: "image");
                if (dlg.ShowDialog() != true) continue;
                await CreateFavoriteAsync(dlg);
                added++;
                lastTitle = dlg.FavTitle;
            }
            if (added == 0)
            {
                _host.StatusText = "已取消添加。";
                return;
            }
            await LoadFavoritesAsync();
            _host.StatusText = added == 1
                ? "已添加收藏「" + lastTitle + "」"
                : $"已添加 {added} 张图片";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "添加失败");
        }
        finally
        {
            _favBusy = false;
        }
    }

    private async Task ImportFavoriteLinkAsync(string url)
    {
        if (_favBusy) return;
        _favBusy = true;
        try
        {
            var title = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;
            var dlg = new FavoriteAddWindow { Owner = _host.OwnerWindow };
            dlg.PrefillCreate(title: title, kind: "link", source: url);
            if (dlg.ShowDialog() != true)
            {
                _host.StatusText = "已取消添加。";
                return;
            }
            await CreateFavoriteAsync(dlg);
            await LoadFavoritesAsync();
            _host.StatusText = "已添加链接「" + dlg.FavTitle + "」";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "添加失败");
        }
        finally
        {
            _favBusy = false;
        }
    }

    private async Task EditFavoriteAsync(FavoriteItem item)
    {
        var dlg = new FavoriteAddWindow { Owner = _host.OwnerWindow };
        item.OriginalPath = await DownloadAttachmentTempAsync(_host.Session.Favorites, item.Original, "pm-fav-orig-");
        dlg.PrefillEdit(item);
        if (dlg.ShowDialog() != true) return;
        try
        {
            await UpdateFavoriteAsync(item.Id, dlg);
            await LoadFavoritesAsync();
            _host.StatusText = "已更新收藏「" + dlg.FavTitle + "」";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "保存失败");
        }
    }

    private async Task CreateFavoriteAsync(FavoriteAddWindow dlg)
    {
        object? fileMeta = null;
        object? thumbMeta = null;
        object? originalMeta = null;
        if (dlg.FilePath is not null)
            fileMeta = await UploadFileAsync(dlg.FilePath);
        if (dlg.ThumbPath is not null)
            thumbMeta = await UploadFileAsync(dlg.ThumbPath);
        if (dlg.OriginalPath is not null)
            originalMeta = await UploadFileAsync(dlg.OriginalPath);
        await _host.Session.Favorites.CreateRecordAsync(StoreTables.Favorites, new Dictionary<string, object?>
        {
            ["Title"] = dlg.FavTitle,
            ["Kind"] = dlg.Kind,
            ["Source"] = dlg.Source,
            ["Tags"] = dlg.Tags,
            ["IsPrivate"] = dlg.IsPrivate,
            ["File"] = fileMeta,
            ["Thumb"] = thumbMeta,
            ["Original"] = originalMeta,
            ["CropJson"] = dlg.CropJson
        });
    }

    private async Task UpdateFavoriteAsync(string id, FavoriteAddWindow dlg)
    {
        var fields = new Dictionary<string, object?>
        {
            ["Id"] = id,
            ["Title"] = dlg.FavTitle,
            ["Kind"] = dlg.Kind,
            ["Source"] = dlg.Source,
            ["Tags"] = dlg.Tags,
            ["IsPrivate"] = dlg.IsPrivate
        };
        if (dlg.FilePath is not null)
            fields["File"] = await UploadFileAsync(dlg.FilePath);
        if (dlg.ThumbPath is not null)
            fields["Thumb"] = await UploadFileAsync(dlg.ThumbPath);
        if (dlg.OriginalPath is not null)
            fields["Original"] = await UploadFileAsync(dlg.OriginalPath);
        if (dlg.CropJson is not null)
            fields["CropJson"] = dlg.CropJson;
        await _host.Session.Favorites.PatchRecordAsync(StoreTables.Favorites, fields);
    }

    private async void CopyFavorite_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedFavorites().FirstOrDefault() ?? VisibleFavorites().FirstOrDefault();
        if (item is null)
        {
            MessageBox.Show("请先勾选一张图片。");
            return;
        }
        try
        {
            var path = await FavoriteService.DownloadBestAsync(_host.Session.Favorites, item);
            FavoriteService.CopyImage(path);
            _host.StatusText = "已复制到剪贴板：" + item.Title;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "复制失败");
        }
    }

    private async void HoneyViewFavorite_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedFavorites().FirstOrDefault() ?? VisibleFavorites().FirstOrDefault();
        if (item is null) { MessageBox.Show("请先勾选一项。"); return; }
        await OpenFavoriteAsync(item);
    }

    private async void ExportFavorites_Click(object sender, RoutedEventArgs e)
    {
        var items = SelectedFavorites();
        if (items.Count == 0)
        {
            MessageBox.Show("请勾选要导出的条目。");
            return;
        }
        using var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "导出到文件夹" };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        try
        {
            await FavoriteService.ExportAsync(_host.Session.Favorites, items, dlg.SelectedPath);
            _host.StatusText = $"已导出 {items.Count} 项";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "导出失败");
        }
    }

    private void FavTagBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (FavWrap is null) return;
        RenderFavGrid();
    }

    private async void FavPrivate_Checked(object sender, RoutedEventArgs e)
    {
        if (_privateUnlocked)
        {
            FillFavTags();
            RenderFavGrid();
            return;
        }
        try
        {
            var rows = await _host.Session.PinStore.ListRecordsAsync(StoreTables.State);
            var row = rows.FirstOrDefault();
            var stored = NocoClient.ReadString(row, "PrivatePin");
            if (string.IsNullOrWhiteSpace(stored) || stored == "null")
            {
                var created = PinWindow.Prompt(_host.OwnerWindow, "设置二级密码", confirmTwice: true);
                if (created is null)
                {
                    FavPrivateBox.IsChecked = false;
                    return;
                }
                var id = NocoClient.ReadId(row);
                if (id is null)
                {
                    await _host.Session.PinStore.CreateRecordAsync(StoreTables.State, new Dictionary<string, object?>
                    {
                        ["Title"] = "main",
                        ["DrawTickets"] = 0,
                        ["WishlistQuota"] = 0,
                        ["RewardScheme"] = "prob-v1",
                        ["PrivatePin"] = FavoriteService.HashPin(created)
                    });
                }
                else
                {
                    await _host.Session.PinStore.PatchRecordAsync(StoreTables.State, new Dictionary<string, object?>
                    {
                        ["Id"] = id,
                        ["PrivatePin"] = FavoriteService.HashPin(created)
                    });
                }
                _privateUnlocked = true;
            }
            else
            {
                var input = PinWindow.Prompt(_host.OwnerWindow, "二级密码");
                if (input is null || !string.Equals(FavoriteService.HashPin(input), stored, StringComparison.OrdinalIgnoreCase))
                {
                    if (input is not null) MessageBox.Show("密码不对。");
                    FavPrivateBox.IsChecked = false;
                    return;
                }
                _privateUnlocked = true;
            }
            FillFavTags();
            RenderFavGrid();
        }
        catch (Exception ex)
        {
            FavPrivateBox.IsChecked = false;
            MessageBox.Show(ex.Message, "无法解锁私密收藏");
        }
    }

    private void FavPrivate_Unchecked(object sender, RoutedEventArgs e)
    {
        FillFavTags();
        RenderFavGrid();
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
    public void UpdateOfflineOverlay()
    {
        FavOfflineOverlay.SetActive(FavDock, !_host.Session.FavoritesReady);
    }

    public async Task ReloadAsync() => await LoadFavoritesAsync();

    public void ClearUi()
    {
        _favs.Clear();
        RenderFavGrid();
    }

    public void OnHostThemeChanged() => RenderFavGrid();

    public Task ImportClipboardFromHostAsync() => ImportFromClipboardAsync();

    private async void TryConnectNoco_Click(object sender, RoutedEventArgs e) =>
        await _host.TryConnectNocoAsync();

    private async Task<object> UploadFileAsync(string path) =>
        await _host.Session.Favorites.UploadAsync(System.IO.Path.GetFileName(path), await File.ReadAllBytesAsync(path), MimeOf(path));
}
