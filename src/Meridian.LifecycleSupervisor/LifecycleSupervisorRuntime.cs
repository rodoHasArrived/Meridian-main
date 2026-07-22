using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Meridian.Contracts.Lifecycle;
using Meridian.Contracts.Operations;

namespace Meridian.LifecycleSupervisor;

internal sealed class LifecycleSupervisorRuntime : IAsyncDisposable
{
    private readonly LifecycleSupervisorConfiguration _configuration;
    private readonly LifecycleSupervisorPipeServer _pipeServer;
    private readonly LifecycleDatabaseController _database;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly object _gate = new();
    private readonly object _logGate = new();
    private readonly LifecycleOpenOutcomeGate _openOutcomeGate = new();
    private readonly Dictionary<string, LifecycleStartupOperationRequest> _pendingOpenRequests =
        new(StringComparer.Ordinal);
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
    private LifecycleStartupOperationRequest? _sessionStartupRequest;
    private string? _restartStartupRequestId;
    private bool _restartRequested;
    private bool _stopRequested;
    private bool _preflightSucceeded;
    private bool _startupFailed;
    private bool _readinessGatePersisted;
    private LifecycleStartupOutcomeReceipt? _latestStartupOutcomeReceipt;
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

    public async Task<int> RunAsync(bool openBrowser, string? requestId, CancellationToken ct)
    {
        _sessionStartupRequest = LifecycleStartupOutcome.CreateRequest(
            _configuration,
            requestId,
            openBrowser);
        _pipeServer.Start();
        using var cancellationRegistration = ct.Register(() => RequestAction(SupervisorAction.Stop));

        while (true)
        {
            TaskCompletionSource<SupervisorAction> actionSource;
            lock (_gate)
            {
                if (_stopRequested)
                    return 0;
                _requestedAction = NewActionSource();
                actionSource = _requestedAction;
                if (_restartRequested)
                    actionSource.TrySetResult(SupervisorAction.Restart);
            }
            using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try
            {
                var startupTask = StartSessionAsync(startupCts.Token);
                var startupCompletion = await Task.WhenAny(startupTask, actionSource.Task).ConfigureAwait(false);
                if (startupCompletion == actionSource.Task)
                {
                    var startupAction = await actionSource.Task.ConfigureAwait(false);
                    var startupWasBlocked = !_readinessGatePersisted;
                    startupCts.Cancel();
                    try
                    {
                        await startupTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (startupCts.IsCancellationRequested)
                    {
                    }

                    if (startupWasBlocked)
                    {
                        _message = $"Startup was blocked by a requested {startupAction.ToString().ToLowerInvariant()} " +
                                   "before exact Ready status was established.";
                        AppendSupervisorLog(_message);
                        PersistOutstandingStartupOutcomes(
                            OperationTerminalState.Blocked,
                            readinessSatisfied: false,
                            terminalMessage: _message);
                    }
                    await StopSessionAsync(startupAction, CancellationToken.None).ConfigureAwait(false);
                    if (!ShouldRestartAfterStop(startupAction) || ct.IsCancellationRequested)
                        return startupWasBlocked
                            ? LifecycleSupervisorExitCode.Blocked
                            : LifecycleSupervisorExitCode.Succeeded;

                    _sessionStartupRequest = LifecycleStartupOutcome.CreateRequest(
                        _configuration,
                        TakeRestartStartupRequestId(),
                        browserRequested: true);
                    continue;
                }

                await startupTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                var startupWasBlocked = !_readinessGatePersisted;
                if (startupWasBlocked)
                {
                    _message = "Startup was blocked by cancellation before exact Ready status was established.";
                    AppendSupervisorLog(_message);
                    PersistOutstandingStartupOutcomes(
                        OperationTerminalState.Blocked,
                        readinessSatisfied: false,
                        terminalMessage: _message);
                }
                await StopSessionAsync(SupervisorAction.Stop, CancellationToken.None).ConfigureAwait(false);
                return startupWasBlocked
                    ? LifecycleSupervisorExitCode.Blocked
                    : LifecycleSupervisorExitCode.Succeeded;
            }
            catch (LifecycleStartupBlockedException ex)
            {
                _message = ex.Message;
                AppendSupervisorLog(_message);
                PersistOutstandingStartupOutcomes(
                    OperationTerminalState.Blocked,
                    readinessSatisfied: false,
                    terminalMessage: _message,
                    exceptionType: ex.GetType().Name);
                WriteTerminalDiagnostics(OperationTerminalState.Blocked);
                ResetSessionState();
                return LifecycleSupervisorExitCode.Blocked;
            }
            catch (Exception ex)
            {
                _message = $"Startup failed ({ex.GetType().Name}): {ex.Message}";
                _startupFailed = true;
                AppendSupervisorLog(_message);
                PersistOutstandingStartupOutcomes(
                    OperationTerminalState.Failed,
                    readinessSatisfied: false,
                    terminalMessage: _message,
                    exceptionType: ex.GetType().Name);
                WriteTerminalDiagnostics(OperationTerminalState.Failed);
                await StopSessionAsync(SupervisorAction.HostExited, CancellationToken.None).ConfigureAwait(false);
                return LifecycleSupervisorExitCode.Failed;
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

            _sessionStartupRequest = LifecycleStartupOutcome.CreateRequest(
                _configuration,
                TakeRestartStartupRequestId(),
                browserRequested: true);
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
        _sessionId = Guid.NewGuid().ToString("N");
        _startedAtUtc = DateTimeOffset.UtcNow;
        _preflightSucceeded = false;
        _startupFailed = false;
        _readinessGatePersisted = false;
        _latestStartupOutcomeReceipt = null;
        AppendSupervisorLog($"Startup session {_sessionId} began.");
        var preflight = LifecycleSupervisorPreflight.Evaluate(_configuration);
        if (!preflight.Success)
            throw new LifecycleStartupBlockedException(preflight.Message);

        _preflightSucceeded = true;
        AppendSupervisorLog(preflight.Message);
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
        AppendSupervisorLog(_message);
        PersistStatus();
        var startupRequest = _sessionStartupRequest
            ?? throw new InvalidOperationException("The startup operation request is unavailable.");
        var readyReceipt = TryPersistStartupOutcome(
            startupRequest,
            OperationTerminalState.Succeeded,
            readinessSatisfied: true,
            terminalMessage: _message,
            browserRequested: startupRequest.BrowserRequested,
            readinessGateReceipt: startupRequest.BrowserRequested);
        if (readyReceipt is null)
        {
            throw new IOException(
                "The verified startup receipt could not be retained; the workstation will not open.");
        }
        LifecycleStartupOperationRequest[] pendingOpenRequests;
        lock (_gate)
        {
            _readinessGatePersisted = true;
            pendingOpenRequests = _pendingOpenRequests.Values.ToArray();
            _pendingOpenRequests.Clear();
        }
        if (startupRequest.BrowserRequested)
            OpenBrowserWithOutcome(startupRequest);
        foreach (var pendingRequest in pendingOpenRequests)
            CompleteReadyOpenRequest(pendingRequest);
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
        AppendSupervisorLog(
            $"Waiting for exact Ready status at /readyz until {deadline:O} " +
            $"({_configuration.Manifest.StartupTimeoutSeconds} seconds).");
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (_hostProcess?.HasExited == true)
                throw new InvalidOperationException(
                    $"Meridian host exited during startup with exit code {_hostProcess.ExitCode} before the readiness deadline {deadline:O}.");
            try
            {
                using var response = await _http.GetAsync(
                    $"http://127.0.0.1:{_httpPort}/readyz",
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
                    if (response.IsSuccessStatusCode && IsReadyForBrowser(snapshot))
                        return;
                    if (snapshot.Readiness == RuntimeReadinessStatus.Failed)
                        throw new InvalidOperationException(
                            $"Meridian host reported failed startup readiness before the deadline {deadline:O}.");
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (JsonException)
            {
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Meridian did not reach exact Ready status by {deadline:O} " +
            $"({_configuration.Manifest.StartupTimeoutSeconds} second startup deadline). " +
            $"Last reported readiness was {_hostLifecycle?.Readiness.ToString() ?? "unavailable"}.");
    }

    internal static bool IsReadyForBrowser(RuntimeLifecycleSnapshotDto? lifecycle)
        => lifecycle is
        {
            AcceptingWork: true,
            Readiness: RuntimeReadinessStatus.Ready,
            State: RuntimeLifecycleState.Ready
        };

    private async Task StopSessionAsync(SupervisorAction action, CancellationToken ct)
    {
        if (_sessionId is null)
            return;
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
        if (_startupFailed)
            hostOutcome = LifecycleShutdownOutcome.Failed;
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
            ResetSessionState();
        }
    }

    private async Task<bool> RequestHttpFallbackShutdownAsync(CancellationToken ct)
    {
        var token = LifecycleProtectedSecretStore.Read(_configuration.SecretPath);
        if (string.IsNullOrWhiteSpace(token) || _httpPort is null)
            return false;
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

    private async Task<LifecycleSupervisorMessageDto> HandleCommandAsync(LifecycleSupervisorMessageDto request)
    {
        var command = request.Command.ToLowerInvariant();
        return command switch
        {
            "status" => Response(request, true, status: CreateStatus()),
            "preflight" => PreflightResponse(request),
            "stop" => ActionResponse(request, SupervisorAction.Stop),
            "restart" => ActionResponse(request, SupervisorAction.Restart),
            "open" or "start" => await OpenResponseAsync(request).ConfigureAwait(false),
            _ => Response(request, false, $"Unknown supervisor command '{request.Command}'.")
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
        var accepted = RequestAction(action, request.RequestId);
        var message = accepted
            ? $"Meridian {action.ToString().ToLowerInvariant()} requested."
            : "Meridian is already stopping; a restart cannot be queued.";
        return Response(request, accepted, message, CreateStatus());
    }

    private async Task<LifecycleSupervisorMessageDto> OpenResponseAsync(
        LifecycleSupervisorMessageDto request)
    {
        var operationRequest = LifecycleStartupOutcome.CreateRequest(
            _configuration,
            request.RequestId,
            browserRequested: true);
        var launchImmediately = false;
        Task<LifecycleStartupOutcomeReceipt>? pendingOutcome = null;
        lock (_gate)
        {
            launchImmediately = _readinessGatePersisted && IsReadyForBrowser(_hostLifecycle);
            if (!launchImmediately)
            {
                if (_pendingOpenRequests.TryGetValue(operationRequest.RequestId, out var existing))
                    operationRequest = existing;
                else
                    _pendingOpenRequests.Add(operationRequest.RequestId, operationRequest);
                pendingOutcome = _openOutcomeGate.WaitAsync(
                    operationRequest.RequestId,
                    TimeSpan.FromSeconds(_configuration.Manifest.StartupTimeoutSeconds + 15));
            }
        }

        if (launchImmediately)
        {
            var launch = CompleteReadyOpenRequest(operationRequest);
            return Response(
                request,
                launch.Receipt is not null && LifecycleOpenOutcomeGate.IsSuccessful(launch.Receipt),
                launch.Opened
                    ? $"Meridian opened after exact Ready status was verified. Verified outcome: {launch.ReceiptPath}"
                    : $"Meridian is ready, but the browser did not open. Open {launch.Destination} manually. " +
                      $"Verified outcome: {launch.ReceiptPath}",
                CreateStatus(),
                launch.Receipt?.Outcome.State.ToString(),
                launch.ReceiptPath);
        }

        try
        {
            var receipt = await pendingOutcome!.ConfigureAwait(false);
            return Response(
                request,
                LifecycleOpenOutcomeGate.IsSuccessful(receipt),
                $"Meridian open request reached terminal state {receipt.Outcome.State}. " +
                $"Verified outcome: {receipt.ReceiptPath}",
                CreateStatus(),
                receipt.Outcome.State.ToString(),
                receipt.ReceiptPath);
        }
        catch (TimeoutException)
        {
            bool removedWhilePending;
            lock (_gate)
                removedWhilePending = _pendingOpenRequests.Remove(operationRequest.RequestId);
            if (!removedWhilePending)
            {
                try
                {
                    var completedReceipt = await _openOutcomeGate.WaitAsync(
                        operationRequest.RequestId,
                        TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    return Response(
                        request,
                        LifecycleOpenOutcomeGate.IsSuccessful(completedReceipt),
                        $"Meridian open request reached terminal state {completedReceipt.Outcome.State}. " +
                        $"Verified outcome: {completedReceipt.ReceiptPath}",
                        CreateStatus(),
                        completedReceipt.Outcome.State.ToString(),
                        completedReceipt.ReceiptPath);
                }
                catch (TimeoutException)
                {
                    operationRequest = operationRequest with
                    {
                        AttemptNumber = checked(operationRequest.AttemptNumber + 1)
                    };
                }
            }
            _openOutcomeGate.Remove(operationRequest.RequestId);
            var terminalMessage =
                $"Meridian open request {operationRequest.RequestId} did not reach exact Ready status " +
                $"within {_configuration.Manifest.StartupTimeoutSeconds + 15} seconds.";
            AppendSupervisorLog(terminalMessage);
            var receipt = TryPersistStartupOutcome(
                operationRequest,
                OperationTerminalState.Failed,
                readinessSatisfied: false,
                terminalMessage: terminalMessage,
                browserRequested: true,
                browserOpened: false,
                exceptionType: nameof(TimeoutException));
            return Response(
                request,
                false,
                $"{terminalMessage} Inspect {_configuration.SupervisorLogPath} and retry the open command. " +
                $"Verified outcome: {receipt?.ReceiptPath ?? "unavailable"}",
                CreateStatus(),
                OperationTerminalState.Failed.ToString(),
                receipt?.ReceiptPath);
        }
    }

    private static LifecycleSupervisorMessageDto Response(
        LifecycleSupervisorMessageDto request,
        bool success,
        string? message = null,
        LifecycleSupervisorStatusDto? status = null,
        string? reason = null,
        string? detail = null)
        => new()
        {
            Command = $"{request.Command}-result",
            RequestId = request.RequestId,
            Success = success,
            Message = message,
            Status = status,
            Reason = reason,
            Detail = detail
        };

    private void HandleHostStatus(LifecycleSupervisorMessageDto message)
    {
        LifecycleStartupOperationRequest[] readyRequests = [];
        lock (_gate)
        {
            _hostLifecycle = message.Lifecycle;
            _hostSessionId = message.SessionId ?? message.Lifecycle?.SessionId;
            if (_readinessGatePersisted &&
                IsReadyForBrowser(_hostLifecycle) &&
                _pendingOpenRequests.Count > 0)
            {
                readyRequests = _pendingOpenRequests.Values.ToArray();
                _pendingOpenRequests.Clear();
            }
        }
        PersistStatus();
        foreach (var request in readyRequests)
            CompleteReadyOpenRequest(request);
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
        if (_sessionId is null)
            return;
        AtomicJsonFile.Write(
            Path.Combine(_configuration.RuntimeRoot, "supervisor-session.json"),
            JsonSerializer.SerializeToUtf8Bytes(
                CreateStatus(),
                LifecycleContractsJsonContext.Default.LifecycleSupervisorStatusDto));
    }

    private void DeleteRuntimeStatus()
    {
        var path = Path.Combine(_configuration.RuntimeRoot, "supervisor-session.json");
        if (File.Exists(path))
            File.Delete(path);
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
        if (!Directory.Exists(_configuration.ReceiptRoot))
            return null;
        var latest = Directory.EnumerateFiles(_configuration.ReceiptRoot, "session-*.json")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();
        if (latest is null)
            return null;
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
        if (string.IsNullOrWhiteSpace(hostSessionId))
            return null;
        var path = Path.Combine(_configuration.ReceiptRoot, $"host-{hostSessionId}.json");
        if (!File.Exists(path))
            return null;
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

    private BrowserLaunchResult CompleteReadyOpenRequest(LifecycleStartupOperationRequest request)
    {
        const string readinessMessage =
            "Exact Ready status was verified and retained before the browser launch attempt.";
        var readinessReceipt = TryPersistStartupOutcome(
            request,
            OperationTerminalState.Succeeded,
            readinessSatisfied: true,
            terminalMessage: readinessMessage,
            browserRequested: true,
            browserOpened: false,
            readinessGateReceipt: true);
        if (readinessReceipt is not null)
            return OpenBrowserWithOutcome(request);

        const string failureMessage =
            "The request-specific readiness receipt could not be retained; the workstation was not opened.";
        var failureReceipt = TryPersistStartupOutcome(
            request,
            OperationTerminalState.Failed,
            readinessSatisfied: false,
            terminalMessage: failureMessage,
            browserRequested: true,
            browserOpened: false,
            exceptionType: nameof(IOException));
        return new BrowserLaunchResult(
            false,
            string.Empty,
            failureReceipt?.ReceiptPath,
            failureMessage,
            failureReceipt);
    }

    private BrowserLaunchResult OpenBrowserWithOutcome(LifecycleStartupOperationRequest request)
    {
        if (_httpPort is null)
        {
            const string message = "The workstation URL is unavailable because no HTTP port was assigned.";
            AppendSupervisorLog(message);
            var missingUrlReceipt = TryPersistStartupOutcome(
                request,
                OperationTerminalState.CompletedWithWarnings,
                readinessSatisfied: true,
                terminalMessage: message,
                browserRequested: true,
                browserOpened: false);
            return new BrowserLaunchResult(
                false,
                string.Empty,
                missingUrlReceipt?.ReceiptPath,
                message,
                missingUrlReceipt);
        }
        var accountStore = Path.Combine(_configuration.DataRoot, "governance", "user-accounts.json");
        var hasAccount = File.Exists(accountStore);
        var safeDestination = hasAccount
            ? $"http://127.0.0.1:{_httpPort}/login?returnUrl=%2Fworkstation%2F"
            : $"http://127.0.0.1:{_httpPort}/setup/account";
        var launchDestination = hasAccount
            ? safeDestination
            : $"{safeDestination}#token={Uri.EscapeDataString(_bootstrapToken ?? string.Empty)}";
        try
        {
            using var browserProcess = Process.Start(
                new ProcessStartInfo(launchDestination) { UseShellExecute = true });
            if (browserProcess is null)
                throw new InvalidOperationException("The operating system did not accept the browser launch request.");

            var message = "The workstation URL was handed to the operating system after exact Ready status was verified.";
            AppendSupervisorLog(message);
            var receipt = TryPersistStartupOutcome(
                request,
                OperationTerminalState.Succeeded,
                readinessSatisfied: true,
                terminalMessage: message,
                browserRequested: true,
                browserOpened: true,
                browserUri: safeDestination);
            return new BrowserLaunchResult(
                true,
                safeDestination,
                receipt?.ReceiptPath,
                message,
                receipt);
        }
        catch (Exception ex)
        {
            var message = $"Browser launch failed ({ex.GetType().Name}): {ex.Message} " +
                          $"Retry the open command or open {safeDestination} manually.";
            AppendSupervisorLog(message);
            var receipt = TryPersistStartupOutcome(
                request,
                OperationTerminalState.CompletedWithWarnings,
                readinessSatisfied: true,
                terminalMessage: message,
                browserRequested: true,
                browserOpened: false,
                browserUri: safeDestination,
                exceptionType: ex.GetType().Name);
            Console.Error.WriteLine(message);
            if (receipt is not null)
                Console.Error.WriteLine($"Verified outcome: {receipt.ReceiptPath}");
            return new BrowserLaunchResult(
                false,
                safeDestination,
                receipt?.ReceiptPath,
                message,
                receipt);
        }
    }

    private void PersistOutstandingStartupOutcomes(
        OperationTerminalState state,
        bool readinessSatisfied,
        string terminalMessage,
        string? exceptionType = null)
    {
        LifecycleStartupOperationRequest[] requests;
        lock (_gate)
        {
            requests = (_sessionStartupRequest is null
                    ? _pendingOpenRequests.Values
                    : _pendingOpenRequests.Values.Prepend(_sessionStartupRequest))
                .DistinctBy(static item => item.RequestId, StringComparer.Ordinal)
                .ToArray();
            _pendingOpenRequests.Clear();
        }

        foreach (var request in requests)
        {
            TryPersistStartupOutcome(
                request,
                state,
                readinessSatisfied,
                terminalMessage,
                browserRequested: request.BrowserRequested,
                browserOpened: false,
                exceptionType: exceptionType);
        }
    }

    private LifecycleStartupOutcomeReceipt? TryPersistStartupOutcome(
        LifecycleStartupOperationRequest request,
        OperationTerminalState state,
        bool readinessSatisfied,
        string terminalMessage,
        bool browserRequested = false,
        bool browserOpened = false,
        string? browserUri = null,
        string? exceptionType = null,
        bool readinessGateReceipt = false)
    {
        if (_sessionId is null || _startedAtUtc is null)
            return null;
        try
        {
            var receipt = LifecycleStartupOutcome.Persist(
                _configuration,
                request,
                _sessionId,
                _startedAtUtc.Value,
                state,
                _preflightSucceeded,
                readinessSatisfied,
                terminalMessage,
                _httpPort,
                browserRequested,
                browserOpened,
                browserUri,
                exceptionType,
                readinessGateReceipt);
            _latestStartupOutcomeReceipt = receipt;
            if (!readinessGateReceipt)
                _openOutcomeGate.Complete(request.RequestId, receipt);
            AppendSupervisorLog(
                $"Verified startup outcome {state} retained at {receipt.ReceiptPath}.");
            return receipt;
        }
        catch (Exception ex)
        {
            var message =
                $"Verified startup outcome persistence failed ({ex.GetType().Name}): {ex.Message}";
            AppendSupervisorLog(message);
            Console.Error.WriteLine(message);
            return null;
        }
    }

    private void WriteTerminalDiagnostics(OperationTerminalState state)
    {
        Console.Error.WriteLine($"Meridian startup terminal state: {state}.");
        Console.Error.WriteLine(_message);
        if (_latestStartupOutcomeReceipt is not null)
            Console.Error.WriteLine($"Verified outcome: {_latestStartupOutcomeReceipt.ReceiptPath}");
        Console.Error.WriteLine($"Supervisor log: {_configuration.SupervisorLogPath}");
        Console.Error.WriteLine($"Host logs: {_configuration.HostLogRoot}");
        Console.Error.WriteLine($"PostgreSQL log: {_configuration.DatabaseLogPath}");
        Console.Error.WriteLine(
            "Recovery: repair the reported condition, run Meridian.LifecycleSupervisor preflight, " +
            "then retry Meridian.LifecycleSupervisor start.");
    }

    private void AppendSupervisorLog(string message)
    {
        try
        {
            lock (_logGate)
            {
                var directory = Path.GetDirectoryName(_configuration.SupervisorLogPath)
                    ?? throw new InvalidOperationException("The supervisor log requires a parent directory.");
                Directory.CreateDirectory(directory);
                using var stream = new FileStream(
                    _configuration.SupervisorLogPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read,
                    4096,
                    FileOptions.WriteThrough);
                using var writer = new StreamWriter(stream);
                writer.WriteLine($"{DateTimeOffset.UtcNow:O} {SanitizeDiagnostic(message)}");
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Lifecycle supervisor could not append its diagnostic log ({ex.GetType().Name}).");
        }
    }

    private void ResetSessionState()
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
        _preflightSucceeded = false;
        _startupFailed = false;
        _readinessGatePersisted = false;
        lock (_gate)
        {
            _sessionStartupRequest = null;
            _pendingOpenRequests.Clear();
        }
    }

    private static string SanitizeDiagnostic(string message)
    {
        var sanitized = message
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return sanitized.Length <= 2000 ? sanitized : sanitized[..2000];
    }

    private bool RequestAction(SupervisorAction action, string? requestId = null)
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

            if (_stopRequested)
                return false;
            if (action == SupervisorAction.Restart)
            {
                _restartRequested = true;
                _restartStartupRequestId = LifecycleStartupOutcome.NormalizeRequestId(requestId);
            }
            _requestedAction.TrySetResult(action);
            return true;
        }
    }

    private string? TakeRestartStartupRequestId()
    {
        lock (_gate)
        {
            var requestId = _restartStartupRequestId;
            _restartStartupRequestId = null;
            return requestId;
        }
    }

    private bool ShouldRestartAfterStop(SupervisorAction action)
    {
        lock (_gate)
        {
            if (_stopRequested)
                return false;
            var shouldRestart = action == SupervisorAction.Restart || _restartRequested;
            if (shouldRestart)
                _restartRequested = false;
            return shouldRestart;
        }
    }

    private static TaskCompletionSource<SupervisorAction> NewActionSource()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        { return ((IPEndPoint)listener.LocalEndpoint).Port; }
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
        if (identity is null)
            return false;
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

internal sealed record BrowserLaunchResult(
    bool Opened,
    string Destination,
    string? ReceiptPath,
    string Message,
    LifecycleStartupOutcomeReceipt? Receipt);

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
        if (!File.Exists(path))
            return null;
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
        if (File.Exists(path))
            File.Delete(path);
    }
}
