using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonalManagement.Desktop;

/// <summary>日程块完整样式（底色 + 多层纹样）。</summary>
public sealed class BlockStyleSpec
{
    public string BaseColor { get; set; } = TaskVisual.DefaultColor;
    public List<BlockStyleLayer> Layers { get; set; } = [];

    public BlockStyleSpec Clone() => new()
    {
        BaseColor = BaseColor,
        Layers = Layers.Select(l => l.Clone()).ToList()
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    public static BlockStyleSpec? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;
        try
        {
            var s = JsonSerializer.Deserialize<BlockStyleSpec>(json, JsonOpts);
            s?.Normalize();
            return s;
        }
        catch { return null; }
    }

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(BaseColor)) BaseColor = TaskVisual.DefaultColor;
        Layers ??= [];
        foreach (var l in Layers) l.Normalize();
    }

    /// <summary>从旧版单纹样字段迁移。</summary>
    public static BlockStyleSpec FromLegacy(string? baseHex, string? patternId, string? patternHex)
    {
        var spec = new BlockStyleSpec
        {
            BaseColor = string.IsNullOrWhiteSpace(baseHex) ? TaskVisual.DefaultColor : baseHex!
        };
        var id = BlockPatterns.Normalize(patternId);
        if (id == BlockPatterns.None) return spec;
        var layer = BlockStyleLayer.FromLegacyKind(id, patternHex);
        if (layer is not null) spec.Layers.Add(layer);
        return spec;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}

/// <summary>单层纹样；参数尽量统一，各类按需使用子集。</summary>
public sealed class BlockStyleLayer
{
    /// <summary>stripe | sine | diamond | star | dot | moon</summary>
    public string Kind { get; set; } = "stripe";
    public string Color { get; set; } = BlockPatterns.DefaultPatternColor;
    /// <summary>0–1 层透明度。</summary>
    public double Opacity { get; set; } = 1;
    /// <summary>线类粗细；散布类可忽略。</summary>
    public double Thickness { get; set; } = 2.5;
    /// <summary>重复周期（间隔）。</summary>
    public double Spacing { get; set; } = 10;
    /// <summary>整体旋转角度（度）。</summary>
    public double Angle { get; set; } = 45;
    /// <summary>图案尺寸：散布半径/半宽；正弦振幅。</summary>
    public double Size { get; set; } = 3.5;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }

    public BlockStyleLayer Clone() => new()
    {
        Kind = Kind,
        Color = Color,
        Opacity = Opacity,
        Thickness = Thickness,
        Spacing = Spacing,
        Angle = Angle,
        Size = Size,
        OffsetX = OffsetX,
        OffsetY = OffsetY
    };

    public void Normalize()
    {
        Kind = NormalizeKind(Kind);
        if (string.IsNullOrWhiteSpace(Color)) Color = BlockPatterns.DefaultPatternColor;
        Opacity = Math.Clamp(Opacity, 0, 1);
        Thickness = Math.Clamp(Thickness, 0.5, 64);
        Spacing = Math.Clamp(Spacing, 2, 128);
        Angle = Angle % 360;
        if (Angle < 0) Angle += 360;
        Size = Math.Clamp(Size, 0.5, 64);
        OffsetX = Math.Clamp(OffsetX, -256, 256);
        OffsetY = Math.Clamp(OffsetY, -256, 256);
    }

    public static string NormalizeKind(string? kind)
    {
        var k = (kind ?? "").Trim().ToLowerInvariant();
        return k switch
        {
            "stripe" or "stripe-right" or "stripe-left" or "sine"
                or "diamond" or "star" or "dot" or "moon" =>
                k is "stripe-right" or "stripe-left" ? "stripe" : k,
            _ => "stripe"
        };
    }

    public static readonly (string Id, string Label)[] Kinds =
    [
        ("stripe", "斜纹"),
        ("sine", "正弦纹路"),
        ("diamond", "棱形散布"),
        ("star", "星形散布"),
        ("dot", "圆点散布"),
        ("moon", "月亮散布")
    ];

    public static BlockStyleLayer? FromLegacyKind(string normalizedId, string? color)
    {
        var c = string.IsNullOrWhiteSpace(color) ? BlockPatterns.DefaultPatternColor : color!;
        return normalizedId switch
        {
            BlockPatterns.StripeRight => new BlockStyleLayer { Kind = "stripe", Color = c, Angle = 45, Thickness = 2.2, Spacing = 8, Size = 2.2 },
            BlockPatterns.StripeLeft => new BlockStyleLayer { Kind = "stripe", Color = c, Angle = 135, Thickness = 2.2, Spacing = 8, Size = 2.2 },
            BlockPatterns.Diamond => new BlockStyleLayer { Kind = "diamond", Color = c, Angle = 0, Spacing = 10, Size = 2.2 },
            BlockPatterns.Star => new BlockStyleLayer { Kind = "star", Color = c, Angle = 0, Spacing = 12, Size = 2.6 },
            BlockPatterns.Dot => new BlockStyleLayer { Kind = "dot", Color = c, Angle = 0, Spacing = 10, Size = 1.8 },
            BlockPatterns.Moon => new BlockStyleLayer { Kind = "moon", Color = c, Angle = 0, Spacing = 12, Size = 2.6 },
            _ => null
        };
    }
}
