using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PersonalManagement.Desktop;

public sealed class BlockStylePreset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>分组名；根目录散落文件为「未分组」。</summary>
    public string Group { get; set; } = Ungrouped;
    public bool IsBuiltin { get; set; }
    public string FilePath { get; set; } = "";
    public BlockStyleSpec Spec { get; set; } = new();

    public const string Ungrouped = "未分组";
}

/// <summary>
/// 日程块样式预设：每预设一个 json；
/// 内置 <c>ProgramData/DefaultBlockStyle</c>，用户 <c>UserData/&lt;user&gt;/BlockStyle</c>。
/// </summary>
internal static class BlockStylePresets
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string BuiltinDir => Path.Combine(AppPaths.ProgramDataDir, "DefaultBlockStyle");
    public static string UserDir =>
        AppPaths.CurrentUser is null
            ? Path.Combine(AppPaths.ProgramDataDir, "BlockStyle")
            : Path.Combine(AppPaths.CurrentUserDir, "BlockStyle");

    private static string LegacyFilePath =>
        AppPaths.CurrentUser is null
            ? Path.Combine(AppPaths.ProgramDataDir, "block-style-presets.json")
            : Path.Combine(AppPaths.CurrentUserDir, "block-style-presets.json");

    private static string MigratedMarker => Path.Combine(UserDir, ".presets-migrated");

    public static void EnsureMigrated()
    {
        try
        {
            Directory.CreateDirectory(UserDir);
            Directory.CreateDirectory(BuiltinDir);
            if (File.Exists(MigratedMarker)) return;
            if (!File.Exists(LegacyFilePath))
            {
                File.WriteAllText(MigratedMarker, DateTime.Now.ToString("o"));
                return;
            }

            var list = JsonSerializer.Deserialize<List<LegacyPreset>>(File.ReadAllText(LegacyFilePath), JsonOpts);
            if (list is not null)
            {
                foreach (var p in list)
                {
                    if (p.Spec is null) continue;
                    var name = string.IsNullOrWhiteSpace(p.Name) ? "未命名" : p.Name.Trim();
                    WriteUserFile(UserDir, name, p.Spec);
                }
            }

            var bak = LegacyFilePath + ".bak";
            if (File.Exists(bak)) File.Delete(bak);
            File.Move(LegacyFilePath, bak);
            File.WriteAllText(MigratedMarker, DateTime.Now.ToString("o"));
        }
        catch
        {
            /* ignore migrate errors */
        }
    }

    public static List<BlockStylePreset> Load()
    {
        EnsureMigrated();
        var result = new List<BlockStylePreset>();
        ScanRoot(BuiltinDir, builtin: true, result);
        ScanRoot(UserDir, builtin: false, result);
        return result
            .OrderBy(p => p.IsBuiltin ? 0 : 1)
            .ThenBy(p => p.Group, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void ScanRoot(string root, bool builtin, List<BlockStylePreset> into)
    {
        if (!Directory.Exists(root)) return;

        foreach (var file in Directory.EnumerateFiles(root, "*.json"))
        {
            var p = TryReadFile(file, BlockStylePreset.Ungrouped, builtin);
            if (p is not null) into.Add(p);
        }

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var group = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(group) || group.StartsWith('.')) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
            {
                var p = TryReadFile(file, group, builtin);
                if (p is not null) into.Add(p);
            }
        }
    }

    private static BlockStylePreset? TryReadFile(string path, string group, bool builtin)
    {
        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string name;
            BlockStyleSpec? spec;
            if (root.TryGetProperty("spec", out var specEl))
            {
                name = root.TryGetProperty("name", out var n)
                    ? n.GetString() ?? Path.GetFileNameWithoutExtension(path)
                    : Path.GetFileNameWithoutExtension(path);
                spec = JsonSerializer.Deserialize<BlockStyleSpec>(specEl.GetRawText(), JsonOpts);
            }
            else
            {
                name = Path.GetFileNameWithoutExtension(path);
                spec = JsonSerializer.Deserialize<BlockStyleSpec>(json, JsonOpts);
            }

            if (spec is null) return null;
            spec.Normalize();
            if (string.IsNullOrWhiteSpace(name)) name = "未命名";
            return new BlockStylePreset
            {
                Id = path,
                Name = name,
                Group = group,
                IsBuiltin = builtin,
                FilePath = path,
                Spec = spec
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>保存到用户目录；group 为空或「未分组」则写根目录。</summary>
    public static BlockStylePreset Upsert(string name, BlockStyleSpec spec, string? group = null)
    {
        EnsureMigrated();
        Directory.CreateDirectory(UserDir);
        name = string.IsNullOrWhiteSpace(name) ? "未命名" : name.Trim();
        var g = string.IsNullOrWhiteSpace(group) || group == BlockStylePreset.Ungrouped
            ? null
            : group.Trim();
        var dir = g is null ? UserDir : Path.Combine(UserDir, SanitizeFolder(g));
        Directory.CreateDirectory(dir);
        var path = WriteUserFile(dir, name, spec);
        return new BlockStylePreset
        {
            Id = path,
            Name = name,
            Group = g ?? BlockStylePreset.Ungrouped,
            IsBuiltin = false,
            FilePath = path,
            Spec = spec.Clone()
        };
    }

    public static void Delete(string idOrPath)
    {
        if (string.IsNullOrWhiteSpace(idOrPath)) return;
        if (!File.Exists(idOrPath)) return;
        var full = Path.GetFullPath(idOrPath);
        var userRoot = Path.GetFullPath(UserDir);
        if (!full.StartsWith(userRoot, StringComparison.OrdinalIgnoreCase))
            return;
        File.Delete(full);
    }

    private static string WriteUserFile(string dir, string name, BlockStyleSpec spec)
    {
        var file = Path.Combine(dir, SanitizeFileName(name) + ".json");
        var payload = new NamedPresetFile { Name = name, Spec = spec.Clone() };
        payload.Spec.Normalize();
        File.WriteAllText(file, JsonSerializer.Serialize(payload, JsonOpts));
        return file;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name.Trim())
            sb.Append(invalid.Contains(ch) || ch is '/' or '\\' ? '_' : ch);
        var s = sb.ToString().Trim();
        if (string.IsNullOrEmpty(s)) s = "preset";
        if (s.Length > 80) s = s[..80];
        return s;
    }

    private static string SanitizeFolder(string name)
    {
        var s = SanitizeFileName(name);
        return string.IsNullOrEmpty(s) ? "group" : s;
    }

    private sealed class NamedPresetFile
    {
        public string Name { get; set; } = "";
        public BlockStyleSpec Spec { get; set; } = new();
    }

    private sealed class LegacyPreset
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public BlockStyleSpec? Spec { get; set; }
    }
}
