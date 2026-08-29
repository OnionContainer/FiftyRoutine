using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;
using Microsoft.Toolkit.Uwp.Notifications;
using Windows.UI.Notifications;
using IOPath = System.IO.Path;

namespace PersonalManagement.Probes;

internal static class Program
{
    private static readonly List<ProbeResult> Results = [];

    [STAThread]
    private static int Main()
    {
        try
        {
            return RunAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.WriteLine("FATAL: " + ex);
            return 1;
        }
    }

    private static async Task<int> RunAsync()
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (Environment.GetCommandLineArgs().Any(a =>
                a.Equals("--toast", StringComparison.OrdinalIgnoreCase)))
        {
            new ToastContentBuilder()
                .AddText("Personal_Management 探针")
                .AddText("补发通知：你现在应能在右下角或通知中心看到这条。")
                .Show();
            Console.WriteLine("Toast sent. Check the bottom-right corner / Action Center.");
            return 0;
        }

        if (Environment.GetCommandLineArgs().Any(a =>
                a.Equals("--alert-window", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("Showing a topmost WPF alert window for 12 seconds...");
            AlertWindowProbe.ShowBlocking();
            Console.WriteLine("Alert window closed.");
            return 0;
        }

        if (Environment.GetCommandLineArgs().Any(a =>
                a.Equals("--honeyview", StringComparison.OrdinalIgnoreCase)))
        {
            var honeyWorkspace = FindWorkspaceRoot()
                ?? throw new DirectoryNotFoundException("Could not find workspace root.");
            var honeyCreds = AdminFile.Load(IOPath.Combine(honeyWorkspace, "nocodb-admin.txt"));
            var honey = FindHoneyView(honeyCreds.HoneyView)
                ?? throw new FileNotFoundException("Honeyview.exe not found");
            var pngPath = ColorSwatchPng.SaveTemp();
            var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = honey,
                Arguments = $"\"{pngPath}\"",
                UseShellExecute = true,
            }) ?? throw new InvalidOperationException("Process.Start returned null");
            Console.WriteLine($"Opened 200x200 color swatch in HoneyView pid={proc.Id}");
            Console.WriteLine(pngPath);
            Console.WriteLine("Expect: red TL, green TR, blue BL, yellow BR, magenta center, RGB gradient.");
            return 0;
        }

        var args = Environment.GetCommandLineArgs();
        var thumbIdx = Array.FindIndex(args, a =>
            a.Equals("--thumb-pixels", StringComparison.OrdinalIgnoreCase));
        if (thumbIdx >= 0)
        {
            string? pathArg = null;
            if (thumbIdx + 1 < args.Length && !args[thumbIdx + 1].StartsWith('-'))
                pathArg = args[thumbIdx + 1];
            return ThumbPixelsProbe.Run(pathArg);
        }

        if (args.Any(a => a.Equals("--block-style-editor", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("Opening BlockStyleEditorWindow (WYSIWYG)…");
            var thread = new Thread(() =>
            {
                PersonalManagement.Desktop.BlockStyleEditorWindow.RunStandalone();
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            Console.WriteLine("Block style editor closed.");
            return 0;
        }

        var workspace = FindWorkspaceRoot()
            ?? throw new DirectoryNotFoundException("Could not find workspace root (个人管理工具需求.md / nocodb-admin.txt).");
        var adminFile = IOPath.Combine(workspace, "nocodb-admin.txt");
        var creds = AdminFile.Load(adminFile);
        var honeyViewPath = creds.HoneyView;

        using var noco = new NocoClient(creds.Url);

        await ProbeAsync("1. NocoDB reachable", async () =>
        {
            var info = await noco.GetJsonAsync("/api/v2/meta/nocodb/info");
            var version = info?["version"]?.ToString();
            var hasAdmin = info?["baseHasAdmin"]?.GetValue<bool>() ?? false;
            if (string.IsNullOrWhiteSpace(version))
                throw new InvalidOperationException("info.version missing");
            return $"version={version} baseHasAdmin={hasAdmin}";
        });

        await ProbeAsync("1. Sign in (JWT)", async () =>
        {
            await noco.SignInAsync(creds.Email, creds.Password);
            return "got xc-auth JWT";
        });

        string? apiTokenHint = null;
        await ProbeAsync("1. Create/list API token", async () =>
        {
            var created = await noco.EnsureApiTokenAsync("pm-probe");
            noco.SetApiToken(created);
            apiTokenHint = created.Length > 8 ? created[..4] + "…" + created[^4..] : "(short)";
            return $"xc-token ready ({apiTokenHint})";
        });

        string? baseId = null;
        await ProbeAsync("1. Ensure base PM_Probe", async () =>
        {
            baseId = await noco.EnsureBaseAsync("PM_Probe");
            return $"baseId={baseId}";
        });

        string? tasksId = null;
        string? completionsId = null;
        string? favoritesId = null;
        await ProbeAsync("2. Ensure tables", async () =>
        {
            if (baseId is null) throw new InvalidOperationException("no base");
            tasksId = await noco.EnsureTableAsync(baseId, "probe_tasks",
            [
                Col("Type", "SingleLineText"),
                Col("RewardLevel", "Number"),
            ]);
            completionsId = await noco.EnsureTableAsync(baseId, "probe_completions",
            [
                Col("CompletedOn", "Date"),
            ]);
            favoritesId = await noco.EnsureTableAsync(baseId, "probe_favorites",
            [
                Col("File", "Attachment"),
            ]);
            return $"tasks={tasksId} completions={completionsId} favorites={favoritesId}";
        });

        string? linkColId = null;
        var usedNumericFk = false;
        await ProbeAsync("2. Completions → Tasks relation", async () =>
        {
            if (tasksId is null || completionsId is null)
                throw new InvalidOperationException("missing tables");
            try
            {
                linkColId = await noco.EnsureLinkColumnAsync(
                    parentTableId: tasksId,
                    childTableId: completionsId,
                    titleOnChild: "Task");
                return $"link column Task id={linkColId}";
            }
            catch (Exception ex)
            {
                usedNumericFk = true;
                await noco.EnsureColumnAsync(completionsId, Col("TaskRecordId", "Number"));
                return $"Links API failed ({ex.Message}); fell back to TaskRecordId number";
            }
        });

        string? taskRecordId = null;
        await ProbeAsync("1+2. Insert task and two completions, then query", async () =>
        {
            if (tasksId is null || completionsId is null)
                throw new InvalidOperationException("missing tables");
            var title = "probe-daily-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var createdTask = await noco.CreateRecordAsync(tasksId, new Dictionary<string, object?>
            {
                ["Title"] = title,
                ["Type"] = "daily",
                ["RewardLevel"] = 1,
            });
            taskRecordId = NocoClient.ReadId(createdTask)
                ?? throw new InvalidOperationException("task id missing: " + createdTask);
            var dates = new[] { "2026-08-20", "2026-08-21" };
            foreach (var day in dates)
            {
                var row = new Dictionary<string, object?>
                {
                    ["Title"] = title + "-" + day,
                    ["CompletedOn"] = day,
                };
                if (usedNumericFk)
                    row["TaskRecordId"] = long.Parse(taskRecordId);
                else
                    row["Task"] = taskRecordId;
                var completion = await noco.CreateRecordAsync(completionsId, row);
                var completionId = NocoClient.ReadId(completion)
                    ?? throw new InvalidOperationException("completion id missing");
                if (!usedNumericFk && linkColId is not null)
                {
                    try
                    {
                        await noco.LinkAsync(completionsId, linkColId, completionId, taskRecordId);
                    }
                    catch (HttpRequestException)
                    {
                        // Create payload may already have linked Task; ignore duplicate-link failures later if query succeeds.
                    }
                }
            }

            JsonNode listed;
            if (usedNumericFk)
            {
                listed = await noco.ListRecordsAsync(completionsId, $"(TaskRecordId,eq,{taskRecordId})");
            }
            else
            {
                listed = await noco.ListRecordsAsync(completionsId, $"(Title,like,{title}%)");
            }

            var count = NocoClient.AsList(listed).Count;
            if (count < 2)
                throw new InvalidOperationException($"expected >=2 completions, got {count}: {listed}");
            return $"task={taskRecordId} completions={count} title={title}";
        });

        string? downloadedPath = null;
        await ProbeAsync("4. Upload attachment, save to record, download temp file", async () =>
        {
            if (favoritesId is null) throw new InvalidOperationException("no favorites table");
            var png = ColorSwatchPng.Create();
            var uploaded = await noco.UploadAsync("probe-200x200.png", png, "image/png");
            var record = await noco.CreateRecordAsync(favoritesId, new Dictionary<string, object?>
            {
                ["Title"] = "probe-image-" + DateTime.Now.ToString("HHmmss"),
                ["File"] = uploaded,
            });
            var fileNode = record?["File"] ?? uploaded;
            var url = NocoClient.FirstFileUrl(fileNode)
                ?? throw new InvalidOperationException("no file url in " + fileNode);
            downloadedPath = IOPath.Combine(IOPath.GetTempPath(), "pm-probe-" + Guid.NewGuid().ToString("N") + ".png");
            await noco.DownloadAsync(url, downloadedPath);
            var len = new FileInfo(downloadedPath).Length;
            if (len <= 0) throw new InvalidOperationException("downloaded empty file");
            return $"uploaded+downloaded {len} bytes → {downloadedPath}";
        });

        await ProbeAsync("4. Load bitmap in WPF (thumbnail source)", () =>
            RunSta(() =>
            {
                if (downloadedPath is null) throw new InvalidOperationException("no downloaded file");
                var bmp = LoadBitmap(downloadedPath);
                return $"decoded {bmp.PixelWidth}x{bmp.PixelHeight} px";
            }));

        await ProbeAsync("4. Clipboard SetImage / GetImage roundtrip", () =>
            RunSta(() =>
            {
                if (downloadedPath is null) throw new InvalidOperationException("no downloaded file");
                var bmp = LoadBitmap(downloadedPath);
                System.Windows.Clipboard.SetImage(bmp);
                var roundtrip = System.Windows.Clipboard.GetImage()
                    ?? throw new InvalidOperationException("Clipboard.GetImage returned null");
                return $"clipboard image {roundtrip.PixelWidth}x{roundtrip.PixelHeight} (paste into Paint to visually confirm)";
            }));

        await ProbeAsync("4. HoneyView Process.Start", () =>
        {
            var honey = FindHoneyView(honeyViewPath);
            if (honey is null)
                return Skip("Honeyview.exe not found in common install paths");
            if (downloadedPath is null) throw new InvalidOperationException("no downloaded file");
            var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = honey,
                Arguments = $"\"{downloadedPath}\"",
                UseShellExecute = true,
            }) ?? throw new InvalidOperationException("Process.Start returned null");
            var alive = !proc.HasExited;
            return $"started pid={proc.Id} alive={alive} path={honey}";
        });

        await ProbeAsync("3. Immediate Windows toast (API)", () =>
        {
            new ToastContentBuilder()
                .AddText("Personal_Management 探针")
                .AddText("立即通知：若你看见这条，Toast 人眼确认通过。")
                .Show();
            return "ToastContentBuilder.Show() returned without exception";
        });

        await ProbeAsync("3. Schedule toast +60s (API + queue)", () =>
        {
            var when = DateTimeOffset.Now.AddSeconds(60);
            ScheduledToastNotification? scheduled = null;
            new ToastContentBuilder()
                .AddText("Personal_Management 探针")
                .AddText("预约通知：这条应在约 60 秒后出现（进程可能已退出）。")
                .Schedule(when, toast =>
                {
                    toast.Tag = "pm-probe-scheduled";
                    scheduled = toast;
                });
            var queued = ToastNotificationManagerCompat.CreateToastNotifier()
                .GetScheduledToastNotifications()
                .Any(t => t.Tag == "pm-probe-scheduled" || t.DeliveryTime >= DateTimeOffset.Now);
            return $"AddToSchedule ok delivery={when:T} queuedVisible={queued} scheduledAt={scheduled?.DeliveryTime:T}";
        });

        var reportPath = IOPath.Combine(workspace, "Personal_Management", "Probes", "last-probe-result.md");
        WriteReport(reportPath, workspace);
        Console.WriteLine();
        Console.WriteLine("Report: " + reportPath);
        var failed = Results.Count(r => r.Status == "FAIL");
        var skipped = Results.Count(r => r.Status == "SKIP");
        Console.WriteLine(failed == 0
            ? $"Independent probes OK (skip={skipped}). Visual checks still need you."
            : $"{failed} probe(s) failed, {skipped} skipped.");
        return failed == 0 ? 0 : 2;
    }

    private static Dictionary<string, object?> Col(string title, string uidt) =>
        new() { ["title"] = title, ["uidt"] = uidt };

    private const string SkipMarker = "\u0001SKIP\u0001";

    private static string Skip(string reason) => SkipMarker + reason;

    private static string RunSta(Func<string> action)
    {
        string? result = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw error;
        return result ?? throw new InvalidOperationException("STA probe returned null");
    }

    private static async Task ProbeAsync(string name, Func<Task<string>> action)
    {
        try
        {
            var detail = await action();
            if (detail.StartsWith(SkipMarker, StringComparison.Ordinal))
            {
                detail = detail[SkipMarker.Length..];
                Results.Add(new ProbeResult(name, "SKIP", detail));
                Console.WriteLine($"SKIP  {name}");
                Console.WriteLine($"      {detail}");
                return;
            }
            Results.Add(new ProbeResult(name, "PASS", detail));
            Console.WriteLine($"PASS  {name}");
            Console.WriteLine($"      {detail}");
        }
        catch (Exception ex)
        {
            var detail = Unwrap(ex);
            Results.Add(new ProbeResult(name, "FAIL", detail));
            Console.WriteLine($"FAIL  {name}");
            Console.WriteLine($"      {detail}");
        }
    }

    private static Task ProbeAsync(string name, Func<string> action) =>
        ProbeAsync(name, () => Task.FromResult(action()));

    private static string Unwrap(Exception ex)
    {
        var cur = ex;
        while (cur.InnerException is not null) cur = cur.InnerException;
        var msg = cur.Message.Replace("\r", " ").Replace("\n", " ");
        return $"{cur.GetType().Name}: {msg}";
    }

    private static BitmapSource LoadBitmap(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static string? FindHoneyView(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;
        string[] candidates =
        [
            IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Honeyview", "Honeyview.exe"),
            IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Honeyview", "Honeyview.exe"),
            IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Bandisoft", "Honeyview", "Honeyview.exe"),
            IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Honeyview", "Honeyview.exe"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(IOPath.Combine(dir.FullName, "nocodb-admin.txt"))
                || File.Exists(IOPath.Combine(dir.FullName, "个人管理工具需求.md"))
                || File.Exists(IOPath.Combine(dir.FullName, "需求.md")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static void WriteReport(string path, string workspace)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Independent probe result");
        sb.AppendLine();
        sb.AppendLine("Time (local): " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("Workspace: `" + workspace.Replace('\\', '/') + "`");
        sb.AppendLine();
        sb.AppendLine("| Probe | Result | Detail |");
        sb.AppendLine("|-------|--------|--------|");
        foreach (var r in Results)
        {
            var detail = r.Detail.Replace("|", "\\|");
            sb.AppendLine($"| {r.Name} | {r.Status} | {detail} |");
        }
        sb.AppendLine();
        sb.AppendLine("Visual follow-up still needed: Windows toast appearance, scheduled toast after exit, clipboard paste into Paint/WeChat, HoneyView window.");
        Directory.CreateDirectory(IOPath.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private sealed record ProbeResult(string Name, string Status, string Detail);
}

internal sealed class AdminFile
{
    public required string Url { get; init; }
    public required string Email { get; init; }
    public required string Password { get; init; }
    public string? ApiToken { get; init; }
    public string? HoneyView { get; init; }

    public static AdminFile Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Missing NocoDB admin file", path);
        string? url = null, email = null, password = null, apiToken = null, honeyView = null;
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
        }
        if (url is null || email is null || password is null)
            throw new InvalidDataException("nocodb-admin.txt needs URL / Email / Password");
        return new AdminFile
        {
            Url = url.TrimEnd('/'),
            Email = email,
            Password = password,
            ApiToken = apiToken,
            HoneyView = honeyView,
        };
    }
}

internal sealed class NocoClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
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
            ?? throw new InvalidOperationException("signin returned no token: " + node);
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
            ?? throw new InvalidOperationException("token create returned no token: " + created);
    }

    public async Task<string> EnsureBaseAsync(string title)
    {
        var listed = await SendAsync(HttpMethod.Get, "/api/v1/db/meta/projects/");
        foreach (var item in AsList(listed))
        {
            if (string.Equals(item?["title"]?.ToString(), title, StringComparison.OrdinalIgnoreCase))
                return ReadId(item) ?? throw new InvalidOperationException("base has no id");
        }

        JsonNode created;
        try
        {
            created = await SendAsync(HttpMethod.Post, "/api/v1/db/meta/projects/", new { title })
                ?? throw new InvalidOperationException("create base empty");
        }
        catch (Exception first)
        {
            var info = await GetJsonAsync("/api/v2/meta/nocodb/info");
            var ws = info?["defaultWorkspaceId"]?.ToString();
            if (string.IsNullOrWhiteSpace(ws)) throw;
            try
            {
                created = await SendAsync(HttpMethod.Post, "/api/v2/meta/bases", new { title, fk_workspace_id = ws })
                    ?? throw new InvalidOperationException("v2 create base empty");
            }
            catch (Exception second)
            {
                throw new InvalidOperationException($"create base failed. v1={first.Message}; v2={second.Message}");
            }
        }

        return ReadId(created) ?? throw new InvalidOperationException("created base has no id: " + created);
    }

    public async Task<string> EnsureTableAsync(string baseId, string tableName, IReadOnlyList<Dictionary<string, object?>> extraColumns)
    {
        var listed = await SendAsync(HttpMethod.Get, $"/api/v1/db/meta/projects/{baseId}/tables");
        foreach (var item in AsList(listed))
        {
            var name = item?["table_name"]?.ToString() ?? item?["title"]?.ToString();
            if (string.Equals(name, tableName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item?["title"]?.ToString(), tableName, StringComparison.OrdinalIgnoreCase))
                return ReadId(item) ?? throw new InvalidOperationException("table has no id");
        }

        var columns = new List<Dictionary<string, object?>> { Col("Title", "SingleLineText") };
        columns.AddRange(extraColumns);
        var created = await SendAsync(HttpMethod.Post, $"/api/v1/db/meta/projects/{baseId}/tables", new
        {
            table_name = tableName,
            title = tableName,
            columns,
        }) ?? throw new InvalidOperationException("create table empty");
        return ReadId(created) ?? throw new InvalidOperationException("created table has no id: " + created);
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
            ["type"] = "bt",
        });

        return await FindAsync()
            ?? throw new InvalidOperationException("link column created but id not found on re-fetch");
    }

    public async Task<JsonNode> CreateRecordAsync(string tableId, Dictionary<string, object?> fields)
    {
        var node = await SendAsync(HttpMethod.Post, $"/api/v2/tables/{tableId}/records", fields)
            ?? throw new InvalidOperationException("create record empty");
        if (node is JsonArray arr && arr.Count > 0)
            return arr[0]!;
        return node;
    }

    public async Task<JsonNode> ListRecordsAsync(string tableId, string where)
    {
        return await SendAsync(HttpMethod.Get, $"/api/v2/tables/{tableId}/records?where={Uri.EscapeDataString(where)}")
            ?? new JsonObject();
    }

    public async Task LinkAsync(string tableId, string columnId, string rowId, string otherId)
    {
        await SendAsync(
            HttpMethod.Post,
            $"/api/v2/tables/{tableId}/links/{columnId}/records/{rowId}",
            new[] { new Dictionary<string, object?> { ["Id"] = otherId } });
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

    public Task<JsonNode?> GetJsonAsync(string path) => SendAsync(HttpMethod.Get, path);

    public static string? ReadId(JsonNode? node) =>
        node?["id"]?.ToString()
        ?? node?["Id"]?.ToString()
        ?? node?["ID"]?.ToString();

    public static JsonArray AsList(JsonNode? node)
    {
        if (node is JsonArray arr) return arr;
        if (node?["list"] is JsonArray list) return list;
        return [];
    }

    public static string? FirstFileUrl(JsonNode? node)
    {
        if (node is JsonArray arr && arr.Count > 0)
            node = arr[0];
        return node?["signedUrl"]?.ToString()
            ?? node?["url"]?.ToString()
            ?? node?["path"]?.ToString();
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
        {
            req.Content = form;
            var resp = await _http.SendAsync(req);
            return await ReadJsonAsync(resp, path);
        }
        if (body is not null)
        {
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }
        using var response = await _http.SendAsync(req);
        return await ReadJsonAsync(response, path);
    }

    private static async Task<JsonNode?> ReadJsonAsync(HttpResponseMessage resp, string path)
    {
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"{resp.RequestMessage?.Method} {path} {(int)resp.StatusCode}: {text}");
        if (string.IsNullOrWhiteSpace(text)) return null;
        return JsonNode.Parse(text);
    }
}
