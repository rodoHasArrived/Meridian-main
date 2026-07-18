using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Meridian.Contracts.Lifecycle;

namespace Meridian.LifecycleSupervisor;

internal sealed class LifecycleSupervisorRuntime : IAsyncDisposable
{
    private readonly LifecycleSupervisorConfiguration _configuration;
    private readonly LifecycleSupervisorPipeServer _pipeServer;
    private readonly LifecycleDatabaseController _database;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly object _gate = new();
    private TaskCompletionSource<SupervisorAction> _requestedAction = NewActionSource();
    private Process? _hostProcess;
    private LifecycleOwnedProcessDto? _hostIdentity;
    private LifecycleDatabaseIdentityDto? _databaseIdentity;
    private RuntimeLifecycleSnapshotDto? _hostLifecycle;
    private LifecycleSessionReceiptDto? _latestReceipt;
    private string? _sessionId;
    private string? _hostSessionId;
    private DateTimeOffset? _startedAtUtc;
    private int? _httpPort;
    private string? _bootstrapToken;
    private string? _shutdownToken;
    private bool _openRequested;
    private bool _restartRequested;
    private bool _stopRequested;
    private string? _message;

    public LifecycleSupervisorRuntime(LifecycleSupervisorConfiguration configuration)
    {
        _configuration = configuration;
        _database = new LifecycleDatabaseController(configuration);
        _latestReceipt = LoadLatestSessionReceipt();
        _pipeServer = new LifecycleSupervisorPipeServer(
            configuration.PipeName,
            HandleCommandAsync,
            HandleHostStatus);
    }

    public async Task<int> RunAsync(bool openBrowser, CancellationToken ct)
    {
        _openRequested = openBrowser;
        _pipeServer.Start();
        using var cancellationRegistration = ct.Register(() => RequestAction(SupervisorAction.Stop));

        while (true)
        {
            TaskCompletionSource<SupervisorAction> actionSource;
            lock (_gate)
            {
                if (_stopRequested) return 0;
                _requestedAction = NewActionSource();
                actionSource = _requestedAction;
                if (_restartRequested) actionSource.TrySetResult(SupervisorAction.Restart);
            }
            using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try
            {
                var startupTask = StartSessionAsync(startupCts.Token);
                var startupCompletion = await Task.WhenAny(startupTask, actionSource.Task).ConfigureAwait(false);
                if (startupCompletion == actionSource.Task)
                {
                    var startupAction = await actionSource.Task.ConfigureAwait(false);
                    startupCts.Cancel();
                    try
                    {
                        await startupTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (startupCts.IsCancellationRequested)
                    {
                    }

                    await StopSessionAsync(startupAction, CancellationToken.None).ConfigureAwait(false);
                    if (!ShouldRestartAfterStop(startupAction) || ct.IsCancellationRequested)
                        return 0;

                    _openRequested = true;
                    continue;
                }

                await startupTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await StopSessionAsync(SupervisorAction.Stop, CancellationToken.None).ConfigureAwait(false);
                return 0;
            }
            catch (Exception ex)
            {
                _message = $"Startup failed ({ex.GetType().Name}): {ex.Message}";
                await StopSessionAsync(SupervisorAction.Stop, CancellationToken.None).ConfigureAwait(false);
                return 1;
            }

            var actionTask = actionSource.Task;
            var exitTask = _hostProcess!.WaitForExitAsync(CancellationToken.None);
            var completed = await Task.WhenAny(actionTask, exitTask).ConfigureAwait(false);
            var action = completed == actionTask
                ? await actionTask.ConfigureAwait(false)
                : ClassifyHostExit(_hostLifecycle, LoadHostReceipt());

            await StopSessionAsync(action, CancellationToken.None).ConfigureAwait(false);
            if (!ShouldRestartAfterStop(action))
            {
                return action == SupervisorAction.HostExited ? 1 : 0;
            }

            _openRequested = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _pipeServer.DisposeAsync().ConfigureAwait(false);
        _hostProcess?.Dispose();
    }

    private async Task StartSessionAsync(CancellationToken ct)
    {
        var preflight = LifecycleSupervisorPreflight.Evaluate(_configuration);
        if (!preflight.Success) throw new InvalidOperationException(preflight.Message);

        _sessionId = Guid.NewGuid().ToString("N");
        _startedAtUtc = DateTimeOffset.UtcNow;
        _httpPort = _configuration.Manifest.HttpPort ?? ReserveLoopbackPort();
        _bootstrapToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _shutdownToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        LifecycleProtectedSecretStore.Write(_configuration.SecretPath, _shutdownToken);
        _message = "Starting dedicated runtime dependencies.";

        _databaseIdentity = await _database.StartAsync(ct).ConfigureAwait(false);
        _message = "Starting Meridian host.";
        await StartOwnedHostAsync().ConfigureAwait(false);
        PersistStatus();

        await WaitForReadinessAsync(ct).ConfigureAwait(false);
        _message = "Meridian is ready.";
        PersistStatus();
        if (_openRequested)
        {
            _openRequested = false;
            OpenBrowser();
        }
    }

    private async Task StartOwnedHostAsync()
    {
        _hostProcess = StartHost();
        try
        {
            _hostIdentity = CaptureHostIdentity(_hostProcess, _configuration.HostPath);
        }
        catch
        {
            if (!_hostProcess.HasExited)
            {
                _hostProcess.Kill(entireProcessTree: true);
                await _hostProcess.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            _hostProcess.Dispose();
            _hostProcess = null;
            throw;
        }
    }

    private Process StartHost()
    {
        var url = $"http://127.0.0.1:{_httpPort}";
        var start = new ProcessStartInfo(_configuration.HostPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_configuration.HostPath)!
        };
        foreach (var argument in new[] { "--mode", "workstation", "--http-port", _httpPort!.Value.ToString() })
            start.ArgumentList.Add(argument);
        var configPath = _configuration.ResolveConfigPath();
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            start.ArgumentList.Add("--config");
            start.ArgumentList.Add(configPath);
        }

        start.Environment["ASPNETCORE_URLS"] = url;
        start.Environment["MDC_BOOTSTRAP_TOKEN"] = _bootstrapToken;
        start.Environment["MDC_AUTH_MODE"] = "required";
        start.Environment["MDC_PACKAGED_BUILD"] = "true";
        start.Environment["MERIDIAN_INSTALL_ROOT"] = _configuration.InstallRoot;
        start.Environment["MDC_DATA_ROOT"] = _configuration.DataRoot;
        start.Environment["MDC_LIFECYCLE_PIPE"] = _configuration.PipeName;
        start.Environment["MDC_SHUTDOWN_TOKEN"] = _shutdownToken;

        var connection = _database.BuildHostConnectionString();
        foreach (var variable in new[]
                 {
                     "MERIDIAN_SECURITY_MASTER_CONNECTION_STRING",
                     "MERIDIAN_LEDGER_CONNECTION_STRING",
                     "MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING",
                     "MERIDIAN_FUND_STRUCTURE_CONNECTION_STRING",
                     "MERIDIAN_DIRECT_LENDING_CONNECTION_STRING"
                 })
        {
            start.Environment[variable] = connection;
        }

        return Process.Start(start) ?? throw new InvalidOperationException("Failed to start Meridian host.");
    }

    private async Task WaitForReadinessAsync(CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(_configuration.Manifest.StartupTimeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (_hostProcess?.HasExited == true)
                throw new InvalidOperationException($"Meridian host exited during startup with code {_hostProcess.ExitCode}.");
            try
            {
                using var response = await _http.GetAsync(
                    $"http://127.0.0.1:{_httpPort}/startupz",
                    ct).ConfigureAwait(false);
                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var snapshot = JsonSerializer.Deserialize(
                    json,
                    LifecycleContractsJsonContext.Default.RuntimeLifecycleSnapshotDto);
                if (snapshot is not null)
                {
                    lock (_gate)
                    {
                        _hostLifecycle = snapshot;
                        _hostSessionId = snapshot.SessionId;
                    }
                    PersistStatus();
                    if (snapshot.AcceptingWork &&
                        snapshot.Readiness is RuntimeReadinessStatus.Ready or RuntimeReadinessStatus.Degraded)
                        return;
                    if (snapshot.Readiness == RuntimeReadinessStatus.Failed)
                        throw new InvalidOperationException("Meridian host reported failed startup readiness.");
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Meridian did not become ready within {_configuration.Manifest.StartupTimeoutSeconds} seconds.");
    }

    private async Task StopSessionAsync(SupervisorAction action, CancellationToken ct)
    {
        if (_sessionId is null) return;
        var shutdownStarted = DateTimeOffset.UtcNow;
        var hostForced = false;
        var databaseForced = false;
        var hostOutcome = LifecycleShutdownOutcome.Succeeded;
        var databaseOutcome = LifecycleShutdownOutcome.Succeeded;

        if (_hostProcess is { HasExited: false })
        {
            var command = action == SupervisorAction.Restart ? "restart" : "shutdown";
            var accepted = await _pipeServer.SendHostCommandAsync(
                command,
                "Supervisor is stopping its owned runtime session.",
                TimeSpan.FromSeconds(5),
                ct).ConfigureAwait(false);
            if (!accepted)
                accepted = await RequestHttpFallbackShutdownAsync(ct).ConfigureAwait(false);

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(_configuration.Manifest.ShutdownTimeoutSeconds));
            try
            {
                await _hostProcess.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                hostOutcome = accepted
                    ? LifecycleShutdownOutcome.Succeeded
                    : LifecycleShutdownOutcome.SucceededWithWarnings;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                if (TryValidateHostIdentity(_hostIdentity, out var validatedHost))
                {
                    validatedHost!.Kill(entireProcessTree: true);
                    await validatedHost.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    validatedHost.Dispose();
                    hostForced = true;
                    hostOutcome = LifecycleShutdownOutcome.Forced;
                }
                else
                {
                    hostOutcome = LifecycleShutdownOutcome.Failed;
                }
            }
        }
        else if (action == SupervisorAction.HostExited)
        {
            hostOutcome = LifecycleShutdownOutcome.Failed;
        }

        try
        {
            var databaseResult = await _database.StopAsync(ct).ConfigureAwait(false);
            databaseOutcome = databaseResult.Outcome;
            databaseForced = databaseResult.Forced;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            databaseOutcome = LifecycleShutdownOutcome.Failed;
            _message = $"Database shutdown failed ({ex.GetType().Name}).";
        }

        var hostReceipt = LoadHostReceipt();
        if (hostReceipt is not null && !hostForced)
            hostOutcome = hostReceipt.Outcome;
        else if (!hostForced && _hostLifecycle?.State == RuntimeLifecycleState.Failed)
            hostOutcome = LifecycleShutdownOutcome.Failed;
        else if (!hostForced && action is SupervisorAction.Stop or SupervisorAction.Restart)
            hostOutcome = LifecycleShutdownOutcome.SucceededWithWarnings;
        var outcome = CombineOutcome(hostOutcome, databaseOutcome);
        var receipt = new LifecycleSessionReceiptDto
        {
            SessionId = _sessionId,
            StartedAtUtc = _startedAtUtc ?? shutdownStarted,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Outcome = outcome,
            HostReceipt = hostReceipt,
            DatabaseOutcome = databaseOutcome,
            HostForced = hostForced,
            DatabaseForced = databaseForced,
            Message = _message
        };
        try
        {
            PersistSessionReceipt(receipt);
            _latestReceipt = receipt;
        }
        finally
        {
            LifecycleProtectedSecretStore.Delete(_configuration.SecretPath);
            DeleteRuntimeStatus();

            _hostProcess?.Dispose();
            _hostProcess = null;
            _hostIdentity = null;
            _databaseIdentity = null;
            _hostLifecycle = null;
            _hostSessionId = null;
            _sessionId = null;
            _startedAtUtc = null;
            _httpPort = null;
            _bootstrapToken = null;
            _shutdownToken = null;
        }
    }

    private async Task<bool> RequestHttpFallbackShutdownAsync(CancellationToken ct)
    {
        var token = LifecycleProtectedSecretStore.Read(_configuration.SecretPath);
        if (string.IsNullOrWhiteSpace(token) || _httpPort is null) return false;
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"http://127.0.0.1:{_httpPort}/api/system/shutdown");
            request.Headers.Add("X-Meridian-Shutdown-Token", token);
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.Accepted || response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    internal static SupervisorAction ClassifyHostExit(
        RuntimeLifecycleSnapshotDto? lifecycle,
        LifecycleShutdownReceiptDto? receipt)
    {
        if (string.Equals(lifecycle?.ShutdownReason, nameof(LifecycleShutdownReason.Restart), StringComparison.OrdinalIgnoreCase) ||
            receipt?.Reason == LifecycleShutdownReason.Restart)
            return SupervisorAction.Restart;

        return receipt is not null || lifecycle?.ShutdownRequested == true
            ? SupervisorAction.Stop
            : SupervisorAction.HostExited;
    }

    private Task<LifecycleSupervisorMessageDto> HandleCommandAsync(LifecycleSupervisorMessageDto request)
    {
        var command = request.Command.ToLowerInvariant();
        return command switch
        {
            "status" => Task.FromResult(Response(request, true, status: CreateStatus())),
            "preflight" => Task.FromResult(PreflightResponse(request)),
            "stop" => Task.FromResult(ActionResponse(request, SupervisorAction.Stop)),
            "restart" => Task.FromResult(ActionResponse(request, SupervisorAction.Restart)),
            "open" or "start" => Task.FromResult(OpenResponse(request)),
            _ => Task.FromResult(Response(request, false, $"Unknown supervisor command '{request.Command}'."))
        };
    }

    private LifecycleSupervisorMessageDto PreflightResponse(LifecycleSupervisorMessageDto request)
    {
        var result = LifecycleSupervisorPreflight.Evaluate(_configuration);
        return Response(request, result.Success, result.Message, CreateStatus());
    }

    private LifecycleSupervisorMessageDto ActionResponse(
        LifecycleSupervisorMessageDto request,
        SupervisorAction action)
    {
        var accepted = RequestAction(action);
        var message = accepted
            ? $"Meridian {action.ToString().ToLowerInvariant()} requested."
            : "Meridian is already stopping; a restart cannot be queued.";
        return Response(request, accepted, message, CreateStatus());
    }

    private LifecycleSupervisorMessageDto OpenResponse(LifecycleSupervisorMessageDto request)
    {
        _openRequested = true;
        if (_hostLifecycle?.AcceptingWork == true)
        {
            _openRequested = false;
            OpenBrowser();
        }
        return Response(request, true, "Meridian will open when readiness is established.", CreateStatus());
    }

    private static LifecycleSupervisorMessageDto Response(
        LifecycleSupervisorMessageDto request,
        bool success,
        string? message = null,
        LifecycleSupervisorStatusDto? status = null)
        => new()
        {
            Command = $"{request.Command}-result",
            RequestId = request.RequestId,
            Success = success,
            Message = message,
            Status = status
        };

    private void HandleHostStatus(LifecycleSupervisorMessageDto message)
    {
        lock (_gate)
        {
            _hostLifecycle = message.Lifecycle;
            _hostSessionId = message.SessionId ?? message.Lifecycle?.SessionId;
        }
        PersistStatus();
    }

    private LifecycleSupervisorStatusDto CreateStatus()
    {
        lock (_gate)
        {
            return new LifecycleSupervisorStatusDto
            {
                Running = true,
                PipeName = _configuration.PipeName,
                ManifestPath = _configuration.ManifestPath,
                SessionId = _sessionId,
                StartedAtUtc = _startedAtUtc,
                HttpPort = _httpPort,
                Host = _hostIdentity,
                Database = _databaseIdentity,
                HostLifecycle = _hostLifecycle,
                LatestSessionReceipt = _latestReceipt,
                Message = _message
            };
        }
    }

    private void PersistStatus()
    {
        if (_sessionId is null) return;
        AtomicJsonFile.Write(
            Path.Combine(_configuration.RuntimeRoot, "supervisor-session.json"),
            JsonSerializer.SerializeToUtf8Bytes(
                CreateStatus(),
                LifecycleContractsJsonContext.Default.LifecycleSupervisorStatusDto));
    }

    private void DeleteRuntimeStatus()
    {
        var path = Path.Combine(_configuration.RuntimeRoot, "supervisor-session.json");
        if (File.Exists(path)) File.Delete(path);
    }

    private void PersistSessionReceipt(LifecycleSessionReceiptDto receipt)
    {
        var path = Path.Combine(_configuration.ReceiptRoot, $"session-{receipt.SessionId}.json");
        AtomicJsonFile.Write(
            path,
            JsonSerializer.SerializeToUtf8Bytes(
                receipt,
                LifecycleContractsJsonContext.Default.LifecycleSessionReceiptDto));
    }

    private LifecycleSessionReceiptDto? LoadLatestSessionReceipt()
    {
        if (!Directory.Exists(_configuration.ReceiptRoot)) return null;
        var latest = Directory.EnumerateFiles(_configuration.ReceiptRoot, "session-*.json")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();
        if (latest is null) return null;
        try
        {
            return JsonSerializer.Deserialize(
                File.ReadAllText(latest.FullName),
                LifecycleContractsJsonContext.Default.LifecycleSessionReceiptDto);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private LifecycleShutdownReceiptDto? LoadHostReceipt()
    {
        var hostSessionId = _hostSessionId;
        if (string.IsNullOrWhiteSpace(hostSessionId)) return null;
        var path = Path.Combine(_configuration.ReceiptRoot, $"host-{hostSessionId}.json");
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize(
                File.ReadAllText(path),
                LifecycleContractsJsonContext.Default.LifecycleShutdownReceiptDto);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void OpenBrowser()
    {
        if (_httpPort is null) return;
        var accountStore = Path.Combine(_configuration.DataRoot, "governance", "user-accounts.json");
        var destination = File.Exists(accountStore)
            ? $"http://127.0.0.1:{_httpPort}/login?returnUrl=%2Fworkstation%2F"
            : $"http://127.0.0.1:{_httpPort}/setup/account#token={Uri.EscapeDataString(_bootstrapToken ?? string.Empty)}";
        Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true });
    }

    private bool RequestAction(SupervisorAction action)
    {
        lock (_gate)
        {
            if (action == SupervisorAction.Stop)
            {
                _stopRequested = true;
                _restartRequested = false;
                _requestedAction.TrySetResult(SupervisorAction.Stop);
                return true;
            }

            if (_stopRequested) return false;
            if (action == SupervisorAction.Restart) _restartRequested = true;
            _requestedAction.TrySetResult(action);
            return true;
        }
    }

    private bool ShouldRestartAfterStop(SupervisorAction action)
    {
        lock (_gate)
        {
            if (_stopRequested) return false;
            var shouldRestart = action == SupervisorAction.Restart || _restartRequested;
            if (shouldRestart) _restartRequested = false;
            return shouldRestart;
        }
    }

    private static TaskCompletionSource<SupervisorAction> NewActionSource()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private static LifecycleOwnedProcessDto CaptureHostIdentity(Process process, string expectedPath)
        => new()
        {
            ProcessId = process.Id,
            ExecutablePath = Path.GetFullPath(expectedPath),
            StartedAtUtc = new DateTimeOffset(process.StartTime.ToUniversalTime())
        };

    private static bool TryValidateHostIdentity(
        LifecycleOwnedProcessDto? identity,
        out Process? process)
    {
        process = null;
        if (identity is null) return false;
        try
        {
            process = Process.GetProcessById(identity.ProcessId);
            var path = process.MainModule?.FileName;
            var startedAt = new DateTimeOffset(process.StartTime.ToUniversalTime());
            if (string.IsNullOrWhiteSpace(path) ||
                !string.Equals(Path.GetFullPath(path), Path.GetFullPath(identity.ExecutablePath), StringComparison.OrdinalIgnoreCase) ||
                Math.Abs((startedAt - identity.StartedAtUtc).TotalSeconds) > 2)
            {
                process.Dispose();
                process = null;
                return false;
            }
            return true;
        }
        catch
        {
            process?.Dispose();
            process = null;
            return false;
        }
    }

    private static LifecycleShutdownOutcome CombineOutcome(
        LifecycleShutdownOutcome host,
        LifecycleShutdownOutcome database)
    {
        if (host == LifecycleShutdownOutcome.Failed || database == LifecycleShutdownOutcome.Failed)
            return LifecycleShutdownOutcome.Failed;
        if (host == LifecycleShutdownOutcome.Forced || database == LifecycleShutdownOutcome.Forced)
            return LifecycleShutdownOutcome.Forced;
        if (host == LifecycleShutdownOutcome.TimedOut || database == LifecycleShutdownOutcome.TimedOut)
            return LifecycleShutdownOutcome.TimedOut;
        if (host == LifecycleShutdownOutcome.SucceededWithWarnings || database == LifecycleShutdownOutcome.SucceededWithWarnings)
            return LifecycleShutdownOutcome.SucceededWithWarnings;
        return LifecycleShutdownOutcome.Succeeded;
    }

}

internal enum SupervisorAction
{
    Stop,
    Restart,
    HostExited
}

internal static class LifecycleProtectedSecretStore
{
    private static readonly byte[] Entropy = "Meridian.LifecycleSupervisor.v1"u8.ToArray();

    public static void Write(string path, string secret)
    {
        var protectedBytes = ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes(secret),
            Entropy,
            DataProtectionScope.CurrentUser);
        AtomicJsonFile.Write(path, protectedBytes);
    }

    public static string? Read(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var clear = ProtectedData.Unprotect(
                File.ReadAllBytes(path),
                Entropy,
                DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(clear);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public static void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
