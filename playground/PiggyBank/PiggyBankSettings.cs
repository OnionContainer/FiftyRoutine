using System.Globalization;
using System.IO;
using System.Text.Json;
using nkast.Aether.Physics2D;

namespace Playground.PiggyBank;

/// <summary>
/// 存钱罐物理调参。独立 JSON，便于日后迁到主程序 UserData。
/// </summary>
public sealed class PiggyBankSettings
{
    public const int SchemaVersion = 1;

    public int Version { get; set; } = SchemaVersion;

    public double PadPx { get; set; } = 1;
    public float Restitution { get; set; } = 0.05f;

    /// <summary>位置窗观察时长（秒）；窗内峰峰值够小则视为静止。</summary>
    public float StillSeconds { get; set; } = 0.6f;

    /// <summary>位置窗允许的位移峰峰值（像素）。</summary>
    public float JitterPosPx { get; set; } = 2f;

    /// <summary>位置窗允许的转角峰峰值（弧度）。</summary>
    public float JitterAng { get; set; } = 0.08f;

    public float Baumgarte { get; set; } = Settings.Baumgarte;
    public float LinearSlop { get; set; } = Settings.LinearSlop;
    public float MaxLinearCorrection { get; set; } = Settings.MaxLinearCorrection;
    /// <summary>运行时可改；Aether 2.2 下 Baumgarte/Slop/MaxLinCorr 为引擎常量，仅本项与 VelIter 即时生效。</summary>
    public int PositionIterations { get; set; } = Settings.PositionIterations;
    public int VelocityIterations { get; set; } = Settings.VelocityIterations;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string FilePath
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "PiggyBank.csproj")))
                    return Path.Combine(dir.FullName, "piggybank-settings.json");
                dir = dir.Parent;
            }
            return Path.Combine(AppContext.BaseDirectory, "piggybank-settings.json");
        }
    }

    public static PiggyBankSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new PiggyBankSettings();
            var s = JsonSerializer.Deserialize<PiggyBankSettings>(File.ReadAllText(FilePath), JsonOptions);
            return s?.Clamp() ?? new PiggyBankSettings();
        }
        catch
        {
            return new PiggyBankSettings();
        }
    }

    public void Save()
    {
        Clamp();
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    public PiggyBankSettings Clamp()
    {
        Version = SchemaVersion;
        PadPx = Math.Clamp(PadPx, 0, 8);
        Restitution = Math.Clamp(Restitution, 0, 1);
        StillSeconds = Math.Clamp(StillSeconds, 0.1f, 5f);
        JitterPosPx = Math.Clamp(JitterPosPx, 0.1f, 40f);
        JitterAng = Math.Clamp(JitterAng, 0.001f, 1.5f);
        Baumgarte = Math.Clamp(Baumgarte, 0.01f, 1f);
        LinearSlop = Math.Clamp(LinearSlop, 0.0001f, 0.05f);
        MaxLinearCorrection = Math.Clamp(MaxLinearCorrection, 0.01f, 1f);
        PositionIterations = Math.Clamp(PositionIterations, 1, 50);
        VelocityIterations = Math.Clamp(VelocityIterations, 1, 50);
        return this;
    }

    public static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    public static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    public static string F(int v) => v.ToString(CultureInfo.InvariantCulture);
}
