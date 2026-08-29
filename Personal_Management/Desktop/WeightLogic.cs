using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace PersonalManagement.Desktop;

internal static class WeightLogic
{
    public static readonly (string Id, string Label, double Factor)[] ActivityLevels =
    [
        ("sedentary", "久坐", 1.2),
        ("light", "轻度活动", 1.375),
        ("moderate", "中度活动", 1.55),
        ("high", "高度活动", 1.725)
    ];

    public static string ActivityLabel(string? id)
    {
        foreach (var (key, label, _) in ActivityLevels)
            if (key == id) return label;
        return id ?? "";
    }

    public static double ActivityFactor(string? id)
    {
        foreach (var (key, _, factor) in ActivityLevels)
            if (key == id) return factor;
        return 1.2;
    }

    public static double? Bmi(double weightKg, double heightCm)
    {
        if (weightKg <= 0 || heightCm <= 0) return null;
        var m = heightCm / 100.0;
        return weightKg / (m * m);
    }

    public static string BmiCategory(double? bmi)
    {
        if (bmi is null) return "—";
        if (bmi < 18.5) return "偏瘦";
        if (bmi < 24) return "正常";
        if (bmi < 28) return "超重";
        return "肥胖";
    }

    /// <summary>Mifflin–St Jeor. sex: male / female.</summary>
    public static double? Bmr(double weightKg, double heightCm, int ageYears, string sex)
    {
        if (weightKg <= 0 || heightCm <= 0 || ageYears <= 0) return null;
        var baseVal = 10 * weightKg + 6.25 * heightCm - 5 * ageYears;
        return sex == "female" ? baseVal - 161 : baseVal + 5;
    }

    public static double? Tdee(double? bmr, string? activityId) =>
        bmr is null ? null : bmr.Value * ActivityFactor(activityId);

    public static bool TryParseWeight(string raw, out double kg, out string? error)
    {
        kg = 0;
        error = null;
        var s = raw.Trim();
        if (!double.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out kg)
            && !double.TryParse(s, NumberStyles.Number, CultureInfo.CurrentCulture, out kg))
        {
            error = "体重不是合法数字";
            return false;
        }
        var dot = s.IndexOf('.') >= 0 ? s.IndexOf('.') : s.IndexOf(',');
        if (dot >= 0 && s.Length - dot - 1 > 2)
        {
            error = "体重最多两位小数";
            return false;
        }
        kg = Math.Round(kg, 2, MidpointRounding.AwayFromZero);
        if (kg <= 0 || kg > 500)
        {
            error = "体重超出合理范围";
            return false;
        }
        return true;
    }

    /// <summary>
    /// 批量粘贴：日期 + 空白（空格/Tab 任意组合）+ 体重。
    /// 日期为 yyyy-MM-dd 或 a（上一有效日+1）。失败抛 InvalidOperationException，含行号。
    /// </summary>
    public static List<(DateTime Date, double Kg)> ParseBatch(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
            throw new InvalidOperationException("没有可解析的行。");

        DateTime? last = null;
        var result = new List<(DateTime, double)>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var parts = System.Text.RegularExpressions.Regex.Split(line, @"[\t ]+")
                .Where(p => p.Length > 0)
                .ToArray();
            if (parts.Length < 2)
                throw new InvalidOperationException($"第 {i + 1} 行：需要用空格或 Tab 分隔日期和体重。");
            if (parts.Length > 2)
                throw new InvalidOperationException($"第 {i + 1} 行：多余分段，请只保留日期和体重两列。");

            var dateRaw = parts[0];
            var weightRaw = parts[1];
            if (!TryParseWeight(weightRaw, out var kg, out var werr))
                throw new InvalidOperationException($"第 {i + 1} 行：{werr}。");

            DateTime day;
            if (dateRaw.Equals("a", StringComparison.OrdinalIgnoreCase))
            {
                if (last is null)
                    throw new InvalidOperationException($"第 {i + 1} 行：首行或缺少上一日时不能使用 a。");
                day = last.Value.Date.AddDays(1);
            }
            else if (DateTime.TryParseExact(dateRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                         DateTimeStyles.None, out var parsed))
            {
                day = parsed.Date;
            }
            else
            {
                throw new InvalidOperationException($"第 {i + 1} 行：日期既不是 a 也不是合法的 yyyy-MM-dd。");
            }

            last = day;
            result.Add((day, kg));
        }
        return result;
    }

    public static string FormatDate(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateTime? ParseEntryDate(JsonNode? row)
    {
        var s = NocoClient.ReadString(row, "Date");
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.Date;
        return RewardLogic.ParseDate(row, "Date")?.Date;
    }
}
