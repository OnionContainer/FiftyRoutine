using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace PersonalManagement.Desktop;

internal static class WindowBounds
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static bool _suppress;
    private static WindowState _lastShown = WindowState.Normal;
    private static DispatcherTimer? _timer;
    private static Window? _target;
    private static int _taskRailColumns = 1;
    private static bool _suppressHiddenHourTip;
    private static bool _scheduleStatsExpanded;
    private static double _scheduleStatsHeight = 240;
    private static int _scheduleStatsDays = 14;

    public static string FilePath
    {
        get
        {
            if (AppPaths.CurrentUser is null)
                return Path.Combine(AppPaths.ProgramDataDir, "_prelogin-window.json");
            return Path.Combine(AppPaths.CurrentUserDir, "window.json");
        }
    }

    /// <summary>日程左侧任务栏容纳几列卡片（持久化；像素宽由卡片尺寸现场计算）。</summary>
    public static int TaskRailColumns => Math.Clamp(_taskRailColumns, 1, 12);

    /// <summary>是否不再提示「日程块落在未显示时间」。</summary>
    public static bool SuppressHiddenHourTip => _suppressHiddenHourTip;

    public static bool ScheduleStatsExpanded => _scheduleStatsExpanded;
    public static double ScheduleStatsHeight => Math.Clamp(_scheduleStatsHeight, 120, 600);
    public static int ScheduleStatsDays => _scheduleStatsDays is 7 or 30 ? _scheduleStatsDays : 14;

    public static void SetSuppressHiddenHourTip(bool value)
    {
        if (_suppressHiddenHourTip == value) return;
        _suppressHiddenHourTip = value;
        if (_target is not null)
            ScheduleSave(_target);
        else
            PersistLayoutOnly();
    }

    public static void SetScheduleStatsExpanded(bool expanded)
    {
        if (_scheduleStatsExpanded == expanded) return;
        _scheduleStatsExpanded = expanded;
        if (_target is not null)
            ScheduleSave(_target);
        else
            PersistLayoutOnly();
    }

    public static void SetScheduleStatsHeight(double height)
    {
        var next = Math.Clamp(height, 120, 600);
        if (Math.Abs(next - _scheduleStatsHeight) < 0.5) return;
        _scheduleStatsHeight = next;
        if (_target is not null)
            ScheduleSave(_target);
        else
            PersistLayoutOnly();
    }

    public static void SetScheduleStatsDays(int days)
    {
        var next = days is 7 or 30 ? days : 14;
        if (next == _scheduleStatsDays) return;
        _scheduleStatsDays = next;
        if (_target is not null)
            ScheduleSave(_target);
        else
            PersistLayoutOnly();
    }

    public static void SetTaskRailColumns(int cols)
    {
        var next = Math.Clamp(cols, 1, 12);
        if (next == _taskRailColumns) return;
        _taskRailColumns = next;
        if (_target is not null)
            ScheduleSave(_target);
        else
            PersistLayoutOnly();
    }

    public static void Restore(Window window)
    {
        FileState? saved = null;
        try
        {
            if (File.Exists(FilePath))
                saved = JsonSerializer.Deserialize<FileState>(File.ReadAllText(FilePath), JsonOptions);
        }
        catch { /* keep defaults */ }
        if (saved is not null)
        {
            _taskRailColumns = Math.Clamp(saved.TaskRailColumns <= 0 ? 1 : saved.TaskRailColumns, 1, 12);
            _suppressHiddenHourTip = saved.SuppressHiddenHourTip;
            _scheduleStatsExpanded = saved.ScheduleStatsExpanded;
            _scheduleStatsHeight = saved.ScheduleStatsHeight > 0 ? saved.ScheduleStatsHeight : 240;
            _scheduleStatsDays = saved.ScheduleStatsDays is 7 or 30 ? saved.ScheduleStatsDays : 14;
        }

        if (saved is null || saved.Width < 200 || saved.Height < 160)
            return;

        _suppress = true;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        var width = saved.Width;
        var height = saved.Height;
        var left = saved.Left;
        var top = saved.Top;
        Clamp(ref left, ref top, ref width, ref height);
        window.Width = width;
        window.Height = height;
        window.Left = left;
        window.Top = top;
        if (saved.Maximized)
        {
            window.WindowState = WindowState.Maximized;
            _lastShown = WindowState.Maximized;
        }
        else
            _lastShown = WindowState.Normal;
        _suppress = false;
    }

    public static void Attach(Window window)
    {
        _target = window;
        _lastShown = window.WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        window.SizeChanged += (_, _) => ScheduleSave(window);
        window.LocationChanged += (_, _) => ScheduleSave(window);
        window.StateChanged += (_, _) =>
        {
            if (window.WindowState != WindowState.Minimized)
                _lastShown = window.WindowState;
            ScheduleSave(window);
        };
        window.Closing += (_, _) => Save(window);
    }

    public static void RestoreFromTray(Window window)
    {
        window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = _lastShown == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        window.Activate();
    }

    public static void Save(Window? window = null)
    {
        window ??= _target;
        if (window is null || _suppress) return;
        if (window.WindowState == WindowState.Minimized) return;
        var bounds = window.RestoreBounds;
        if (bounds.Width < 200 || bounds.Height < 160) return;
        var left = bounds.Left;
        var top = bounds.Top;
        var width = bounds.Width;
        var height = bounds.Height;
        Clamp(ref left, ref top, ref width, ref height);
        WriteState(new FileState
        {
            Width = width,
            Height = height,
            Left = left,
            Top = top,
            Maximized = window.WindowState == WindowState.Maximized,
            TaskRailColumns = TaskRailColumns,
            SuppressHiddenHourTip = SuppressHiddenHourTip,
            ScheduleStatsExpanded = ScheduleStatsExpanded,
            ScheduleStatsHeight = ScheduleStatsHeight,
            ScheduleStatsDays = ScheduleStatsDays
        });
    }

    private static void PersistLayoutOnly()
    {
        FileState state;
        try
        {
            if (File.Exists(FilePath))
                state = JsonSerializer.Deserialize<FileState>(File.ReadAllText(FilePath), JsonOptions) ?? new FileState();
            else
                state = new FileState();
        }
        catch
        {
            state = new FileState();
        }
        state.TaskRailColumns = TaskRailColumns;
        state.SuppressHiddenHourTip = SuppressHiddenHourTip;
        state.ScheduleStatsExpanded = ScheduleStatsExpanded;
        state.ScheduleStatsHeight = ScheduleStatsHeight;
        state.ScheduleStatsDays = ScheduleStatsDays;
        WriteState(state);
    }

    private static void WriteState(FileState state)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch { /* ignore disk errors */ }
    }

    private static void ScheduleSave(Window window)
    {
        if (_suppress) return;
        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _target = window;
        _timer.Start();
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        _timer?.Stop();
        Save(_target);
    }

    private static void Clamp(ref double left, ref double top, ref double width, ref double height)
    {
        var vsL = SystemParameters.VirtualScreenLeft;
        var vsT = SystemParameters.VirtualScreenTop;
        var vsW = SystemParameters.VirtualScreenWidth;
        var vsH = SystemParameters.VirtualScreenHeight;
        width = Math.Clamp(width, 200, Math.Max(200, vsW));
        height = Math.Clamp(height, 160, Math.Max(160, vsH));
        if (left + width < vsL + 80) left = vsL;
        if (top + height < vsT + 80) top = vsT;
        if (left > vsL + vsW - 80) left = vsL + vsW - width;
        if (top > vsT + vsH - 80) top = vsT + vsH - height;
    }

    private sealed class FileState
    {
        public double Width { get; set; } = 980;
        public double Height { get; set; } = 640;
        public double Left { get; set; }
        public double Top { get; set; }
        public bool Maximized { get; set; }
        public int TaskRailColumns { get; set; } = 1;
        public bool SuppressHiddenHourTip { get; set; }
        public bool ScheduleStatsExpanded { get; set; }
        public double ScheduleStatsHeight { get; set; } = 240;
        public int ScheduleStatsDays { get; set; } = 14;
    }
}
