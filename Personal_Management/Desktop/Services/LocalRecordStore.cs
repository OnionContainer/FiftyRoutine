using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PersonalManagement.Desktop;

/// <summary>本地 JSON + files/ 附件。表名用逻辑名（tasks 等）。</summary>
internal sealed class LocalRecordStore : IRecordStore
{
    public const string LocalUrlPrefix = "localfile:";

    private readonly string _root;
    private readonly object _gate = new();

    public LocalRecordStore(string rootDir)
    {
        _root = rootDir;
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(FilesDir);
    }

    public string Root => _root;
    private string FilesDir => Path.Combine(_root, "files");

    public void EnsureSeeded(bool includeDefaultRewards)
    {
        lock (_gate)
        {
            EnsureTableFile(StoreTables.Tasks);
            EnsureTableFile(StoreTables.Completions);
            EnsureTableFile(StoreTables.Sessions);
            EnsureTableFile(StoreTables.ScheduleNotes);
            EnsureTableFile(StoreTables.Rewards);
            EnsureTableFile(StoreTables.Wishlist);
            EnsureTableFile(StoreTables.State);
            EnsureTableFile(StoreTables.Favorites);

            var state = ReadArrayUnlocked(StoreTables.State);
            if (state.Count == 0)
            {
                state.Add(ToObject(new Dictionary<string, object?>
                {
                    ["Id"] = Guid.NewGuid().ToString("N"),
                    ["Title"] = "main",
                    ["DrawTickets"] = 0,
                    ["WishlistQuota"] = 0,
                    ["RewardScheme"] = "prob-v1",
                    ["PrivatePin"] = null
                }));
                WriteArrayUnlocked(StoreTables.State, state);
            }

            if (includeDefaultRewards)
            {
                var rewards = ReadArrayUnlocked(StoreTables.Rewards);
                if (rewards.Count == 0)
                {
                    rewards.Add(Prize("贴纸一张", "item", 0, 0, true));
                    rewards.Add(Prize("愿望单额度 +1", "quota", 1, 0, false));
                    rewards.Add(Prize("愿望单额度 +3", "quota", 3, 0, false));
                    rewards.Add(Prize("奖券 +1", "ticket", 1, 0, false));
                    WriteArrayUnlocked(StoreTables.Rewards, rewards);
                }
            }
        }
    }

    public void EnsureWeightSeeded()
    {
        lock (_gate)
        {
            EnsureTableFile(StoreTables.WeightProfile);
            EnsureTableFile(StoreTables.WeightEntries);
            var profile = ReadArrayUnlocked(StoreTables.WeightProfile);
            if (profile.Count == 0)
            {
                profile.Add(ToObject(new Dictionary<string, object?>
                {
                    ["Id"] = Guid.NewGuid().ToString("N"),
                    ["Title"] = "main",
                    ["HeightCm"] = 170,
                    ["AgeYears"] = 30,
                    ["Sex"] = "male",
                    ["Activity"] = "sedentary"
                }));
                WriteArrayUnlocked(StoreTables.WeightProfile, profile);
            }
        }
    }

    public Task<JsonArray> ListRecordsAsync(string table)
    {
        lock (_gate)
            return Task.FromResult(CloneArray(ReadArrayUnlocked(table)));
    }

    public Task<JsonNode> CreateRecordAsync(string table, Dictionary<string, object?> fields)
    {
        lock (_gate)
        {
            var arr = ReadArrayUnlocked(table);
            var obj = ToObject(fields);
            if (string.IsNullOrWhiteSpace(NocoClient.ReadId(obj)))
                obj["Id"] = Guid.NewGuid().ToString("N");
            arr.Add(obj);
            WriteArrayUnlocked(table, arr);
            return Task.FromResult((JsonNode)obj.DeepClone()!);
        }
    }

    public Task PatchRecordAsync(string table, Dictionary<string, object?> fields)
    {
        lock (_gate)
        {
            var id = fields.TryGetValue("Id", out var idObj) ? idObj?.ToString() : null;
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("Patch 需要 Id");
            var arr = ReadArrayUnlocked(table);
            for (var i = 0; i < arr.Count; i++)
            {
                if (NocoClient.ReadId(arr[i]) != id) continue;
                var obj = arr[i] as JsonObject ?? new JsonObject();
                foreach (var (k, v) in fields)
                {
                    if (k.Equals("Id", StringComparison.OrdinalIgnoreCase)) continue;
                    obj[k] = ToJson(v);
                }
                arr[i] = obj;
                WriteArrayUnlocked(table, arr);
                return Task.CompletedTask;
            }
            throw new InvalidOperationException($"本地记录不存在：{table}/{id}");
        }
    }

    public Task DeleteRecordAsync(string table, string id)
    {
        lock (_gate)
        {
            var arr = ReadArrayUnlocked(table);
            for (var i = arr.Count - 1; i >= 0; i--)
            {
                if (NocoClient.ReadId(arr[i]) == id)
                    arr.RemoveAt(i);
            }
            WriteArrayUnlocked(table, arr);
            return Task.CompletedTask;
        }
    }

    public Task<JsonNode> UploadAsync(string fileName, byte[] bytes, string contentType)
    {
        lock (_gate)
        {
            var ext = Path.GetExtension(fileName);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".bin";
            var name = Guid.NewGuid().ToString("N") + ext.ToLowerInvariant();
            var abs = Path.Combine(FilesDir, name);
            File.WriteAllBytes(abs, bytes);
            var url = LocalUrlPrefix + name;
            var node = new JsonArray
            {
                new JsonObject
                {
                    ["title"] = fileName,
                    ["url"] = url,
                    ["path"] = url,
                    ["mimetype"] = contentType
                }
            };
            return Task.FromResult((JsonNode)node);
        }
    }

    public Task<byte[]> DownloadBytesAsync(string url)
    {
        if (url.StartsWith(LocalUrlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var name = url[LocalUrlPrefix.Length..];
            var abs = Path.Combine(FilesDir, name);
            if (!File.Exists(abs))
                throw new FileNotFoundException("本地附件不存在", abs);
            return Task.FromResult(File.ReadAllBytes(abs));
        }

        // Absolute path fallback (migration leftovers)
        if (File.Exists(url))
            return Task.FromResult(File.ReadAllBytes(url));

        throw new InvalidOperationException("本地库无法下载远程 URL：" + url);
    }

    public void ClearAll()
    {
        lock (_gate)
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(FilesDir);
        }
    }

    public bool HasAnyData(IEnumerable<string> tables)
    {
        lock (_gate)
        {
            foreach (var t in tables)
            {
                if (t == StoreTables.State)
                {
                    // seed state alone doesn't count as "user data" for wipe-after-upload
                    continue;
                }
                if (ReadArrayUnlocked(t).Count > 0) return true;
            }
            var state = ReadArrayUnlocked(StoreTables.State);
            if (state.Count > 0)
            {
                var row = state[0];
                if (NocoClient.ReadInt(row, "DrawTickets") > 0) return true;
                if (NocoClient.ReadInt(row, "WishlistQuota") > 0) return true;
                if (!string.IsNullOrWhiteSpace(NocoClient.ReadString(row, "PrivatePin"))) return true;
            }
            return false;
        }
    }

    private void EnsureTableFile(string table)
    {
        var path = TablePath(table);
        if (!File.Exists(path))
            File.WriteAllText(path, "[]");
    }

    private string TablePath(string table) => Path.Combine(_root, table + ".json");

    private JsonArray ReadArrayUnlocked(string table)
    {
        EnsureTableFile(table);
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(TablePath(table)));
            return node as JsonArray ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void WriteArrayUnlocked(string table, JsonArray arr)
    {
        EnsureTableFile(table);
        File.WriteAllText(TablePath(table), arr.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static JsonArray CloneArray(JsonArray src)
    {
        var copy = new JsonArray();
        foreach (var n in src)
            copy.Add(n?.DeepClone());
        return copy;
    }

    private static JsonObject ToObject(Dictionary<string, object?> fields)
    {
        var obj = new JsonObject();
        foreach (var (k, v) in fields)
            obj[k] = ToJson(v);
        return obj;
    }

    private static JsonNode? ToJson(object? v) => v switch
    {
        null => null,
        JsonNode jn => jn.DeepClone(),
        string s => s,
        bool b => b,
        int i => i,
        long l => l,
        double d => d,
        float f => f,
        decimal m => m,
        _ => JsonSerializer.SerializeToNode(v)
    };

    private static JsonObject Prize(string title, string kind, int quota, double probability, bool isBase) =>
        ToObject(new Dictionary<string, object?>
        {
            ["Id"] = Guid.NewGuid().ToString("N"),
            ["Title"] = title,
            ["Kind"] = kind,
            ["QuotaAmount"] = quota,
            ["Probability"] = probability,
            ["IsBase"] = isBase,
            ["Archived"] = false
        });
}
