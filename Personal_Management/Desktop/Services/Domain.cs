using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PersonalManagement.Desktop;

internal enum TaskKind
{
    Daily,
    Deadline,
    Flexible,
    FlexibleRepeat
}

internal enum DailyPhase
{
    Forming,
    Consolidate,
    Observe
}

internal sealed class TaskRow
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Type { get; set; } = "daily";
    public int RewardLevel { get; set; } = 1;
    public DateTime? RegisteredAt { get; set; }
    public DateTime? DueAt { get; set; }
    public DateTime? ReminderAt { get; set; }
    public string ColorHex { get; set; } = "#8EB4E3";
    public string BlockPattern { get; set; } = BlockPatterns.None;
    public string BlockPatternColor { get; set; } = BlockPatterns.DefaultPatternColor;
    /// <summary>完整多层样式 JSON；优先于旧 BlockPattern 字段。</summary>
    public string? BlockStyleJson { get; set; }
    public BitmapImage? Preview { get; set; }

    public BlockStyleSpec ResolveStyle() =>
        BlockStyleSpec.FromJson(BlockStyleJson)
        ?? BlockStyleSpec.FromLegacy(ColorHex, BlockPattern, BlockPatternColor);
    public JsonNode? OriginalField { get; set; }
    public string? OriginalPath { get; set; }
    public string? CropJson { get; set; }
    public string TypeLabel => Type switch
    {
        "daily" => "每日重复",
        "deadline" => "截止型",
        "flexible" => "灵活型",
        "flexible_repeat" => "灵活重复",
        _ => Type
    };
    public string PhaseLabel { get; set; } = "";
    public bool DoneToday { get; set; }
    public bool Running { get; set; }
    public bool Archived { get; set; }
    public int RewardMinutes { get; set; } = 30;
    public bool AllowOverflow { get; set; }
    public double OverflowSeconds { get; set; }
    /// <summary>直接生产力任务：用于日后统计每日有效执行。</summary>
    public bool IsDirectProductivity { get; set; }
    public string Status => Running ? "执行中" : DoneToday && (Type is "daily" or "flexible_repeat") ? "今日已完成" : "";
    public string LevelLabel => "L" + RewardLevel;
    public System.Windows.Media.Color CardColor => TaskVisual.ParseColor(ColorHex);
}

internal static class TaskKinds
{
    public static readonly (string Id, string Label)[] All =
    [
        ("daily", "每日重复"),
        ("flexible_repeat", "灵活重复")
    ];
}

internal static class RewardKinds
{
    public static readonly (string Id, string Label)[] All =
    [
        ("item", "实物"),
        ("quota", "愿望单额度"),
        ("ticket", "奖券")
    ];

    public static string Label(string? id)
    {
        foreach (var (key, label) in All)
            if (key == id) return label;
        return id ?? "";
    }
}

internal sealed class RewardRow
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "item";
    public int QuotaAmount { get; set; }
    public double Probability { get; set; }
    public bool IsBase { get; set; }
    public BitmapImage? Preview { get; set; }
    public JsonNode? OriginalField { get; set; }
    public string? OriginalPath { get; set; }
    public string? CropJson { get; set; }
    public bool Archived { get; set; }
    public string KindLabel => RewardKinds.Label(Kind);
    /// <summary>展示用：基础奖为动态值，否则为固定 Probability。</summary>
    public double DisplayProbability { get; set; }
}

internal sealed class WishRow
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public int Cost { get; set; }
    public bool Archived { get; set; }
    public BitmapImage? Preview { get; set; }
    public JsonNode? OriginalField { get; set; }
    public string? OriginalPath { get; set; }
    public string? CropJson { get; set; }
}

internal static class RewardLogic
{
    public static DailyPhase Phase(IReadOnlyList<DateTime> completionDays)
    {
        var streak = CurrentStreakDays(completionDays);
        if (streak >= 14) return DailyPhase.Observe;
        if (streak >= 7) return DailyPhase.Consolidate;
        return DailyPhase.Forming;
    }

    public static string PhaseLabel(DailyPhase phase) => phase switch
    {
        DailyPhase.Consolidate => "巩固期",
        DailyPhase.Observe => "观察期",
        _ => "养成期"
    };

    /// <summary>连续完成天数：允许漏 1 天；连续漏 2 天则断档。</summary>
    public static int CurrentStreakDays(IReadOnlyList<DateTime> completionDays)
    {
        if (completionDays.Count == 0) return 0;
        var days = completionDays.Select(d => d.Date).Distinct().OrderByDescending(d => d).ToList();
        var streak = 1;
        var cursor = days[0];
        for (var i = 1; i < days.Count; i++)
        {
            var gap = (cursor - days[i]).TotalDays;
            if (gap == 1)
            {
                streak++;
                cursor = days[i];
            }
            else if (gap == 2)
            {
                cursor = days[i];
            }
            else
            {
                break;
            }
        }
        return streak;
    }

    public static int TicketsForCompletion(string type, int rewardLevel, DailyPhase phase, Random rng)
    {
        if (type == "daily")
        {
            var tickets = phase switch
            {
                DailyPhase.Consolidate => rng.Next(3) == 0 ? 1 : 0,
                DailyPhase.Observe => rng.Next(10) == 0 ? 1 : 0,
                _ => 1
            };
            if (rng.Next(100) == 0) tickets += 3;
            return tickets;
        }

        return rewardLevel is >= 1 and <= 3 ? rewardLevel : 0;
    }

    /// <summary>写入 Noco DateTime：带本机时区，避免 Docker 默认 UTC 把墙上时钟当成世界时。</summary>
    public static string FormatDateTime(DateTime dt)
    {
        DateTimeOffset dto = dt.Kind switch
        {
            DateTimeKind.Utc => new DateTimeOffset(dt, TimeSpan.Zero),
            DateTimeKind.Local => new DateTimeOffset(dt),
            _ => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Local))
        };
        return dto.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz");
    }

    /// <summary>读出后一律转成本机墙上时间（Kind=Local）。</summary>
    public static DateTime? ParseDate(JsonNode? node, string name)
    {
        var s = node?[name]?.ToString();
        if (string.IsNullOrWhiteSpace(s) || s == "null") return null;
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
            return dto.LocalDateTime;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d))
            return d.Kind == DateTimeKind.Utc ? d.ToLocalTime() : DateTime.SpecifyKind(d, DateTimeKind.Local);
        if (DateTime.TryParse(s, out d))
            return d.Kind == DateTimeKind.Utc ? d.ToLocalTime() : d;
        return null;
    }

    public static string? LinkedId(JsonNode? node, string field)
    {
        var n = node?[field];
        if (n is null || n.GetValueKind() == System.Text.Json.JsonValueKind.Null) return null;
        if (n is JsonValue v) return v.ToString();
        return NocoClient.ReadId(n);
    }
}

public readonly record struct PauseSpan(DateTime Start, DateTime End);

internal static class TaskVisual
{
    public const string DefaultColor = "#8EB4E3";

    public static System.Windows.Media.Color ParseColor(string? hex)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex))
                return (System.Windows.Media.Color)ColorConverter.ConvertFromString(hex)!;
        }
        catch { /* fallback */ }
        return System.Windows.Media.Color.FromRgb(0x8E, 0xB4, 0xE3);
    }

    public static SolidColorBrush BrushOf(string? hex)
    {
        var c = ParseColor(hex);
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

internal static class SessionLogic
{
    public static int RequiredSeconds(int rewardMinutes) =>
        Math.Max(1, rewardMinutes) * 60;

    public static int RewardUnits(double activeSeconds, int requiredSeconds) =>
        (int)Math.Floor(Math.Max(0, activeSeconds) / Math.Max(1, requiredSeconds));

    public static double ActiveSeconds(DateTime start, DateTime end, double pausedSeconds) =>
        Math.Max(0, (end - start).TotalSeconds - pausedSeconds);

    public readonly record struct OverflowSettle(int Units, int Extra, double NewOverflowSeconds, int Total);

    public static OverflowSettle ComputeOverflow(double activeSeconds, int rewardMinutes, bool allowOverflow, double oldOverflow)
    {
        var r = RequiredSeconds(rewardMinutes);
        var units = RewardUnits(activeSeconds, r);
        var remainder = Math.Max(0, activeSeconds) - units * r;
        if (!allowOverflow)
            return new OverflowSettle(units, 0, 0, units);
        var bank = Math.Max(0, oldOverflow) + remainder;
        var extra = (int)Math.Floor(bank / r);
        var leftover = bank - extra * r;
        return new OverflowSettle(units, extra, leftover, units + extra);
    }

    public static string SerializePauses(IEnumerable<PauseSpan> pauses) =>
        JsonSerializer.Serialize(pauses.Select(p => new { s = p.Start.ToString("o"), e = p.End.ToString("o") }).ToList());

    public static List<PauseSpan> ParsePauses(string? json)
    {
        var list = new List<PauseSpan>();
        if (string.IsNullOrWhiteSpace(json) || json == "null") return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var s = item.TryGetProperty("s", out var sv) ? sv.GetString() : null;
                var e = item.TryGetProperty("e", out var ev) ? ev.GetString() : null;
                if (DateTimeOffset.TryParse(s, out var start) && DateTimeOffset.TryParse(e, out var end) && end > start)
                    list.Add(new PauseSpan(start.LocalDateTime, end.LocalDateTime));
            }
        }
        catch { /* ignore bad json */ }
        return list;
    }
}
