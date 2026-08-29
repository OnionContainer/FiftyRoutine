using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PersonalManagement.Desktop;

public sealed class NocoClient : IDisposable
{
    private readonly HttpClient _http;
    private string? _jwt;
    private string? _apiToken;

    public NocoClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public void Dispose() => _http.Dispose();
    public void SetApiToken(string token) => _apiToken = token;

    public async Task SignInAsync(string email, string password)
    {
        var node = await SendAsync(HttpMethod.Post, "/api/v1/auth/user/signin", new { email, password });
        _jwt = node?["token"]?.ToString()
            ?? throw new InvalidOperationException("登录未返回 token");
    }

    public async Task<string> EnsureApiTokenAsync(string description)
    {
        var listed = await SendAsync(HttpMethod.Get, "/api/v1/tokens");
        foreach (var item in AsList(listed))
        {
            if (string.Equals(item?["description"]?.ToString(), description, StringComparison.OrdinalIgnoreCase)
                && item?["token"]?.ToString() is { Length: > 0 } existing)
                return existing;
        }
        var created = await SendAsync(HttpMethod.Post, "/api/v1/tokens", new { description });
        return created?["token"]?.ToString()
            ?? throw new InvalidOperationException("创建 API Token 失败");
    }

    public async Task<string> EnsureBaseAsync(string title)
    {
        var listed = await SendAsync(HttpMethod.Get, "/api/v1/db/meta/projects/");
        foreach (var item in AsList(listed))
        {
            if (string.Equals(item?["title"]?.ToString(), title, StringComparison.OrdinalIgnoreCase))
                return ReadId(item) ?? throw new InvalidOperationException("base 无 id");
        }

        try
        {
            var created = await SendAsync(HttpMethod.Post, "/api/v1/db/meta/projects/", new { title })
                ?? throw new InvalidOperationException("create base empty");
            return ReadId(created) ?? throw new InvalidOperationException("created base 无 id");
        }
        catch (Exception first)
        {
            var info = await GetJsonAsync("/api/v2/meta/nocodb/info");
            var ws = info?["defaultWorkspaceId"]?.ToString();
            if (string.IsNullOrWhiteSpace(ws)) throw;
            var created = await SendAsync(HttpMethod.Post, "/api/v2/meta/bases", new { title, fk_workspace_id = ws })
                ?? throw new InvalidOperationException("v2 create base empty: " + first.Message);
            return ReadId(created) ?? throw new InvalidOperationException("v2 base 无 id");
        }
    }

    public async Task<string> EnsureTableAsync(string baseId, string tableName, IReadOnlyList<Dictionary<string, object?>> extraColumns)
    {
        var listed = await SendAsync(HttpMethod.Get, $"/api/v1/db/meta/projects/{baseId}/tables");
        foreach (var item in AsList(listed))
        {
            var name = item?["table_name"]?.ToString() ?? item?["title"]?.ToString();
            if (string.Equals(name, tableName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item?["title"]?.ToString(), tableName, StringComparison.OrdinalIgnoreCase))
                return ReadId(item) ?? throw new InvalidOperationException("table 无 id");
        }

        var columns = new List<Dictionary<string, object?>> { Col("Title", "SingleLineText") };
        columns.AddRange(extraColumns);
        var created = await SendAsync(HttpMethod.Post, $"/api/v1/db/meta/projects/{baseId}/tables", new
        {
            table_name = tableName,
            title = tableName,
            columns
        }) ?? throw new InvalidOperationException("create table empty");
        return ReadId(created) ?? throw new InvalidOperationException("created table 无 id");
    }

    public async Task<string> EnsureLinkColumnAsync(string parentTableId, string childTableId, string titleOnChild)
    {
        async Task<string?> FindAsync()
        {
            var table = await SendAsync(HttpMethod.Get, $"/api/v1/db/meta/tables/{childTableId}");
            foreach (var col in table?["columns"] as JsonArray ?? [])
            {
                var uidt = col?["uidt"]?.ToString();
                if (string.Equals(col?["title"]?.ToString(), titleOnChild, StringComparison.OrdinalIgnoreCase)
                    && uidt is "Links" or "LinkToAnotherRecord" or "Link")
                {
                    var colId = col?["id"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(colId) && colId != childTableId && colId != parentTableId)
                        return colId;
                }
            }
            return null;
        }

        var existing = await FindAsync();
        if (existing is not null) return existing;

        await SendAsync(HttpMethod.Post, $"/api/v1/db/meta/tables/{childTableId}/columns", new Dictionary<string, object?>
        {
            ["title"] = titleOnChild,
            ["uidt"] = "Links",
            ["parentId"] = parentTableId,
            ["childId"] = childTableId,
            ["type"] = "bt"
        });
        return await FindAsync() ?? throw new InvalidOperationException("关系列创建后找不到 id");
    }

    public async Task EnsureColumnAsync(string tableId, Dictionary<string, object?> column)
    {
        var table = await SendAsync(HttpMethod.Get, $"/api/v1/db/meta/tables/{tableId}");
        var title = column["title"]?.ToString();
        foreach (var col in table?["columns"] as JsonArray ?? [])
        {
            if (string.Equals(col?["title"]?.ToString(), title, StringComparison.OrdinalIgnoreCase))
                return;
        }

        await SendAsync(HttpMethod.Post, $"/api/v1/db/meta/tables/{tableId}/columns", column);
    }

    public async Task<JsonNode> CreateRecordAsync(string tableId, Dictionary<string, object?> fields)
    {
        var node = await SendAsync(HttpMethod.Post, $"/api/v2/tables/{tableId}/records", fields)
            ?? throw new InvalidOperationException("create record empty");
        if (node is JsonArray arr && arr.Count > 0) return arr[0]!;
        return node;
    }

    public async Task PatchRecordAsync(string tableId, Dictionary<string, object?> fields)
    {
        await SendAsync(HttpMethod.Patch, $"/api/v2/tables/{tableId}/records", fields);
    }

    public async Task DeleteRecordAsync(string tableId, string id)
    {
        await SendAsync(HttpMethod.Delete, $"/api/v2/tables/{tableId}/records",
            new[] { new Dictionary<string, object?> { ["Id"] = id } });
    }

    public async Task<JsonArray> ListRecordsAsync(string tableId, string? where = null, int limit = 500)
    {
        var q = $"limit={limit}";
        if (!string.IsNullOrWhiteSpace(where))
            q += "&where=" + Uri.EscapeDataString(where);
        var node = await SendAsync(HttpMethod.Get, $"/api/v2/tables/{tableId}/records?{q}");
        return AsList(node);
    }

    public async Task<JsonNode> UploadAsync(string fileName, byte[] bytes, string contentType)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
        try
        {
            return await SendAsync(HttpMethod.Post, "/api/v2/storage/upload", form: form)
                ?? throw new InvalidOperationException("upload empty");
        }
        catch
        {
            using var form2 = new MultipartFormDataContent();
            var fileContent2 = new ByteArrayContent(bytes);
            fileContent2.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form2.Add(fileContent2, "file", fileName);
            return await SendAsync(HttpMethod.Post, "/api/v1/db/storage/upload", form: form2)
                ?? throw new InvalidOperationException("upload v1 empty");
        }
    }

    public async Task DownloadAsync(string url, string destPath)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, ToAbsolute(url));
        ApplyAuth(req);
        using var resp = await _http.SendAsync(req);
        var body = await resp.Content.ReadAsByteArrayAsync();
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"GET {url} {(int)resp.StatusCode}: {Encoding.UTF8.GetString(body)}");
        await File.WriteAllBytesAsync(destPath, body);
    }

    public async Task<byte[]> DownloadBytesAsync(string url)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, ToAbsolute(url));
        ApplyAuth(req);
        using var resp = await _http.SendAsync(req);
        var body = await resp.Content.ReadAsByteArrayAsync();
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"GET {url} {(int)resp.StatusCode}: {Encoding.UTF8.GetString(body)}");
        return body;
    }

    public Task<JsonNode?> GetJsonAsync(string path) => SendAsync(HttpMethod.Get, path);

    public static string? ReadId(JsonNode? node) =>
        node?["id"]?.ToString() ?? node?["Id"]?.ToString() ?? node?["ID"]?.ToString();

    public static int ReadInt(JsonNode? node, string name, int fallback = 0)
    {
        var v = node?[name];
        if (v is null) return fallback;
        if (v is JsonValue jv)
        {
            if (jv.TryGetValue<int>(out var i)) return i;
            if (jv.TryGetValue<long>(out var l)) return (int)l;
            if (jv.TryGetValue<double>(out var d)) return (int)d;
        }
        return int.TryParse(v.ToString(), out var parsed) ? parsed : fallback;
    }

    public static double ReadDouble(JsonNode? node, string name, double fallback = 0)
    {
        var v = node?[name];
        if (v is null) return fallback;
        if (v is JsonValue jv)
        {
            if (jv.TryGetValue<double>(out var d)) return d;
            if (jv.TryGetValue<int>(out var i)) return i;
            if (jv.TryGetValue<long>(out var l)) return l;
        }
        return double.TryParse(v.ToString(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    }

    public static string? ReadString(JsonNode? node, string name) => node?[name]?.ToString();

    public static bool ReadBool(JsonNode? node, string name)
    {
        var v = node?[name];
        if (v is null) return false;
        if (v is JsonValue jv)
        {
            if (jv.TryGetValue<bool>(out var b)) return b;
            if (jv.TryGetValue<int>(out var i)) return i != 0;
        }
        var s = v.ToString();
        return s is "true" or "True" or "1";
    }

    public static string? FirstFileUrl(JsonNode? node)
    {
        if (node is JsonArray arr && arr.Count > 0)
            node = arr[0];
        return node?["signedUrl"]?.ToString()
            ?? node?["url"]?.ToString()
            ?? node?["path"]?.ToString();
    }

    public static JsonNode? FileField(JsonNode? record, string name) => record?[name];

    public static JsonArray AsList(JsonNode? node)
    {
        if (node is JsonArray arr) return arr;
        if (node?["list"] is JsonArray list) return list;
        return [];
    }

    private static Dictionary<string, object?> Col(string title, string uidt) =>
        new() { ["title"] = title, ["uidt"] = uidt };

    private Uri ToAbsolute(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var abs) ? abs : new Uri(_http.BaseAddress!, url.TrimStart('/'));

    private void ApplyAuth(HttpRequestMessage req)
    {
        if (!string.IsNullOrWhiteSpace(_jwt))
        {
            req.Headers.TryAddWithoutValidation("xc-auth", _jwt);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jwt);
        }
        if (!string.IsNullOrWhiteSpace(_apiToken))
            req.Headers.TryAddWithoutValidation("xc-token", _apiToken);
    }

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, object? body = null, MultipartFormDataContent? form = null)
    {
        using var req = new HttpRequestMessage(method, path.TrimStart('/'));
        ApplyAuth(req);
        if (form is not null)
            req.Content = form;
        else if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(req);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{method} {path} {(int)response.StatusCode}: {text}");
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }
}
