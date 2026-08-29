using System.IO;
using System.Text.Json.Nodes;

namespace PersonalManagement.Desktop;

internal static class DataMigrator
{
    public static async Task DownloadBusinessToLocalAsync(NocoRecordStore noco, LocalRecordStore local, Action<string>? log = null)
    {
        local.EnsureSeeded(includeDefaultRewards: false);
        await CopyToLocalPreservingIdsAsync(noco, local, StoreTables.BusinessTables, log);
    }

    public static async Task DownloadFavoritesToLocalAsync(NocoRecordStore noco, LocalRecordStore local, Action<string>? log = null)
    {
        local.EnsureSeeded(includeDefaultRewards: false);
        await CopyToLocalPreservingIdsAsync(noco, local, [StoreTables.Favorites], log);

        log?.Invoke("同步二级密码…");
        var nocoState = (await noco.ListRecordsAsync(StoreTables.State)).FirstOrDefault();
        var pin = NocoClient.ReadString(nocoState, "PrivatePin");
        var localState = (await local.ListRecordsAsync(StoreTables.State)).FirstOrDefault();
        var id = NocoClient.ReadId(localState);
        if (id is null)
        {
            await local.CreateRecordAsync(StoreTables.State, new Dictionary<string, object?>
            {
                ["Title"] = "main",
                ["DrawTickets"] = 0,
                ["WishlistQuota"] = 0,
                ["RewardScheme"] = "prob-v1",
                ["PrivatePin"] = pin
            });
        }
        else
        {
            await local.PatchRecordAsync(StoreTables.State, new Dictionary<string, object?>
            {
                ["Id"] = id,
                ["PrivatePin"] = pin
            });
        }
    }

    public static async Task UploadBusinessFromLocalAsync(LocalRecordStore local, NocoRecordStore noco, Action<string>? log = null)
    {
        // Order: clear dependent → tasks → linked → rest
        await ClearTableAsync(noco, StoreTables.Completions, log);
        await ClearTableAsync(noco, StoreTables.Sessions, log);
        await ClearTableAsync(noco, StoreTables.Tasks, log);
        await ClearTableAsync(noco, StoreTables.Rewards, log);
        await ClearTableAsync(noco, StoreTables.Wishlist, log);

        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
        log?.Invoke("上传 tasks…");
        foreach (var row in await local.ListRecordsAsync(StoreTables.Tasks))
        {
            if (row is null) continue;
            var oldId = NocoClient.ReadId(row);
            var fields = await MaterializeAsync(local, noco, row, preserveId: false, idMap: null);
            var created = await noco.CreateRecordAsync(StoreTables.Tasks, fields);
            var newId = NocoClient.ReadId(created);
            if (oldId is not null && newId is not null)
                idMap[oldId] = newId;
        }

        log?.Invoke("上传 completions…");
        foreach (var row in await local.ListRecordsAsync(StoreTables.Completions))
        {
            if (row is null) continue;
            var fields = await MaterializeAsync(local, noco, row, preserveId: false, idMap);
            await noco.CreateRecordAsync(StoreTables.Completions, fields);
        }

        log?.Invoke("上传 sessions…");
        foreach (var row in await local.ListRecordsAsync(StoreTables.Sessions))
        {
            if (row is null) continue;
            var fields = await MaterializeAsync(local, noco, row, preserveId: false, idMap);
            await noco.CreateRecordAsync(StoreTables.Sessions, fields);
        }

        log?.Invoke("上传 reward_pool…");
        foreach (var row in await local.ListRecordsAsync(StoreTables.Rewards))
        {
            if (row is null) continue;
            var fields = await MaterializeAsync(local, noco, row, preserveId: false, idMap: null);
            await noco.CreateRecordAsync(StoreTables.Rewards, fields);
        }

        log?.Invoke("上传 wishlist…");
        foreach (var row in await local.ListRecordsAsync(StoreTables.Wishlist))
        {
            if (row is null) continue;
            var fields = await MaterializeAsync(local, noco, row, preserveId: false, idMap: null);
            await noco.CreateRecordAsync(StoreTables.Wishlist, fields);
        }

        log?.Invoke("上传 app_state（钱包）…");
        var localState = (await local.ListRecordsAsync(StoreTables.State)).FirstOrDefault();
        var nocoState = (await noco.ListRecordsAsync(StoreTables.State)).FirstOrDefault();
        if (localState is not null && NocoClient.ReadId(nocoState) is { } sid)
        {
            await noco.PatchRecordAsync(StoreTables.State, new Dictionary<string, object?>
            {
                ["Id"] = sid,
                ["DrawTickets"] = NocoClient.ReadInt(localState, "DrawTickets"),
                ["WishlistQuota"] = NocoClient.ReadInt(localState, "WishlistQuota"),
                ["RewardScheme"] = NocoClient.ReadString(localState, "RewardScheme") ?? "prob-v1"
            });
        }
    }

    public static async Task UploadFavoritesFromLocalAsync(LocalRecordStore local, NocoRecordStore noco, Action<string>? log = null)
    {
        await ClearTableAsync(noco, StoreTables.Favorites, log);
        log?.Invoke("上传 favorites…");
        foreach (var row in await local.ListRecordsAsync(StoreTables.Favorites))
        {
            if (row is null) continue;
            var fields = await MaterializeAsync(local, noco, row, preserveId: false, idMap: null);
            await noco.CreateRecordAsync(StoreTables.Favorites, fields);
        }

        var localState = (await local.ListRecordsAsync(StoreTables.State)).FirstOrDefault();
        var pin = NocoClient.ReadString(localState, "PrivatePin");
        var nocoState = (await noco.ListRecordsAsync(StoreTables.State)).FirstOrDefault();
        if (NocoClient.ReadId(nocoState) is { } id)
        {
            await noco.PatchRecordAsync(StoreTables.State, new Dictionary<string, object?>
            {
                ["Id"] = id,
                ["PrivatePin"] = pin
            });
        }
    }

    public static async Task DownloadWeightToLocalAsync(NocoRecordStore noco, LocalRecordStore local, Action<string>? log = null)
    {
        local.EnsureWeightSeeded();
        await CopyToLocalPreservingIdsAsync(noco, local, StoreTables.WeightTables, log);
    }

    public static async Task UploadWeightFromLocalAsync(LocalRecordStore local, NocoRecordStore noco, Action<string>? log = null)
    {
        await ClearTableAsync(noco, StoreTables.WeightEntries, log);
        await ClearTableAsync(noco, StoreTables.WeightProfile, log);
        log?.Invoke("上传 weight_profile…");
        foreach (var row in await local.ListRecordsAsync(StoreTables.WeightProfile))
        {
            if (row is null) continue;
            var fields = await MaterializeAsync(local, noco, row, preserveId: false, idMap: null);
            await noco.CreateRecordAsync(StoreTables.WeightProfile, fields);
        }
        log?.Invoke("上传 weight_entries…");
        foreach (var row in await local.ListRecordsAsync(StoreTables.WeightEntries))
        {
            if (row is null) continue;
            var fields = await MaterializeAsync(local, noco, row, preserveId: false, idMap: null);
            await noco.CreateRecordAsync(StoreTables.WeightEntries, fields);
        }
    }

    private static async Task CopyToLocalPreservingIdsAsync(
        IRecordStore from, LocalRecordStore to, IReadOnlyList<string> tables, Action<string>? log)
    {
        foreach (var table in tables)
        {
            log?.Invoke($"下载表 {table}…");
            await ClearTableAsync(to, table, null);
            foreach (var row in await from.ListRecordsAsync(table))
            {
                if (row is null) continue;
                var fields = await MaterializeAsync(from, to, row, preserveId: true, idMap: null);
                await to.CreateRecordAsync(table, fields);
            }
        }
    }

    private static async Task ClearTableAsync(IRecordStore store, string table, Action<string>? log)
    {
        log?.Invoke($"清空 {table}…");
        foreach (var row in await store.ListRecordsAsync(table))
        {
            var id = NocoClient.ReadId(row);
            if (id is not null)
                await store.DeleteRecordAsync(table, id);
        }
    }

    private static async Task<Dictionary<string, object?>> MaterializeAsync(
        IRecordStore from,
        IRecordStore to,
        JsonNode row,
        bool preserveId,
        Dictionary<string, string>? idMap)
    {
        var fields = new Dictionary<string, object?>();
        if (row is not JsonObject obj) return fields;

        foreach (var (key, value) in obj)
        {
            if (IsMetaKey(key)) continue;

            if (IsAttachmentField(key) && value is not null)
            {
                fields[key] = await TransferAttachmentAsync(from, to, value);
                continue;
            }

            if (key.Equals("Task", StringComparison.OrdinalIgnoreCase) && idMap is not null)
            {
                var old = RewardLogic.LinkedId(row, "Task");
                if (old is not null && idMap.TryGetValue(old, out var mapped))
                    fields[key] = mapped;
                else
                    fields[key] = old;
                continue;
            }

            fields[key] = Unwrap(value);
        }

        if (preserveId)
        {
            var id = NocoClient.ReadId(row);
            if (id is not null)
                fields["Id"] = id;
        }

        return fields;
    }

    private static bool IsMetaKey(string key) =>
        key.Equals("Id", StringComparison.OrdinalIgnoreCase)
        || key.Equals("id", StringComparison.OrdinalIgnoreCase)
        || key.Equals("ID", StringComparison.OrdinalIgnoreCase)
        || key.Equals("CreatedAt", StringComparison.OrdinalIgnoreCase)
        || key.Equals("UpdatedAt", StringComparison.OrdinalIgnoreCase)
        || key.Equals("created_at", StringComparison.OrdinalIgnoreCase)
        || key.Equals("updated_at", StringComparison.OrdinalIgnoreCase);

    private static bool IsAttachmentField(string key) =>
        key.Equals("Thumb", StringComparison.OrdinalIgnoreCase)
        || key.Equals("File", StringComparison.OrdinalIgnoreCase);

    private static async Task<object?> TransferAttachmentAsync(IRecordStore from, IRecordStore to, JsonNode value)
    {
        var url = NocoClient.FirstFileUrl(value);
        if (url is null) return null;
        try
        {
            var bytes = await from.DownloadBytesAsync(url);
            var title = "file.bin";
            var mime = "application/octet-stream";
            if (value is JsonArray arr && arr.Count > 0)
            {
                title = arr[0]?["title"]?.ToString() ?? title;
                mime = arr[0]?["mimetype"]?.ToString() ?? mime;
            }
            else
            {
                title = value["title"]?.ToString() ?? Path.GetFileName(url.Split(':').LastOrDefault() ?? "") ?? title;
                mime = value["mimetype"]?.ToString() ?? mime;
            }
            return await to.UploadAsync(title, bytes, mime);
        }
        catch
        {
            return null;
        }
    }

    private static object? Unwrap(JsonNode? value)
    {
        if (value is null) return null;
        if (value is JsonValue jv)
        {
            if (jv.TryGetValue<bool>(out var b)) return b;
            if (jv.TryGetValue<int>(out var i)) return i;
            if (jv.TryGetValue<long>(out var l)) return l;
            if (jv.TryGetValue<double>(out var d)) return d;
            return jv.ToString();
        }
        var id = NocoClient.ReadId(value);
        if (id is not null && value is JsonObject)
            return id;
        return value.DeepClone();
    }
}
