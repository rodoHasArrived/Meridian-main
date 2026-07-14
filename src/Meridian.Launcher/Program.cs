using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Meridian.Launcher;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        var installRoot = AppContext.BaseDirectory;
        var hostPath = Path.Combine(installRoot, "host", "Meridian.exe");
        if (!File.Exists(hostPath))
        {
            MessageBox("Meridian needs repair because the local host is missing.", "Meridian");
            return 2;
        }

        var port = ReserveLoopbackPort();
        var bootstrapToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var url = $"http://127.0.0.1:{port}";
        var start = new ProcessStartInfo(hostPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(hostPath)!
        };
        start.Environment["ASPNETCORE_URLS"] = url;
        start.Environment["MDC_BOOTSTRAP_TOKEN"] = bootstrapToken;
        start.Environment["MDC_AUTH_MODE"] = "required";
        start.Environment["MERIDIAN_INSTALL_ROOT"] = installRoot;
        start.Environment["MDC_DATA_ROOT"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Meridian", "Data");
        using var process = Process.Start(start);
        if (process is null) return 3;

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        for (var attempt = 0; attempt < 80 && !process.HasExited; attempt++)
        {
            try
            {
                var response = await client.GetAsync($"{url}/healthz").ConfigureAwait(false);
                if (response.IsSuccessStatusCode) break;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(250).ConfigureAwait(false);
        }

        var accountStore = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Meridian", "Data", "governance", "user-accounts.json");
        var destination = File.Exists(accountStore)
            ? $"{url}/workstation/"
            : $"{url}/setup/account#token={Uri.EscapeDataString(bootstrapToken)}";
        Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true });
        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void MessageBox(string message, string title) =>
        Process.Start(new ProcessStartInfo("mshta.exe", $"javascript:alert('{message.Replace("'", "") }');close()") { UseShellExecute = true });
}
