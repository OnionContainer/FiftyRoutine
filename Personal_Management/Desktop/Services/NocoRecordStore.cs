using System.Text.Json.Nodes;

namespace PersonalManagement.Desktop;

internal sealed class NocoRecordStore : IRecordStore
{
    private readonly NocoClient _noco;
    private readonly SchemaIds _schema;

    public NocoRecordStore(NocoClient noco, SchemaIds schema)
    {
        _noco = noco;
        _schema = schema;
    }

    public NocoClient Client => _noco;
    public SchemaIds Schema => _schema;

    public Task<JsonArray> ListRecordsAsync(string table) =>
        _noco.ListRecordsAsync(Map(table));

    public Task<JsonNode> CreateRecordAsync(string table, Dictionary<string, object?> fields) =>
        _noco.CreateRecordAsync(Map(table), fields);

    public Task PatchRecordAsync(string table, Dictionary<string, object?> fields) =>
        _noco.PatchRecordAsync(Map(table), fields);

    public Task DeleteRecordAsync(string table, string id) =>
        _noco.DeleteRecordAsync(Map(table), id);

    public Task<JsonNode> UploadAsync(string fileName, byte[] bytes, string contentType) =>
        _noco.UploadAsync(fileName, bytes, contentType);

    public Task<byte[]> DownloadBytesAsync(string url) =>
        _noco.DownloadBytesAsync(url);

    private string Map(string table) => table switch
    {
        StoreTables.Tasks => _schema.Tasks,
        StoreTables.Completions => _schema.Completions,
        StoreTables.Sessions => _schema.Sessions,
        StoreTables.ScheduleNotes => _schema.ScheduleNotes,
        StoreTables.Rewards => _schema.Rewards,
        StoreTables.Wishlist => _schema.Wishlist,
        StoreTables.State => _schema.State,
        StoreTables.Favorites => _schema.Favorites,
        StoreTables.WeightProfile => _schema.WeightProfile,
        StoreTables.WeightEntries => _schema.WeightEntries,
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, null)
    };
}
