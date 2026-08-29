using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PersonalManagement.Desktop;

/// <summary>从旧 Personal_Management 布局迁到 UserData。</summary>
internal static class LegacyMigrator
{
    public static string? FindLegacyPersonalManagementDir()
    {
        var root = AppPaths.ProjectRoot;
        var direct = Path.Combine(root, "Personal_Management");
        if (LooksLikeLegacy(direct)) return direct;

        var dir = new DirectoryInfo(root);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Personal_Management");
            if (LooksLikeLegacy(candidate)) return candidate;
            // 当前目录本身就是 Personal_Management
            if (dir.Name.Equals("Personal_Management", StringComparison.OrdinalIgnoreCase)
                && LooksLikeLegacy(dir.FullName))
                return dir.FullName;
        }
        return null;
    }

    public static string? FindLegacyAdminTxt(string? personalManagementDir)
    {
        if (personalManagementDir is not null)
        {
            var parent = Directory.GetParent(personalManagementDir)?.FullName;
            if (parent is not null)
            {
                var p = Path.Combine(parent, "nocodb-admin.txt");
                if (File.Exists(p)) return p;
            }
        }
        var up = new DirectoryInfo(AppPaths.ProjectRoot);
        for (var i = 0; i < 8 && up is not null; i++, up = up.Parent)
        {
            var p = Path.Combine(up.FullName, "nocodb-admin.txt");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public static bool NeedsMigration()
    {
        if (UserAccounts.ListUsers().Count > 0) return false;
        var mig = MigrationRecord.Load();
        if (mig.Completed) return false;
        return FindLegacyPersonalManagementDir() is not null;
    }

    private static bool LooksLikeLegacy(string pmDir) =>
        Directory.Exists(Path.Combine(pmDir, "local"))
        || File.Exists(Path.Combine(pmDir, "storage.json"));

    public static void MigrateIntoUser(string userName)
    {
        var pm = FindLegacyPersonalManagementDir()
                 ?? throw new InvalidOperationException("未找到旧 Personal_Management 数据。");
        var adminPath = FindLegacyAdminTxt(pm);

        if (UserAccounts.UserExists(userName))
            throw new InvalidOperationException("用户已存在。");

        var settings = new StorageSettings();
        var storagePath = Path.Combine(pm, "storage.json");
        if (File.Exists(storagePath))
            settings = StorageSettings.LoadFromFile(storagePath);

        if (adminPath is not null)
            MergeAdminFile(settings, adminPath);

        UserAccounts.CreateUserSkeleton(userName, settings);
        AppPaths.SetCurrentUser(userName);
        var dest = AppPaths.CurrentUserDir;

        CopyDirIfExists(Path.Combine(pm, "local"), Path.Combine(dest, "local"));
        CopyFileIfExists(Path.Combine(pm, "styles.json"), Path.Combine(dest, "styles.json"));
        CopyFileIfExists(Path.Combine(pm, "window.json"), Path.Combine(dest, "window.json"));
        CopyFileIfExists(Path.Combine(pm, "block-style-presets.json"), Path.Combine(dest, "block-style-presets.json"));
        CopyDirIfExists(Path.Combine(pm, "Desktop", "Assets", "user"), Path.Combine(dest, "assets"));

        settings.Save();

        var cfg = ProgramConfig.Load();
        cfg.DirectLogin = true;
        cfg.LastUser = userName.Trim();
        cfg.Save();

        MigrationRecord.Save(new MigrationRecord
        {
            Completed = true,
            SourcePath = pm,
            UserName = userName.Trim(),
            MigratedAt = DateTime.Now.ToString("o")
        });
    }

    private static void MergeAdminFile(StorageSettings s, string path)
    {
        var admin = AdminFile.TryLoad(path);
        if (admin is null) return;
        s.Url ??= admin.Url;
        s.Email ??= admin.Email;
        s.Password ??= admin.Password;
        s.ApiToken ??= admin.ApiToken;
        s.HoneyView ??= admin.HoneyView;
        s.Container ??= admin.Container;
    }

    private static void CopyFileIfExists(string src, string dest)
    {
        if (!File.Exists(src)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(src, dest, overwrite: true);
    }

    private static void CopyDirIfExists(string src, string dest)
    {
        if (!Directory.Exists(src)) return;
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}

internal sealed class MigrationRecord
{
    public bool Completed { get; set; }
    public string? SourcePath { get; set; }
    public string? UserName { get; set; }
    public string? MigratedAt { get; set; }

    private static string FilePath => Path.Combine(AppPaths.ProgramDataDir, "migration.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static MigrationRecord Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new MigrationRecord();
            return JsonSerializer.Deserialize<MigrationRecord>(File.ReadAllText(FilePath), JsonOpts)
                   ?? new MigrationRecord();
        }
        catch { return new MigrationRecord(); }
    }

    public static void Save(MigrationRecord r)
    {
        AppPaths.EnsureProgramData();
        File.WriteAllText(FilePath, JsonSerializer.Serialize(r, JsonOpts));
    }
}
