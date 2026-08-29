using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PersonalManagement.Desktop;

public enum NocoConnectMode
{
    UrlOnly = 0,
    DockerThenUrl = 1
}

public sealed class StorageSettings
{
    public bool UseNocoBusiness { get; set; } = true;
    public bool UseNocoFavorites { get; set; } = true;
    public bool UseNocoWeight { get; set; } = true;
    public NocoConnectMode ConnectMode { get; set; } = NocoConnectMode.DockerThenUrl;
    public string? Url { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? ApiToken { get; set; }
    public string? Container { get; set; }
    public string? HoneyView { get; set; }
    public string? LlmApiKey { get; set; }
    public string? LlmBaseUrl { get; set; }
    public string? LlmModel { get; set; }

    public static string FilePath()
    {
        var root = Paths.FindWorkspaceRoot();
        return root is null
            ? Path.Combine(AppContext.BaseDirectory, "storage.json")
            : Path.Combine(root, "Personal_Management", "storage.json");
    }

    public static string LocalRoot()
    {
        var root = Paths.FindWorkspaceRoot();
        return root is null
            ? Path.Combine(AppContext.BaseDirectory, "local")
            : Path.Combine(root, "Personal_Management", "local");
    }

    public static StorageSettings Load()
    {
        var path = FilePath();
        if (!File.Exists(path))
            return new StorageSettings();
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (node is null) return new StorageSettings();
            return new StorageSettings
            {
                UseNocoBusiness = node["useNocoBusiness"]?.GetValue<bool>() ?? true,
                UseNocoFavorites = node["useNocoFavorites"]?.GetValue<bool>() ?? true,
                UseNocoWeight = node["useNocoWeight"]?.GetValue<bool>() ?? true,
                ConnectMode = (NocoConnectMode)(node["connectMode"]?.GetValue<int>() ?? 1),
                Url = node["url"]?.GetValue<string>(),
                Email = node["email"]?.GetValue<string>(),
                Password = node["password"]?.GetValue<string>(),
                ApiToken = node["apiToken"]?.GetValue<string>(),
                Container = node["container"]?.GetValue<string>(),
                HoneyView = node["honeyView"]?.GetValue<string>(),
                LlmApiKey = node["llmApiKey"]?.GetValue<string>(),
                LlmBaseUrl = node["llmBaseUrl"]?.GetValue<string>(),
                LlmModel = node["llmModel"]?.GetValue<string>()
            };
        }
        catch
        {
            return new StorageSettings();
        }
    }

    public void Save()
    {
        var path = FilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var obj = new JsonObject
        {
            ["useNocoBusiness"] = UseNocoBusiness,
            ["useNocoFavorites"] = UseNocoFavorites,
            ["useNocoWeight"] = UseNocoWeight,
            ["connectMode"] = (int)ConnectMode,
            ["url"] = Url,
            ["email"] = Email,
            ["password"] = Password,
            ["apiToken"] = ApiToken,
            ["container"] = Container,
            ["honeyView"] = HoneyView,
            ["llmApiKey"] = LlmApiKey,
            ["llmBaseUrl"] = LlmBaseUrl,
            ["llmModel"] = LlmModel
        };
        File.WriteAllText(path, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public AdminFile ResolveAdmin(AdminFile? fileFallback)
    {
        var url = FirstNonEmpty(Url, fileFallback?.Url)
                  ?? throw new InvalidDataException("未配置 NocoDB URL（设置页或 nocodb-admin.txt）");
        var email = FirstNonEmpty(Email, fileFallback?.Email)
                    ?? throw new InvalidDataException("未配置 NocoDB Email");
        var password = FirstNonEmpty(Password, fileFallback?.Password)
                       ?? throw new InvalidDataException("未配置 NocoDB Password");
        return new AdminFile
        {
            Url = url.TrimEnd('/'),
            Email = email,
            Password = password,
            ApiToken = FirstNonEmpty(ApiToken, fileFallback?.ApiToken),
            HoneyView = FirstNonEmpty(HoneyView, fileFallback?.HoneyView),
            Container = FirstNonEmpty(Container, fileFallback?.Container) ?? "nocodb-vibecoding"
        };
    }

    private static string? FirstNonEmpty(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) ? a.Trim() : (!string.IsNullOrWhiteSpace(b) ? b.Trim() : null);
}
