using System.IO;

namespace PersonalManagement.Desktop;

internal static class Paths
{
    /// <summary>仅用于探测旧工作区（迁移）；正式根见 <see cref="AppPaths.ProjectRoot"/>。</summary>
    public static string? FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "nocodb-admin.txt"))
                || File.Exists(Path.Combine(dir.FullName, "个人管理工具需求.md"))
                || File.Exists(Path.Combine(dir.FullName, "需求.md"))
                || Directory.Exists(Path.Combine(dir.FullName, "Personal_Management", "local")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}

public sealed class AdminFile
{
    public required string Url { get; init; }
    public required string Email { get; init; }
    public required string Password { get; init; }
    public string? ApiToken { get; init; }
    public string? HoneyView { get; init; }
    public string? Container { get; init; }

    public static AdminFile? TryLoad(string path)
    {
        try
        {
            return Load(path);
        }
        catch
        {
            return null;
        }
    }

    public static AdminFile Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("缺少 nocodb-admin.txt", path);
        string? url = null, email = null, password = null, apiToken = null, honeyView = null, container = null;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var idx = line.IndexOf(':');
            if (idx < 0) continue;
            var key = line[..idx].Trim();
            var val = line[(idx + 1)..].Trim();
            if (key.Equals("URL", StringComparison.OrdinalIgnoreCase)) url = val;
            else if (key.Equals("Email", StringComparison.OrdinalIgnoreCase)) email = val;
            else if (key.Equals("Password", StringComparison.OrdinalIgnoreCase)) password = val;
            else if (key.Equals("ApiToken", StringComparison.OrdinalIgnoreCase)) apiToken = val;
            else if (key.Equals("HoneyView", StringComparison.OrdinalIgnoreCase)) honeyView = val;
            else if (key.Equals("Container", StringComparison.OrdinalIgnoreCase)) container = val;
        }
        if (url is null || email is null || password is null)
            throw new InvalidDataException("nocodb-admin.txt 需要 URL / Email / Password");
        return new AdminFile
        {
            Url = url.TrimEnd('/'),
            Email = email,
            Password = password,
            ApiToken = apiToken,
            HoneyView = honeyView,
            Container = string.IsNullOrWhiteSpace(container) ? "nocodb-vibecoding" : container
        };
    }
}

public sealed class SchemaIds
{
    public required string BaseId { get; init; }
    public required string Tasks { get; init; }
    public required string Completions { get; init; }
    public required string Sessions { get; init; }
    public required string ScheduleNotes { get; init; }
    public required string Rewards { get; init; }
    public required string Wishlist { get; init; }
    public required string State { get; init; }
    public required string Favorites { get; init; }
    public required string WeightProfile { get; init; }
    public required string WeightEntries { get; init; }
}

/// <summary>运行时会话：业务/收藏可分别走 Noco 或本地。</summary>
public sealed class AppSession
{
    private readonly object _gate = new();
    private NocoRecordStore? _nocoStore;
    private LocalRecordStore _businessLocal = null!;
    private LocalRecordStore _favoritesLocal = null!;
    private LocalRecordStore _weightLocal = null!;

    public string UserName { get; private set; } = "";
    public StorageSettings Settings { get; private set; } = null!;
    public AdminFile? Admin { get; private set; }
    public bool NocoConnected { get; private set; }
    public string? LastConnectError { get; private set; }

    public IRecordStore Business { get; private set; } = null!;
    public IRecordStore Favorites { get; private set; } = null!;
    public IRecordStore Weight { get; private set; } = null!;

    /// <summary>二级密码跟随收藏存储。</summary>
    public IRecordStore PinStore => Favorites;

    public bool BusinessReady => !Settings.UseNocoBusiness || NocoConnected;
    public bool FavoritesReady => !Settings.UseNocoFavorites || NocoConnected;
    public bool WeightReady => !Settings.UseNocoWeight || NocoConnected;

    public string? HoneyViewPath =>
        string.IsNullOrWhiteSpace(Settings.HoneyView) ? null : Settings.HoneyView.Trim();

    public static AppSession Create(string userName)
    {
        var session = new AppSession();
        session.Initialize(userName);
        return session;
    }

    private void Initialize(string userName)
    {
        AppPaths.SetCurrentUser(userName);
        UserName = userName.Trim();
        Settings = StorageSettings.Load();
        try { Admin = Settings.ResolveAdmin(); }
        catch { Admin = null; }

        var localRoot = StorageSettings.LocalRoot();
        _businessLocal = new LocalRecordStore(Path.Combine(localRoot, "business"));
        _favoritesLocal = new LocalRecordStore(Path.Combine(localRoot, "favorites"));
        _weightLocal = new LocalRecordStore(Path.Combine(localRoot, "weight"));
        _businessLocal.EnsureSeeded(includeDefaultRewards: true);
        _favoritesLocal.EnsureSeeded(includeDefaultRewards: false);
        _weightLocal.EnsureWeightSeeded();
        RebuildStores();
    }

    public void RebuildStores()
    {
        lock (_gate)
        {
            if (Settings.UseNocoBusiness && NocoConnected && _nocoStore is not null)
                Business = _nocoStore;
            else
            {
                _businessLocal.EnsureSeeded(includeDefaultRewards: true);
                Business = _businessLocal;
            }

            if (Settings.UseNocoFavorites && NocoConnected && _nocoStore is not null)
                Favorites = _nocoStore;
            else
            {
                _favoritesLocal.EnsureSeeded(includeDefaultRewards: false);
                Favorites = _favoritesLocal;
            }

            if (Settings.UseNocoWeight && NocoConnected && _nocoStore is not null)
                Weight = _nocoStore;
            else
            {
                _weightLocal.EnsureWeightSeeded();
                Weight = _weightLocal;
            }
        }
    }

    public async Task<bool> TryConnectAsync(Action<string>? log = null)
    {
        try
        {
            AdminFile admin;
            try
            {
                admin = Settings.ResolveAdmin();
            }
            catch (Exception ex)
            {
                LastConnectError = ex.Message;
                log?.Invoke(ex.Message);
                return false;
            }

            Admin = admin;
            if (Settings.ConnectMode == NocoConnectMode.DockerThenUrl)
            {
                await DockerBootstrap.EnsureReachableAsync(
                    admin.Url,
                    admin.Container ?? "nocodb-vibecoding",
                    msg => log?.Invoke(msg));
            }
            else
            {
                log?.Invoke("正在访问 " + admin.Url + "…");
                if (!await DockerBootstrap.PingAsync(admin.Url))
                    throw new InvalidOperationException("无法访问 NocoDB：" + admin.Url);
            }

            var noco = new NocoClient(admin.Url);
            await noco.SignInAsync(admin.Email, admin.Password);
            if (!string.IsNullOrWhiteSpace(admin.ApiToken))
                noco.SetApiToken(admin.ApiToken);
            else
                noco.SetApiToken(await noco.EnsureApiTokenAsync("personal-management-app"));

            log?.Invoke("正在检查数据表…");
            var schema = await SchemaService.EnsureAsync(noco);
            _nocoStore = new NocoRecordStore(noco, schema);
            NocoConnected = true;
            LastConnectError = null;
            Admin = admin;
            RebuildStores();
            log?.Invoke("NocoDB 已连接。");
            return true;
        }
        catch (Exception ex)
        {
            NocoConnected = false;
            _nocoStore = null;
            LastConnectError = ex.Message;
            RebuildStores();
            log?.Invoke(ex.Message);
            return false;
        }
    }

    public async Task SetUseNocoBusinessAsync(bool use, Action<string>? log = null)
    {
        if (use == Settings.UseNocoBusiness) return;
        if (use)
        {
            if (!NocoConnected && !await TryConnectAsync(log))
                throw new InvalidOperationException("无法连接 NocoDB，未开启业务云端。\n" + LastConnectError);
            log?.Invoke("正在上传业务数据到 NocoDB…");
            await DataMigrator.UploadBusinessFromLocalAsync(_businessLocal, _nocoStore!, log);
            Settings.UseNocoBusiness = true;
            Settings.Save();
            RebuildStores();
            _businessLocal.ClearAll();
            _businessLocal = new LocalRecordStore(Path.Combine(StorageSettings.LocalRoot(), "business"));
            _businessLocal.EnsureSeeded(includeDefaultRewards: true);
        }
        else
        {
            if (NocoConnected && _nocoStore is not null)
            {
                log?.Invoke("正在下载业务数据到本地…");
                await DataMigrator.DownloadBusinessToLocalAsync(_nocoStore, _businessLocal, log);
            }
            else
                log?.Invoke("未连接 NocoDB，直接改用本地业务库。");
            Settings.UseNocoBusiness = false;
            Settings.Save();
            RebuildStores();
        }
    }

    public async Task SetUseNocoFavoritesAsync(bool use, Action<string>? log = null)
    {
        if (use == Settings.UseNocoFavorites) return;
        if (use)
        {
            if (!NocoConnected && !await TryConnectAsync(log))
                throw new InvalidOperationException("无法连接 NocoDB，未开启收藏云端。\n" + LastConnectError);
            log?.Invoke("正在上传收藏到 NocoDB…");
            await DataMigrator.UploadFavoritesFromLocalAsync(_favoritesLocal, _nocoStore!, log);
            Settings.UseNocoFavorites = true;
            Settings.Save();
            RebuildStores();
            _favoritesLocal.ClearAll();
            _favoritesLocal = new LocalRecordStore(Path.Combine(StorageSettings.LocalRoot(), "favorites"));
            _favoritesLocal.EnsureSeeded(includeDefaultRewards: false);
        }
        else
        {
            if (NocoConnected && _nocoStore is not null)
            {
                log?.Invoke("正在下载收藏到本地…");
                await DataMigrator.DownloadFavoritesToLocalAsync(_nocoStore, _favoritesLocal, log);
            }
            else
                log?.Invoke("未连接 NocoDB，直接改用本地收藏库。");
            Settings.UseNocoFavorites = false;
            Settings.Save();
            RebuildStores();
        }
    }

    public async Task SetUseNocoWeightAsync(bool use, Action<string>? log = null)
    {
        if (use == Settings.UseNocoWeight) return;
        if (use)
        {
            if (!NocoConnected && !await TryConnectAsync(log))
                throw new InvalidOperationException("无法连接 NocoDB，未开启体重云端。\n" + LastConnectError);
            log?.Invoke("正在上传体重数据到 NocoDB…");
            await DataMigrator.UploadWeightFromLocalAsync(_weightLocal, _nocoStore!, log);
            Settings.UseNocoWeight = true;
            Settings.Save();
            RebuildStores();
            _weightLocal.ClearAll();
            _weightLocal = new LocalRecordStore(Path.Combine(StorageSettings.LocalRoot(), "weight"));
            _weightLocal.EnsureWeightSeeded();
        }
        else
        {
            if (NocoConnected && _nocoStore is not null)
            {
                log?.Invoke("正在下载体重数据到本地…");
                await DataMigrator.DownloadWeightToLocalAsync(_nocoStore, _weightLocal, log);
            }
            else
                log?.Invoke("未连接 NocoDB，直接改用本地体重库。");
            Settings.UseNocoWeight = false;
            Settings.Save();
            RebuildStores();
        }
    }

    public void SaveSettingsFromUi(
        bool useBiz,
        bool useFav,
        bool useWeight,
        NocoConnectMode mode,
        string url,
        string email,
        string password,
        string? apiToken,
        string? container,
        string? honeyView,
        string? llmApiKey,
        string? llmBaseUrl,
        string? llmModel)
    {
        Settings.ConnectMode = mode;
        Settings.Url = string.IsNullOrWhiteSpace(url) ? null : url.Trim().TrimEnd('/');
        Settings.Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        Settings.Password = string.IsNullOrWhiteSpace(password) ? null : password;
        Settings.ApiToken = string.IsNullOrWhiteSpace(apiToken) ? null : apiToken.Trim();
        Settings.Container = string.IsNullOrWhiteSpace(container) ? null : container.Trim();
        Settings.HoneyView = string.IsNullOrWhiteSpace(honeyView) ? null : honeyView.Trim();
        Settings.LlmApiKey = string.IsNullOrWhiteSpace(llmApiKey) ? null : llmApiKey.Trim();
        Settings.LlmBaseUrl = string.IsNullOrWhiteSpace(llmBaseUrl) ? null : llmBaseUrl.Trim().TrimEnd('/');
        Settings.LlmModel = string.IsNullOrWhiteSpace(llmModel) ? null : llmModel.Trim();
        Settings.UseNocoBusiness = useBiz;
        Settings.UseNocoFavorites = useFav;
        Settings.UseNocoWeight = useWeight;
        Settings.Save();
        try { Admin = Settings.ResolveAdmin(); }
        catch { /* keep previous */ }
    }
}
