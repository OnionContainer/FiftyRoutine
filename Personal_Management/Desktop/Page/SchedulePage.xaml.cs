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

public partial class SchedulePage : UserControl
{
    public SchedulePage()
    {
        InitializeComponent();
    }


    private IAppHost _host = null!;

    public void Attach(IAppHost host)
    {
        _host = host;
        _reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _reminderTimer.Tick += async (_, _) => await CheckRemindersAsync();
        _reminderTimer.Start();
        _runTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _runTimer.Tick += (_, _) => OnRunTick();
        _weekBoard = new WeekBoard(WeekHost, WeekScroll, GoToNowButton)
        {
            IsRunActive = () => IsRunActive,
            GetMarkableTasks = () => _tasks.Where(t => !t.Archived),
            SetStatusText = s => _host.StatusText = s,
            OnEditTask = EditTaskAsync,
            OnAddNotePin = AddNotePinAtPendingAsync,
            OnEditNotePin = EditNotePinAsync,
            OnMarkSelectionAsTask = MarkSelectionAsTaskAsync
        };
        ApplyTaskRailWidth();
        UpdateCurrentRunChrome();
        ApplyStatsLayout(fromSaved: true);
    }

    private readonly ObservableCollection<TaskRow> _tasks = [];
    private DispatcherTimer _reminderTimer = null!;
    private readonly HashSet<string> _reminded = [];
    private readonly Random _rng = new();
    private bool _taskBusy;
    private bool _showArchivedTasks;
    private string? _runningSessionId;
    private string? _runningTaskId;
    private string? _selectedTaskId;
    private RewardWishWindow? _rewardWishWindow;
    private TaskRunState? _run;
    private TaskRunWindow? _runWindow;
    private DispatcherTimer _runTimer = null!;
    private WeekBoard _weekBoard = null!;
    private int _statsRangeDays = 14;
    private readonly Dictionary<DateTime, double> _statsDirectHours = new();
    private readonly Dictionary<DateTime, double> _statsOtherHours = new();

    private bool IsRunActive => _run is { Finished: false };

    private async Task<(int Tickets, int Quota)> ReadWalletAsync()
    {
        var rows = await _host.Session.Business.ListRecordsAsync(StoreTables.State);
        var row = rows.FirstOrDefault();
        var tickets = NocoClient.ReadInt(row, "DrawTickets");
        var quota = NocoClient.ReadInt(row, "WishlistQuota");
        return (tickets, quota);
    }

    private TaskRow? SelectedTask => _tasks.FirstOrDefault(t => t.Id == _selectedTaskId);


    private async Task LoadWalletAsync()
    {
        var (tickets, quota) = await ReadWalletAsync();
        if (ScheduleWalletText is not null)
            ScheduleWalletText.Text = $"当前奖券数量：{tickets} · 愿望单额度：{quota}";
    }

    private async void ScheduleWallet_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_host.Session.BusinessReady) return;
        try
        {
            var (tickets, quota) = await ReadWalletAsync();
            var edited = WalletPrompt.Ask(_host.OwnerWindow, tickets, quota);
            if (edited is null) return;
            await WriteWalletAsync(edited.Value.Tickets, edited.Value.Quota);
            _host.StatusText = "已调整钱包";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "调整失败");
        }
    }

    private async Task WriteWalletAsync(int tickets, int quota)
    {
        var rows = await _host.Session.Business.ListRecordsAsync(StoreTables.State);
        var id = NocoClient.ReadId(rows.FirstOrDefault()) ?? throw new InvalidOperationException("没有 app_state 行");
        await _host.Session.Business.PatchRecordAsync(StoreTables.State, new Dictionary<string, object?>
        {
            ["Id"] = id,
            ["DrawTickets"] = tickets,
            ["WishlistQuota"] = quota
        });
        await LoadWalletAsync();
        if (_rewardWishWindow is not null)
            await _rewardWishWindow.ReloadAsync();
    }

    private async Task LoadTasksAsync()
    {
        var taskNodes = await _host.Session.Business.ListRecordsAsync(StoreTables.Tasks);
        var completions = await _host.Session.Business.ListRecordsAsync(StoreTables.Completions);
        var sessions = await _host.Session.Business.ListRecordsAsync(StoreTables.Sessions);
        var today = DateTime.Today;
        if (!IsRunActive)
        {
            _runningTaskId = null;
            _runningSessionId = null;
        }

        _tasks.Clear();
        foreach (var node in taskNodes)
        {
            var row = new TaskRow
            {
                Id = NocoClient.ReadId(node) ?? "",
                Title = NocoClient.ReadString(node, "Title") ?? "",
                Type = NocoClient.ReadString(node, "Type") ?? "daily",
                RewardLevel = NocoClient.ReadInt(node, "RewardLevel", 1),
                RegisteredAt = RewardLogic.ParseDate(node, "RegisteredAt"),
                DueAt = RewardLogic.ParseDate(node, "DueAt"),
                ReminderAt = RewardLogic.ParseDate(node, "ReminderAt"),
                ColorHex = NocoClient.ReadString(node, "Color") ?? TaskVisual.DefaultColor,
                BlockPattern = BlockPatterns.Normalize(NocoClient.ReadString(node, "BlockPattern")),
                BlockPatternColor = string.IsNullOrWhiteSpace(NocoClient.ReadString(node, "BlockPatternColor"))
                    ? BlockPatterns.DefaultPatternColor
                    : NocoClient.ReadString(node, "BlockPatternColor")!,
                BlockStyleJson = NocoClient.ReadString(node, "BlockStyleJson"),
                Archived = NocoClient.ReadBool(node, "Archived"),
                RewardMinutes = Math.Clamp(NocoClient.ReadInt(node, "RewardMinutes", 30), 1, 1440),
                AllowOverflow = NocoClient.ReadBool(node, "AllowOverflow"),
                OverflowSeconds = Math.Max(0, NocoClient.ReadDouble(node, "OverflowSeconds")),
                OriginalField = NocoClient.FileField(node, "Original"),
                CropJson = NocoClient.ReadString(node, "CropJson"),
                IsDirectProductivity = NocoClient.ReadBool(node, "IsDirectProductivity")
            };
            var dates = completions
                .Where(c => RewardLogic.LinkedId(c, "Task") == row.Id)
                .Select(c => RewardLogic.ParseDate(c, "CompletedOn"))
                .Where(d => d.HasValue)
                .Select(d => d!.Value.Date)
                .ToList();
            row.DoneToday = dates.Contains(today);
            if (row.Type == "daily")
                row.PhaseLabel = RewardLogic.PhaseLabel(RewardLogic.Phase(dates));
            if (IsRunActive)
            {
                if (row.Id == _runningTaskId)
                    row.Running = true;
            }
            else
            {
                var open = sessions.FirstOrDefault(s =>
                    RewardLogic.LinkedId(s, "Task") == row.Id && RewardLogic.ParseDate(s, "EndedAt") is null);
                if (open is not null)
                {
                    row.Running = true;
                    _runningTaskId = row.Id;
                    _runningSessionId = NocoClient.ReadId(open);
                }
            }
            var thumb = NocoClient.FileField(node, "Thumb");
            row.Preview = await LoadPreviewAsync(thumb);
            _tasks.Add(row);
        }

        if (_selectedTaskId is not null && _tasks.All(t => t.Id != _selectedTaskId))
            _selectedTaskId = null;
        RenderTaskCards();
    }

    private async Task<BitmapImage?> LoadPreviewAsync(JsonNode? thumbField)
    {
        var url = NocoClient.FirstFileUrl(thumbField);
        if (url is null) return null;
        try
        {
            var bytes = await _host.Session.Business.DownloadBytesAsync(url);
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

    private async Task LoadWeekAsync()
    {
        var start = StartOfWeek(DateTime.Today);
        var sessions = await _host.Session.Business.ListRecordsAsync(StoreTables.Sessions);
        var byId = _tasks.ToDictionary(t => t.Id);
        var spans = new List<WeekSpan>();
        foreach (var s in sessions)
        {
            var st = RewardLogic.ParseDate(s, "StartedAt");
            var en = RewardLogic.ParseDate(s, "EndedAt");
            if (st is null || en is null) continue;
            var sid = NocoClient.ReadId(s);
            if (string.IsNullOrEmpty(sid)) continue;
            var id = RewardLogic.LinkedId(s, "Task");
            byId.TryGetValue(id ?? "", out var task);
            if (task is { Archived: true }) continue;
            spans.Add(new WeekSpan
            {
                SessionId = sid,
                Task = task,
                Start = st.Value,
                End = en.Value,
                Outcome = NocoClient.ReadString(s, "Outcome") ?? "",
                Pauses = SessionLogic.ParsePauses(NocoClient.ReadString(s, "PauseJson"))
            });
        }

        var notes = new List<ScheduleNoteRow>();
        var noteRows = await _host.Session.Business.ListRecordsAsync(StoreTables.ScheduleNotes);
        foreach (var n in noteRows)
        {
            var nid = NocoClient.ReadId(n);
            var at = RewardLogic.ParseDate(n, "At");
            if (string.IsNullOrEmpty(nid) || at is null) continue;
            notes.Add(new ScheduleNoteRow
            {
                Id = nid,
                At = at.Value,
                DayColumnPercent = Math.Clamp(NocoClient.ReadDouble(n, "DayColumnPercent"), 0, 1),
                Body = NocoClient.ReadString(n, "Body") ?? ""
            });
        }

        _weekBoard.SetData(start, spans, notes);
        _weekBoard.Render();
        await RefreshStatsAsync();
    }

    private void GoToNow_Click(object sender, RoutedEventArgs e) => _weekBoard.GoToNow();

    private void WeekScroll_ScrollChanged(object sender, ScrollChangedEventArgs e) =>
        _weekBoard.OnScrollChanged(e);

    private void WeekScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        _weekBoard.OnPreviewMouseWheel();

    private static string NoteTitleFromBody(string body)
    {
        var t = body.Replace("\r", " ").Replace("\n", " ").Trim();
        if (t.Length == 0) return "笔记钉";
        return t.Length <= 40 ? t : t[..40] + "…";
    }

    private async Task AddNotePinAtPendingAsync(DateTime at, double dayColumnPercent)
    {
        var dlg = new NotePinEditWindow(at, dayColumnPercent) { Owner = _host.OwnerWindow };
        if (dlg.ShowDialog() != true || dlg.DeleteRequested) return;
        try
        {
            await _host.Session.Business.CreateRecordAsync(StoreTables.ScheduleNotes, new Dictionary<string, object?>
            {
                ["Title"] = NoteTitleFromBody(dlg.Body),
                ["At"] = RewardLogic.FormatDateTime(at),
                ["DayColumnPercent"] = dayColumnPercent,
                ["Body"] = dlg.Body,
                ["RecordedAt"] = RewardLogic.FormatDateTime(DateTime.Now)
            });
            await LoadWeekAsync();
            _host.StatusText = "已添加笔记钉";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "添加笔记钉失败");
        }
    }

    private async Task EditNotePinAsync(ScheduleNoteRow note)
    {
        var dlg = new NotePinEditWindow(note.At, note.DayColumnPercent, note.Body, isEdit: true)
        {
            Owner = _host.OwnerWindow
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            if (dlg.DeleteRequested)
            {
                await _host.Session.Business.DeleteRecordAsync(StoreTables.ScheduleNotes, note.Id);
                await LoadWeekAsync();
                _host.StatusText = "已删除笔记钉";
                return;
            }
            await _host.Session.Business.PatchRecordAsync(StoreTables.ScheduleNotes, new Dictionary<string, object?>
            {
                ["Id"] = note.Id,
                ["Title"] = NoteTitleFromBody(dlg.Body),
                ["Body"] = dlg.Body
            });
            await LoadWeekAsync();
            _host.StatusText = "已更新笔记钉";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "笔记钉保存失败");
        }
    }

    private async Task MarkSelectionAsTaskAsync(TaskRow task)
    {
        if (!_weekBoard.CanMarkSelection() || _weekBoard.MarkRangeStart is null || _weekBoard.MarkRangeEnd is null)
        {
            MessageBox.Show("所选时段无效：不可覆盖已有记录，也不可超过当前时间。");
            return;
        }
        try
        {
            var start = _weekBoard.MarkRangeStart.Value;
            var end = _weekBoard.MarkRangeEnd.Value;
            await _host.Session.Business.CreateRecordAsync(StoreTables.Sessions, new Dictionary<string, object?>
            {
                ["Title"] = task.Title,
                ["StartedAt"] = RewardLogic.FormatDateTime(start),
                ["EndedAt"] = RewardLogic.FormatDateTime(end),
                ["Outcome"] = "success",
                ["PausedSeconds"] = 0,
                ["PauseJson"] = "[]",
                ["Task"] = task.Id
            });
            _weekBoard.ClearMarkSelection(rerender: false);
            await LoadWeekAsync();
            _host.StatusText = $"已标记「{task.Title}」{start:HH:mm}–{end:HH:mm}（仅记录，未发奖）";
            MaybeWarnHiddenHours(task.Title, start, end);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "标记失败");
        }
    }

    private async void DeleteWeekSpan_Click(object sender, RoutedEventArgs e)
    {
        if (_weekBoard.SelectedSessionId is null)
        {
            MessageBox.Show("请先点选一条日程记录。");
            return;
        }
        var span = _weekBoard.FindSpan(_weekBoard.SelectedSessionId);
        if (span is null)
        {
            MessageBox.Show("找不到选中的记录。");
            return;
        }
        var ok = MessageBox.Show(
            $"确认删除这条执行记录？\n\n{span.Describe}\n\n（不会撤销当时的抽奖次数、打卡或溢出。）",
            "删除日程",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;
        try
        {
            await _host.Session.Business.DeleteRecordAsync(StoreTables.Sessions, span.SessionId);
            _weekBoard.SelectedSessionId = null;
            await LoadWeekAsync();
            _host.StatusText = "已删除执行记录";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "删除失败");
        }
    }

    private static DateTime StartOfWeek(DateTime day)
    {
        var diff = (7 + (day.DayOfWeek - DayOfWeek.Monday)) % 7;
        return day.Date.AddDays(-diff);
    }

    private void TaskArchived_Changed(object sender, RoutedEventArgs e)
    {
        if (TaskArchivedBox is null || TaskWrap is null) return;
        _showArchivedTasks = TaskArchivedBox.IsChecked == true;
        RenderTaskCards();
    }

    private void RenderTaskCards()
    {
        TaskWrap.Children.Clear();
        var visible = _tasks.Where(t => _showArchivedTasks || !t.Archived).ToList();
        if (visible.Count == 0)
        {
            var hint = _tasks.Count == 0
                ? "还没有任务。点「新建任务」，或把图片拖进来。"
                : "没有可显示的任务。勾选「显示已归档内容」可查看已归档项。";
            TaskWrap.Children.Add(ThumbCard.Hint(hint));
            return;
        }
        foreach (var task in visible)
        {
            var captured = task;
            TaskWrap.Children.Add(TaskCard.Build(captured, new TaskCard.Handlers
            {
                Selected = captured.Id == _selectedTaskId,
                OnSelect = () =>
                {
                    _selectedTaskId = captured.Id;
                    RenderTaskCards();
                },
                OnEdit = () => EditTaskAsync(captured),
                OnStart = async () =>
                {
                    _selectedTaskId = captured.Id;
                    RenderTaskCards();
                    await StartOrFocusRunAsync(captured);
                },
                OnRecord = async () =>
                {
                    _selectedTaskId = captured.Id;
                    RenderTaskCards();
                    await RecordTaskAsync(captured);
                }
            }));
        }
    }

    private void TaskDock_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = FavoriteService.DataHasImage(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void TaskDock_Drop(object sender, DragEventArgs e)
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
        await PromptNewTaskAsync(files[0]);
    }

    private async Task ImportTaskFromClipboardAsync()
    {
        if (_taskBusy) return;
        _taskBusy = true;
        try
        {
            var files = FavoriteService.ImageFilesFromClipboard().ToList();
            if (files.Count == 0)
            {
                var saved = FavoriteService.SaveImageFromClipboard();
                if (saved is not null) files.Add(saved);
            }
            if (files.Count == 0)
            {
                _host.StatusText = "剪贴板里没有图片。";
                return;
            }
            await PromptNewTaskAsync(files[0]);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "粘贴失败");
        }
        finally
        {
            _taskBusy = false;
        }
    }

    private async void NewTask_Click(object sender, RoutedEventArgs e) =>
        await PromptNewTaskAsync();

    private void OpenRewardWish_Click(object sender, RoutedEventArgs e)
    {
        if (_rewardWishWindow is not null)
        {
            _rewardWishWindow.Activate();
            return;
        }
        _rewardWishWindow = new RewardWishWindow(_host.Session) { Owner = _host.OwnerWindow };
        _rewardWishWindow.WalletChanged += () => Dispatcher.InvokeAsync(async () => await LoadWalletAsync());
        _rewardWishWindow.Closed += (_, _) => _rewardWishWindow = null;
        _rewardWishWindow.Show();
    }

    private async Task EditTaskAsync(TaskRow task)
    {
        var dlg = new NewTaskWindow { Owner = _host.OwnerWindow };
        dlg.PrefillEdit(task);
        var origPath = await DownloadAttachmentTempAsync(_host.Session.Business, task.OriginalField, "pm-task-orig-");
        dlg.PrefillCropState(origPath, task.CropJson);
        if (dlg.ShowDialog() != true) return;
        try
        {
            var fields = new Dictionary<string, object?>
            {
                ["Id"] = task.Id,
                ["Title"] = dlg.TaskTitle,
                ["Type"] = dlg.TaskType,
                ["RewardLevel"] = dlg.TaskType == "daily" ? 1 : dlg.RewardLevel,
                ["Color"] = dlg.ColorHex,
                ["BlockPattern"] = dlg.BlockPattern,
                ["BlockPatternColor"] = dlg.BlockPatternColor,
                ["BlockStyleJson"] = dlg.BlockStyleJson,
                ["RewardMinutes"] = dlg.RewardMinutes,
                ["AllowOverflow"] = dlg.AllowOverflow,
                ["OverflowSeconds"] = dlg.OverflowSeconds,
                ["Archived"] = dlg.Archived,
                ["IsDirectProductivity"] = dlg.IsDirectProductivity
            };
            if (dlg.ClearThumb)
            {
                fields["Thumb"] = null;
                fields["Original"] = null;
                fields["CropJson"] = null;
            }
            else
            {
                if (dlg.ThumbPath is not null)
                    fields["Thumb"] = await _host.Session.Business.UploadAsync(System.IO.Path.GetFileName(dlg.ThumbPath), await File.ReadAllBytesAsync(dlg.ThumbPath), MimeOf(dlg.ThumbPath));
                if (dlg.OriginalPath is not null)
                    fields["Original"] = await _host.Session.Business.UploadAsync(System.IO.Path.GetFileName(dlg.OriginalPath), await File.ReadAllBytesAsync(dlg.OriginalPath), MimeOf(dlg.OriginalPath));
                if (dlg.CropJson is not null)
                    fields["CropJson"] = dlg.CropJson;
            }
            fields["DueAt"] = dlg.DueAt is null ? null : RewardLogic.FormatDateTime(dlg.DueAt.Value);
            fields["ReminderAt"] = dlg.ReminderAt is null ? null : RewardLogic.FormatDateTime(dlg.ReminderAt.Value);
            await _host.Session.Business.PatchRecordAsync(StoreTables.Tasks, fields);
            await LoadTasksAsync();
            _host.StatusText = "已保存任务「" + dlg.TaskTitle + "」";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "保存失败");
        }
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

    private async Task StartOrFocusRunAsync(TaskRow? task)
    {
        if (IsRunActive)
        {
            ShowRunWindow();
            _host.StatusText = "已有任务在执行";
            return;
        }
        if (task is null)
        {
            MessageBox.Show("请先选一个任务。");
            return;
        }
        try
        {
            JsonNode? open = null;
            string? sessionId = _runningSessionId;
            DateTime startedAt = DateTime.Now;
            List<PauseSpan> pauses = [];
            if (sessionId is not null && _runningTaskId == task.Id)
            {
                var sessions = await _host.Session.Business.ListRecordsAsync(StoreTables.Sessions);
                open = sessions.FirstOrDefault(s => NocoClient.ReadId(s) == sessionId);
            }
            if (open is null)
            {
                var created = await _host.Session.Business.CreateRecordAsync(StoreTables.Sessions, new Dictionary<string, object?>
                {
                    ["Title"] = task.Title,
                    ["StartedAt"] = RewardLogic.FormatDateTime(startedAt),
                    ["Task"] = task.Id
                });
                sessionId = NocoClient.ReadId(created);
                MaybeWarnHiddenHours(task.Title, startedAt, endedAt: null);
            }
            else
            {
                startedAt = RewardLogic.ParseDate(open, "StartedAt") ?? DateTime.Now;
                pauses = SessionLogic.ParsePauses(NocoClient.ReadString(open, "PauseJson"));
            }
            if (sessionId is null) throw new InvalidOperationException("没有 session id");
            _runningSessionId = sessionId;
            _runningTaskId = task.Id;
            _run = new TaskRunState(task.Id, sessionId, task.Title, startedAt, task.RewardMinutes, pauses);
            _runTimer.Start();
            UpdateCurrentRunChrome();
            ShowRunWindow();
            await LoadTasksAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "执行记录失败");
        }
    }

    private void CurrentRunButton_Click(object sender, RoutedEventArgs e) => ShowRunWindow();

    private void ShowRunWindow()
    {
        if (_run is null || _run.Finished) return;
        var owner = _host.OwnerWindow;
        if (owner.WindowState == WindowState.Minimized)
            owner.WindowState = WindowState.Normal;
        if (!owner.IsVisible)
            owner.Show();
        owner.Activate();
        if (_runWindow is null)
        {
            _runWindow = new TaskRunWindow(_run);
            _runWindow.FinishedRequested += FinishRunAsync;
            _runWindow.RunStateChanged += UpdateCurrentRunChrome;
            _runWindow.Closed += (_, _) => _runWindow = null;
            _runWindow.Show();
        }
        else
        {
            _runWindow.Show();
            _runWindow.Activate();
            _runWindow.Refresh();
        }
    }

    private void OnRunTick()
    {
        if (_run is null || _run.Finished)
        {
            _runTimer.Stop();
            return;
        }
        UpdateCurrentRunChrome();
        if (_runWindow is { IsVisible: true })
            _runWindow.Refresh();

        if (!_run.NotifiedRequired && _run.ActiveSeconds >= _run.RequiredSeconds)
        {
            _run.NotifiedRequired = true;
            ReminderWindow.ShowFor(_run.TaskTitle,
                $"已满 {_run.RewardMinutes} 分钟。可以继续做，或点退出结束并结算。",
                extraButton: "退出",
                extraClick: () => _ = FinishRunAsync(true));
        }
    }

    private void UpdateCurrentRunChrome()
    {
        if (CurrentRunButton is null) return;
        if (_run is null || _run.Finished)
        {
            CurrentRunButton.Visibility = Visibility.Collapsed;
            return;
        }
        CurrentRunButton.Visibility = Visibility.Visible;
        var paused = _run.IsPaused ? "（已暂停）" : "";
        CurrentRunText.Text = $"当前执行：{_run.TaskTitle}{paused}";
        var (lap, frac) = _run.Progress();
        CurrentRunBar.Maximum = _run.RequiredSeconds;
        CurrentRunBar.Value = frac;
        CurrentRunBar.Foreground = TaskRunState.BarBrush(lap);
    }

    private async Task FinishRunAsync(bool success)
    {
        if (_run is null || _run.Finished) return;
        TaskRunFinish finish;
        try
        {
            finish = _run.Finish(success);
        }
        catch
        {
            return;
        }
        _runTimer.Stop();
        if (_runWindow is not null)
        {
            _runWindow.CloseForReal();
            _runWindow = null;
        }
        _run = null;
        _runningSessionId = null;
        _runningTaskId = null;
        UpdateCurrentRunChrome();
        await SettleRunFinishAsync(finish);
    }

    private async Task SettleRunFinishAsync(TaskRunFinish finish)
    {
        try
        {
            await _host.Session.Business.PatchRecordAsync(StoreTables.Sessions, new Dictionary<string, object?>
            {
                ["Id"] = finish.SessionId,
                ["EndedAt"] = RewardLogic.FormatDateTime(finish.EndedAt),
                ["Outcome"] = finish.Success ? "success" : "failed",
                ["PausedSeconds"] = (int)Math.Round(finish.PausedSeconds),
                ["PauseJson"] = SessionLogic.SerializePauses(finish.Pauses)
            });

            if (finish.Success)
            {
                var active = SessionLogic.ActiveSeconds(finish.StartedAt, finish.EndedAt, finish.PausedSeconds);
                await SettleSuccessAsync(finish.TaskId, finish.TaskTitle, active);
            }
            else
                _host.StatusText = $"「{finish.TaskTitle}」记为失败";

            await LoadTasksAsync();
            await LoadWeekAsync();
            MaybeWarnHiddenHours(finish.TaskTitle, finish.StartedAt, finish.EndedAt);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "结算失败");
        }
    }

    private async Task RecordTaskAsync(TaskRow task)
    {
        if (IsRunActive)
        {
            ShowRunWindow();
            _host.StatusText = "已有任务在执行";
            return;
        }
        if (_taskBusy) return;
        _taskBusy = true;
        try
        {
            var sessions = await _host.Session.Business.ListRecordsAsync(StoreTables.Sessions);
            DateTime? lastEnded = null;
            foreach (var s in sessions)
            {
                var ended = RewardLogic.ParseDate(s, "EndedAt");
                if (ended is null) continue;
                if (lastEnded is null || ended > lastEnded) lastEnded = ended;
            }
            if (lastEnded is null)
            {
                MessageBox.Show("还没有上一次任务结束时间");
                return;
            }
            var now = DateTime.Now;
            var pick = new RecordDurationWindow(lastEnded.Value, now) { Owner = _host.OwnerWindow };
            if (pick.ShowDialog() != true) return;
            DateTime endedAt;
            if (pick.Choice == RecordDurationWindow.ChoiceKind.UntilNow)
                endedAt = now;
            else
                endedAt = lastEnded.Value.AddMinutes(pick.Minutes);
            if (endedAt > now)
            {
                MessageBox.Show("结束时间不能超过现在。");
                return;
            }
            await _host.Session.Business.CreateRecordAsync(StoreTables.Sessions, new Dictionary<string, object?>
            {
                ["Title"] = task.Title,
                ["StartedAt"] = RewardLogic.FormatDateTime(lastEnded.Value),
                ["EndedAt"] = RewardLogic.FormatDateTime(endedAt),
                ["Outcome"] = "success",
                ["PausedSeconds"] = 0,
                ["PauseJson"] = SessionLogic.SerializePauses([]),
                ["Task"] = task.Id
            });
            var active = SessionLogic.ActiveSeconds(lastEnded.Value, endedAt, 0);
            await SettleSuccessAsync(task.Id, task.Title, active);
            await LoadTasksAsync();
            await LoadWeekAsync();
            MaybeWarnHiddenHours(task.Title, lastEnded.Value, endedAt);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "记录失败");
        }
        finally
        {
            _taskBusy = false;
        }
    }

    private bool SessionTouchesHiddenHours(DateTime startedAt, DateTime? endedAt)
    {
        var (s, e) = WeekBoard.VisibleWeekHours;
        if (s == 0 && e == 24) return false;
        var sh = startedAt.TimeOfDay.TotalHours;
        if (sh < s || sh >= e) return true;
        if (endedAt is null) return false;
        var eh = endedAt.Value.TimeOfDay.TotalHours;
        return eh < s || eh > e;
    }

    private void MaybeWarnHiddenHours(string title, DateTime startedAt, DateTime? endedAt)
    {
        if (WindowBounds.SuppressHiddenHourTip) return;
        if (!SessionTouchesHiddenHours(startedAt, endedAt)) return;
        ShowHiddenHourTipDialog(title);
    }

    private void ShowHiddenHourTipDialog(string blockTitle)
    {
        var name = string.IsNullOrWhiteSpace(blockTitle) ? "未命名" : blockTitle.Trim();
        var win = new Window
        {
            Title = "未显示时间",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = _host.OwnerWindow,
            ResizeMode = ResizeMode.NoResize
        };
        Theme.Tint(win);
        var noMore = new CheckBox
        {
            Content = "不再提示",
            Margin = new Thickness(0, 12, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var ok = new Button
        {
            Content = "确定",
            Width = 88,
            Height = 32,
            IsDefault = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        ok.Click += (_, _) =>
        {
            if (noMore.IsChecked == true)
                WindowBounds.SetSuppressHiddenHourTip(true);
            win.DialogResult = true;
            win.Close();
        };
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = $"日程块【{name}】记录在了未显示时间，您可以在【界面】中调整未显示时间以适配您的作息",
            TextWrapping = TextWrapping.Wrap,
            FontSize = Theme.Current.FontSizeBody
        });
        root.Children.Add(noMore);
        root.Children.Add(ok);
        win.Content = root;
        win.ShowDialog();
    }

    private async Task SettleSuccessAsync(string taskId, string title, double activeSeconds)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == taskId);
        var minutes = task?.RewardMinutes ?? 30;
        var allow = task?.AllowOverflow ?? false;
        var oldOverflow = task?.OverflowSeconds ?? 0;
        var settle = SessionLogic.ComputeOverflow(activeSeconds, minutes, allow, oldOverflow);
        if (!string.IsNullOrEmpty(taskId))
        {
            await _host.Session.Business.PatchRecordAsync(StoreTables.Tasks, new Dictionary<string, object?>
            {
                ["Id"] = taskId,
                ["OverflowSeconds"] = settle.NewOverflowSeconds
            });
            if (task is not null) task.OverflowSeconds = settle.NewOverflowSeconds;
        }

        if (settle.Total < 1)
        {
            _host.StatusText = allow && settle.NewOverflowSeconds > 0
                ? $"已结束「{title}」（未满 {minutes} 分钟，余数已计入溢出）"
                : $"已结束「{title}」（未满 {minutes} 分钟，不发奖）";
            return;
        }

        var type = task?.Type ?? "flexible";
        var level = task?.RewardLevel ?? 1;
        var dates = (await _host.Session.Business.ListRecordsAsync(StoreTables.Completions))
            .Where(c => RewardLogic.LinkedId(c, "Task") == taskId)
            .Select(c => RewardLogic.ParseDate(c, "CompletedOn"))
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToList();
        var today = DateTime.Today;
        if (!dates.Any(d => d.Date == today))
        {
            await _host.Session.Business.CreateRecordAsync(StoreTables.Completions, new Dictionary<string, object?>
            {
                ["Title"] = title + " " + today.ToString("yyyy-MM-dd"),
                ["CompletedOn"] = today.ToString("yyyy-MM-dd"),
                ["Task"] = taskId
            });
            dates.Add(today);
        }
        var phase = RewardLogic.Phase(dates);
        var ticketsGain = 0;
        for (var i = 0; i < settle.Total; i++)
            ticketsGain += RewardLogic.TicketsForCompletion(type, level, phase, _rng);
        if (ticketsGain > 0)
        {
            var (tickets, quota) = await ReadWalletAsync();
            await WriteWalletAsync(tickets + ticketsGain, quota);
        }
        var extra = type == "daily" && ticketsGain >= 3 ? "（含稀有 L3）" : "";
        _host.StatusText = ticketsGain > 0
            ? $"完成「{title}」，获得 {ticketsGain} 张奖券{extra}"
            : $"完成「{title}」，本次没有奖券";
    }

    public async Task AbandonRunningAsync()
    {
        if (IsRunActive)
        {
            await FinishRunAsync(false);
            return;
        }
        if (_runningSessionId is null) return;
        try
        {
            await _host.Session.Business.PatchRecordAsync(StoreTables.Sessions, new Dictionary<string, object?>
            {
                ["Id"] = _runningSessionId,
                ["EndedAt"] = RewardLogic.FormatDateTime(DateTime.Now),
                ["Outcome"] = "failed"
            });
        }
        catch { /* shutting down */ }
        _runningSessionId = null;
        _runningTaskId = null;
    }

    private async Task CheckRemindersAsync()
    {
        var now = DateTime.Now;
        foreach (var task in _tasks)
        {
            if (task.Archived) continue;
            if (task.ReminderAt is null) continue;
            var due = task.ReminderAt.Value;
            var key = task.Id + due.ToString("yyyyMMddHHmm");
            if (_reminded.Contains(key)) continue;
            if (Math.Abs((now - due).TotalMinutes) > 1) continue;
            _reminded.Add(key);
            ReminderWindow.ShowFor(task.Title, $"提醒时间到了（{due:HH:mm}）");
        }
        await Task.CompletedTask;
    }

    private void TaskRailSplitter_MouseUp(object sender, MouseButtonEventArgs e)
    {
        // 松手后对齐列数；延迟一帧等列宽落定
        Dispatcher.BeginInvoke(SnapTaskRailToColumns, DispatcherPriority.Background);
    }

    private void SnapTaskRailToColumns()
    {
        if (TaskRailCol is null) return;
        var cardW = Theme.Current.ScheduleCardWidth;
        var unit = cardW + 12; // Margin 6*2
        var chrome = TaskRailChromeWidth();
        var avail = Math.Max(unit, TaskRailCol.ActualWidth - chrome);
        var cols = Math.Max(1, (int)Math.Round(avail / unit));
        WindowBounds.SetTaskRailColumns(cols);
        ApplyTaskRailWidth();
        RenderTaskCards();
    }

    private void ApplyTaskRailWidth()
    {
        if (TaskRailCol is null) return;
        TaskRailCol.Width = new GridLength(
            SnapRailWidth(Theme.Current.ScheduleCardWidth, WindowBounds.TaskRailColumns));
    }

    private void ApplyStatsLayout(bool fromSaved)
    {
        if (StatsRow is null || StatsBody is null || StatsExpandGlyph is null) return;
        _ = fromSaved;
        _statsRangeDays = WindowBounds.ScheduleStatsDays;
        HighlightStatsDayButtons();
        SetStatsExpanded(WindowBounds.ScheduleStatsExpanded, persist: false);
    }

    private void SetStatsExpanded(bool expanded, bool persist)
    {
        if (StatsRow is null || StatsBody is null || StatsExpandGlyph is null) return;
        StatsBody.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        StatsExpandGlyph.Text = expanded ? "▾" : "▸";
        if (expanded)
            StatsRow.Height = new GridLength(WindowBounds.ScheduleStatsHeight);
        else
            StatsRow.Height = new GridLength(28);
        if (persist)
            WindowBounds.SetScheduleStatsExpanded(expanded);
        if (expanded)
            RenderStatsChart();
    }

    private void StatsHeader_Click(object sender, MouseButtonEventArgs e)
    {
        // 点在近 N 天按钮上不切换展开
        if (e.OriginalSource is DependencyObject d)
        {
            var btn = FindAncestor<Button>(d);
            if (btn is not null && btn.Tag is string)
                return;
        }
        var expanded = StatsBody?.Visibility != Visibility.Visible;
        SetStatsExpanded(expanded, persist: true);
    }

    private void StatsSplitter_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (StatsBody?.Visibility != Visibility.Visible || StatsRow is null) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (StatsRow.ActualHeight >= 80)
                WindowBounds.SetScheduleStatsHeight(StatsRow.ActualHeight);
        }, DispatcherPriority.Background);
    }

    private void StatsDays_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out var days)) return;
        _statsRangeDays = days is 7 or 30 ? days : 14;
        WindowBounds.SetScheduleStatsDays(_statsRangeDays);
        HighlightStatsDayButtons();
        _ = RefreshStatsAsync();
    }

    private void HighlightStatsDayButtons()
    {
        void StyleBtn(Button? b, int d)
        {
            if (b is null) return;
            b.FontWeight = _statsRangeDays == d ? FontWeights.Bold : FontWeights.Normal;
        }
        StyleBtn(StatsDays7, 7);
        StyleBtn(StatsDays14, 14);
        StyleBtn(StatsDays30, 30);
    }

    private void StatsChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) =>
        RenderStatsChart();

    private async Task RefreshStatsAsync()
    {
        _statsDirectHours.Clear();
        _statsOtherHours.Clear();
        if (!_host.Session.BusinessReady)
        {
            RenderStatsChart();
            return;
        }

        try
        {
            var days = _statsRangeDays;
            var to = DateTime.Today;
            var from = to.AddDays(1 - days);
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                _statsDirectHours[d] = 0;
                _statsOtherHours[d] = 0;
            }

            var byId = _tasks.ToDictionary(t => t.Id);
            var sessions = await _host.Session.Business.ListRecordsAsync(StoreTables.Sessions);
            foreach (var s in sessions)
            {
                var st = RewardLogic.ParseDate(s, "StartedAt");
                var en = RewardLogic.ParseDate(s, "EndedAt");
                if (st is null || en is null || en <= st) continue;
                if (en.Value.Date < from || st.Value.Date > to) continue;

                var paused = NocoClient.ReadDouble(s, "PausedSeconds");
                var active = SessionLogic.ActiveSeconds(st.Value, en.Value, paused);
                if (active <= 0) continue;

                var tid = RewardLogic.LinkedId(s, "Task");
                byId.TryGetValue(tid ?? "", out var task);
                var direct = task is { IsDirectProductivity: true };
                var wall = (en.Value - st.Value).TotalSeconds;
                if (wall <= 0) continue;

                for (var day = st.Value.Date; day <= en.Value.Date; day = day.AddDays(1))
                {
                    if (day < from || day > to) continue;
                    var dayStart = day;
                    var dayEnd = day.AddDays(1);
                    var o0 = st.Value > dayStart ? st.Value : dayStart;
                    var o1 = en.Value < dayEnd ? en.Value : dayEnd;
                    if (o1 <= o0) continue;
                    var share = (o1 - o0).TotalSeconds / wall;
                    var hours = active * share / 3600.0;
                    if (direct) _statsDirectHours[day] = _statsDirectHours.GetValueOrDefault(day) + hours;
                    else _statsOtherHours[day] = _statsOtherHours.GetValueOrDefault(day) + hours;
                }
            }
        }
        catch
        {
            /* keep empty */
        }

        RenderStatsChart();
    }

    private void RenderStatsChart() =>
        ScheduleStatsChart.Render(
            StatsChartCanvas,
            StatsBody?.Visibility == Visibility.Visible,
            _statsDirectHours,
            _statsOtherHours);

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        while (start is not null)
        {
            if (start is T match) return match;
            start = VisualTreeHelper.GetParent(start);
        }
        return null;
    }

    /// <summary>
    /// WrapPanel 左右 Margin(4) + 始终预留竖向滚动条宽。
    /// Auto 滚动条出现时会挤占内容区；预留后有/无滚动条都能摆满整数列，避免裁切与错位。
    /// </summary>
    private static double TaskRailChromeWidth() =>
        8 + SystemParameters.VerticalScrollBarWidth;

    private static double SnapRailWidth(double cardWidth, int preferCols)
    {
        var unit = cardWidth + 12;
        return Math.Max(120, preferCols * unit + TaskRailChromeWidth());
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
        WeekOfflineOverlay.SetActive(WeekContent, !_host.Session.BusinessReady);
        ApplyTaskRailWidth();
    }

    public async Task ReloadAsync()
    {
        await LoadWalletAsync();
        await LoadTasksAsync();
        await LoadWeekAsync();
    }

    public void ClearBusinessUi()
    {
        if (ScheduleWalletText is not null)
            ScheduleWalletText.Text = "当前奖券数量：— · 愿望单额度：—";
        _tasks.Clear();
        RenderTaskCards();
        _weekBoard.ClearData();
        _weekBoard.Render();
        _statsDirectHours.Clear();
        _statsOtherHours.Clear();
        RenderStatsChart();
    }

    public async Task ReloadRewardWishIfOpenAsync()
    {
        if (_rewardWishWindow is not null)
            await _rewardWishWindow.ReloadAsync();
    }

    public void OnHostThemeChanged()
    {
        ApplyTaskRailWidth();
        RenderTaskCards();
        if (_weekBoard.Spans.Count > 0 || WeekHost.Child is not null)
            _weekBoard.Render();
        RenderStatsChart();
    }

    public void ToggleFocusFromHost() => _weekBoard.ToggleFocusMode();

    public Task ImportClipboardFromHostAsync() => ImportTaskFromClipboardAsync();

    private async void TryConnectNoco_Click(object sender, RoutedEventArgs e) =>
        await _host.TryConnectNocoAsync();

    private async Task PromptNewTaskAsync(string? thumbPath = null)
    {
        var dlg = new NewTaskWindow { Owner = _host.OwnerWindow };
        if (thumbPath is not null)
        {
            var original = ThumbCropWindow.PersistOriginalCopy(thumbPath);
            var crop = ThumbCropWindow.AskFull(_host.OwnerWindow, original, "task");
            if (crop is not null)
                dlg.PrefillFromCrop(crop, System.IO.Path.GetFileNameWithoutExtension(thumbPath));
            else
                dlg.PrefillTitle(System.IO.Path.GetFileNameWithoutExtension(thumbPath));
        }
        if (dlg.ShowDialog() != true) return;
        try
        {
            var fields = new Dictionary<string, object?>
            {
                ["Title"] = dlg.TaskTitle,
                ["Type"] = dlg.TaskType,
                ["RewardLevel"] = dlg.TaskType == "daily" ? 1 : dlg.RewardLevel,
                ["RegisteredAt"] = RewardLogic.FormatDateTime(DateTime.Now),
                ["Color"] = dlg.ColorHex,
                ["BlockPattern"] = dlg.BlockPattern,
                ["BlockPatternColor"] = dlg.BlockPatternColor,
                ["BlockStyleJson"] = dlg.BlockStyleJson,
                ["RewardMinutes"] = dlg.RewardMinutes,
                ["AllowOverflow"] = dlg.AllowOverflow,
                ["OverflowSeconds"] = dlg.OverflowSeconds,
                ["Archived"] = dlg.Archived,
                ["IsDirectProductivity"] = dlg.IsDirectProductivity
            };
            if (dlg.ThumbPath is not null)
                fields["Thumb"] = await _host.Session.Business.UploadAsync(System.IO.Path.GetFileName(dlg.ThumbPath), await File.ReadAllBytesAsync(dlg.ThumbPath), MimeOf(dlg.ThumbPath));
            if (dlg.OriginalPath is not null)
                fields["Original"] = await _host.Session.Business.UploadAsync(System.IO.Path.GetFileName(dlg.OriginalPath), await File.ReadAllBytesAsync(dlg.OriginalPath), MimeOf(dlg.OriginalPath));
            if (dlg.CropJson is not null)
                fields["CropJson"] = dlg.CropJson;
            if (dlg.DueAt is not null)
                fields["DueAt"] = RewardLogic.FormatDateTime(dlg.DueAt.Value);
            if (dlg.ReminderAt is not null)
                fields["ReminderAt"] = RewardLogic.FormatDateTime(dlg.ReminderAt.Value);
            await _host.Session.Business.CreateRecordAsync(StoreTables.Tasks, fields);
            await LoadTasksAsync();
            _host.StatusText = "已添加任务「" + dlg.TaskTitle + "」";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "新建失败");
        }
    }
}
