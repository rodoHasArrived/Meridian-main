using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services.Services;


public sealed class BackendServiceStatus
{
    public bool IsInstalled { get; init; }
    public bool IsRunning { get; init; }
    public bool IsHealthy { get; init; }
    public int? ProcessId { get; init; }
    public string? ExecutablePath { get; init; }
    public DateTime LastCheckedAtUtc { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
}

public sealed class BackendServiceOperationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;

    public static BackendServiceOperationResult SuccessResult(string message) => new() { Success = true, Message = message };
    public static BackendServiceOperationResult Failed(string message) => new() { Success = false, Message = message };
}

public sealed class BackendInstallationInfo
{
    public string ExecutablePath { get; init; } = string.Empty;
    public DateTime InstalledAtUtc { get; init; }
}

public sealed class BackendRuntimeInfo
{
    public int ProcessId { get; init; }
    public DateTime StartedAtUtc { get; init; }
}


/// <summary>
/// Abstract base class for backend service lifecycle management shared between platforms.
/// Provides state persistence, health checking, and installation management logic.
/// Platform-specific process management and HTTP access are delegated to derived classes.
/// Part of Phase 2 service extraction.
/// </summary>
public abstract class BackendServiceManagerBase
{
    private readonly string _stateDirectory;
    private readonly string _installationFilePath;
    private readonly string _runtimeFilePath;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private static readonly IReadOnlyDictionary<string, string?> EmptyProcessEnvironmentVariables =
        new Dictionary<string, string?>(StringComparer.Ordinal);

    protected static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    protected BackendServiceManagerBase(string? appDataDirectory = null)
    {
        var appDataRoot = appDataDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _stateDirectory = Path.Combine(appDataRoot, "Meridian", "service");
        _installationFilePath = Path.Combine(_stateDirectory, "backend-installation.json");
        _runtimeFilePath = Path.Combine(_stateDirectory, "backend-runtime.json");

        Directory.CreateDirectory(_stateDirectory);
    }

    /// <summary>Gets the local application data directory.</summary>
    protected virtual string GetAppDataDirectory()
        => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>Resolves the backend executable path.</summary>
    protected abstract string? ResolveExecutablePath(string? preferredPath);

    /// <summary>Gets process arguments for the backend executable.</summary>
    protected virtual IReadOnlyList<string> GetProcessArguments(string executablePath)
        => Array.Empty<string>();

    /// <summary>Gets environment variables for the backend executable.</summary>
    protected virtual IReadOnlyDictionary<string, string?> GetProcessEnvironmentVariables(string executablePath)
        => EmptyProcessEnvironmentVariables;

    /// <summary>Starts a process and returns its process ID, or null if failed.</summary>
    protected abstract int? StartProcess(
        string executablePath,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environmentVariables);

    /// <summary>Kills a process by ID. Returns true if the process was successfully terminated.</summary>
    protected abstract Task<bool> KillProcessAsync(int processId, CancellationToken ct);

    /// <summary>Checks if a process with the given ID is running.</summary>
    protected abstract bool IsProcessRunning(int processId);

    /// <summary>Checks the health endpoint. Returns true if healthy.</summary>
    protected abstract Task<bool> IsHealthyAsync(CancellationToken ct);

    /// <summary>Logs an info message.</summary>
    protected abstract void LogInfo(string message, params (string key, string value)[] properties);

    /// <summary>Logs an error message.</summary>
    protected abstract void LogError(string message, Exception? exception = null);

    public async Task<BackendServiceOperationResult> InstallAsync(string? executablePath = null, CancellationToken ct = default)
    {
        await _operationLock.WaitAsync(ct);
        try
        {
            var resolvedPath = ResolveExecutablePath(executablePath);
            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                return BackendServiceOperationResult.Failed("Backend executable not found.");
            }

            var install = new BackendInstallationInfo
            {
                ExecutablePath = resolvedPath,
                InstalledAtUtc = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(install, SerializerOptions);
            await File.WriteAllTextAsync(_installationFilePath, json, ct);

            LogInfo("Backend service installation updated", ("ExecutablePath", resolvedPath));
            return BackendServiceOperationResult.SuccessResult("Backend service registered.");
        }
        catch (Exception ex)
        {
            LogError("Failed to install backend service", ex);
            return BackendServiceOperationResult.Failed($"Installation failed: {ex.Message}");
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<BackendServiceOperationResult> StartAsync(CancellationToken ct = default)
    {
        await _operationLock.WaitAsync(ct);
        try
        {
            // Only block startup when a tracked process is actively running.
            // Reachability of the health endpoint alone is not sufficient — the backend
            // may be externally managed, and we should still honour an explicit Start.
            var runtime = await ReadRuntimeInfoAsync(ct);
            var processRunning = runtime is not null && IsProcessRunning(runtime.ProcessId);

            if (runtime is not null && !processRunning)
            {
                DeleteFileIfExists(_runtimeFilePath);
            }

            if (processRunning)
            {
                return BackendServiceOperationResult.SuccessResult("Backend is already running.");
            }

            var installation = await ReadInstallationInfoAsync(ct);
            if (installation is null || !File.Exists(installation.ExecutablePath))
            {
                var resolvedPath = ResolveExecutablePath(null);
                if (string.IsNullOrWhiteSpace(resolvedPath))
                {
                    return BackendServiceOperationResult.Failed("No backend installation found.");
                }

                installation = new BackendInstallationInfo
                {
                    ExecutablePath = resolvedPath,
                    InstalledAtUtc = DateTime.UtcNow
                };

                await File.WriteAllTextAsync(_installationFilePath, JsonSerializer.Serialize(installation, SerializerOptions), ct);
            }

            var workingDirectory = Path.GetDirectoryName(installation.ExecutablePath)
                ?? AppDomain.CurrentDomain.BaseDirectory;
            var arguments = GetProcessArguments(installation.ExecutablePath);
            var environmentVariables = GetProcessEnvironmentVariables(installation.ExecutablePath);

            var processId = StartProcess(
                installation.ExecutablePath,
                workingDirectory,
                arguments,
                environmentVariables);
            if (processId is null)
            {
                return BackendServiceOperationResult.Failed("Failed to start backend process.");
            }

            var runtimeInfo = new BackendRuntimeInfo
            {
                ProcessId = processId.Value,
                StartedAtUtc = DateTime.UtcNow
            };

            await File.WriteAllTextAsync(_runtimeFilePath, JsonSerializer.Serialize(runtimeInfo, SerializerOptions), ct);

            var becameHealthy = await WaitForHealthyAsync(TimeSpan.FromSeconds(15), ct);
            var message = becameHealthy
                ? "Backend service started and passed health checks."
                : "Backend process started, but health checks are still warming up.";

            LogInfo("Backend service start requested", ("Pid", processId.Value.ToString()));
            return BackendServiceOperationResult.SuccessResult(message);
        }
        catch (Exception ex)
        {
            LogError("Failed to start backend service", ex);
            return BackendServiceOperationResult.Failed($"Start failed: {ex.Message}");
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<BackendServiceOperationResult> StopAsync(CancellationToken ct = default)
    {
        await _operationLock.WaitAsync(ct);
        try
        {
            var runtime = await ReadRuntimeInfoAsync(ct);
            if (runtime is null)
            {
                return BackendServiceOperationResult.SuccessResult("Backend runtime was not tracked.");
            }

            if (!IsProcessRunning(runtime.ProcessId))
            {
                DeleteFileIfExists(_runtimeFilePath);
                return BackendServiceOperationResult.SuccessResult("Backend process already stopped.");
            }

            try
            {
                await KillProcessAsync(runtime.ProcessId, ct);
            }
            finally
            {
                DeleteFileIfExists(_runtimeFilePath);
            }

            LogInfo("Backend service stopped", ("Pid", runtime.ProcessId.ToString()));
            return BackendServiceOperationResult.SuccessResult("Backend service stopped.");
        }
        catch (Exception ex)
        {
            LogError("Failed to stop backend service", ex);
            return BackendServiceOperationResult.Failed($"Stop failed: {ex.Message}");
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<BackendServiceOperationResult> RestartAsync(CancellationToken ct = default)
    {
        var stopResult = await StopAsync(ct);
        if (!stopResult.Success)
            return stopResult;
        return await StartAsync(ct);
    }

    public async Task<BackendServiceStatus> GetStatusAsync(CancellationToken ct = default)
    {
        await _operationLock.WaitAsync(ct);
        try
        {
            return await GetStatusCoreAsync(ct);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task<BackendServiceStatus> GetStatusCoreAsync(CancellationToken ct)
    {
        var installation = await ReadInstallationInfoAsync(ct);
        var runtime = await ReadRuntimeInfoAsync(ct);

        var processRunning = runtime is not null && IsProcessRunning(runtime.ProcessId);
        var isHealthy = await IsHealthyAsync(ct);

        if (runtime is not null && !processRunning)
        {
            DeleteFileIfExists(_runtimeFilePath);
        }

        return new BackendServiceStatus
        {
            IsInstalled = installation is not null,
            IsRunning = processRunning || isHealthy,
            IsHealthy = isHealthy,
            ProcessId = processRunning ? runtime?.ProcessId : null,
            ExecutablePath = installation?.ExecutablePath,
            LastCheckedAtUtc = DateTime.UtcNow,
            StatusMessage = BuildStatusMessage(installation is not null, processRunning, isHealthy)
        };
    }

    private async Task<bool> WaitForHealthyAsync(TimeSpan timeout, CancellationToken ct)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            if (await IsHealthyAsync(ct))
                return true;
            await Task.Delay(400, ct);
        }
        return false;
    }

    private async Task<BackendInstallationInfo?> ReadInstallationInfoAsync(CancellationToken ct)
    {
        if (!File.Exists(_installationFilePath))
            return null;
        var json = await File.ReadAllTextAsync(_installationFilePath, ct);
        return JsonSerializer.Deserialize<BackendInstallationInfo>(json, SerializerOptions);
    }

    private async Task<BackendRuntimeInfo?> ReadRuntimeInfoAsync(CancellationToken ct)
    {
        if (!File.Exists(_runtimeFilePath))
            return null;
        var json = await File.ReadAllTextAsync(_runtimeFilePath, ct);
        return JsonSerializer.Deserialize<BackendRuntimeInfo>(json, SerializerOptions);
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static string BuildStatusMessage(bool installed, bool processRunning, bool healthy)
    {
        if (!installed)
            return "Backend is not installed for lifecycle management yet.";
        if (processRunning && healthy)
            return "Backend is running and healthy.";
        if (processRunning)
            return "Backend process is running, waiting for healthy response.";
        if (healthy)
            return "Backend is reachable (managed externally).";
        return "Backend is stopped.";
    }
}
