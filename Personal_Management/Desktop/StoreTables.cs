using System.Text.Json.Nodes;

namespace PersonalManagement.Desktop;

internal static class StoreTables
{
    public const string Tasks = "tasks";
    public const string Completions = "completions";
    public const string Sessions = "sessions";
    public const string Rewards = "reward_pool";
    public const string Wishlist = "wishlist";
    public const string State = "app_state";
    public const string Favorites = "favorites";
    public const string WeightProfile = "weight_profile";
    public const string WeightEntries = "weight_entries";

    public static readonly string[] BusinessTables =
    [
        Tasks, Completions, Sessions, Rewards, Wishlist, State
    ];

    public static readonly string[] FavoriteTables =
    [
        Favorites, State
    ];

    public static readonly string[] WeightTables =
    [
        WeightProfile, WeightEntries
    ];
}

public interface IRecordStore
{
    Task<JsonArray> ListRecordsAsync(string table);
    Task<JsonNode> CreateRecordAsync(string table, Dictionary<string, object?> fields);
    Task PatchRecordAsync(string table, Dictionary<string, object?> fields);
    Task DeleteRecordAsync(string table, string id);
    Task<JsonNode> UploadAsync(string fileName, byte[] bytes, string contentType);
    Task<byte[]> DownloadBytesAsync(string url);
}
