using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PersonalManagement.Desktop;

internal sealed class ScheduleNoteRow
{
    public string Id { get; set; } = "";
    public DateTime At { get; set; }
    public double DayColumnPercent { get; set; }
    public string Body { get; set; } = "";
}

internal sealed class WeekSpan
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

/// <summary>周视图看板：仅持临时 UI 状态，不负责持久化。</summary>
internal sealed class WeekBoard
{
    private readonly Border _weekHost;
    private readonly ScrollViewer _weekScroll;
    private readonly Button _goToNowButton;
    private readonly DispatcherTimer _weekNowTimer;

    private DateTime _weekStart;
    private List<WeekSpan> _weekSpans = [];
    private List<ScheduleNoteRow> _weekNotes = [];
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
    private const double NotePinSize = 10;
    private const int ScheduleTipDelayMs = 100;

    private readonly List<(DateTime Day, Canvas Canvas)> _weekDayCanvases = [];
    private readonly List<Line> _hoverGuideLines = [];
    private TextBlock? _hoverTimeLabel;
    private System.Windows.Controls.Primitives.Popup? _notePopup;
    private string? _openNoteId;
    private DateTime _pendingNoteAt;
    private double _pendingNotePercent;

    public WeekBoard(Border weekHost, ScrollViewer weekScroll, Button goToNowButton)
    {
        _weekHost = weekHost;
        _weekScroll = weekScroll;
        _goToNowButton = goToNowButton;
        _weekNowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _weekNowTimer.Tick += (_, _) => UpdateWeekNowLine();
        _weekNowTimer.Start();
        UpdateGoToNowButton();
    }

    public string? SelectedSessionId { get; set; }
    public DateTime WeekStart => _weekStart;
    public IReadOnlyList<WeekSpan> Spans => _weekSpans;
    public DateTime? MarkRangeStart => _markRangeStart;
    public DateTime? MarkRangeEnd => _markRangeEnd;

    public Func<bool> IsRunActive { get; set; } = () => false;
    public Func<IEnumerable<TaskRow>> GetMarkableTasks { get; set; } = () => [];
    public Action<string>? SetStatusText { get; set; }
    public Func<TaskRow, Task>? OnEditTask { get; set; }
    public Func<DateTime, double, Task>? OnAddNotePin { get; set; }
    public Func<ScheduleNoteRow, Task>? OnEditNotePin { get; set; }
    public Func<TaskRow, Task>? OnMarkSelectionAsTask { get; set; }

    /// <summary>可见整点区间 [Start, End]，底边为 End:00；0–24 与全日一致。</summary>
    public static (int Start, int End) VisibleWeekHours
    {
        get
        {
            var s = Math.Clamp(Theme.Current.WeekHourStart, 0, 24);
            var e = Math.Clamp(Theme.Current.WeekHourEnd, 0, 24);
            if (e <= s) return (0, 24);
            return (s, e);
        }
    }

    private double EffectiveWeekPxPerHour =>
        _weekFocusMode ? Theme.Current.WeekFocusPxPerHour : Theme.Current.WeekPxPerHour;

    private static double WeekDayHeight(double pxPerHour)
    {
        var (s, e) = VisibleWeekHours;
        return Math.Max(pxPerHour, (e - s) * pxPerHour);
    }

    private static double YFromDayTime(DateTime dayStart, DateTime t, double pxPerHour)
    {
        var (s, _) = VisibleWeekHours;
        return ((t - dayStart).TotalHours - s) * pxPerHour;
    }

    private static DateTime DayTimeFromY(DateTime day, double y, double pxPerHour)
    {
        var (s, _) = VisibleWeekHours;
        return day.Date.AddHours(s + y / pxPerHour);
    }

    public void SetData(DateTime weekStart, List<WeekSpan> spans, List<ScheduleNoteRow> notes)
    {
        _weekStart = weekStart;
        _weekSpans = spans;
        _weekNotes = notes;
        if (SelectedSessionId is not null && spans.All(x => x.SessionId != SelectedSessionId))
            SelectedSessionId = null;
    }

    public void ClearData()
    {
        _weekSpans = [];
        _weekNotes = [];
        SelectedSessionId = null;
        ClearMarkSelection(rerender: false);
    }

    public WeekSpan? FindSpan(string sessionId) =>
        _weekSpans.FirstOrDefault(s => s.SessionId == sessionId);

    public void Render() => RenderWeekBoard(_weekStart, _weekSpans);

    private void RenderWeekBoard(DateTime weekStart, List<WeekSpan> spans)
    {
        CloseNotePopup();
        var pxPerHour = EffectiveWeekPxPerHour;
        var (hourStart, hourEnd) = VisibleWeekHours;
        var height = WeekDayHeight(pxPerHour);
        _weekNowLines.Clear();
        _weekTodayDots.Clear();
        _weekDayCanvases.Clear();
        _hoverGuideLines.Clear();
        _hoverTimeLabel = null;
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
        _hoverTimeLabel = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Brush("AccentBrush"),
            Visibility = Visibility.Collapsed
        };
        labels.Children.Add(_hoverTimeLabel);
        var labelGuide = new Line
        {
            X1 = 0,
            X2 = 40,
            Stroke = Theme.Brush("AccentBrush"),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 2, 2 },
            Opacity = 0.85,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Tag = "hoverGuide"
        };
        labels.Children.Add(labelGuide);
        _hoverGuideLines.Add(labelGuide);
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
                    else if (child is Line line && (tag is "hour" or "hourStrong" or "now" or "hoverGuide"))
                    {
                        line.X2 = colW;
                    }
                    else if (tag == "mark")
                    {
                        child.Width = Math.Max(20, colW - 4);
                        Canvas.SetLeft(child, 2);
                    }
                    else if (tag == "notePin" && child.DataContext is ScheduleNoteRow note)
                    {
                        Canvas.SetLeft(child, note.DayColumnPercent * colW - child.Width / 2);
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
            _weekDayCanvases.Add((day.Date, canvas));

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
                var selected = span.SessionId == SelectedSessionId;
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
                        if (span.Task is not null && OnEditTask is { } edit)
                            await edit(span.Task);
                        return;
                    }
                    SelectedSessionId = SelectedSessionId == sessionId ? null : sessionId;
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

            foreach (var note in _weekNotes)
            {
                if (note.At.Date != dayStart) continue;
                if (note.At < winStart || note.At > winEnd) continue;
                AddNotePinVisual(canvas, note, dayStart, pxPerHour);
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

            var hoverLine = new Line
            {
                X1 = 0,
                X2 = 4000,
                Stroke = Theme.Brush("AccentBrush"),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2, 2 },
                Opacity = 0.9,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                Tag = "hoverGuide"
            };
            Panel.SetZIndex(hoverLine, 40);
            canvas.Children.Add(hoverLine);
            _hoverGuideLines.Add(hoverLine);

            DrawMarkRectOnCanvas(canvas, day, pxPerHour, height);

            Grid.SetColumn(canvas, d + 1);
            Grid.SetRow(canvas, 1);
            root.Children.Add(canvas);
        }

        _weekHost.Child = root;
        UpdateWeekNowLine();
        if (_weekFollowNow)
            ScrollWeekToNow();
        UpdateGoToNowButton();
    }

    public void UpdateWeekNowLine()
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
        if (_weekScroll is null || _weekHost?.Child is null) return;
        var now = DateTime.Now;
        if (now.Date < _weekStart || now.Date >= _weekStart.AddDays(7)) return;
        var px = EffectiveWeekPxPerHour;
        var (hourStart, hourEnd) = VisibleWeekHours;
        var hod = Math.Clamp(now.TimeOfDay.TotalHours, hourStart, hourEnd);
        var y = (hod - hourStart) * px;
        var target = Math.Max(0, y - _weekScroll.ViewportHeight * 0.25);
        _weekIgnoreScroll = true;
        _weekScroll.ScrollToVerticalOffset(target);
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
        if (_goToNowButton is null) return;
        _goToNowButton.Visibility = _weekFollowNow ? Visibility.Collapsed : Visibility.Visible;
    }

    public void GoToNow()
    {
        _weekFollowNow = true;
        UpdateGoToNowButton();
        ScrollWeekToNow();
        UpdateWeekNowLine();
    }

    public void ToggleFocusMode()
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
        SetStatusText?.Invoke(_weekFocusMode ? "已进入聚焦模式（F1 退出）" : "已退出聚焦模式");
    }

    public void OnScrollChanged(ScrollChangedEventArgs e)
    {
        if (_weekIgnoreScroll) return;
        if (e.VerticalChange == 0) return;
        StopWeekFollow();
    }

    public void OnPreviewMouseWheel() => StopWeekFollow();

    private void WireWeekColumnCanvas(Canvas canvas, DateTime day, double height, double pxPerHour)
    {
        canvas.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is FrameworkElement fe && fe.Tag as string is "span" or "notePin")
                return;
            if (e.OriginalSource is not Canvas && e.OriginalSource is FrameworkElement src
                && src.Tag as string is "name" or "pause" or "mark")
                return;

            CloseNotePopup();
            SelectedSessionId = null;
            CancelMarkPress();
            ClearMarkSelection(rerender: false);
            if (IsRunActive())
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
            if (!_markDragging && !_markPressArmed)
                UpdateHoverGuide(ClampY(e.GetPosition(canvas).Y, height), pxPerHour);

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
        canvas.MouseLeave += (_, _) =>
        {
            canvas.Dispatcher.BeginInvoke(() =>
            {
                if (_weekDayCanvases.Any(c => c.Canvas.IsMouseOver)) return;
                HideHoverGuide();
            });
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
            var pos = e.GetPosition(canvas);
            ShowWeekContextMenu(canvas, day.Date, pos, height, pxPerHour);
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
            ShowWeekContextMenu(canvas, day.Date, e.GetPosition(canvas), WeekDayHeight(pxPerHour), pxPerHour);
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

    public void ClearMarkSelection(bool rerender)
    {
        _markRangeStart = null;
        _markRangeEnd = null;
        _markDragging = false;
        if (rerender)
            RenderWeekBoard(_weekStart, _weekSpans);
    }

    public bool CanMarkSelection()
    {
        if (IsRunActive()) return false;
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

    private void ShowWeekContextMenu(Canvas canvas, DateTime day, Point pos, double height, double pxPerHour)
    {
        var y = ClampY(pos.Y, height);
        _pendingNoteAt = DayTimeFromY(day, y, pxPerHour);
        var colW = Math.Max(1, canvas.ActualWidth);
        _pendingNotePercent = Math.Clamp(pos.X / colW, 0, 1);

        var menu = new ContextMenu();
        var addPin = new MenuItem { Header = "添加笔记钉" };
        addPin.Click += async (_, _) =>
        {
            if (OnAddNotePin is { } add)
                await add(_pendingNoteAt, _pendingNotePercent);
        };
        menu.Items.Add(addPin);

        var hasSelection = _markRangeStart is not null && _markRangeEnd is not null
                           && _markRangeStart.Value.Date == day.Date;
        if (hasSelection)
        {
            var root = new MenuItem
            {
                Header = "标记为指定活动",
                IsEnabled = CanMarkSelection(),
                ToolTip = IsRunActive() ? "有任务正在执行，不可框选补记" : null
            };
            foreach (var task in GetMarkableTasks().OrderBy(t => t.Title))
            {
                var item = new MenuItem { Header = task.Title, Tag = task };
                item.Click += async (_, _) =>
                {
                    if (OnMarkSelectionAsTask is { } mark)
                        await mark(task);
                };
                root.Items.Add(item);
            }
            if (root.Items.Count == 0)
                root.Items.Add(new MenuItem { Header = "（无可用任务）", IsEnabled = false });
            menu.Items.Add(root);
        }

        canvas.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private void UpdateHoverGuide(double y, double pxPerHour)
    {
        var (s, _) = VisibleWeekHours;
        var t = TimeSpan.FromHours(s + y / pxPerHour);
        foreach (var line in _hoverGuideLines)
        {
            line.Visibility = Visibility.Visible;
            line.Y1 = y;
            line.Y2 = y;
        }
        if (_hoverTimeLabel is not null)
        {
            _hoverTimeLabel.Visibility = Visibility.Visible;
            _hoverTimeLabel.Text = $"{(int)t.TotalHours:00}:{t.Minutes:00}";
            Canvas.SetTop(_hoverTimeLabel, Math.Max(0, y - 8));
            Canvas.SetLeft(_hoverTimeLabel, 0);
        }
    }

    private void HideHoverGuide()
    {
        foreach (var line in _hoverGuideLines)
            line.Visibility = Visibility.Collapsed;
        if (_hoverTimeLabel is not null)
            _hoverTimeLabel.Visibility = Visibility.Collapsed;
    }

    private void AddNotePinVisual(Canvas canvas, ScheduleNoteRow note, DateTime dayStart, double pxPerHour)
    {
        var pin = new Ellipse
        {
            Width = NotePinSize,
            Height = NotePinSize,
            Fill = Theme.Brush("AccentBrush"),
            Stroke = Theme.Brush("WindowBackgroundBrush"),
            StrokeThickness = 1.5,
            Tag = "notePin",
            DataContext = note,
            Cursor = Cursors.Hand,
            ToolTip = note.Body.Length > 80 ? note.Body[..80] + "…" : note.Body
        };
        Panel.SetZIndex(pin, 60);
        var y = YFromDayTime(dayStart, note.At, pxPerHour);
        Canvas.SetTop(pin, y - NotePinSize / 2);
        var colW = Math.Max(40, canvas.ActualWidth > 0 ? canvas.ActualWidth : 80);
        Canvas.SetLeft(pin, note.DayColumnPercent * colW - NotePinSize / 2);
        pin.MouseLeftButtonDown += async (_, e) =>
        {
            e.Handled = true;
            CloseNotePopup();
            if (e.ClickCount >= 2)
            {
                if (OnEditNotePin is { } edit)
                    await edit(note);
                return;
            }
            ShowNotePopup(pin, note);
        };
        canvas.Children.Add(pin);
    }

    private void ShowNotePopup(FrameworkElement anchor, ScheduleNoteRow note)
    {
        CloseNotePopup();
        var border = new Border
        {
            Background = Theme.Brush("SurfaceBackgroundBrush"),
            BorderBrush = Theme.Brush("BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(4),
            MaxWidth = 280,
            Child = new TextBlock
            {
                Text = note.Body,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Theme.Brush("TextPrimaryBrush")
            }
        };
        _notePopup = new System.Windows.Controls.Primitives.Popup
        {
            Child = border,
            PlacementTarget = anchor,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true
        };
        _openNoteId = note.Id;
        _notePopup.Closed += (_, _) =>
        {
            if (_openNoteId == note.Id)
            {
                _openNoteId = null;
                _notePopup = null;
            }
        };
        _notePopup.IsOpen = true;
    }

    private void CloseNotePopup()
    {
        if (_notePopup is not null)
            _notePopup.IsOpen = false;
        _notePopup = null;
        _openNoteId = null;
    }

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

    public static void ScheduleTip(FrameworkElement el, object tip)
    {
        el.ToolTip = tip;
        ToolTipService.SetInitialShowDelay(el, ScheduleTipDelayMs);
    }
}
