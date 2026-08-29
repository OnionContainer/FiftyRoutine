using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PersonalManagement.Desktop;

/// <summary>程序根 = exe 所在目录；UserData / ProgramData 建在其下。</summary>
internal static class AppPaths
{
    public static string ProjectRoot =>
        Path.GetFullPath(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    public static string ProgramDataDir => Path.Combine(ProjectRoot, "ProgramData");
    public static string UserDataRoot => Path.Combine(ProjectRoot, "UserData");

    public static string? CurrentUser { get; private set; }

    public static string CurrentUserDir =>
        CurrentUser is null
            ? throw new InvalidOperationException("尚未选择用户。")
            : UserDir(CurrentUser);

    public static string UserDir(string userName) => Path.Combine(UserDataRoot, userName);

    public static void EnsureProgramData() => Directory.CreateDirectory(ProgramDataDir);

    public static void SetCurrentUser(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("用户名无效。", nameof(userName));
        CurrentUser = userName.Trim();
        Directory.CreateDirectory(CurrentUserDir);
    }

    public static void ClearCurrentUser() => CurrentUser = null;
}

internal sealed class ProgramConfig
{
    public bool DirectLogin { get; set; } = true;
    public string? LastUser { get; set; }
    public int LayoutVersion { get; set; } = 1;

    public static string FilePath => Path.Combine(AppPaths.ProgramDataDir, "app.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static ProgramConfig Load()
    {
        AppPaths.EnsureProgramData();
        try
        {
            if (!File.Exists(FilePath)) return new ProgramConfig();
            return JsonSerializer.Deserialize<ProgramConfig>(File.ReadAllText(FilePath), JsonOpts)
                   ?? new ProgramConfig();
        }
        catch
        {
            return new ProgramConfig();
        }
    }

    public void Save()
    {
        AppPaths.EnsureProgramData();
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
    }
}

internal static class UserAccounts
{
    private static readonly Regex InvalidName = new(@"[\\/:*?""<>|]", RegexOptions.Compiled);

    public static IReadOnlyList<string> ListUsers()
    {
        if (!Directory.Exists(AppPaths.UserDataRoot)) return [];
        return Directory.GetDirectories(AppPaths.UserDataRoot)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static bool UserExists(string userName) =>
        ListUsers().Any(u => u.Equals(userName.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string? ValidateUserName(string? raw)
    {
        var name = (raw ?? "").Trim();
        if (name.Length is < 1 or > 64) return "用户名长度须为 1–64。";
        if (name is "." or "..") return "用户名无效。";
        if (InvalidName.IsMatch(name)) return "用户名不能包含 \\ / : * ? \" < > |";
        if (UserExists(name)) return "该用户名已存在。";
        return null;
    }

    public static void CreateUserSkeleton(string userName, StorageSettings? initialSettings = null)
    {
        var err = ValidateUserName(userName);
        if (err is not null) throw new InvalidOperationException(err);
        var name = userName.Trim();
        var dir = AppPaths.UserDir(name);
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "local", "business"));
        Directory.CreateDirectory(Path.Combine(dir, "local", "favorites"));
        Directory.CreateDirectory(Path.Combine(dir, "local", "weight"));
        Directory.CreateDirectory(Path.Combine(dir, "assets"));
        var settings = initialSettings ?? new StorageSettings();
        settings.SaveTo(Path.Combine(dir, "settings.json"));
    }
}
