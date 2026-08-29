using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PersonalManagement.Desktop;

internal sealed class ThemeStyle
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Builtin { get; set; }

    public string WindowBackground { get; set; } = "#FFFFFF";
    public string SurfaceBackground { get; set; } = "#FAFAFA";
    public string TextPrimary { get; set; } = "#1A1A1A";
    public string TextSecondary { get; set; } = "#6B6B6B";
    public string Accent { get; set; } = "#4682B4";
    public string BorderSubtle { get; set; } = "#D0D0D0";
    public string GridLine { get; set; } = "#DCDCDC";
    public string WeekColumnBackground { get; set; } = "#FAFAFA";
    public string WeekAltColumnBackground { get; set; } = "#F0F4F8";
    public string PauseOverlay { get; set; } = "#A0A0A0";
    public string ControlBackground { get; set; } = "#FFFFFF";
    public string Danger { get; set; } = "#C0392B";
    public string ScrollbarTrack { get; set; } = "#F0F0F0";
    public string ScrollbarThumb { get; set; } = "#A0A0A0";
    public string TabText { get; set; } = "#555555";
    public string TabBackground { get; set; } = "#FAFAFA";
    public string TabHoverBackground { get; set; } = "#E8E8E8";
    public string TabSelectedBackground { get; set; } = "#FFFFFF";
    public string ComboBackground { get; set; } = "#FFFFFF";
    public string ComboText { get; set; } = "#1A1A1A";
    public string ComboHoverBackground { get; set; } = "#E8E8E8";
    public string ComboHoverText { get; set; } = "#1A1A1A";
    public string ComboSelectedBackground { get; set; } = "#D6E6F5";
    public string ComboSelectedText { get; set; } = "#1A1A1A";
    public string TooltipBackground { get; set; } = "#FFFFF0";
    public string TooltipText { get; set; } = "#1A1A1A";
    public string ButtonBackground { get; set; } = "#FAFAFA";
    public string ButtonText { get; set; } = "#1A1A1A";
    public string ButtonHoverBackground { get; set; } = "#E8E8E8";
    public string ButtonHoverText { get; set; } = "#1A1A1A";
    public string OverflowTrack { get; set; } = "#D0D0D0";
    public string OverflowFill { get; set; } = "#D4A017";

    public double FontSizeBody { get; set; } = 13;
    public double FontSizeTitle { get; set; } = 18;
    public double CardCornerRadius { get; set; } = 8;
    public int CardColumns { get; set; } = 3;
    public double CardWidth { get; set; } = 200;
    public double CardHeight { get; set; } = 200;
    public double CardThumbHeight { get; set; } = 96;
    /// <summary>日程页左侧任务栏卡片尺寸（与奖励/愿望的 Card* 分开）。</summary>
    public double ScheduleCardWidth { get; set; } = 200;
    public double ScheduleCardHeight { get; set; } = 200;
    public double ScheduleCardThumbHeight { get; set; } = 96;
    public double FavCardWidth { get; set; } = 160;
    public double FavCardHeight { get; set; } = 148;
    public double FavCardThumbHeight { get; set; } = 110;
    public string WeightChartLine { get; set; } = "#5B9BD5";
    public string WeightChartGrid { get; set; } = "#D0D0D0";
    public string WeightChartPlotBackground { get; set; } = "#FAFAFA";
    public double WeightChartLineThickness { get; set; } = 2;
    public double WeightChartPointSize { get; set; } = 6;
    public double WeightChartHeight { get; set; } = 280;
    public double WeekPxPerHour { get; set; } = 22;
    public double WeekFocusPxPerHour { get; set; } = 100;
    /// <summary>日程每日可见起始整点（含），0–24；与 WeekHourEnd 组成 [start,end]，默认 6–23。</summary>
    public int WeekHourStart { get; set; } = 6;
    /// <summary>日程每日可见结束整点（画布底边时刻，不含之后），0–24；6–23 即显示到 23:00。</summary>
    public int WeekHourEnd { get; set; } = 23;
    /// <summary>「当前时间」横虚线颜色。</summary>
    public string WeekNowLine { get; set; } = "#C0392B";
    public double WeekNowLineThickness { get; set; } = 1;
    /// <summary>0–1，「当前时间」横虚线不透明度。</summary>
    public double WeekNowLineOpacity { get; set; } = 0.5;
    public string StartTaskIcon { get; set; } = "";
    public double StartTaskButtonSize { get; set; } = 28;
    public double StartTaskButtonCornerRadius { get; set; } = 6;
    public double FailedFillOpacity { get; set; } = 0.35;

    public ThemeStyle Clone() => new()
    {
        Id = Id,
        Name = Name,
        Builtin = Builtin,
        WindowBackground = WindowBackground,
        SurfaceBackground = SurfaceBackground,
        TextPrimary = TextPrimary,
        TextSecondary = TextSecondary,
        Accent = Accent,
        BorderSubtle = BorderSubtle,
        GridLine = GridLine,
        WeekColumnBackground = WeekColumnBackground,
        WeekAltColumnBackground = WeekAltColumnBackground,
        PauseOverlay = PauseOverlay,
        ControlBackground = ControlBackground,
        Danger = Danger,
        ScrollbarTrack = ScrollbarTrack,
        ScrollbarThumb = ScrollbarThumb,
        TabText = TabText,
        TabBackground = TabBackground,
        TabHoverBackground = TabHoverBackground,
        TabSelectedBackground = TabSelectedBackground,
        ComboBackground = ComboBackground,
        ComboText = ComboText,
        ComboHoverBackground = ComboHoverBackground,
        ComboHoverText = ComboHoverText,
        ComboSelectedBackground = ComboSelectedBackground,
        ComboSelectedText = ComboSelectedText,
        TooltipBackground = TooltipBackground,
        TooltipText = TooltipText,
        ButtonBackground = ButtonBackground,
        ButtonText = ButtonText,
        ButtonHoverBackground = ButtonHoverBackground,
        ButtonHoverText = ButtonHoverText,
        OverflowTrack = OverflowTrack,
        OverflowFill = OverflowFill,
        FontSizeBody = FontSizeBody,
        FontSizeTitle = FontSizeTitle,
        CardCornerRadius = CardCornerRadius,
        CardColumns = CardColumns,
        CardWidth = CardWidth,
        CardHeight = CardHeight,
        CardThumbHeight = CardThumbHeight,
        ScheduleCardWidth = ScheduleCardWidth,
        ScheduleCardHeight = ScheduleCardHeight,
        ScheduleCardThumbHeight = ScheduleCardThumbHeight,
        FavCardWidth = FavCardWidth,
        FavCardHeight = FavCardHeight,
        FavCardThumbHeight = FavCardThumbHeight,
        WeightChartLine = WeightChartLine,
        WeightChartGrid = WeightChartGrid,
        WeightChartPlotBackground = WeightChartPlotBackground,
        WeightChartLineThickness = WeightChartLineThickness,
        WeightChartPointSize = WeightChartPointSize,
        WeightChartHeight = WeightChartHeight,
        WeekPxPerHour = WeekPxPerHour,
        WeekFocusPxPerHour = WeekFocusPxPerHour,
        WeekHourStart = WeekHourStart,
        WeekHourEnd = WeekHourEnd,
        WeekNowLine = WeekNowLine,
        WeekNowLineThickness = WeekNowLineThickness,
        WeekNowLineOpacity = WeekNowLineOpacity,
        StartTaskIcon = StartTaskIcon,
        StartTaskButtonSize = StartTaskButtonSize,
        StartTaskButtonCornerRadius = StartTaskButtonCornerRadius,
        FailedFillOpacity = FailedFillOpacity
    };
}

internal static class Theme
{
    public const string LightId = "light";
    public const string DarkId = "dark";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static event EventHandler? Changed;

    public static ThemeStyle Current { get; private set; } = Light();
    public static List<ThemeStyle> Copies { get; } = [];

    public static IEnumerable<ThemeStyle> All
    {
        get
        {
            yield return Light();
            yield return Dark();
            foreach (var c in Copies)
                yield return c;
        }
    }

    public static bool IsBuiltin => Current.Builtin;

    public static string FilePath
    {
        get
        {
            var root = Paths.FindWorkspaceRoot();
            if (root is null) return Path.Combine(AppContext.BaseDirectory, "styles.json");
            return Path.Combine(root, "Personal_Management", "styles.json");
        }
    }

    public static string UserIconDir
    {
        get
        {
            var root = Paths.FindWorkspaceRoot() ?? AppContext.BaseDirectory;
            return Path.Combine(root, "Personal_Management", "Desktop", "Assets", "user");
        }
    }

    public static ThemeStyle Light() => new()
    {
        Id = LightId,
        Name = "普通模式",
        Builtin = true,
        WindowBackground = "#FFFFFF",
        SurfaceBackground = "#FAFAFA",
        TextPrimary = "#1A1A1A",
        TextSecondary = "#6B6B6B",
        Accent = "#4682B4",
        BorderSubtle = "#D0D0D0",
        GridLine = "#DCDCDC",
        WeekColumnBackground = "#FAFAFA",
        WeekAltColumnBackground = "#E8F0F6",
        PauseOverlay = "#A0A0A0",
        ControlBackground = "#FFFFFF",
        Danger = "#C0392B",
        ScrollbarTrack = "#F0F0F0",
        ScrollbarThumb = "#A0A0A0",
        TabText = "#555555",
        TabBackground = "#FAFAFA",
        TabHoverBackground = "#E8E8E8",
        TabSelectedBackground = "#FFFFFF",
        ComboBackground = "#FFFFFF",
        ComboText = "#1A1A1A",
        ComboHoverBackground = "#E8E8E8",
        ComboHoverText = "#1A1A1A",
        ComboSelectedBackground = "#D6E6F5",
        ComboSelectedText = "#1A1A1A",
        TooltipBackground = "#FFFFF0",
        TooltipText = "#1A1A1A",
        ButtonBackground = "#FAFAFA",
        ButtonText = "#1A1A1A",
        ButtonHoverBackground = "#E8E8E8",
        ButtonHoverText = "#1A1A1A",
        OverflowTrack = "#D0D0D0",
        OverflowFill = "#D4A017",
        CardWidth = 200,
        ScheduleCardWidth = 200,
        ScheduleCardHeight = 200,
        ScheduleCardThumbHeight = 96,
        FavCardWidth = 160,
        FavCardHeight = 148,
        FavCardThumbHeight = 110,
        WeightChartLine = "#5B9BD5",
        WeightChartGrid = "#D0D0D0",
        WeightChartPlotBackground = "#FAFAFA",
        WeightChartLineThickness = 2,
        WeightChartPointSize = 6,
        WeightChartHeight = 280,
        WeekPxPerHour = 22,
        WeekFocusPxPerHour = 100,
        WeekHourStart = 6,
        WeekHourEnd = 23,
        WeekNowLine = "#C0392B",
        WeekNowLineThickness = 1,
        WeekNowLineOpacity = 0.5
    };

    public static ThemeStyle Dark() => new()
    {
        Id = DarkId,
        Name = "黑夜模式",
        Builtin = true,
        WindowBackground = "#1E1E1E",
        SurfaceBackground = "#2D2D2D",
        TextPrimary = "#E8E8E8",
        TextSecondary = "#A0A0A0",
        Accent = "#5B9BD5",
        BorderSubtle = "#555555",
        GridLine = "#444444",
        WeekColumnBackground = "#252525",
        WeekAltColumnBackground = "#2A3038",
        PauseOverlay = "#C8C8C8",
        ControlBackground = "#333333",
        Danger = "#E74C3C",
        ScrollbarTrack = "#2A2A2A",
        ScrollbarThumb = "#111111",
        TabText = "#B0B0B0",
        TabBackground = "#2D2D2D",
        TabHoverBackground = "#3A3A3A",
        TabSelectedBackground = "#252525",
        ComboBackground = "#333333",
        ComboText = "#E8E8E8",
        ComboHoverBackground = "#3A3A3A",
        ComboHoverText = "#E8E8E8",
        ComboSelectedBackground = "#3A5A7A",
        ComboSelectedText = "#E8E8E8",
        TooltipBackground = "#2D2D2D",
        TooltipText = "#E8E8E8",
        ButtonBackground = "#2D2D2D",
        ButtonText = "#E8E8E8",
        ButtonHoverBackground = "#3A3A3A",
        ButtonHoverText = "#E8E8E8",
        OverflowTrack = "#555555",
        OverflowFill = "#D4A017",
        CardWidth = 200,
        ScheduleCardWidth = 200,
        ScheduleCardHeight = 200,
        ScheduleCardThumbHeight = 96,
        FavCardWidth = 160,
        FavCardHeight = 148,
        FavCardThumbHeight = 110,
        WeightChartLine = "#5B9BD5",
        WeightChartGrid = "#555555",
        WeightChartPlotBackground = "#252525",
        WeightChartLineThickness = 2,
        WeightChartPointSize = 6,
        WeightChartHeight = 280,
        WeekPxPerHour = 22,
        WeekFocusPxPerHour = 100,
        WeekNowLine = "#E74C3C",
        WeekNowLineThickness = 1,
        WeekNowLineOpacity = 0.5
    };

    public static void LoadAndApply()
    {
        Copies.Clear();
        var active = LightId;
        try
        {
            if (File.Exists(FilePath))
            {
                var saved = JsonSerializer.Deserialize<ThemeFile>(File.ReadAllText(FilePath), JsonOptions);
                if (saved is not null)
                {
                    if (!string.IsNullOrWhiteSpace(saved.ActiveId)) active = saved.ActiveId;
                    if (saved.Copies is not null)
                        Copies.AddRange(saved.Copies.Where(c => !c.Builtin && !string.IsNullOrWhiteSpace(c.Id)));
                }
            }
        }
        catch { /* keep defaults */ }
        Activate(active, persist: false);
    }

    public static void Activate(string id, bool persist = true)
    {
        Current = All.FirstOrDefault(s => s.Id == id)?.Clone() ?? Light();
        if (Current.Id is LightId or DarkId) Current.Builtin = true;
        Current.CardColumns = Math.Clamp(Current.CardColumns, 1, 8);
        Current.CardWidth = Math.Clamp(Current.CardWidth, 80, 480);
        Current.CardHeight = Math.Clamp(Current.CardHeight, 80, 800);
        Current.CardThumbHeight = Math.Clamp(Current.CardThumbHeight, 32, 400);
        Current.ScheduleCardWidth = Math.Clamp(Current.ScheduleCardWidth, 80, 480);
        Current.ScheduleCardHeight = Math.Clamp(Current.ScheduleCardHeight, 80, 800);
        Current.ScheduleCardThumbHeight = Math.Clamp(Current.ScheduleCardThumbHeight, 32, 400);
        Current.FontSizeBody = Math.Clamp(Current.FontSizeBody, 10, 28);
        Current.FontSizeTitle = Math.Clamp(Current.FontSizeTitle, 12, 36);
        Current.FavCardWidth = Math.Clamp(Current.FavCardWidth, 80, 480);
        Current.FavCardHeight = Math.Clamp(Current.FavCardHeight, 80, 480);
        Current.FavCardThumbHeight = Math.Clamp(Current.FavCardThumbHeight, 40, 400);
        Current.WeightChartLineThickness = Math.Clamp(Current.WeightChartLineThickness, 1, 8);
        Current.WeightChartPointSize = Math.Clamp(Current.WeightChartPointSize, 2, 16);
        Current.WeightChartHeight = Math.Clamp(Current.WeightChartHeight, 120, 800);
        Current.WeekPxPerHour = Math.Clamp(Current.WeekPxPerHour, 8, 150);
        Current.WeekFocusPxPerHour = Math.Clamp(Current.WeekFocusPxPerHour, 8, 200);
        Current.WeekHourStart = Math.Clamp(Current.WeekHourStart, 0, 24);
        Current.WeekHourEnd = Math.Clamp(Current.WeekHourEnd, 0, 24);
        if (Current.WeekHourEnd <= Current.WeekHourStart)
        {
            Current.WeekHourStart = 0;
            Current.WeekHourEnd = 24;
        }
        if (string.IsNullOrWhiteSpace(Current.WeekNowLine))
            Current.WeekNowLine = Current.Danger;
        Current.WeekNowLineThickness = Math.Clamp(Current.WeekNowLineThickness, 0.5, 8);
        Current.WeekNowLineOpacity = Math.Clamp(Current.WeekNowLineOpacity, 0.05, 1);
        Apply();
        if (persist) Save();
    }

    public static void SaveCopy(ThemeStyle style)
    {
        style.Builtin = false;
        var i = Copies.FindIndex(c => c.Id == style.Id);
        if (i >= 0) Copies[i] = style.Clone();
        else Copies.Add(style.Clone());
        if (Current.Id != style.Id)
            Current = style.Clone();
        Apply();
        Save();
    }

    public static void PersistCurrentIfCopy()
    {
        if (Current.Builtin) return;
        var i = Copies.FindIndex(c => c.Id == Current.Id);
        if (i >= 0) Copies[i] = Current.Clone();
        else Copies.Add(Current.Clone());
        Save();
        Apply();
    }

    public static void DeleteCopy(string id)
    {
        Copies.RemoveAll(c => c.Id == id);
        if (Current.Id == id)
            Activate(LightId);
        else
            Save();
    }

    public static void Save()
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var file = new ThemeFile { ActiveId = Current.Id, Copies = Copies };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(file, JsonOptions));
    }

    public static void Apply()
    {
        var app = Application.Current;
        if (app is null) return;
        var t = Current;
        SetBrush(app, "WindowBackgroundBrush", t.WindowBackground);
        SetBrush(app, "SurfaceBackgroundBrush", t.SurfaceBackground);
        SetBrush(app, "TextPrimaryBrush", t.TextPrimary);
        SetBrush(app, "TextSecondaryBrush", t.TextSecondary);
        SetBrush(app, "AccentBrush", t.Accent);
        SetBrush(app, "BorderSubtleBrush", t.BorderSubtle);
        SetBrush(app, "GridLineBrush", t.GridLine);
        SetBrush(app, "WeekColumnBackgroundBrush", t.WeekColumnBackground);
        SetBrush(app, "WeekAltColumnBackgroundBrush", t.WeekAltColumnBackground);
        SetBrush(app, "PauseOverlayBrush", t.PauseOverlay, 180);
        SetBrush(app, "ControlBackgroundBrush", t.ControlBackground);
        SetBrush(app, "DangerBrush", t.Danger);
        SetBrush(app, "ScrollbarTrackBrush", t.ScrollbarTrack);
        SetBrush(app, "ScrollbarThumbBrush", t.ScrollbarThumb);
        SetBrush(app, "TabTextBrush", t.TabText);
        SetBrush(app, "TabBackgroundBrush", t.TabBackground);
        SetBrush(app, "TabHoverBackgroundBrush", t.TabHoverBackground);
        SetBrush(app, "TabSelectedBackgroundBrush", t.TabSelectedBackground);
        SetBrush(app, "ComboBackgroundBrush", t.ComboBackground);
        SetBrush(app, "ComboTextBrush", t.ComboText);
        SetBrush(app, "ComboHoverBackgroundBrush", t.ComboHoverBackground);
        SetBrush(app, "ComboHoverTextBrush", t.ComboHoverText);
        SetBrush(app, "ComboSelectedBackgroundBrush", t.ComboSelectedBackground);
        SetBrush(app, "ComboSelectedTextBrush", t.ComboSelectedText);
        SetBrush(app, "TooltipBackgroundBrush", t.TooltipBackground);
        SetBrush(app, "TooltipTextBrush", t.TooltipText);
        SetBrush(app, "ButtonBackgroundBrush", t.ButtonBackground);
        SetBrush(app, "ButtonTextBrush", t.ButtonText);
        SetBrush(app, "ButtonHoverBackgroundBrush", t.ButtonHoverBackground);
        SetBrush(app, "ButtonHoverTextBrush", t.ButtonHoverText);
        SetBrush(app, "OverflowTrackBrush", t.OverflowTrack);
        SetBrush(app, "OverflowFillBrush", t.OverflowFill);
        app.Resources["FontSizeBody"] = t.FontSizeBody;
        app.Resources["FontSizeTitle"] = t.FontSizeTitle;
        foreach (Window w in app.Windows)
            Tint(w);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void Tint(Window window)
    {
        window.Background = Brush("WindowBackgroundBrush");
        window.Foreground = Brush("TextPrimaryBrush");
        window.FontSize = Current.FontSizeBody;
        CaptionTheme.Apply(window);
        var icon = AppIcon.Wpf();
        if (icon is not null) window.Icon = icon;
    }

    public static Size ThumbDisplaySize(string wall, Window? owner)
    {
        const double pad = 20;
        var t = Current;
        if (wall == "fav")
            return new Size(Math.Max(32, t.FavCardWidth - pad), Math.Max(32, t.FavCardThumbHeight));
        if (wall == "task")
            return new Size(Math.Max(32, t.ScheduleCardWidth - pad), Math.Max(32, t.ScheduleCardThumbHeight));
        return new Size(Math.Max(32, t.CardWidth - pad), Math.Max(32, t.CardThumbHeight));
    }

    public static SolidColorBrush Brush(string key)
    {
        if (Application.Current?.TryFindResource(key) is SolidColorBrush b)
            return b;
        var fallback = new SolidColorBrush(Colors.Gray);
        fallback.Freeze();
        return fallback;
    }

    public static SolidColorBrush FromHex(string hex, byte? alpha = null)
    {
        var c = TaskVisual.ParseColor(hex);
        if (alpha is not null) c.A = alpha.Value;
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public static string DefaultStartIconPath()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "Assets", "start-task.png");
        if (File.Exists(local)) return local;
        var root = Paths.FindWorkspaceRoot();
        if (root is not null)
        {
            var src = Path.Combine(root, "Personal_Management", "Desktop", "Assets", "start-task.png");
            if (File.Exists(src)) return src;
        }
        return local;
    }

    public static string ResolveStartIconPath()
    {
        var custom = Current.StartTaskIcon;
        if (!string.IsNullOrWhiteSpace(custom))
        {
            if (File.Exists(custom)) return custom;
            var root = Paths.FindWorkspaceRoot();
            if (root is not null)
            {
                var rel = Path.Combine(root, custom);
                if (File.Exists(rel)) return rel;
            }
            var fromBase = Path.Combine(AppContext.BaseDirectory, custom);
            if (File.Exists(fromBase)) return fromBase;
        }
        return DefaultStartIconPath();
    }

    public static BitmapImage? LoadStartIcon()
    {
        var path = ResolveStartIconPath();
        return FavoriteService.LoadLocalPreview(path, 64);
    }

    public static string DefaultRecordIconPath()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "Assets", "record-task.png");
        if (File.Exists(local)) return local;
        var root = Paths.FindWorkspaceRoot();
        if (root is not null)
        {
            var src = Path.Combine(root, "Personal_Management", "Desktop", "Assets", "record-task.png");
            if (File.Exists(src)) return src;
        }
        return local;
    }

    public static BitmapImage? LoadRecordIcon() =>
        FavoriteService.LoadLocalPreview(DefaultRecordIconPath(), 64);

    public static string ImportUserIcon(string sourcePath)
    {
        Directory.CreateDirectory(UserIconDir);
        var dest = Path.Combine(UserIconDir, Guid.NewGuid().ToString("N")[..10] + Path.GetExtension(sourcePath));
        File.Copy(sourcePath, dest, overwrite: true);
        var root = Paths.FindWorkspaceRoot();
        if (root is not null && dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return Path.GetRelativePath(root, dest).Replace('\\', '/');
        return dest;
    }

    private static void SetBrush(Application app, string key, string hex, byte? alpha = null)
    {
        app.Resources[key] = FromHex(hex, alpha);
    }

    private sealed class ThemeFile
    {
        public string ActiveId { get; set; } = LightId;
        public List<ThemeStyle> Copies { get; set; } = [];
    }
}

internal static class CaptionTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static bool UseDark
    {
        get
        {
            if (Theme.Current.Id == Theme.DarkId) return true;
            if (Theme.Current.Id == Theme.LightId) return false;
            var c = TaskVisual.ParseColor(Theme.Current.WindowBackground);
            return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B < 128;
        }
    }

    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            window.SourceInitialized -= OnSourceInitialized;
            window.SourceInitialized += OnSourceInitialized;
            return;
        }
        Set(hwnd, UseDark);
    }

    private static void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is not Window w) return;
        w.SourceInitialized -= OnSourceInitialized;
        Apply(w);
    }

    private static void Set(IntPtr hwnd, bool dark)
    {
        var value = dark ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
            _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeLegacy, ref value, sizeof(int));
    }
}

internal static class TextPrompt
{
    public static string? Ask(Window? owner, string title, string label, string initial = "")
    {
        var box = new System.Windows.Controls.TextBox { Text = initial, Margin = new Thickness(0, 8, 0, 12), MinWidth = 240 };
        var ok = new System.Windows.Controls.Button { Content = "确定", Width = 80, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = label });
        panel.Children.Add(box);
        panel.Children.Add(ok);
        var win = new Window
        {
            Title = title,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = owner
        };
        Theme.Tint(win);
        string? result = null;
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(box.Text))
            {
                MessageBox.Show("请填写名称。");
                return;
            }
            result = box.Text.Trim();
            win.DialogResult = true;
            win.Close();
        };
        return win.ShowDialog() == true ? result : null;
    }
}

/// <summary>手动改奖券数量 / 愿望单额度（迁移用）。</summary>
internal static class WalletPrompt
{
    public static (int Tickets, int Quota)? Ask(Window? owner, int tickets, int quota)
    {
        var ticketBox = new System.Windows.Controls.TextBox
        {
            Text = tickets.ToString(),
            Margin = new Thickness(0, 4, 0, 8),
            MinWidth = 200
        };
        var quotaBox = new System.Windows.Controls.TextBox
        {
            Text = quota.ToString(),
            Margin = new Thickness(0, 4, 0, 12),
            MinWidth = 200
        };
        var ok = new System.Windows.Controls.Button { Content = "确定", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new System.Windows.Controls.Button { Content = "取消", Width = 80, IsCancel = true };
        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16), MaxWidth = 360 };
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "可手动改成你在旧系统里的奖券数量与愿望单额度。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
            Foreground = Theme.Brush("TextSecondaryBrush")
        });
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "当前奖券数量" });
        panel.Children.Add(ticketBox);
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "愿望单额度" });
        panel.Children.Add(quotaBox);
        panel.Children.Add(buttons);
        var win = new Window
        {
            Title = "调整钱包",
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = owner
        };
        Theme.Tint(win);
        (int, int)? result = null;
        ok.Click += (_, _) =>
        {
            if (!int.TryParse(ticketBox.Text.Trim(), out var t) || t < 0)
            {
                MessageBox.Show("奖券数量请填 ≥0 的整数。");
                return;
            }
            if (!int.TryParse(quotaBox.Text.Trim(), out var q) || q < 0)
            {
                MessageBox.Show("愿望单额度请填 ≥0 的整数。");
                return;
            }
            result = (t, q);
            win.DialogResult = true;
            win.Close();
        };
        return win.ShowDialog() == true ? result : null;
    }
}
