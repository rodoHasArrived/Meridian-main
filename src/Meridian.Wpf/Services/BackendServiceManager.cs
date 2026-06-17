using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Contracts;

namespace Meridian.Wpf.Services;

/// <summary>
/// WPF platform-specific backend service manager.
/// Extends <see cref="BackendServiceManagerBase"/> with process management and HTTP health checks.
/// Part of Phase 2 service extraction.
/// </summary>
public sealed class BackendServiceManager : BackendServiceManagerBase
{
    private static readonly Lazy<BackendServiceManager> _instance = new(() => new BackendServiceManager());
    private const int DefaultDesktopPort = 8080;
    private readonly HttpClient _httpClient;
    private readonly IRemoteWorkstationClient _remoteClient;

    public static BackendServiceManager Instance => _instance.Value;

    private BackendServiceManager()
        : this(WpfRemoteWorkstationClient.Instance)
    {
    }

    internal BackendServiceManager(IRemoteWorkstationClient remoteClient, string? appDataDirectory = null)
        : base(appDataDirectory)
    {
        _remoteClient = remoteClient ?? throw new ArgumentNullException(nameof(remoteClient));
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    protected override string? ResolveExecutablePath(string? preferredPath)
    {
        if (!string.IsNullOrWhiteSpace(preferredPath) && File.Exists(preferredPath))
        {
            return preferredPath;
        }

        var configuredPath = Environment.GetEnvironmentVariable("MDC_BACKEND_PATH", EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "Meridian.exe"),
            Path.Combine(baseDirectory, "Meridian", "Meridian.exe"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "Meridian", "Meridian.exe")),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "Meridian", "bin", "Release", "net9.0", "Meridian.exe")),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "Meridian", "bin", "Debug", "net9.0", "Meridian.exe"))
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    protected override IReadOnlyList<string> GetProcessArguments(string executablePath)
        => BuildProcessArguments(
            FirstRunService.Instance.ConfigFilePath,
            ConnectionService.Instance.ServiceUrl);

    protected override IReadOnlyDictionary<string, string?> GetProcessEnvironmentVariables(string executablePath)
        => new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["MDC_CONFIG_PATH"] = FirstRunService.Instance.ConfigFilePath,
            ["MDC_SHUTDOWN_TOKEN"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
        };

    internal static IReadOnlyList<string> BuildProcessArguments(string configPath, string serviceUrl)
        => [
            "--mode",
            "desktop",
            "--config",
            configPath,
            "--http-port",
            ResolveHttpPort(serviceUrl).ToString(CultureInfo.InvariantCulture)
        ];

    internal static int ResolveHttpPort(string serviceUrl)
    {
        if (Uri.TryCreate(serviceUrl, UriKind.Absolute, out var serviceUri) && serviceUri.Port > 0)
        {
            return serviceUri.Port;
        }

        return DefaultDesktopPort;
    }

    private static int ResolveHttpPort(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (string.Equals(arguments[index], "--http-port", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(arguments[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) &&
                port > 0)
            {
                return port;
            }
        }

        return DefaultDesktopPort;
    }

    protected override int? StartProcess(
        string executablePath,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environmentVariables)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            processStartInfo.ArgumentList.Add(argument);
        }

        foreach (var environmentVariable in environmentVariables)
        {
            if (environmentVariable.Value == null)
            {
                processStartInfo.Environment.Remove(environmentVariable.Key);
            }
            else
            {
                processStartInfo.Environment[environmentVariable.Key] = environmentVariable.Value;
            }
        }

        var process = Process.Start(processStartInfo);
        return process?.Id;
    }

    protected override async Task<bool> KillProcessAsync(int processId, CancellationToken ct)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            if (process.HasExited)
                return true;

            LogInfo("Backend graceful shutdown failed; terminating owned process", ("Pid", processId.ToString(CultureInfo.InvariantCulture)));
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(ct);
            process.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            return false;
        }
    }

    protected override async Task<bool> RequestGracefulStopAsync(BackendRuntimeInfo runtime, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(runtime.ShutdownToken))
            return false;

        var port = ResolveHttpPort(runtime.Arguments);
        var shutdownUrl = $"http://localhost:{port}/api/system/shutdown";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, shutdownUrl);
            request.Headers.TryAddWithoutValidation("X-Meridian-Shutdown-Token", runtime.ShutdownToken);
            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogInfo(
                    "Backend graceful shutdown endpoint returned non-success",
                    ("StatusCode", ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)));
                return false;
            }

            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (!IsProcessRunning(runtime.ProcessId))
                {
                    LogInfo("Backend process exited after graceful shutdown", ("Pid", runtime.ProcessId.ToString(CultureInfo.InvariantCulture)));
                    return true;
                }

                await Task.Delay(250, ct).ConfigureAwait(false);
            }

            LogInfo("Backend graceful shutdown timed out", ("Pid", runtime.ProcessId.ToString(CultureInfo.InvariantCulture)));
            return false;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            LogError("Backend graceful shutdown request failed", ex);
            return false;
        }
    }

    protected override bool IsTrackedProcessOwned(BackendRuntimeInfo runtime)
    {
        try
        {
            using var process = Process.GetProcessById(runtime.ProcessId);
            if (process.HasExited)
                return false;

            try
            {
                var startedAtUtc = process.StartTime.ToUniversalTime();
                if (startedAtUtc < runtime.StartedAtUtc.AddSeconds(-5))
                    return false;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                LogInfo("Unable to verify backend process start time", ("Pid", runtime.ProcessId.ToString(CultureInfo.InvariantCulture)));
            }

            if (!string.IsNullOrWhiteSpace(runtime.ExecutablePath))
            {
                try
                {
                    var actualPath = process.MainModule?.FileName;
                    if (string.Equals(actualPath, runtime.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    LogInfo("Unable to verify backend process executable path", ("Pid", runtime.ProcessId.ToString(CultureInfo.InvariantCulture)));
                }
            }

            return process.ProcessName.Contains("Meridian", StringComparison.OrdinalIgnoreCase)
                && runtime.Arguments.Contains("--mode", StringComparer.OrdinalIgnoreCase)
                && runtime.Arguments.Contains("desktop", StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    protected override bool IsProcessRunning(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    protected override async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        try
        {
            return await _remoteClient.CheckHealthEndpointAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            return false;
        }
    }

    protected override void LogInfo(string message, params (string key, string value)[] properties)
        => LoggingService.Instance.LogInfo(message, properties);

    protected override void LogError(string message, Exception? exception)
        => LoggingService.Instance.LogError(message, exception);
}
