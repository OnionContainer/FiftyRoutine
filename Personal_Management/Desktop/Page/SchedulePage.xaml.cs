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
        _weekNowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _weekNowTimer.Tick += (_, _) => UpdateWeekNowLine();
        _weekNowTimer.Start();
        _runTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _runTimer.Tick += (_, _) => OnRunTick();
        ApplyTaskRailWidth();
        UpdateGoToNowButton();
        UpdateCurrentRunChrome();

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
    private DateTime _weekStart;
    private string? _selectedSessionId;
    private List<WeekSpan> _weekSpans = [];
    private DispatcherTimer _weekNowTimer = null!;
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

    private async Task<(int Tickets, int Quota)> ReadWalletAsync()
    {
        var rows = await _host.Session.Business.ListRecordsAsync(StoreTables.State);
        var row = rows.FirstOrDefault();
        var tickets = NocoClient.ReadInt(row, "DrawTickets");
        var quota = NocoClient.ReadInt(row, "WishlistQuota");
        return (tickets, quota);
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

    private static void SetOfflineOverlay(UIElement? overlay, UIElement? content, bool show)
    {
        if (overlay is null || content is null) return;
        overlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        content.IsEnabled = !show;
        content.Opacity = show ? 0.35 : 1;
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
                var fill = BlockPatterns.CreateBrush(span.Task?.ResolveStyle(), 480, blockH).Clone();
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
                rect.MouseLeftButtonDown += async (_, e) =>
                {
                    e.Handled = true;
                    if (e.ClickCount >= 2)
                    {
                        if (span.Task is not null)
                            await EditTaskAsync(span.Task);
                        return;
                    }
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
        _host.StatusText = _weekFocusMode ? "已进入聚焦模式（F1 退出）" : "已退出聚焦模式";
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
            ClearMarkSelection(rerender: false);
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
            await _host.Session.Business.DeleteRecordAsync(StoreTables.Sessions, span.SessionId);
            _selectedSessionId = null;
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
                ? BlockPatterns.CreateBrush(task.ResolveStyle(), w, thumbH)
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
        SetOfflineOverlay(WeekOfflineOverlay, WeekContent, !_host.Session.BusinessReady);
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
        _weekSpans = [];
        RenderWeekBoard(_weekStart, _weekSpans);
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
        if (_weekSpans.Count > 0 || WeekHost.Child is not null)
            RenderWeekBoard(_weekStart, _weekSpans);
    }

    public void ToggleFocusFromHost() => ToggleWeekFocusMode();

    public Task ImportClipboardFromHostAsync() => ImportTaskFromClipboardAsync();

    private async void TryConnectNoco_Click(object sender, RoutedEventArgs e) =>
        await _host.TryConnectNocoAsync();

    private TextBlock HintBlock(string text) => new()
    {
        Text = text,
        Margin = new Thickness(8),
        Foreground = Theme.Brush("TextSecondaryBrush"),
        TextWrapping = TextWrapping.Wrap
    };

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
