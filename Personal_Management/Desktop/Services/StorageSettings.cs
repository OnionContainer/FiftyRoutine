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
    /// <summary>新用户默认全关；迁移自旧文件时保留原值。</summary>
    public bool UseNocoBusiness { get; set; }
    public bool UseNocoFavorites { get; set; }
    public bool UseNocoWeight { get; set; }
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
        if (AppPaths.CurrentUser is null)
            throw new InvalidOperationException("尚未选择用户，无法读写设置。");
        return Path.Combine(AppPaths.CurrentUserDir, "settings.json");
    }

    public static string LocalRoot()
    {
        if (AppPaths.CurrentUser is null)
            throw new InvalidOperationException("尚未选择用户，无法访问本地库。");
        return Path.Combine(AppPaths.CurrentUserDir, "local");
    }

    public static StorageSettings Load() => LoadFromFile(FilePath());

    public static StorageSettings LoadFromFile(string path)
    {
        if (!File.Exists(path))
            return new StorageSettings();
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (node is null) return new StorageSettings();
            return new StorageSettings
            {
                UseNocoBusiness = node["useNocoBusiness"]?.GetValue<bool>() ?? false,
                UseNocoFavorites = node["useNocoFavorites"]?.GetValue<bool>() ?? false,
                UseNocoWeight = node["useNocoWeight"]?.GetValue<bool>() ?? false,
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

    public void Save() => SaveTo(FilePath());

    public void SaveTo(string path)
    {
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

    /// <summary>仅使用本用户设置，不再回退 admin txt。</summary>
    public AdminFile ResolveAdmin()
    {
        var url = string.IsNullOrWhiteSpace(Url)
            ? throw new InvalidDataException("未配置 NocoDB URL（请在设置页填写）。")
            : Url.Trim().TrimEnd('/');
        var email = string.IsNullOrWhiteSpace(Email)
            ? throw new InvalidDataException("未配置 NocoDB Email。")
            : Email.Trim();
        var password = string.IsNullOrWhiteSpace(Password)
            ? throw new InvalidDataException("未配置 NocoDB Password。")
            : Password;
        return new AdminFile
        {
            Url = url,
            Email = email,
            Password = password,
            ApiToken = string.IsNullOrWhiteSpace(ApiToken) ? null : ApiToken.Trim(),
            HoneyView = string.IsNullOrWhiteSpace(HoneyView) ? null : HoneyView.Trim(),
            Container = string.IsNullOrWhiteSpace(Container) ? "nocodb-vibecoding" : Container.Trim()
        };
    }
}
