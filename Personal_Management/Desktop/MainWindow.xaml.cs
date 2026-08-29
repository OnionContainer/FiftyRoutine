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

public partial class MainWindow : Window
{
    private readonly AppSession _session;
    private readonly ObservableCollection<TaskRow> _tasks = [];
    private readonly DispatcherTimer _reminderTimer;
    private readonly HashSet<string> _reminded = [];
    private readonly Random _rng = new();
    private readonly List<FavoriteItem> _favs = [];
    private bool _privateUnlocked;
    private bool _favBusy;
    private bool _taskBusy;
    private bool _themeBoxSilent;
    private bool _settingsBuilding;
    private bool _showArchivedTasks;
    private string? _runningSessionId;
    private string? _runningTaskId;
    private string? _selectedTaskId;
    private string? _weightProfileId;
    private readonly List<(DateTime Date, double Kg)> _weightEntries = [];
    private bool _weightRangeReady;
    private RewardWishWindow? _rewardWishWindow;
    private TaskRunState? _run;
    private TaskRunWindow? _runWindow;
    private readonly DispatcherTimer _runTimer;
    private DateTime _weekStart;
    private string? _selectedSessionId;
    private List<WeekSpan> _weekSpans = [];
    private readonly DispatcherTimer _weekNowTimer;
    private bool _weekFollowNow;
    private bool _weekFocusMode;
    private bool _weekIgnoreScroll;
    private readonly List<(DateTime Day, Line Line)> _weekNowLines = [];
    private readonly List<(DateTime Day, Ellipse Dot)> _weekTodayDots = [];
    private DateTime? _markRangeStart;
    private DateTime? _markRangeEnd;
    private bool _markDragging;
    private bool _markPressArmed;
    private Point _markPressPoint;
    private double _markAnchorY;
    private DateTime _markPressDay;
    private Canvas? _markPressCanvas;
    private DispatcherTimer? _markPressTimer;
    private const double MarkSnapPx = 8;
    private const int MarkPressMs = 380;

    private sealed class WeekSpan
    {
        public string SessionId { get; set; } = "";
        public TaskRow? Task { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public string Outcome { get; set; } = "";
        public List<PauseSpan> Pauses { get; set; } = [];
        public string Describe
        {
            get
            {
                static string Day(DateTime d) => $"{d.Month}月{d.Day}日";
                static string Dur(TimeSpan t)
                {
                    if (t < TimeSpan.Zero) t = TimeSpan.Zero;
                    var h = (int)t.TotalHours;
                    var m = t.Minutes;
                    if (h > 0 && m > 0) return $"{h}小时{m}分";
                    if (h > 0) return $"{h}小时";
                    if (m > 0) return $"{m}分钟";
                    var sec = Math.Max(0, (int)Math.Round(t.TotalSeconds));
                    return sec > 0 ? $"{sec}秒" : "0分钟";
                }

                var range = Start.Date == End.Date
                    ? $"{Day(Start)} {Start:HH:mm}–{End:HH:mm}"
                    : $"{Day(Start)} {Start:HH:mm}–{Day(End)} {End:HH:mm}";
                return $"{Task?.Title ?? "任务"} {range} · 共{Dur(End - Start)}" +
                       (Outcome == "failed" ? "（失败）" : "");
            }
        }
    }

    public MainWindow(AppSession session)
    {
        _session = session;
        InitializeComponent();
        WindowBounds.Restore(this);
        WindowBounds.Attach(this);
        ApplyTaskRailWidth();
        Theme.Tint(this);
        FillThemeBox();
        BuildSettingsEditor();
        BuildConfigEditor();
        Theme.Changed += (_, _) => Dispatcher.Invoke(OnThemeChanged);
        Loaded += async (_, _) => await ReloadAllAsync();
        _reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _reminderTimer.Tick += async (_, _) => await CheckRemindersAsync();
        _reminderTimer.Start();
        _weekNowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _weekNowTimer.Tick += (_, _) => UpdateWeekNowLine();
        _weekNowTimer.Start();
        _runTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _runTimer.Tick += (_, _) => OnRunTick();
        UpdateGoToNowButton();
        UpdateCurrentRunChrome();

        if (WeightSexBox is not null && WeightSexBox.Items.Count == 0)
        {
            WeightSexBox.Items.Add(new ComboBoxItem { Content = "男", Tag = "male" });
            WeightSexBox.Items.Add(new ComboBoxItem { Content = "女", Tag = "female" });
            WeightSexBox.SelectedIndex = 0;
        }
        if (WeightActivityBox is not null && WeightActivityBox.Items.Count == 0)
        {
            foreach (var (id, label, _) in WeightLogic.ActivityLevels)
                WeightActivityBox.Items.Add(new ComboBoxItem { Content = label, Tag = id });
            if (WeightActivityBox.Items.Count > 0)
                WeightActivityBox.SelectedIndex = 0;
        }
    }

    private double EffectiveWeekPxPerHour =>
        _weekFocusMode ? Theme.Current.WeekFocusPxPerHour : Theme.Current.WeekPxPerHour;

    /// <summary>可见整点区间 [Start, End]，底边为 End:00；0–24 与全日一致。</summary>
    private (int Start, int End) VisibleWeekHours
    {
        get
        {
            var s = Math.Clamp(Theme.Current.WeekHourStart, 0, 24);
            var e = Math.Clamp(Theme.Current.WeekHourEnd, 0, 24);
            if (e <= s) return (0, 24);
            return (s, e);
        }
    }

    private double WeekDayHeight(double pxPerHour)
    {
        var (s, e) = VisibleWeekHours;
        return Math.Max(pxPerHour, (e - s) * pxPerHour);
    }

    private double YFromDayTime(DateTime dayStart, DateTime t, double pxPerHour)
    {
        var (s, _) = VisibleWeekHours;
        return ((t - dayStart).TotalHours - s) * pxPerHour;
    }

    private DateTime DayTimeFromY(DateTime day, double y, double pxPerHour)
    {
        var (s, _) = VisibleWeekHours;
        return day.Date.AddHours(s + y / pxPerHour);
    }

    private bool IsRunActive => _run is { Finished: false };

    private TaskRow? SelectedTask => _tasks.FirstOrDefault(t => t.Id == _selectedTaskId);

    private async Task ReloadAllAsync()
    {
        try
        {
            UpdateOfflineOverlays();
            if (_session.BusinessReady)
            {
                await LoadWalletAsync();
                await LoadTasksAsync();
                await LoadWeekAsync();
            }
            else
            {
                if (ScheduleWalletText is not null)
                    ScheduleWalletText.Text = "当前奖券数量：— · 愿望单额度：—";
                _tasks.Clear();
                RenderTaskCards();
                _weekSpans = [];
                RenderWeekBoard(_weekStart, _weekSpans);
            }

            if (_session.FavoritesReady)
                await LoadFavoritesAsync();
            else
            {
                _favs.Clear();
                RenderFavGrid();
            }

            if (_session.WeightReady)
                await LoadWeightAsync();
            else
                ClearWeightUi();

            if (_rewardWishWindow is not null)
                await _rewardWishWindow.ReloadAsync();

            StatusText.Text = "已同步 " + DateTime.Now.ToString("HH:mm:ss");
        }
        catch (Exception ex)
        {
            StatusText.Text = "同步失败";
            MessageBox.Show(ex.Message, "同步失败");
        }
    }

    private void UpdateOfflineOverlays()
    {
        SetOfflineOverlay(WeekOfflineOverlay, WeekContent, !_session.BusinessReady);
        SetOfflineOverlay(FavOfflineOverlay, FavDock, !_session.FavoritesReady);
        SetOfflineOverlay(WeightOfflineOverlay, WeightDock, !_session.WeightReady);
        ApplyTaskRailWidth();
    }

    private static void SetOfflineOverlay(UIElement? overlay, UIElement? content, bool show)
    {
        if (overlay is null || content is null) return;
        overlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        content.IsEnabled = !show;
        content.Opacity = show ? 0.35 : 1;
    }

    private async void TryConnectNoco_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "正在连接 NocoDB…";
        var ok = await _session.TryConnectAsync(msg => Dispatcher.Invoke(() => StatusText.Text = msg));
        if (!ok)
        {
            MessageBox.Show(
                "连接失败：\n" + (_session.LastConnectError ?? "未知错误"),
                "NocoDB");
            UpdateOfflineOverlays();
            return;
        }
        await ReloadAllAsync();
        BuildConfigEditor();
    }

    private async Task LoadWalletAsync()
    {
        var (tickets, quota) = await ReadWalletAsync();
        if (ScheduleWalletText is not null)
            ScheduleWalletText.Text = $"当前奖券数量：{tickets} · 愿望单额度：{quota}";
    }

    private async void ScheduleWallet_Click(object sender, MouseButtonEventArgs e)
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
        if (_rewardWishWindow is not null)
            await _rewardWishWindow.ReloadAsync();
    }

    private async Task LoadTasksAsync()
    {
        var taskNodes = await _session.Business.ListRecordsAsync(StoreTables.Tasks);
        var completions = await _session.Business.ListRecordsAsync(StoreTables.Completions);
        var sessions = await _session.Business.ListRecordsAsync(StoreTables.Sessions);
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
                Type = NocoClient.ReadString(node, "Type") ?? "flexible",
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
                CropJson = NocoClient.ReadString(node, "CropJson")
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

    private async Task LoadWeekAsync()
    {
        var start = StartOfWeek(DateTime.Today);
        var sessions = await _session.Business.ListRecordsAsync(StoreTables.Sessions);
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
        if (_selectedSessionId is not null && spans.All(x => x.SessionId != _selectedSessionId))
            _selectedSessionId = null;
        _weekStart = start;
        _weekSpans = spans;
        RenderWeekBoard(start, spans);
    }

    private void RenderWeekBoard(DateTime weekStart, List<WeekSpan> spans)
    {
        var pxPerHour = EffectiveWeekPxPerHour;
        var (hourStart, hourEnd) = VisibleWeekHours;
        var height = WeekDayHeight(pxPerHour);
        _weekNowLines.Clear();
        _weekTodayDots.Clear();
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        for (var i = 0; i < 7; i++)
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });

        var labels = new Canvas { Height = height, Width = 40 };
        for (var h = hourStart; h <= hourEnd; h++)
        {
            if (h != hourStart && h != hourEnd && h % 4 != 0) continue;
            var label = new TextBlock
            {
                Text = $"{h:00}",
                FontSize = 11,
                Foreground = Theme.Brush("TextSecondaryBrush")
            };
            Canvas.SetTop(label, Math.Max(0, (h - hourStart) * pxPerHour - 8));
            labels.Children.Add(label);
        }
        Grid.SetRow(labels, 1);
        root.Children.Add(labels);

        var today = DateTime.Today;
        for (var d = 0; d < 7; d++)
        {
            var day = weekStart.AddDays(d);
            var header = new TextBlock
            {
                Text = day.ToString("MM-dd ddd"),
                Foreground = Theme.Brush("TextPrimaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            };
            Grid.SetColumn(header, d + 1);
            root.Children.Add(header);

            var altDay = d is 1 or 3 or 5;
            var canvas = new Canvas
            {
                Height = height,
                ClipToBounds = true,
                Background = altDay
                    ? Theme.Brush("WeekAltColumnBackgroundBrush")
                    : Theme.Brush("WeekColumnBackgroundBrush")
            };
            void FitLayout()
            {
                var colW = Math.Max(40, canvas.ActualWidth);
                var spanW = Math.Max(8, colW - 6);
                var pauseW = Math.Max(6, spanW - 8);
                foreach (var child in canvas.Children.OfType<FrameworkElement>())
                {
                    var tag = child.Tag as string;
                    if (tag == "span")
                    {
                        child.Width = spanW;
                        Canvas.SetLeft(child, 3);
                    }
                    else if (tag == "pause")
                    {
                        child.Width = pauseW;
                        Canvas.SetLeft(child, 7);
                    }
                    else if (tag == "name")
                    {
                        child.Width = Math.Max(20, colW - 6);
                        foreach (var tb in child is Panel p
                                     ? p.Children.OfType<TextBlock>()
                                     : child is TextBlock one ? new[] { one } : Enumerable.Empty<TextBlock>())
                        {
                            tb.Width = child.Width;
                            tb.TextAlignment = TextAlignment.Center;
                        }
                        Canvas.SetLeft(child, 3);
                    }
                    else if (child is Line line && (tag is "hour" or "hourStrong" or "now"))
                    {
                        line.X2 = colW;
                    }
                    else if (tag == "mark")
                    {
                        child.Width = Math.Max(20, colW - 4);
                        Canvas.SetLeft(child, 2);
                    }
                    else if (tag == "todayDot" && child is Ellipse)
                    {
                        Canvas.SetLeft(child, Math.Max(0, (colW - child.Width) / 2));
                    }
                }
            }
            canvas.Loaded += (_, _) => FitLayout();
            canvas.SizeChanged += (_, _) => FitLayout();
            WireWeekColumnCanvas(canvas, day, height, pxPerHour);

            for (var h = hourStart; h <= hourEnd; h++)
            {
                var strong = h == hourStart || h == hourEnd || h % 4 == 0;
                canvas.Children.Add(new Line
                {
                    X1 = 0,
                    X2 = 4000,
                    Y1 = (h - hourStart) * pxPerHour,
                    Y2 = (h - hourStart) * pxPerHour,
                    Stroke = Theme.Brush("GridLineBrush"),
                    StrokeThickness = strong ? 1.2 : 1,
                    StrokeDashArray = strong ? null : new DoubleCollection { 3, 3 },
                    Tag = strong ? "hourStrong" : "hour",
                    IsHitTestVisible = false
                });
            }

            var dayStart = day.Date;
            var dayEnd = dayStart.AddDays(1);
            var winStart = dayStart.AddHours(hourStart);
            var winEnd = dayStart.AddHours(hourEnd);
            foreach (var span in spans)
            {
                if (span.Start >= dayEnd || span.End <= dayStart) continue;
                var visStart = span.Start < dayStart ? dayStart : span.Start;
                var visEnd = span.End > dayEnd ? dayEnd : span.End;
                if (visStart < winStart) visStart = winStart;
                if (visEnd > winEnd) visEnd = winEnd;
                if (visEnd <= visStart) continue;

                var top = YFromDayTime(dayStart, visStart, pxPerHour);
                var blockH = Math.Max(4, (visEnd - visStart).TotalHours * pxPerHour);
                var failed = span.Outcome == "failed";
                var selected = span.SessionId == _selectedSessionId;
                var fill = BlockPatterns.CreateBrush(span.Task?.ResolveStyle()).Clone();
                if (failed) fill.Opacity = Theme.Current.FailedFillOpacity;
                var rect = new Rectangle
                {
                    Height = blockH,
                    Fill = fill,
                    Stroke = selected
                        ? Theme.Brush("AccentBrush")
                        : failed ? Theme.Brush("BorderSubtleBrush") : Brushes.Transparent,
                    StrokeThickness = selected ? 3 : 1,
                    StrokeDashArray = failed && !selected ? new DoubleCollection { 2, 2 } : null,
                    RadiusX = 3,
                    RadiusY = 3,
                    Tag = "span",
                    Cursor = Cursors.Hand
                };
                ScheduleTip(rect, span.Describe);
                var sessionId = span.SessionId;
                rect.MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    _selectedSessionId = _selectedSessionId == sessionId ? null : sessionId;
                    RenderWeekBoard(_weekStart, _weekSpans);
                };
                Canvas.SetLeft(rect, 3);
                Canvas.SetTop(rect, top);
                canvas.Children.Add(rect);

                var fontSize = Math.Clamp(Theme.Current.FontSizeBody - 2, 9, 14);
                var title = span.Task?.Title ?? "任务";
                var tip = span.Describe;
                var name = CreateOutlinedBlockTitle(title, fontSize);
                ScheduleTip(name, tip);
                Canvas.SetTop(name, top + Math.Max(0, (blockH - fontSize - 2) / 2));
                canvas.Children.Add(name);

                foreach (var pause in span.Pauses)
                {
                    var p0 = pause.Start < visStart ? visStart : pause.Start;
                    var p1 = pause.End > visEnd ? visEnd : pause.End;
                    if (p1 <= p0) continue;
                    var pTop = YFromDayTime(dayStart, p0, pxPerHour);
                    var pH = Math.Max(2, (p1 - p0).TotalHours * pxPerHour);
                    var gray = new Rectangle
                    {
                        Height = pH,
                        Fill = Theme.Brush("PauseOverlayBrush"),
                        RadiusX = 2,
                        RadiusY = 2,
                        Tag = "pause",
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(gray, 7);
                    Canvas.SetTop(gray, pTop);
                    canvas.Children.Add(gray);
                }
            }

            var nowTheme = Theme.Current;
            var nowLine = new Line
            {
                X1 = 0,
                X2 = 4000,
                Stroke = Theme.FromHex(nowTheme.WeekNowLine),
                StrokeThickness = nowTheme.WeekNowLineThickness,
                Opacity = nowTheme.WeekNowLineOpacity,
                StrokeDashArray = new DoubleCollection { 6, 4 },
                Tag = "now",
                IsHitTestVisible = false
            };
            canvas.Children.Add(nowLine);
            _weekNowLines.Add((day.Date, nowLine));

            if (day.Date == today)
            {
                var todayDot = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = Theme.Brush("DangerBrush"),
                    Stroke = Theme.Brush("WindowBackgroundBrush"),
                    StrokeThickness = 1.5,
                    Tag = "todayDot",
                    IsHitTestVisible = false
                };
                canvas.Children.Add(todayDot);
                _weekTodayDots.Add((day.Date, todayDot));
            }

            DrawMarkRectOnCanvas(canvas, day, pxPerHour, height);

            Grid.SetColumn(canvas, d + 1);
            Grid.SetRow(canvas, 1);
            root.Children.Add(canvas);
        }

        WeekHost.Child = root;
        UpdateWeekNowLine();
        if (_weekFollowNow)
            ScrollWeekToNow();
        UpdateGoToNowButton();
    }

    private void UpdateWeekNowLine()
    {
        var now = DateTime.Now;
        var px = EffectiveWeekPxPerHour;
        var (hourStart, hourEnd) = VisibleWeekHours;
        var hod = now.TimeOfDay.TotalHours;
        var inWindow = hod >= hourStart && hod <= hourEnd;
        var y = (hod - hourStart) * px;
        foreach (var (_, line) in _weekNowLines)
        {
            if (!inWindow)
            {
                line.Visibility = Visibility.Collapsed;
                continue;
            }
            line.Visibility = Visibility.Visible;
            line.Y1 = y;
            line.Y2 = y;
        }
        foreach (var (day, dot) in _weekTodayDots)
        {
            if (now.Date != day || !inWindow)
            {
                dot.Visibility = Visibility.Collapsed;
                continue;
            }
            dot.Visibility = Visibility.Visible;
            Canvas.SetTop(dot, y - dot.Height / 2);
            if (dot.Parent is Canvas canvas && canvas.ActualWidth > 0)
                Canvas.SetLeft(dot, Math.Max(0, (canvas.ActualWidth - dot.Width) / 2));
        }
        if (_weekFollowNow)
            ScrollWeekToNow();
    }

    private void ScrollWeekToNow()
    {
        if (WeekScroll is null || WeekHost?.Child is null) return;
        var now = DateTime.Now;
        if (now.Date < _weekStart || now.Date >= _weekStart.AddDays(7)) return;
        var px = EffectiveWeekPxPerHour;
        var (hourStart, hourEnd) = VisibleWeekHours;
        var hod = Math.Clamp(now.TimeOfDay.TotalHours, hourStart, hourEnd);
        var y = (hod - hourStart) * px;
        var target = Math.Max(0, y - WeekScroll.ViewportHeight * 0.25);
        _weekIgnoreScroll = true;
        WeekScroll.ScrollToVerticalOffset(target);
        _weekIgnoreScroll = false;
    }

    private void StopWeekFollow()
    {
        if (!_weekFollowNow) return;
        _weekFollowNow = false;
        UpdateGoToNowButton();
    }

    private void UpdateGoToNowButton()
    {
        if (GoToNowButton is null) return;
        GoToNowButton.Visibility = _weekFollowNow ? Visibility.Collapsed : Visibility.Visible;
    }

    private void GoToNow_Click(object sender, RoutedEventArgs e)
    {
        _weekFollowNow = true;
        UpdateGoToNowButton();
        ScrollWeekToNow();
        UpdateWeekNowLine();
    }

    private void ToggleWeekFocusMode()
    {
        _weekFocusMode = !_weekFocusMode;
        if (_weekFocusMode)
        {
            _weekFollowNow = true;
            UpdateGoToNowButton();
        }
        RenderWeekBoard(_weekStart, _weekSpans);
        if (_weekFocusMode)
            ScrollWeekToNow();
        StatusText.Text = _weekFocusMode ? "已进入聚焦模式（F1 退出）" : "已退出聚焦模式";
    }

    private void WeekScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_weekIgnoreScroll) return;
        if (e.VerticalChange == 0) return;
        StopWeekFollow();
    }

    private void WeekScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        StopWeekFollow();

    private void WireWeekColumnCanvas(Canvas canvas, DateTime day, double height, double pxPerHour)
    {
        canvas.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is FrameworkElement fe && fe.Tag as string == "span")
                return;
            if (e.OriginalSource is not Canvas && e.OriginalSource is FrameworkElement src
                && src.Tag as string is "name" or "pause" or "mark")
                return;

            _selectedSessionId = null;
            CancelMarkPress();
            ClearMarkSelection(rerender: false);
            if (IsRunActive)
            {
                e.Handled = true;
                return;
            }
            _markPressArmed = true;
            _markPressPoint = e.GetPosition(canvas);
            _markPressDay = day.Date;
            _markPressCanvas = canvas;
            _markPressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(MarkPressMs) };
            _markPressTimer.Tick += MarkPressTimer_Tick;
            _markPressTimer.Start();
            canvas.CaptureMouse();
            e.Handled = true;
        };
        canvas.MouseMove += (_, e) =>
        {
            if (_markPressArmed && !_markDragging && _markPressCanvas == canvas)
            {
                var p = e.GetPosition(canvas);
                if (Math.Abs(p.X - _markPressPoint.X) > 6 || Math.Abs(p.Y - _markPressPoint.Y) > 6)
                {
                    CancelMarkPress();
                    canvas.ReleaseMouseCapture();
                    _markPressCanvas = null;
                    RenderWeekBoard(_weekStart, _weekSpans);
                }
            }
            if (!_markDragging || _markPressCanvas != canvas) return;
            var y = SnapMarkY(day.Date, ClampY(e.GetPosition(canvas).Y, height), pxPerHour);
            UpdateMarkFromAnchor(day.Date, y, pxPerHour);
            RefreshMarkVisual(canvas, day.Date, pxPerHour, height);
            e.Handled = true;
        };
        canvas.MouseLeftButtonUp += (_, e) =>
        {
            if (_markPressCanvas != canvas) return;
            if (_markDragging)
            {
                _markDragging = false;
                canvas.ReleaseMouseCapture();
                CancelMarkPress();
                if (_markRangeStart is null || _markRangeEnd is null
                    || (_markRangeEnd.Value - _markRangeStart.Value).TotalMinutes < 1)
                    ClearMarkSelection(rerender: true);
                else
                    RefreshMarkVisual(canvas, day.Date, pxPerHour, height);
                e.Handled = true;
                return;
            }
            CancelMarkPress();
            canvas.ReleaseMouseCapture();
            // 短点空白：清选区并重画以去掉 session 高亮
            RenderWeekBoard(_weekStart, _weekSpans);
            e.Handled = true;
        };
        canvas.MouseRightButtonUp += (_, e) =>
        {
            if (_markRangeStart is null || _markRangeEnd is null) return;
            if (_markRangeStart.Value.Date != day.Date) return;
            ShowMarkContextMenu(canvas);
            e.Handled = true;
        };
    }

    private void MarkPressTimer_Tick(object? sender, EventArgs e)
    {
        CancelMarkPress(keepArmed: false);
        if (_markPressCanvas is null) return;
        _markDragging = true;
        StopWeekFollow();
        var px = EffectiveWeekPxPerHour;
        var height = WeekDayHeight(px);
        _markAnchorY = SnapMarkY(_markPressDay, ClampY(_markPressPoint.Y, height), px);
        _markRangeStart = DayTimeFromY(_markPressDay, _markAnchorY, px);
        _markRangeEnd = _markRangeStart;
        RefreshMarkVisual(_markPressCanvas, _markPressDay, px, height);
    }

    private void CancelMarkPress(bool keepArmed = false)
    {
        if (_markPressTimer is not null)
        {
            _markPressTimer.Stop();
            _markPressTimer.Tick -= MarkPressTimer_Tick;
            _markPressTimer = null;
        }
        if (!keepArmed)
            _markPressArmed = false;
    }

    private static double ClampY(double y, double height) =>
        Math.Clamp(y, 0, height);

    private double SnapMarkY(DateTime day, double y, double pxPerHour)
    {
        var (hourStart, hourEnd) = VisibleWeekHours;
        var dayStart = day.Date;
        var winStart = dayStart.AddHours(hourStart);
        var winEnd = dayStart.AddHours(hourEnd);
        var candidates = new List<double> { y };
        foreach (var span in _weekSpans)
        {
            if (span.End <= day || span.Start >= day.AddDays(1)) continue;
            var visStart = span.Start < dayStart ? dayStart : span.Start;
            var visEnd = span.End > dayStart.AddDays(1) ? dayStart.AddDays(1) : span.End;
            if (visStart < winStart) visStart = winStart;
            if (visEnd > winEnd) visEnd = winEnd;
            if (visEnd <= visStart) continue;
            candidates.Add(YFromDayTime(dayStart, visStart, pxPerHour));
            candidates.Add(YFromDayTime(dayStart, visEnd, pxPerHour));
        }
        if (day == DateTime.Today)
        {
            var hod = DateTime.Now.TimeOfDay.TotalHours;
            if (hod >= hourStart && hod <= hourEnd)
                candidates.Add((hod - hourStart) * pxPerHour);
        }

        var best = y;
        var bestDist = MarkSnapPx + 1;
        foreach (var c in candidates)
        {
            var d = Math.Abs(c - y);
            if (d <= MarkSnapPx && d < bestDist)
            {
                bestDist = d;
                best = c;
            }
        }
        return best;
    }

    private void UpdateMarkFromAnchor(DateTime day, double endY, double pxPerHour)
    {
        var a = _markAnchorY;
        var y0 = Math.Min(a, endY);
        var y1 = Math.Max(a, endY);
        if (y1 - y0 < pxPerHour / 60) y1 = y0 + pxPerHour / 60;
        _markRangeStart = DayTimeFromY(day, y0, pxPerHour);
        _markRangeEnd = DayTimeFromY(day, y1, pxPerHour);
    }

    private void DrawMarkRectOnCanvas(Canvas canvas, DateTime day, double pxPerHour, double _)
    {
        if (_markRangeStart is null || _markRangeEnd is null) return;
        if (_markRangeStart.Value.Date != day.Date) return;
        var y0 = YFromDayTime(day.Date, _markRangeStart.Value, pxPerHour);
        var y1 = YFromDayTime(day.Date, _markRangeEnd.Value, pxPerHour);
        var top = Math.Min(y0, y1);
        var h = Math.Max(2, Math.Abs(y1 - y0));
        var shape = new Rectangle
        {
            Height = h,
            Stroke = Brushes.DodgerBlue,
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Fill = Brushes.Transparent,
            Tag = "mark",
            IsHitTestVisible = true,
            Cursor = Cursors.Hand
        };
        Canvas.SetLeft(shape, 2);
        Canvas.SetTop(shape, top);
        Panel.SetZIndex(shape, 50);
        shape.MouseRightButtonUp += (_, e) =>
        {
            ShowMarkContextMenu(canvas);
            e.Handled = true;
        };
        canvas.Children.Add(shape);
    }

    private void RefreshMarkVisual(Canvas canvas, DateTime day, double pxPerHour, double height)
    {
        foreach (var child in canvas.Children.OfType<FrameworkElement>().Where(c => c.Tag as string == "mark").ToList())
            canvas.Children.Remove(child);
        DrawMarkRectOnCanvas(canvas, day, pxPerHour, height);
        var colW = Math.Max(40, canvas.ActualWidth);
        foreach (var child in canvas.Children.OfType<FrameworkElement>().Where(c => c.Tag as string == "mark"))
        {
            child.Width = Math.Max(20, colW - 4);
            Canvas.SetLeft(child, 2);
        }
    }

    private void ClearMarkSelection(bool rerender)
    {
        _markRangeStart = null;
        _markRangeEnd = null;
        _markDragging = false;
        if (rerender)
            RenderWeekBoard(_weekStart, _weekSpans);
    }

    private bool CanMarkSelection()
    {
        if (IsRunActive) return false;
        if (_markRangeStart is null || _markRangeEnd is null) return false;
        var start = _markRangeStart.Value;
        var end = _markRangeEnd.Value;
        if (end <= start) return false;
        if (end > DateTime.Now) return false;
        foreach (var span in _weekSpans)
        {
            if (span.End <= start || span.Start >= end) continue;
            return false;
        }
        return true;
    }

    private void ShowMarkContextMenu(Canvas canvas)
    {
        var menu = new ContextMenu();
        var root = new MenuItem
        {
            Header = "标记为指定活动",
            IsEnabled = CanMarkSelection(),
            ToolTip = IsRunActive ? "有任务正在执行，不可框选补记" : null
        };
        foreach (var task in _tasks.Where(t => !t.Archived).OrderBy(t => t.Title))
        {
            var item = new MenuItem { Header = task.Title, Tag = task };
            item.Click += async (_, _) => await MarkSelectionAsTaskAsync(task);
            root.Items.Add(item);
        }
        if (root.Items.Count == 0)
            root.Items.Add(new MenuItem { Header = "（无可用任务）", IsEnabled = false });
        menu.Items.Add(root);
        canvas.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private async Task MarkSelectionAsTaskAsync(TaskRow task)
    {
        if (!CanMarkSelection() || _markRangeStart is null || _markRangeEnd is null)
        {
            MessageBox.Show("所选时段无效：不可覆盖已有记录，也不可超过当前时间。");
            return;
        }
        try
        {
            var start = _markRangeStart.Value;
            var end = _markRangeEnd.Value;
            await _session.Business.CreateRecordAsync(StoreTables.Sessions, new Dictionary<string, object?>
            {
                ["Title"] = task.Title,
                ["StartedAt"] = RewardLogic.FormatDateTime(start),
                ["EndedAt"] = RewardLogic.FormatDateTime(end),
                ["Outcome"] = "success",
                ["PausedSeconds"] = 0,
                ["PauseJson"] = "[]",
                ["Task"] = task.Id
            });
            ClearMarkSelection(rerender: false);
            await LoadWeekAsync();
            StatusText.Text = $"已标记「{task.Title}」{start:HH:mm}–{end:HH:mm}（仅记录，未发奖）";
            MaybeWarnHiddenHours(task.Title, start, end);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "标记失败");
        }
    }

    private async void DeleteWeekSpan_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSessionId is null)
        {
            MessageBox.Show("请先点选一条日程记录。");
            return;
        }
        var span = _weekSpans.FirstOrDefault(s => s.SessionId == _selectedSessionId);
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
            await _session.Business.DeleteRecordAsync(StoreTables.Sessions, span.SessionId);
            _selectedSessionId = null;
            await LoadWeekAsync();
            StatusText.Text = "已删除执行记录";
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

    private void OnThemeChanged()
    {
        Theme.Tint(this);
        if (!_settingsBuilding)
        {
            FillThemeBox();
            BuildSettingsEditor();
            BuildConfigEditor();
        }
        ApplyTaskRailWidth();
        RenderTaskCards();
        RenderFavGrid();
        if (_session.WeightReady)
            RenderWeightChart();
        else
            ClearWeightUi();
        if (_weekSpans.Count > 0 || WeekHost.Child is not null)
            RenderWeekBoard(_weekStart, _weekSpans);
    }

    private void CardHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 卡片宽度已固定，窗口拉宽拉窄不必重算。
    }

    private void TaskArchived_Changed(object sender, RoutedEventArgs e)
    {
        if (TaskArchivedBox is null || TaskWrap is null) return;
        _showArchivedTasks = TaskArchivedBox.IsChecked == true;
        RenderTaskCards();
    }

    private const int ScheduleTipDelayMs = 100;

    /// <summary>色块居中任务名：黑描边 + 主题字色。</summary>
    private static FrameworkElement CreateOutlinedBlockTitle(string title, double fontSize)
    {
        var grid = new Grid
        {
            Tag = "name",
            IsHitTestVisible = false,
            ClipToBounds = false
        };
        foreach (var (dx, dy) in new (double, double)[]
                 {
                     (-1, -1), (0, -1), (1, -1),
                     (-1, 0), (1, 0),
                     (-1, 1), (0, 1), (1, 1)
                 })
        {
            grid.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = fontSize,
                Foreground = Brushes.Black,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false,
                RenderTransform = new TranslateTransform(dx, dy)
            });
        }
        grid.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = fontSize,
            Foreground = Theme.Brush("TextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false
        });
        return grid;
    }

    private static void ScheduleTip(FrameworkElement el, object tip)
    {
        el.ToolTip = tip;
        ToolTipService.SetInitialShowDelay(el, ScheduleTipDelayMs);
    }

    private TextBlock HintBlock(string text) => new()
    {
        Text = text,
        Margin = new Thickness(8),
        Foreground = Theme.Brush("TextSecondaryBrush"),
        TextWrapping = TextWrapping.Wrap
    };

    private void RenderTaskCards()
    {
        TaskWrap.Children.Clear();
        var visible = _tasks.Where(t => _showArchivedTasks || !t.Archived).ToList();
        if (visible.Count == 0)
        {
            var hint = _tasks.Count == 0
                ? "还没有任务。点「新建任务」，或把图片拖进来。"
                : "没有可显示的任务。勾选「显示已归档内容」可查看已归档项。";
            TaskWrap.Children.Add(HintBlock(hint));
            return;
        }
        foreach (var task in visible)
            TaskWrap.Children.Add(CreateTaskCard(task));
    }

    private UIElement CreateTaskCard(TaskRow task)
    {
        var selected = task.Id == _selectedTaskId;
        var t = Theme.Current;
        var w = t.ScheduleCardWidth;
        var thumbH = t.ScheduleCardThumbHeight;
        var btn = t.StartTaskButtonSize;
        var thumbRadius = Math.Max(0, t.CardCornerRadius - 2);
        var image = new System.Windows.Controls.Image
        {
            Height = thumbH,
            Stretch = Stretch.UniformToFill,
            Source = task.Preview
        };
        var icon = new Border
        {
            Height = thumbH,
            Background = task.Preview is null
                ? BlockPatterns.CreateBrush(task.ResolveStyle())
                : Brushes.Transparent,
            Child = task.Preview is null ? null : image,
            CornerRadius = new CornerRadius(thumbRadius),
            ClipToBounds = true
        };
        UiShapes.RoundClip(icon, thumbRadius);
        var playImg = new System.Windows.Controls.Image
        {
            Source = Theme.LoadStartIcon(),
            Stretch = Stretch.Uniform,
            Width = btn,
            Height = btn
        };
        var play = new Border
        {
            Width = btn,
            Height = btn,
            CornerRadius = new CornerRadius(t.StartTaskButtonCornerRadius),
            ClipToBounds = true,
            Child = playImg,
            Cursor = Cursors.Hand,
            Margin = new Thickness(2, 0, 0, 0)
        };
        ScheduleTip(play, "开始任务");
        play.MouseLeftButtonDown += async (_, e) =>
        {
            e.Handled = true;
            _selectedTaskId = task.Id;
            RenderTaskCards();
            await StartOrFocusRunAsync(task);
        };
        var recImg = new System.Windows.Controls.Image
        {
            Source = Theme.LoadRecordIcon(),
            Stretch = Stretch.Uniform,
            Width = btn,
            Height = btn
        };
        var record = new Border
        {
            Width = btn,
            Height = btn,
            Background = Brushes.Transparent,
            Child = recImg,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 2, 0)
        };
        ScheduleTip(record, "记录任务");
        record.MouseLeftButtonDown += async (_, e) =>
        {
            e.Handled = true;
            _selectedTaskId = task.Id;
            RenderTaskCards();
            await RecordTaskAsync(task);
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 4, 0)
        };
        buttons.Children.Add(record);
        buttons.Children.Add(play);
        var thumbGrid = new Grid { Height = thumbH };
        thumbGrid.Children.Add(icon);
        if (task.AllowOverflow)
        {
            var required = SessionLogic.RequiredSeconds(task.RewardMinutes);
            var fill = required <= 0 ? 0 : Math.Clamp(task.OverflowSeconds / required, 0, 1);
            var barH = thumbH / 2;
            var tip = $"溢出进度累计：{Math.Round(task.OverflowSeconds / 60)}/{task.RewardMinutes} min";
            var fillBar = new Border
            {
                Height = Math.Max(0, fill * barH),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = Theme.Brush("OverflowFillBrush"),
                CornerRadius = new CornerRadius(3)
            };
            var barHost = new Grid
            {
                Width = 6,
                Height = barH,
                Margin = new Thickness(5, 5, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            ScheduleTip(barHost, tip);
            barHost.Children.Add(new Border
            {
                Background = Theme.Brush("OverflowTrackBrush"),
                CornerRadius = new CornerRadius(3)
            });
            barHost.Children.Add(fillBar);
            thumbGrid.Children.Add(barHost);
        }
        thumbGrid.Children.Add(buttons);
        var info = new TextBlock
        {
            Text = $"{task.Title}\n{task.TypeLabel} · {task.LevelLabel}" +
                   (string.IsNullOrWhiteSpace(task.PhaseLabel) ? "" : " · " + task.PhaseLabel) +
                   (string.IsNullOrWhiteSpace(task.Status) ? "" : "\n" + task.Status),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = Theme.Brush("TextPrimaryBrush"),
            FontSize = t.FontSizeBody
        };
        var stack = new StackPanel();
        stack.Children.Add(thumbGrid);
        stack.Children.Add(info);
        var border = new Border
        {
            Width = w,
            Height = t.ScheduleCardHeight,
            Margin = new Thickness(6),
            Padding = new Thickness(10),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, task.CardColor.R, task.CardColor.G, task.CardColor.B)),
            BorderBrush = selected ? Theme.Brush("AccentBrush") : TaskVisual.BrushOf(task.ColorHex),
            BorderThickness = new Thickness(selected ? 3 : 1),
            CornerRadius = new CornerRadius(t.CardCornerRadius),
            Cursor = Cursors.Hand,
            Opacity = task.Archived ? 0.55 : 1,
            Child = stack
        };
        border.MouseLeftButtonDown += async (_, e) =>
        {
            _selectedTaskId = task.Id;
            RenderTaskCards();
            if (e.ClickCount == 2)
                await EditTaskAsync(task);
        };
        return border;
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
            StatusText.Text = "拖入的不是图片。";
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
                StatusText.Text = "剪贴板里没有图片。";
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
        _rewardWishWindow = new RewardWishWindow(_session) { Owner = this };
        _rewardWishWindow.WalletChanged += () => Dispatcher.InvokeAsync(async () => await LoadWalletAsync());
        _rewardWishWindow.Closed += (_, _) => _rewardWishWindow = null;
        _rewardWishWindow.Show();
    }

    private async Task PromptNewTaskAsync(string? thumbPath = null)
    {
        var dlg = new NewTaskWindow { Owner = this };
        if (thumbPath is not null)
        {
            var original = ThumbCropWindow.PersistOriginalCopy(thumbPath);
            var crop = ThumbCropWindow.AskFull(this, original, "task");
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
                ["Archived"] = dlg.Archived
            };
            if (dlg.ThumbPath is not null)
                fields["Thumb"] = await _session.Business.UploadAsync(System.IO.Path.GetFileName(dlg.ThumbPath), await File.ReadAllBytesAsync(dlg.ThumbPath), MimeOf(dlg.ThumbPath));
            if (dlg.OriginalPath is not null)
                fields["Original"] = await _session.Business.UploadAsync(System.IO.Path.GetFileName(dlg.OriginalPath), await File.ReadAllBytesAsync(dlg.OriginalPath), MimeOf(dlg.OriginalPath));
            if (dlg.CropJson is not null)
                fields["CropJson"] = dlg.CropJson;
            if (dlg.DueAt is not null)
                fields["DueAt"] = RewardLogic.FormatDateTime(dlg.DueAt.Value);
            if (dlg.ReminderAt is not null)
                fields["ReminderAt"] = RewardLogic.FormatDateTime(dlg.ReminderAt.Value);
            await _session.Business.CreateRecordAsync(StoreTables.Tasks, fields);
            await LoadTasksAsync();
            StatusText.Text = "已添加任务「" + dlg.TaskTitle + "」";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "新建失败");
        }
    }

    private async Task EditTaskAsync(TaskRow task)
    {
        var dlg = new NewTaskWindow { Owner = this };
        dlg.PrefillEdit(task);
        var origPath = await DownloadAttachmentTempAsync(_session.Business, task.OriginalField, "pm-task-orig-");
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
                ["Archived"] = dlg.Archived
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
                    fields["Thumb"] = await _session.Business.UploadAsync(System.IO.Path.GetFileName(dlg.ThumbPath), await File.ReadAllBytesAsync(dlg.ThumbPath), MimeOf(dlg.ThumbPath));
                if (dlg.OriginalPath is not null)
                    fields["Original"] = await _session.Business.UploadAsync(System.IO.Path.GetFileName(dlg.OriginalPath), await File.ReadAllBytesAsync(dlg.OriginalPath), MimeOf(dlg.OriginalPath));
                if (dlg.CropJson is not null)
                    fields["CropJson"] = dlg.CropJson;
            }
            fields["DueAt"] = dlg.DueAt is null ? null : RewardLogic.FormatDateTime(dlg.DueAt.Value);
            fields["ReminderAt"] = dlg.ReminderAt is null ? null : RewardLogic.FormatDateTime(dlg.ReminderAt.Value);
            await _session.Business.PatchRecordAsync(StoreTables.Tasks, fields);
            await LoadTasksAsync();
            StatusText.Text = "已保存任务「" + dlg.TaskTitle + "」";
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
            StatusText.Text = "已有任务在执行";
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
                var sessions = await _session.Business.ListRecordsAsync(StoreTables.Sessions);
                open = sessions.FirstOrDefault(s => NocoClient.ReadId(s) == sessionId);
            }
            if (open is null)
            {
                var created = await _session.Business.CreateRecordAsync(StoreTables.Sessions, new Dictionary<string, object?>
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
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        if (!IsVisible)
            Show();
        Activate();
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
            await _session.Business.PatchRecordAsync(StoreTables.Sessions, new Dictionary<string, object?>
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
                StatusText.Text = $"「{finish.TaskTitle}」记为失败";

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
            StatusText.Text = "已有任务在执行";
            return;
        }
        if (_taskBusy) return;
        _taskBusy = true;
        try
        {
            var sessions = await _session.Business.ListRecordsAsync(StoreTables.Sessions);
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
            await _session.Business.CreateRecordAsync(StoreTables.Sessions, new Dictionary<string, object?>
            {
                ["Title"] = task.Title,
                ["StartedAt"] = RewardLogic.FormatDateTime(lastEnded.Value),
                ["EndedAt"] = RewardLogic.FormatDateTime(now),
                ["Outcome"] = "success",
                ["PausedSeconds"] = 0,
                ["PauseJson"] = SessionLogic.SerializePauses([]),
                ["Task"] = task.Id
            });
            var active = SessionLogic.ActiveSeconds(lastEnded.Value, now, 0);
            await SettleSuccessAsync(task.Id, task.Title, active);
            await LoadTasksAsync();
            await LoadWeekAsync();
            MaybeWarnHiddenHours(task.Title, lastEnded.Value, now);
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
        var (s, e) = VisibleWeekHours;
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
            Owner = this,
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
            await _session.Business.PatchRecordAsync(StoreTables.Tasks, new Dictionary<string, object?>
            {
                ["Id"] = taskId,
                ["OverflowSeconds"] = settle.NewOverflowSeconds
            });
            if (task is not null) task.OverflowSeconds = settle.NewOverflowSeconds;
        }

        if (settle.Total < 1)
        {
            StatusText.Text = allow && settle.NewOverflowSeconds > 0
                ? $"已结束「{title}」（未满 {minutes} 分钟，余数已计入溢出）"
                : $"已结束「{title}」（未满 {minutes} 分钟，不发奖）";
            return;
        }

        var type = task?.Type ?? "flexible";
        var level = task?.RewardLevel ?? 1;
        var dates = (await _session.Business.ListRecordsAsync(StoreTables.Completions))
            .Where(c => RewardLogic.LinkedId(c, "Task") == taskId)
            .Select(c => RewardLogic.ParseDate(c, "CompletedOn"))
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToList();
        var today = DateTime.Today;
        if (!dates.Any(d => d.Date == today))
        {
            await _session.Business.CreateRecordAsync(StoreTables.Completions, new Dictionary<string, object?>
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
        StatusText.Text = ticketsGain > 0
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
            await _session.Business.PatchRecordAsync(StoreTables.Sessions, new Dictionary<string, object?>
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

    private void BuildConfigEditor()
    {
        if (ConfigHost is null) return;
        ConfigHost.Children.Clear();
        var s = _session.Settings;
        var programCfg = ProgramConfig.Load();

        ConfigHost.Children.Add(new TextBlock
        {
            Text = "程序",
            FontSize = Theme.Current.FontSizeTitle,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = Theme.Brush("TextPrimaryBrush")
        });
        ConfigHost.Children.Add(HintBlock(
            "当前用户：" + _session.UserName + "。关闭「直接登录」后，下次启动会先显示登录页选用户（暂无登出按钮）。"));
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
            _session.NocoConnected
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
            StatusText.Text = "正在连接…";
            var ok = await _session.TryConnectAsync(msg => Dispatcher.Invoke(() => StatusText.Text = msg));
            MessageBox.Show(ok ? "连接成功。" : ("连接失败：\n" + _session.LastConnectError), "NocoDB");
            BuildConfigEditor();
            await ReloadAllAsync();
        };
        applySwitches.Click += async (_, _) =>
        {
            PersistConnectionFields(mode, urlBox, emailBox, passBox, tokenBox, containerBox, honeyBox, llmKeyBox, llmUrlBox, llmModelBox);
            var wantBiz = bizBox.IsChecked == true;
            var wantFav = favBox.IsChecked == true;
            var wantWeight = weightBox.IsChecked == true;
            try
            {
                StatusText.Text = "正在迁移存储…";
                if (wantBiz != s.UseNocoBusiness)
                    await _session.SetUseNocoBusinessAsync(wantBiz, msg => Dispatcher.Invoke(() => StatusText.Text = msg));
                if (wantFav != _session.Settings.UseNocoFavorites)
                    await _session.SetUseNocoFavoritesAsync(wantFav, msg => Dispatcher.Invoke(() => StatusText.Text = msg));
                if (wantWeight != _session.Settings.UseNocoWeight)
                    await _session.SetUseNocoWeightAsync(wantWeight, msg => Dispatcher.Invoke(() => StatusText.Text = msg));
                MessageBox.Show("存储开关已更新。", "设置");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "迁移失败");
            }
            BuildConfigEditor();
            await ReloadAllAsync();
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
        _session.SaveSettingsFromUi(
            _session.Settings.UseNocoBusiness,
            _session.Settings.UseNocoFavorites,
            _session.Settings.UseNocoWeight,
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

    private void ClearWeightUi()
    {
        _weightProfileId = null;
        _weightEntries.Clear();
        WeightChartCanvas?.Children.Clear();
        if (WeightStatsText is not null)
            WeightStatsText.Text = "";
    }

    private void EnsureWeightRangeDefaults()
    {
        if (_weightRangeReady) return;
        var today = DateTime.Today;
        if (WeightChartFromBox is not null && string.IsNullOrWhiteSpace(WeightChartFromBox.Text))
            WeightChartFromBox.Text = WeightLogic.FormatDate(today.AddDays(-30));
        if (WeightChartToBox is not null && string.IsNullOrWhiteSpace(WeightChartToBox.Text))
            WeightChartToBox.Text = WeightLogic.FormatDate(today);
        _weightRangeReady = true;
    }

    private static void SelectComboByTag(ComboBox? box, string? tag)
    {
        if (box is null || tag is null) return;
        foreach (var item in box.Items)
        {
            if (item is ComboBoxItem cbi && (cbi.Tag as string) == tag)
            {
                box.SelectedItem = cbi;
                return;
            }
        }
    }

    private static string? SelectedComboTag(ComboBox? box) =>
        (box?.SelectedItem as ComboBoxItem)?.Tag as string;

    private async Task LoadWeightAsync()
    {
        if (WeightChartCanvas is null) return;
        EnsureWeightRangeDefaults();
        var profiles = await _session.Weight.ListRecordsAsync(StoreTables.WeightProfile);
        var profile = profiles.FirstOrDefault();
        _weightProfileId = NocoClient.ReadId(profile);

        var height = NocoClient.ReadDouble(profile, "HeightCm", 170);
        var age = NocoClient.ReadInt(profile, "AgeYears", 30);
        var sex = NocoClient.ReadString(profile, "Sex") ?? "male";
        var activity = NocoClient.ReadString(profile, "Activity") ?? "sedentary";

        if (WeightHeightBox is not null)
            WeightHeightBox.Text = height.ToString("0.##");
        if (WeightAgeBox is not null)
            WeightAgeBox.Text = age.ToString();
        SelectComboByTag(WeightSexBox, sex);
        SelectComboByTag(WeightActivityBox, activity);

        var entries = await _session.Weight.ListRecordsAsync(StoreTables.WeightEntries);
        _weightEntries.Clear();
        foreach (var n in entries)
        {
            var date = WeightLogic.ParseEntryDate(n);
            if (date is null) continue;
            _weightEntries.Add((date.Value, NocoClient.ReadDouble(n, "WeightKg")));
        }
        _weightEntries.Sort((a, b) => a.Date.CompareTo(b.Date));

        var latest = _weightEntries.Count > 0 ? _weightEntries[^1].Kg : (double?)null;
        UpdateWeightStatsText(latest, height, age, sex, activity);
        RenderWeightChart();
    }

    private void WeightChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) =>
        RenderWeightChart();

    private void WeightChartLast30_Click(object sender, RoutedEventArgs e)
    {
        var today = DateTime.Today;
        if (WeightChartFromBox is not null)
            WeightChartFromBox.Text = WeightLogic.FormatDate(today.AddDays(-30));
        if (WeightChartToBox is not null)
            WeightChartToBox.Text = WeightLogic.FormatDate(today);
        RenderWeightChart();
    }

    private void WeightChartRange_Changed(object sender, RoutedEventArgs e) =>
        RenderWeightChart();

    private bool TryReadWeightChartRange(out DateTime from, out DateTime to, out string? error)
    {
        from = default;
        to = default;
        error = null;
        EnsureWeightRangeDefaults();
        var fromRaw = (WeightChartFromBox?.Text ?? "").Trim();
        var toRaw = (WeightChartToBox?.Text ?? "").Trim();
        if (!DateTime.TryParseExact(fromRaw, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out from))
        {
            error = "起始日期请用 yyyy-MM-dd。";
            return false;
        }
        if (!DateTime.TryParseExact(toRaw, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out to))
        {
            error = "结束日期请用 yyyy-MM-dd。";
            return false;
        }
        from = from.Date;
        to = to.Date;
        if (to < from)
        {
            error = "结束日期不能早于起始日期。";
            return false;
        }
        return true;
    }

    private void RenderWeightChart()
    {
        if (WeightChartCanvas is null) return;
        var t = Theme.Current;
        WeightChartCanvas.Height = t.WeightChartHeight;
        WeightChartCanvas.Children.Clear();

        var w = WeightChartCanvas.ActualWidth;
        var h = WeightChartCanvas.ActualHeight;
        if (w < 40 || h < 40) return;

        var plotBg = new System.Windows.Shapes.Rectangle
        {
            Width = w,
            Height = h,
            Fill = Theme.FromHex(t.WeightChartPlotBackground)
        };
        WeightChartCanvas.Children.Add(plotBg);

        if (!TryReadWeightChartRange(out var from, out var to, out _))
        {
            AddChartHint(w, h, "横向日期格式无效（yyyy-MM-dd）。");
            return;
        }

        var spanDays = Math.Max(1, (to - from).TotalDays);
        var inRange = _weightEntries.Where(e => e.Date >= from && e.Date <= to).OrderBy(e => e.Date).ToList();
        // 纵轴固定按全部历史数据，不随横向显示范围伸缩
        var histMax = _weightEntries.Count > 0 ? _weightEntries.Max(e => e.Kg) : 80;
        var histMin = _weightEntries.Count > 0 ? _weightEntries.Min(e => e.Kg) : 40;
        var yMax = Math.Ceiling(histMax + 2);
        var yMin = Math.Floor(histMin - 2);
        if (yMax <= yMin) yMax = yMin + 1;
        var ySpan = yMax - yMin;
        const double padL = 44, padR = 16, padT = 16, padB = 28;
        var plotW = Math.Max(1, w - padL - padR);
        var plotH = Math.Max(1, h - padT - padB);

        var gridBrush = Theme.FromHex(t.WeightChartGrid);
        var textBrush = Theme.Brush("TextSecondaryBrush");
        for (var kg = (int)yMin; kg <= (int)yMax; kg++)
        {
            var y = padT + plotH * (1 - (kg - yMin) / ySpan);
            var grid = new Line
            {
                X1 = padL,
                X2 = padL + plotW,
                Y1 = y,
                Y2 = y,
                Stroke = gridBrush,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2, 2 }
            };
            WeightChartCanvas.Children.Add(grid);
            var label = new TextBlock
            {
                Text = kg.ToString(),
                FontSize = 11,
                Foreground = textBrush
            };
            WeightChartCanvas.Children.Add(label);
            Canvas.SetLeft(label, 4);
            Canvas.SetTop(label, y - 8);
        }

        var axisX = new Line
        {
            X1 = padL, X2 = padL + plotW, Y1 = padT + plotH, Y2 = padT + plotH,
            Stroke = gridBrush, StrokeThickness = 1.5
        };
        var axisY = new Line
        {
            X1 = padL, X2 = padL, Y1 = padT, Y2 = padT + plotH,
            Stroke = gridBrush, StrokeThickness = 1.5
        };
        WeightChartCanvas.Children.Add(axisX);
        WeightChartCanvas.Children.Add(axisY);

        void AddXLabel(DateTime day, double x)
        {
            var label = new TextBlock
            {
                Text = day.ToString("MM-dd"),
                FontSize = 11,
                Foreground = textBrush
            };
            WeightChartCanvas.Children.Add(label);
            Canvas.SetLeft(label, x - 14);
            Canvas.SetTop(label, padT + plotH + 6);
        }
        AddXLabel(from, padL);
        AddXLabel(to, padL + plotW);

        if (inRange.Count == 0)
        {
            AddChartHint(w, h, _weightEntries.Count == 0
                ? "还没有体重记录。点「记今天」或「批量粘贴…」。"
                : "当前横向范围内没有数据点。");
            return;
        }

        double XOf(DateTime d) => padL + plotW * ((d.Date - from).TotalDays / spanDays);
        double YOf(double kg) => padT + plotH * (1 - Math.Clamp((kg - yMin) / ySpan, 0, 1));

        var lineBrush = Theme.FromHex(t.WeightChartLine);
        if (inRange.Count >= 2)
        {
            var poly = new Polyline
            {
                Stroke = lineBrush,
                StrokeThickness = t.WeightChartLineThickness,
                StrokeLineJoin = PenLineJoin.Round
            };
            foreach (var (date, kg) in inRange)
                poly.Points.Add(new Point(XOf(date), YOf(kg)));
            WeightChartCanvas.Children.Add(poly);
        }

        var r = Math.Max(1, t.WeightChartPointSize / 2);
        foreach (var (date, kg) in inRange)
        {
            var cx = XOf(date);
            var cy = YOf(kg);
            var dot = new Ellipse
            {
                Width = r * 2,
                Height = r * 2,
                Fill = lineBrush,
                ToolTip = $"{WeightLogic.FormatDate(date)}  {kg:0.##} kg"
            };
            WeightChartCanvas.Children.Add(dot);
            Canvas.SetLeft(dot, cx - r);
            Canvas.SetTop(dot, cy - r);
        }
    }

    private void AddChartHint(double w, double h, string text)
    {
        if (WeightChartCanvas is null) return;
        var hint = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Width = Math.Max(80, w - 48),
            Foreground = Theme.Brush("TextSecondaryBrush"),
            TextAlignment = TextAlignment.Center
        };
        WeightChartCanvas.Children.Add(hint);
        Canvas.SetLeft(hint, 24);
        Canvas.SetTop(hint, Math.Max(24, h / 2 - 20));
    }

    private void UpdateWeightStatsText(double? latestKg, double heightCm, int ageYears, string sex, string activity)
    {
        if (WeightStatsText is null) return;
        if (latestKg is null || latestKg <= 0)
        {
            WeightStatsText.Text = "填写档案并记录体重后显示 BMI / BMR / TDEE。";
            return;
        }
        var bmi = WeightLogic.Bmi(latestKg.Value, heightCm);
        var bmr = WeightLogic.Bmr(latestKg.Value, heightCm, ageYears, sex);
        var tdee = WeightLogic.Tdee(bmr, activity);
        WeightStatsText.Text =
            $"最近体重 {latestKg.Value:0.##} kg · BMI {(bmi is null ? "—" : bmi.Value.ToString("0.##"))}（{WeightLogic.BmiCategory(bmi)}）" +
            $" · BMR {(bmr is null ? "—" : bmr.Value.ToString("0"))} · TDEE {(tdee is null ? "—" : tdee.Value.ToString("0"))}";
    }

    private bool TryReadWeightProfileFromUi(out double heightCm, out int ageYears, out string sex, out string activity, out string? error)
    {
        heightCm = 0;
        ageYears = 0;
        sex = SelectedComboTag(WeightSexBox) ?? "male";
        activity = SelectedComboTag(WeightActivityBox) ?? "sedentary";
        error = null;
        if (!double.TryParse((WeightHeightBox?.Text ?? "").Trim(), out heightCm) || heightCm < 50 || heightCm > 300)
        {
            error = "身高请填 50–300 cm 的数字。";
            return false;
        }
        if (!int.TryParse((WeightAgeBox?.Text ?? "").Trim(), out ageYears) || ageYears < 1 || ageYears > 120)
        {
            error = "年龄请填 1–120 的整数。";
            return false;
        }
        return true;
    }

    private async void WeightSaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!_session.WeightReady) return;
        if (!TryReadWeightProfileFromUi(out var height, out var age, out var sex, out var activity, out var error))
        {
            MessageBox.Show(error, "体重档案");
            return;
        }
        try
        {
            var fields = new Dictionary<string, object?>
            {
                ["HeightCm"] = height,
                ["AgeYears"] = age,
                ["Sex"] = sex,
                ["Activity"] = activity
            };
            if (string.IsNullOrEmpty(_weightProfileId))
            {
                fields["Title"] = "main";
                var created = await _session.Weight.CreateRecordAsync(StoreTables.WeightProfile, fields);
                _weightProfileId = NocoClient.ReadId(created);
            }
            else
            {
                fields["Id"] = _weightProfileId;
                await _session.Weight.PatchRecordAsync(StoreTables.WeightProfile, fields);
            }
            await LoadWeightAsync();
            StatusText.Text = "已保存体重档案";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "保存失败");
        }
    }

    private async void WeightToday_Click(object sender, RoutedEventArgs e)
    {
        if (!_session.WeightReady) return;
        var raw = TextPrompt.Ask(this, "记今天体重", "体重（kg）", "");
        if (raw is null) return;
        if (!WeightLogic.TryParseWeight(raw, out var kg, out var werr))
        {
            MessageBox.Show(werr ?? "体重不合法", "记今天");
            return;
        }
        try
        {
            await UpsertWeightEntryAsync(DateTime.Today, kg);
            await LoadWeightAsync();
            StatusText.Text = $"已记录今天体重 {kg:0.##} kg";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "记录失败");
        }
    }

    private async void WeightBatch_Click(object sender, RoutedEventArgs e)
    {
        if (!_session.WeightReady) return;
        var box = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinWidth = 420,
            MinHeight = 220,
            FontFamily = new FontFamily("Consolas"),
            Margin = new Thickness(0, 8, 0, 12)
        };
        var ok = new Button { Content = "确定", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "取消", Width = 80, IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = "每行：yyyy-MM-dd（或 a）与体重，中间用空格或 Tab 分隔（可粘贴表格；本框可按 Tab 输入制表符）。",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 480
        });
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        var win = new Window
        {
            Title = "批量粘贴体重",
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            Owner = this
        };
        Theme.Tint(win);
        var accepted = false;
        ok.Click += (_, _) => { accepted = true; win.DialogResult = true; };
        if (win.ShowDialog() != true || !accepted) return;
        try
        {
            var rows = WeightLogic.ParseBatch(box.Text);
            foreach (var (date, kg) in rows)
                await UpsertWeightEntryAsync(date, kg);
            await LoadWeightAsync();
            StatusText.Text = $"已导入 {rows.Count} 条体重记录";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "批量导入失败");
        }
    }

    private async Task UpsertWeightEntryAsync(DateTime date, double kg)
    {
        var dateStr = WeightLogic.FormatDate(date.Date);
        var rows = await _session.Weight.ListRecordsAsync(StoreTables.WeightEntries);
        foreach (var n in rows)
        {
            var d = WeightLogic.ParseEntryDate(n);
            if (d != date.Date) continue;
            var id = NocoClient.ReadId(n) ?? throw new InvalidOperationException("体重记录缺少 Id");
            await _session.Weight.PatchRecordAsync(StoreTables.WeightEntries, new Dictionary<string, object?>
            {
                ["Id"] = id,
                ["Date"] = dateStr,
                ["WeightKg"] = kg
            });
            return;
        }
        await _session.Weight.CreateRecordAsync(StoreTables.WeightEntries, new Dictionary<string, object?>
        {
            ["Date"] = dateStr,
            ["WeightKg"] = kg
        });
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

    private async Task LoadFavoritesAsync()
    {
        var rows = await _session.Favorites.ListRecordsAsync(StoreTables.Favorites);
        _favs.Clear();
        foreach (var n in rows)
        {
            if (n is not null)
                _favs.Add(FavoriteService.FromRecord(n));
        }
        await Task.WhenAll(_favs.Select(i => FavoriteService.LoadPreviewAsync(_session.Favorites, i)));
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
            FavWrap.Children.Add(HintBlock(hint));
        }
    }

    private UIElement CreateFavCard(FavoriteItem item)
    {
        var t = Theme.Current;
        var w = t.FavCardWidth;
        var thumbH = t.FavCardThumbHeight;
        var image = new System.Windows.Controls.Image
        {
            Height = thumbH,
            Stretch = Stretch.UniformToFill,
            Source = item.Preview
        };
        var check = new CheckBox { IsChecked = item.Selected, Margin = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        check.Checked += (_, _) => item.Selected = true;
        check.Unchecked += (_, _) => item.Selected = false;
        var thumb = new Grid { Height = thumbH };
        var thumbRadius = Math.Max(0, t.CardCornerRadius - 2);
        var thumbBorder = new Border
        {
            Height = thumbH,
            Background = item.Preview is null ? Theme.Brush("SurfaceBackgroundBrush") : Brushes.Transparent,
            Child = image,
            ClipToBounds = true,
            CornerRadius = new CornerRadius(thumbRadius)
        };
        UiShapes.RoundClip(thumbBorder, thumbRadius);
        thumb.Children.Add(thumbBorder);
        thumb.Children.Add(check);
        var caption = new TextBlock
        {
            Text = item.Title,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            Foreground = Theme.Brush("TextPrimaryBrush"),
            FontSize = t.FontSizeBody,
            Margin = new Thickness(0, 8, 0, 0),
            ToolTip = item.KindLabel + (string.IsNullOrWhiteSpace(item.Source) ? "" : "\n" + item.Source)
        };
        var stack = new StackPanel();
        stack.Children.Add(thumb);
        stack.Children.Add(caption);
        var border = new Border
        {
            Width = w,
            Height = t.FavCardHeight,
            Margin = new Thickness(6),
            Padding = new Thickness(10),
            Background = Theme.Brush("SurfaceBackgroundBrush"),
            BorderBrush = Theme.Brush("BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(t.CardCornerRadius),
            Child = stack,
            Cursor = Cursors.Hand,
            Tag = item
        };
        border.MouseLeftButtonDown += async (_, e) =>
        {
            if (e.ClickCount == 2)
                await EditFavoriteAsync(item);
        };
        return border;
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
            var path = await FavoriteService.DownloadBestAsync(_session.Favorites, item);
            var honey = _session.HoneyViewPath;
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
        var dlg = new FavoriteAddWindow { Owner = this };
        if (dlg.ShowDialog() != true) return;
        try
        {
            await CreateFavoriteAsync(dlg);
            await LoadFavoritesAsync();
            StatusText.Text = "已添加收藏「" + dlg.FavTitle + "」";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "添加失败");
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            e.Handled = true;
            if (WeekTab?.IsSelected == true)
                ToggleWeekFocusMode();
            return;
        }
        if (e.Key == Key.F5)
        {
            e.Handled = true;
            _ = ReloadAllAsync();
            return;
        }
        if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.OriginalSource is TextBox) return;
        if (FavTab?.IsSelected == true)
        {
            e.Handled = true;
            _ = ImportFromClipboardAsync();
            return;
        }
        if (WeekTab?.IsSelected == true)
        {
            e.Handled = true;
            _ = ImportTaskFromClipboardAsync();
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
            StatusText.Text = "拖入的不是图片。";
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
            StatusText.Text = "剪贴板里没有图片。";
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
                var dlg = new FavoriteAddWindow { Owner = this };
                dlg.PrefillCreate(filePath: path, title: title, kind: "image");
                if (dlg.ShowDialog() != true) continue;
                await CreateFavoriteAsync(dlg);
                added++;
                lastTitle = dlg.FavTitle;
            }
            if (added == 0)
            {
                StatusText.Text = "已取消添加。";
                return;
            }
            await LoadFavoritesAsync();
            StatusText.Text = added == 1
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
            var dlg = new FavoriteAddWindow { Owner = this };
            dlg.PrefillCreate(title: title, kind: "link", source: url);
            if (dlg.ShowDialog() != true)
            {
                StatusText.Text = "已取消添加。";
                return;
            }
            await CreateFavoriteAsync(dlg);
            await LoadFavoritesAsync();
            StatusText.Text = "已添加链接「" + dlg.FavTitle + "」";
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
        var dlg = new FavoriteAddWindow { Owner = this };
        item.OriginalPath = await DownloadAttachmentTempAsync(_session.Favorites, item.Original, "pm-fav-orig-");
        dlg.PrefillEdit(item);
        if (dlg.ShowDialog() != true) return;
        try
        {
            await UpdateFavoriteAsync(item.Id, dlg);
            await LoadFavoritesAsync();
            StatusText.Text = "已更新收藏「" + dlg.FavTitle + "」";
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
        await _session.Favorites.CreateRecordAsync(StoreTables.Favorites, new Dictionary<string, object?>
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
        await _session.Favorites.PatchRecordAsync(StoreTables.Favorites, fields);
    }

    private async Task<object> UploadFileAsync(string path) =>
        await _session.Favorites.UploadAsync(System.IO.Path.GetFileName(path), await File.ReadAllBytesAsync(path), MimeOf(path));

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
            var path = await FavoriteService.DownloadBestAsync(_session.Favorites, item);
            FavoriteService.CopyImage(path);
            StatusText.Text = "已复制到剪贴板：" + item.Title;
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
            await FavoriteService.ExportAsync(_session.Favorites, items, dlg.SelectedPath);
            StatusText.Text = $"已导出 {items.Count} 项";
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
            var rows = await _session.PinStore.ListRecordsAsync(StoreTables.State);
            var row = rows.FirstOrDefault();
            var stored = NocoClient.ReadString(row, "PrivatePin");
            if (string.IsNullOrWhiteSpace(stored) || stored == "null")
            {
                var created = PinWindow.Prompt(this, "设置二级密码", confirmTwice: true);
                if (created is null)
                {
                    FavPrivateBox.IsChecked = false;
                    return;
                }
                var id = NocoClient.ReadId(row);
                if (id is null)
                {
                    await _session.PinStore.CreateRecordAsync(StoreTables.State, new Dictionary<string, object?>
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
                    await _session.PinStore.PatchRecordAsync(StoreTables.State, new Dictionary<string, object?>
                    {
                        ["Id"] = id,
                        ["PrivatePin"] = FavoriteService.HashPin(created)
                    });
                }
                _privateUnlocked = true;
            }
            else
            {
                var input = PinWindow.Prompt(this, "二级密码");
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
        var name = TextPrompt.Ask(this, "另存为副本", "副本名称", Theme.Current.Name + " 副本");
        if (name is null) return;
        var copy = Theme.Current.Clone();
        copy.Id = "u-" + Guid.NewGuid().ToString("N")[..8];
        copy.Name = name;
        copy.Builtin = false;
        Theme.SaveCopy(copy);
        FillThemeBox();
        BuildSettingsEditor();
        StatusText.Text = "已保存样式副本「" + name + "」";
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
        StatusText.Text = "已删除样式副本";
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
        SettingsHost.Children.Add(ColorRow("窗口背景", t.WindowBackground, v => t.WindowBackground = v));
        SettingsHost.Children.Add(ColorRow("表面", t.SurfaceBackground, v => t.SurfaceBackground = v));
        SettingsHost.Children.Add(ColorRow("主文字", t.TextPrimary, v => t.TextPrimary = v));
        SettingsHost.Children.Add(ColorRow("次要文字", t.TextSecondary, v => t.TextSecondary = v));
        SettingsHost.Children.Add(ColorRow("强调色", t.Accent, v => t.Accent = v));
        SettingsHost.Children.Add(ColorRow("边框", t.BorderSubtle, v => t.BorderSubtle = v));
        SettingsHost.Children.Add(ColorRow("参考线", t.GridLine, v => t.GridLine = v));
        SettingsHost.Children.Add(ColorRow("输入框底", t.ControlBackground, v => t.ControlBackground = v));
        SettingsHost.Children.Add(ColorRow("危险", t.Danger, v => t.Danger = v));
        SettingsHost.Children.Add(ColorRow("滚动条槽", t.ScrollbarTrack, v => t.ScrollbarTrack = v));
        SettingsHost.Children.Add(ColorRow("滚动条滑块", t.ScrollbarThumb, v => t.ScrollbarThumb = v));
        SettingsHost.Children.Add(ColorRow("页签文字", t.TabText, v => t.TabText = v));
        SettingsHost.Children.Add(ColorRow("页签底", t.TabBackground, v => t.TabBackground = v));
        SettingsHost.Children.Add(ColorRow("页签悬停底", t.TabHoverBackground, v => t.TabHoverBackground = v));
        SettingsHost.Children.Add(ColorRow("页签选中底", t.TabSelectedBackground, v => t.TabSelectedBackground = v));
        SettingsHost.Children.Add(ColorRow("选项栏底", t.ComboBackground, v => t.ComboBackground = v));
        SettingsHost.Children.Add(ColorRow("选项栏文字", t.ComboText, v => t.ComboText = v));
        SettingsHost.Children.Add(ColorRow("选项栏悬停底", t.ComboHoverBackground, v => t.ComboHoverBackground = v));
        SettingsHost.Children.Add(ColorRow("选项栏悬停字", t.ComboHoverText, v => t.ComboHoverText = v));
        SettingsHost.Children.Add(ColorRow("选项栏选中底", t.ComboSelectedBackground, v => t.ComboSelectedBackground = v));
        SettingsHost.Children.Add(ColorRow("选项栏选中字", t.ComboSelectedText, v => t.ComboSelectedText = v));
        SettingsHost.Children.Add(ColorRow("悬浮提示底", t.TooltipBackground, v => t.TooltipBackground = v));
        SettingsHost.Children.Add(ColorRow("悬浮提示字", t.TooltipText, v => t.TooltipText = v));
        SettingsHost.Children.Add(ColorRow("按钮底", t.ButtonBackground, v => t.ButtonBackground = v));
        SettingsHost.Children.Add(ColorRow("按钮字", t.ButtonText, v => t.ButtonText = v));
        SettingsHost.Children.Add(ColorRow("按钮悬停底", t.ButtonHoverBackground, v => t.ButtonHoverBackground = v));
        SettingsHost.Children.Add(ColorRow("按钮悬停字", t.ButtonHoverText, v => t.ButtonHoverText = v));
        SettingsHost.Children.Add(NumberRow("正文字号", t.FontSizeBody, 10, 22, v => t.FontSizeBody = v));
        SettingsHost.Children.Add(NumberRow("标题字号", t.FontSizeTitle, 12, 28, v => t.FontSizeTitle = v));
        SettingsHost.Children.Add(NumberRow("卡片圆角", t.CardCornerRadius, 0, 24, v => t.CardCornerRadius = v));

        Sec("日程记录");
        SettingsHost.Children.Add(ColorRow("本周列底", t.WeekColumnBackground, v => t.WeekColumnBackground = v));
        SettingsHost.Children.Add(ColorRow("二四六列底", t.WeekAltColumnBackground, v => t.WeekAltColumnBackground = v));
        SettingsHost.Children.Add(ColorRow("暂停块", t.PauseOverlay, v => t.PauseOverlay = v));
        SettingsHost.Children.Add(ColorRow("溢出条底", t.OverflowTrack, v => t.OverflowTrack = v));
        SettingsHost.Children.Add(ColorRow("溢出条进度", t.OverflowFill, v => t.OverflowFill = v));
        SettingsHost.Children.Add(NumberRow("每小时高度（常态）", t.WeekPxPerHour, 8, 150, v => t.WeekPxPerHour = v));
        SettingsHost.Children.Add(NumberRow("每小时高度（聚焦）", t.WeekFocusPxPerHour, 8, 200, v => t.WeekFocusPxPerHour = v));
        SettingsHost.Children.Add(ColorRow("当前时间线颜色", t.WeekNowLine, v => t.WeekNowLine = v));
        SettingsHost.Children.Add(NumberRow("当前时间线粗细", t.WeekNowLineThickness, 0.5, 8, v => t.WeekNowLineThickness = v));
        SettingsHost.Children.Add(NumberRow("当前时间线透明度", t.WeekNowLineOpacity, 0.05, 1, v => t.WeekNowLineOpacity = v));
        SettingsHost.Children.Add(new TextBlock
        {
            Text = "每日仅显示一部分时间（整点，0–24；填 0 与 24 则显示全日。默认 6–23 即显示到 23:00）",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Theme.Brush("TextSecondaryBrush"),
            Margin = new Thickness(0, 4, 0, 4)
        });
        SettingsHost.Children.Add(NumberRow("显示起始时", t.WeekHourStart, 0, 24, v =>
        {
            t.WeekHourStart = (int)Math.Round(v);
            if (t.WeekHourEnd <= t.WeekHourStart)
                t.WeekHourEnd = Math.Min(24, t.WeekHourStart + 1);
        }, integer: true));
        SettingsHost.Children.Add(NumberRow("显示结束时", t.WeekHourEnd, 0, 24, v =>
        {
            t.WeekHourEnd = (int)Math.Round(v);
            if (t.WeekHourEnd <= t.WeekHourStart)
                t.WeekHourStart = Math.Max(0, t.WeekHourEnd - 1);
        }, integer: true));
        SettingsHost.Children.Add(NumberRow("任务卡宽度", t.ScheduleCardWidth, 80, 360, v => t.ScheduleCardWidth = v));
        SettingsHost.Children.Add(NumberRow("任务卡高度", t.ScheduleCardHeight, 120, 400, v => t.ScheduleCardHeight = v));
        SettingsHost.Children.Add(NumberRow("任务图区高度", t.ScheduleCardThumbHeight, 48, 240, v => t.ScheduleCardThumbHeight = v));
        SettingsHost.Children.Add(NumberRow("开始按钮尺寸", t.StartTaskButtonSize, 16, 64, v => t.StartTaskButtonSize = v));
        SettingsHost.Children.Add(NumberRow("开始按钮圆角", t.StartTaskButtonCornerRadius, 0, 32, v => t.StartTaskButtonCornerRadius = v));
        SettingsHost.Children.Add(IconRow());

        Sec("奖励 / 愿望单");
        SettingsHost.Children.Add(NumberRow("卡片宽度", t.CardWidth, 80, 360, v => t.CardWidth = v));
        SettingsHost.Children.Add(NumberRow("卡片高度", t.CardHeight, 120, 400, v => t.CardHeight = v));
        SettingsHost.Children.Add(NumberRow("图区高度", t.CardThumbHeight, 48, 240, v => t.CardThumbHeight = v));

        Sec("收藏夹");
        SettingsHost.Children.Add(NumberRow("收藏卡宽度", t.FavCardWidth, 80, 360, v => t.FavCardWidth = v));
        SettingsHost.Children.Add(NumberRow("收藏卡高度", t.FavCardHeight, 80, 360, v => t.FavCardHeight = v));
        SettingsHost.Children.Add(NumberRow("收藏图区高度", t.FavCardThumbHeight, 40, 320, v => t.FavCardThumbHeight = v));

        Sec("体重");
        SettingsHost.Children.Add(ColorRow("折线颜色", t.WeightChartLine, v => t.WeightChartLine = v));
        SettingsHost.Children.Add(ColorRow("网格线颜色", t.WeightChartGrid, v => t.WeightChartGrid = v));
        SettingsHost.Children.Add(ColorRow("图区背景", t.WeightChartPlotBackground, v => t.WeightChartPlotBackground = v));
        SettingsHost.Children.Add(NumberRow("折线粗细", t.WeightChartLineThickness, 1, 8, v => t.WeightChartLineThickness = v));
        SettingsHost.Children.Add(NumberRow("数据点大小", t.WeightChartPointSize, 2, 16, v => t.WeightChartPointSize = v));
        SettingsHost.Children.Add(NumberRow("图区高度", t.WeightChartHeight, 120, 800, v => t.WeightChartHeight = v));

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

    private UIElement ColorRow(string label, string hex, Action<string> set)
    {
        var currentHex = hex;
        var swatch = new Border
        {
            Width = 36,
            Height = 22,
            Background = Theme.FromHex(hex),
            BorderBrush = Theme.Brush("BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var box = new TextBox { Text = hex, Width = 90, VerticalAlignment = VerticalAlignment.Center };
        var pick = new Button { Content = "取色…" };
        pick.Click += (_, _) =>
        {
            if (!GuardEditable()) return;
            using var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true };
            var c = TaskVisual.ParseColor(currentHex);
            dlg.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            ApplyColor($"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}");
        };
        box.LostFocus += (_, _) => ApplyColor(box.Text.Trim());
        void ApplyColor(string value)
        {
            if (_settingsBuilding) return;
            if (!GuardEditable()) { box.Text = currentHex; return; }
            set(value);
            currentHex = value;
            box.Text = value;
            swatch.Background = Theme.FromHex(value);
            CommitThemeEdits();
        }
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new TextBlock { Text = label, Width = 120, VerticalAlignment = VerticalAlignment.Center });
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(swatch);
        right.Children.Add(box);
        right.Children.Add(pick);
        row.Children.Add(right);
        return row;
    }

    private UIElement NumberRow(string label, double value, double min, double max, Action<double> set, bool integer = false)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Width = 180, VerticalAlignment = VerticalAlignment.Center };
        var box = new TextBox { Text = integer ? ((int)Math.Round(value)).ToString() : value.ToString("0"), Width = 56, Margin = new Thickness(8, 0, 0, 0) };
        slider.ValueChanged += (_, _) =>
        {
            if (_settingsBuilding) return;
            if (!GuardEditable()) return;
            var v = integer ? Math.Round(slider.Value) : Math.Round(slider.Value, 1);
            box.Text = integer ? ((int)v).ToString() : v.ToString("0.#");
            set(v);
            CommitThemeEdits();
        };
        box.LostFocus += (_, _) =>
        {
            if (_settingsBuilding) return;
            if (!double.TryParse(box.Text.Trim(), out var parsed)) return;
            parsed = Math.Clamp(parsed, min, max);
            slider.Value = parsed;
        };
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new TextBlock { Text = label, Width = 120, VerticalAlignment = VerticalAlignment.Center });
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(slider);
        right.Children.Add(box);
        row.Children.Add(right);
        return row;
    }

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
