using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace PersonalManagement.Desktop;

internal static class DockerBootstrap
{
    public static async Task EnsureReachableAsync(string baseUrl, string containerName, Action<string> log)
    {
        if (await PingAsync(baseUrl))
        {
            log("NocoDB 已在运行。");
            return;
        }

        log($"正在启动容器 {containerName}…");
        var start = Run("docker", $"start {containerName}");
        if (start.ExitCode != 0)
        {
            log("Docker 引擎可能未启动，正在打开 Docker Desktop…");
            StartDockerDesktop();
            for (var i = 0; i < 30; i++)
            {
                await Task.Delay(2000);
                if (Run("docker", "info").ExitCode == 0) break;
                log($"等待 Docker 引擎… ({i + 1}/30)");
            }

            start = Run("docker", $"start {containerName}");
            if (start.ExitCode != 0)
                throw new InvalidOperationException("无法启动 NocoDB 容器：\n" + start.Error);
        }

        for (var i = 0; i < 40; i++)
        {
            if (await PingAsync(baseUrl))
            {
                log("NocoDB 已就绪。");
                return;
            }
            log($"等待 NocoDB 启动… ({i + 1}/40)");
            await Task.Delay(2000);
        }

        throw new InvalidOperationException("已尝试启动 Docker，但 " + baseUrl + " 仍无响应。");
    }

    public static async Task<bool> PingAsync(string baseUrl)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var resp = await http.GetAsync(baseUrl.TrimEnd('/') + "/api/v2/meta/nocodb/info");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static void StartDockerDesktop()
    {
        var exe = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Docker", "Docker", "Docker Desktop.exe");
        if (!File.Exists(exe)) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true
        });
    }

    private static (int ExitCode, string Error) Run(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p is null) return (-1, "未能启动进程 " + file);
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit(60_000);
            return (p.ExitCode, err);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
