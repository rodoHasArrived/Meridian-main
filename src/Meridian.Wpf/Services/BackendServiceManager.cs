using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// WPF platform-specific backend service manager.
/// Extends <see cref="BackendServiceManagerBase"/> with process management and HTTP health checks.
/// Part of Phase 2 service extraction.
/// </summary>
public sealed class BackendServiceManager : BackendServiceManagerBase
{
    private static readonly Lazy<BackendServiceManager> _instance = new(() => new BackendServiceManager());
    private readonly HttpClient _httpClient;

    public static BackendServiceManager Instance => _instance.Value;

    private BackendServiceManager()
    {
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
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    protected override IReadOnlyList<string> GetProcessArguments(string executablePath)
        => ["--config", FirstRunService.Instance.ConfigFilePath];

    protected override IReadOnlyDictionary<string, string?> GetProcessEnvironmentVariables(string executablePath)
        => new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["MDC_CONFIG_PATH"] = FirstRunService.Instance.ConfigFilePath
        };

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
            if (process.HasExited) return true;

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
            var response = await _httpClient.GetAsync($"{ConnectionService.Instance.ServiceUrl}/healthz", ct);
            return response.IsSuccessStatusCode;
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
