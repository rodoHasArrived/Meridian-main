using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Contracts;

namespace Meridian.Wpf.Services;

/// <summary>
/// Compatibility facade for the desktop service manager. Process mutations are
/// delegated to the lifecycle supervisor; WPF never starts or stops the host directly.
/// </summary>
public sealed class BackendServiceManager : BackendServiceManagerBase
{
    private static readonly Lazy<BackendServiceManager> _instance = new(() => new BackendServiceManager());
    private readonly IRemoteWorkstationClient _remoteClient;
    private readonly SemaphoreSlim _supervisorOperationGate = new(1, 1);

    public static BackendServiceManager Instance => _instance.Value;

    private BackendServiceManager()
        : this(WpfRemoteWorkstationClient.Instance)
    {
    }

    internal BackendServiceManager(IRemoteWorkstationClient remoteClient, string? appDataDirectory = null)
        : base(appDataDirectory)
    {
        _remoteClient = remoteClient ?? throw new ArgumentNullException(nameof(remoteClient));
    }

    protected override string? ResolveExecutablePath(string? preferredPath)
    {
        if (!string.IsNullOrWhiteSpace(preferredPath) && File.Exists(preferredPath))
        {
            return preferredPath;
        }

        var configuredPath = Environment.GetEnvironmentVariable("MDC_LIFECYCLE_SUPERVISOR_PATH", EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "Meridian.LifecycleSupervisor.exe"),
            Path.Combine(baseDirectory, "Meridian", "Meridian.LifecycleSupervisor.exe"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "Meridian.LifecycleSupervisor.exe"))
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    protected override IReadOnlyList<string> GetProcessArguments(string executablePath)
        => BuildProcessArguments();

    protected override IReadOnlyDictionary<string, string?> GetProcessEnvironmentVariables(string executablePath)
        => new Dictionary<string, string?>(StringComparer.Ordinal);

    internal static IReadOnlyList<string> BuildProcessArguments() => ["start"];

    public override async Task<BackendServiceOperationResult> StartAsync(CancellationToken ct = default)
    {
        await _supervisorOperationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var launched = LaunchSupervisorCommand("start");
            if (!launched)
                return BackendServiceOperationResult.Failed("Lifecycle supervisor could not be started.");

            var healthy = await WaitForHealthStateAsync(expectedHealthy: true, TimeSpan.FromSeconds(60), ct)
                .ConfigureAwait(false);
            return healthy
                ? BackendServiceOperationResult.SuccessResult("Lifecycle supervisor started Meridian and readiness passed.")
                : BackendServiceOperationResult.Failed("Lifecycle supervisor started, but Meridian did not become ready before the deadline.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            LogError("Lifecycle supervisor start failed", ex);
            return BackendServiceOperationResult.Failed($"Start failed: {ex.Message}");
        }
        finally
        {
            _supervisorOperationGate.Release();
        }
    }

    public override async Task<BackendServiceOperationResult> StopAsync(CancellationToken ct = default)
    {
        await _supervisorOperationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var exitCode = await RunTransientSupervisorCommandAsync("stop", ct).ConfigureAwait(false);
            if (exitCode is not (0 or 3))
                return BackendServiceOperationResult.Failed($"Lifecycle supervisor rejected shutdown with exit code {exitCode}.");

            var stopped = await WaitForHealthStateAsync(expectedHealthy: false, TimeSpan.FromSeconds(60), ct)
                .ConfigureAwait(false);
            return stopped
                ? BackendServiceOperationResult.SuccessResult("Lifecycle supervisor stopped Meridian and its owned database.")
                : BackendServiceOperationResult.Failed("Meridian did not stop before the lifecycle deadline.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            LogError("Lifecycle supervisor shutdown failed", ex);
            return BackendServiceOperationResult.Failed($"Stop failed: {ex.Message}");
        }
        finally
        {
            _supervisorOperationGate.Release();
        }
    }

    public override async Task<BackendServiceOperationResult> RestartAsync(CancellationToken ct = default)
    {
        await _supervisorOperationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var exitCode = await RunTransientSupervisorCommandAsync("restart", ct).ConfigureAwait(false);
            return exitCode == 0
                ? BackendServiceOperationResult.SuccessResult("Lifecycle supervisor accepted the Meridian restart request.")
                : BackendServiceOperationResult.Failed($"Lifecycle supervisor rejected restart with exit code {exitCode}.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            LogError("Lifecycle supervisor restart failed", ex);
            return BackendServiceOperationResult.Failed($"Restart failed: {ex.Message}");
        }
        finally
        {
            _supervisorOperationGate.Release();
        }
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

    private bool LaunchSupervisorCommand(string command)
    {
        var executable = ResolveExecutablePath(null);
        if (string.IsNullOrWhiteSpace(executable)) return false;
        var process = Process.Start(CreateSupervisorStartInfo(executable, command));
        process?.Dispose();
        return process is not null;
    }

    private async Task<int> RunTransientSupervisorCommandAsync(string command, CancellationToken ct)
    {
        var executable = ResolveExecutablePath(null)
            ?? throw new FileNotFoundException("Meridian lifecycle supervisor was not found.");
        using var process = Process.Start(CreateSupervisorStartInfo(executable, command))
            ?? throw new InvalidOperationException($"Could not start lifecycle supervisor command '{command}'.");
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return process.ExitCode;
    }

    private static ProcessStartInfo CreateSupervisorStartInfo(string executable, string command)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(command);
        return start;
    }

    private async Task<bool> WaitForHealthStateAsync(
        bool expectedHealthy,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await IsHealthyAsync(ct).ConfigureAwait(false) == expectedHealthy)
                return true;
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct).ConfigureAwait(false);
        }

        return await IsHealthyAsync(ct).ConfigureAwait(false) == expectedHealthy;
    }

    protected override async Task<bool> KillProcessAsync(int processId, CancellationToken ct)
    {
        await Task.CompletedTask;
        LogInfo(
            "Desktop refused to force-terminate the lifecycle supervisor",
            ("Pid", processId.ToString(CultureInfo.InvariantCulture)));
        return false;
    }

    protected override async Task<bool> RequestGracefulStopAsync(BackendRuntimeInfo runtime, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(runtime.ExecutablePath) || !File.Exists(runtime.ExecutablePath))
            return false;

        try
        {
            using var stopProcess = Process.Start(new ProcessStartInfo
            {
                FileName = runtime.ExecutablePath,
                Arguments = "stop",
                WorkingDirectory = Path.GetDirectoryName(runtime.ExecutablePath) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (stopProcess is null)
                return false;

            await stopProcess.WaitForExitAsync(ct).ConfigureAwait(false);
            if (stopProcess.ExitCode is not (0 or 3))
                return false;

            var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (!IsProcessRunning(runtime.ProcessId))
                {
                    LogInfo("Lifecycle supervisor exited after graceful shutdown", ("Pid", runtime.ProcessId.ToString(CultureInfo.InvariantCulture)));
                    return true;
                }

                await Task.Delay(250, ct).ConfigureAwait(false);
            }

            LogInfo("Lifecycle supervisor shutdown timed out", ("Pid", runtime.ProcessId.ToString(CultureInfo.InvariantCulture)));
            return false;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            LogError("Lifecycle supervisor shutdown request failed", ex);
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

            return process.ProcessName.Contains("LifecycleSupervisor", StringComparison.OrdinalIgnoreCase)
                && runtime.Arguments.Contains("start", StringComparer.OrdinalIgnoreCase);
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
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
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
