using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NAudio.Wave.SampleProviders;

namespace Playground;

internal sealed class SavedSettings
{
    public double DurationSeconds { get; set; } = 1.2;
    public List<SavedWave> Waves { get; set; } = [];
}

internal sealed class SavedWave
{
    public string Type { get; set; } = "Sin";
    public double Frequency { get; set; } = 440;
    public double Gain { get; set; } = 0.15;
}

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string FilePath
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Playground.csproj")))
                    return Path.Combine(dir.FullName, "last-settings.json");
                dir = dir.Parent;
            }
            return Path.Combine(AppContext.BaseDirectory, "last-settings.json");
        }
    }

    public static SavedSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return Default();
            var json = File.ReadAllText(FilePath);
            var saved = JsonSerializer.Deserialize<SavedSettings>(json, JsonOptions);
            if (saved is null || saved.Waves.Count == 0) return Default();
            saved.DurationSeconds = Math.Clamp(saved.DurationSeconds, 0.05, 30);
            foreach (var w in saved.Waves)
            {
                w.Frequency = Math.Clamp(w.Frequency, 1, 12000);
                w.Gain = Math.Clamp(w.Gain, 0, 1);
                if (!Enum.TryParse<SignalGeneratorType>(w.Type, true, out _))
                    w.Type = "Sin";
            }
            return saved;
        }
        catch
        {
            return Default();
        }
    }

    public static void Save(SavedSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public static SavedSettings Default() => new()
    {
        DurationSeconds = 1.2,
        Waves = [new SavedWave()]
    };
}
