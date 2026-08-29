using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonalManagement.Desktop;

public sealed class BlockStylePreset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public BlockStyleSpec Spec { get; set; } = new();
}

/// <summary>日程块样式预设，存 Personal_Management/block-style-presets.json。</summary>
internal static class BlockStylePresets
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string FilePath
    {
        get
        {
            var root = Paths.FindWorkspaceRoot();
            if (root is null) return Path.Combine(AppContext.BaseDirectory, "block-style-presets.json");
            return Path.Combine(root, "Personal_Management", "block-style-presets.json");
        }
    }

    public static List<BlockStylePreset> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            var list = JsonSerializer.Deserialize<List<BlockStylePreset>>(File.ReadAllText(FilePath), JsonOpts);
            if (list is null) return [];
            foreach (var p in list)
            {
                if (string.IsNullOrWhiteSpace(p.Id)) p.Id = Guid.NewGuid().ToString("N");
                if (string.IsNullOrWhiteSpace(p.Name)) p.Name = "未命名";
                p.Spec ??= new BlockStyleSpec();
                p.Spec.Normalize();
            }
            return list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch
        {
            return [];
        }
    }

    public static void SaveAll(IEnumerable<BlockStylePreset> presets)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(presets.ToList(), JsonOpts));
    }

    /// <summary>按名称保存；同名则覆盖。</summary>
    public static BlockStylePreset Upsert(string name, BlockStyleSpec spec)
    {
        var list = Load();
        var existing = list.FirstOrDefault(p =>
            p.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
        if (existing is null)
        {
            existing = new BlockStylePreset
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name
            };
            list.Add(existing);
        }
        existing.Name = name;
        existing.Spec = spec.Clone();
        existing.Spec.Normalize();
        SaveAll(list);
        return existing;
    }

    public static void Delete(string id)
    {
        var list = Load();
        list.RemoveAll(p => p.Id == id);
        SaveAll(list);
    }
}
