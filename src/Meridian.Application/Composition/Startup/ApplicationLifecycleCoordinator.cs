using Meridian.Contracts.Lifecycle;
using Serilog;

namespace Meridian.Application.Composition.Startup;

/// <summary>
/// Compatibility process-lifetime contract retained while callers migrate to
/// <see cref="IRuntimeLifecycleControlPlane"/>.
/// </summary>
public interface IApplicationLifecycleCoordinator : IDisposable
{
    DateTimeOffset StartedAtUtc { get; }
    bool IsShutdownRequested { get; }
    string? ShutdownReason { get; }
    CancellationToken ShutdownToken { get; }
    string? LocalShutdownToken { get; }

    Task RequestShutdownAsync(string reason, string? detail = null, CancellationToken ct = default);
}

/// <summary>
/// Authoritative in-process lifecycle state, readiness, and shutdown-operation contract.
/// </summary>
public interface IRuntimeLifecycleControlPlane
{
    RuntimeLifecycleSnapshotDto Snapshot { get; }
    CancellationToken StopWorkToken { get; }
    CancellationToken TerminationToken { get; }
    LifecycleShutdownOperationDto? ActiveShutdownOperation { get; }
    LifecycleShutdownReceiptDto? LatestShutdownReceipt { get; }

    void TransitionTo(RuntimeLifecycleState state, string activePhase);

    void UpdateReadiness(
        RuntimeReadinessStatus readiness,
        IReadOnlyList<RuntimeLifecycleCheckDto> checks);

    ValueTask<LifecycleShutdownAcceptedDto> RequestShutdownAsync(
        LifecycleShutdownRequestDto request,
        CancellationToken ct = default);

    void AdvanceShutdown(
        LifecycleShutdownStage stage,
        LifecycleShutdownOutcome outcome = LifecycleShutdownOutcome.Pending,
        string? message = null);

    void CompleteShutdown(LifecycleShutdownReceiptDto receipt);
    void SignalTermination();
}

/// <summary>
/// Single process lifetime owner used by shared startup orchestration.
/// </summary>
public sealed class ApplicationLifecycleCoordinator : IApplicationLifecycleCoordinator, IRuntimeLifecycleControlPlane
{
    public const string LocalShutdownTokenEnvironmentVariable = "MDC_SHUTDOWN_TOKEN";
    private static readonly TimeSpan DefaultShutdownDeadline = TimeSpan.FromSeconds(45);

    private readonly object _gate = new();
    private readonly ILogger _log;
    private readonly CancellationTokenSource _stopWorkCts;
    private readonly CancellationTokenSource _terminationCts = new();
    private readonly CancellationTokenRegistration _externalShutdownRegistration;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private RuntimeLifecycleState _state = RuntimeLifecycleState.Created;
    private RuntimeReadinessStatus _readiness = RuntimeReadinessStatus.Starting;
    private DateTimeOffset _stateChangedAtUtc;
    private string _activePhase = "created";
    private IReadOnlyList<RuntimeLifecycleCheckDto> _checks = [];
    private LifecycleShutdownOperationDto? _activeShutdownOperation;
    private LifecycleShutdownReceiptDto? _latestShutdownReceipt;
    private int _shutdownRequested;
    private bool _disposed;

    private ApplicationLifecycleCoordinator(
        ILogger log,
        CancellationToken externalToken,
        string? localShutdownToken)
    {
        _log = log.ForContext<ApplicationLifecycleCoordinator>();
        _stopWorkCts = new CancellationTokenSource();
        LocalShutdownToken = string.IsNullOrWhiteSpace(localShutdownToken)
            ? null
            : localShutdownToken;
        StartedAtUtc = DateTimeOffset.UtcNow;
        _stateChangedAtUtc = StartedAtUtc;

        if (externalToken.CanBeCanceled)
        {
            _externalShutdownRegistration = externalToken.Register(() =>
                RequestShutdownFromSignal(
                    LifecycleShutdownReason.ExternalCancellation,
                    "External cancellation requested shutdown"));
        }

        Console.CancelKeyPress += OnConsoleCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public DateTimeOffset StartedAtUtc { get; }

    public bool IsShutdownRequested => Volatile.Read(ref _shutdownRequested) != 0;

    public string? ShutdownReason { get; private set; }

    /// <summary>
    /// Compatibility token. New code should use <see cref="StopWorkToken"/> and
    /// <see cref="TerminationToken"/> explicitly.
    /// </summary>
    public CancellationToken ShutdownToken => StopWorkToken;

    public CancellationToken StopWorkToken => _stopWorkCts.Token;

    public CancellationToken TerminationToken => _terminationCts.Token;

    public string? LocalShutdownToken { get; }

    public RuntimeLifecycleSnapshotDto Snapshot
    {
        get
        {
            lock (_gate)
            {
                return CreateSnapshotUnsafe();
            }
        }
    }

    public LifecycleShutdownOperationDto? ActiveShutdownOperation
    {
        get
        {
            lock (_gate)
            {
                return _activeShutdownOperation;
            }
        }
    }

    public LifecycleShutdownReceiptDto? LatestShutdownReceipt
    {
        get
        {
            lock (_gate)
            {
                return _latestShutdownReceipt;
            }
        }
    }

    public static ApplicationLifecycleCoordinator Create(ILogger log, CancellationToken externalToken = default)
        => new(log, externalToken, Environment.GetEnvironmentVariable(LocalShutdownTokenEnvironmentVariable));

    public void TransitionTo(RuntimeLifecycleState state, string activePhase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activePhase);

        lock (_gate)
        {
            if (IsShutdownRequested && state < RuntimeLifecycleState.ShutdownRequested)
            {
                return;
            }

            _state = state;
            _activePhase = activePhase;
            _stateChangedAtUtc = DateTimeOffset.UtcNow;
            _readiness = state switch
            {
                RuntimeLifecycleState.Ready => RuntimeReadinessStatus.Ready,
                RuntimeLifecycleState.Degraded => RuntimeReadinessStatus.Degraded,
                RuntimeLifecycleState.ShutdownRequested or
                RuntimeLifecycleState.Draining or
                RuntimeLifecycleState.Flushing or
                RuntimeLifecycleState.StoppingHost or
                RuntimeLifecycleState.Stopped => RuntimeReadinessStatus.Stopping,
                RuntimeLifecycleState.Failed => RuntimeReadinessStatus.Failed,
                _ => RuntimeReadinessStatus.Starting
            };
        }
    }

    public void UpdateReadiness(
        RuntimeReadinessStatus readiness,
        IReadOnlyList<RuntimeLifecycleCheckDto> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        lock (_gate)
        {
            _checks = checks.ToArray();
            if (IsShutdownRequested || _state is RuntimeLifecycleState.Stopped or RuntimeLifecycleState.Failed)
            {
                return;
            }

            _readiness = readiness;
            _state = readiness switch
            {
                RuntimeReadinessStatus.Ready => RuntimeLifecycleState.Ready,
                RuntimeReadinessStatus.Degraded => RuntimeLifecycleState.Degraded,
                RuntimeReadinessStatus.Failed => RuntimeLifecycleState.Failed,
                _ => RuntimeLifecycleState.EvaluatingReadiness
            };
            _activePhase = readiness switch
            {
                RuntimeReadinessStatus.Ready => "ready",
                RuntimeReadinessStatus.Degraded => "degraded",
                RuntimeReadinessStatus.Failed => "failed",
                _ => "evaluating-readiness"
            };
            _stateChangedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public Task RequestShutdownAsync(string reason, string? detail = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var typedReason = ParseCompatibilityReason(reason);
        return RequestShutdownCoreAsync(
            new LifecycleShutdownRequestDto
            {
                Reason = typedReason,
                Detail = detail,
                RequestedBy = reason
            },
            ct).AsTask();
    }

    public ValueTask<LifecycleShutdownAcceptedDto> RequestShutdownAsync(
        LifecycleShutdownRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RequestShutdownCoreAsync(request, ct);
    }

    public void AdvanceShutdown(
        LifecycleShutdownStage stage,
        LifecycleShutdownOutcome outcome = LifecycleShutdownOutcome.Pending,
        string? message = null)
    {
        lock (_gate)
        {
            if (_activeShutdownOperation is null)
            {
                throw new InvalidOperationException("A shutdown operation has not been requested.");
            }

            var now = DateTimeOffset.UtcNow;
            var stages = _activeShutdownOperation.Stages.ToList();
            if (stages.Count > 0 && stages[^1].CompletedAtUtc is null)
            {
                stages[^1] = stages[^1] with
                {
                    CompletedAtUtc = now,
                    Outcome = stages[^1].Outcome == LifecycleShutdownOutcome.Pending
                        ? LifecycleShutdownOutcome.Succeeded
                        : stages[^1].Outcome
                };
            }

            stages.Add(new LifecycleShutdownStageDto
            {
                Stage = stage,
                Outcome = outcome,
                StartedAtUtc = now,
                Message = message
            });

            _activeShutdownOperation = _activeShutdownOperation with
            {
                CurrentStage = stage,
                Outcome = outcome,
                Stages = stages
            };

            _state = stage switch
            {
                LifecycleShutdownStage.StopAcceptingWork or LifecycleShutdownStage.Requested => RuntimeLifecycleState.ShutdownRequested,
                LifecycleShutdownStage.Draining => RuntimeLifecycleState.Draining,
                LifecycleShutdownStage.Flushing or LifecycleShutdownStage.PersistingReceipt => RuntimeLifecycleState.Flushing,
                LifecycleShutdownStage.ReleasingHost => RuntimeLifecycleState.StoppingHost,
                LifecycleShutdownStage.Completed => RuntimeLifecycleState.Stopped,
                LifecycleShutdownStage.Failed => RuntimeLifecycleState.Failed,
                _ => _state
            };
            _readiness = stage == LifecycleShutdownStage.Failed
                ? RuntimeReadinessStatus.Failed
                : RuntimeReadinessStatus.Stopping;
            _activePhase = ToPhaseName(stage);
            _stateChangedAtUtc = now;
        }
    }

    public void CompleteShutdown(LifecycleShutdownReceiptDto receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        lock (_gate)
        {
            if (_activeShutdownOperation is null ||
                !string.Equals(_activeShutdownOperation.OperationId, receipt.OperationId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The shutdown receipt does not match the active operation.");
            }

            var now = receipt.CompletedAtUtc;
            var stages = _activeShutdownOperation.Stages.ToList();
            if (stages.Count > 0)
            {
                stages[^1] = stages[^1] with
                {
                    CompletedAtUtc = now,
                    Outcome = receipt.Outcome
                };
            }

            _latestShutdownReceipt = receipt;
            _activeShutdownOperation = _activeShutdownOperation with
            {
                CurrentStage = receipt.Outcome is LifecycleShutdownOutcome.Failed or LifecycleShutdownOutcome.TimedOut
                    ? LifecycleShutdownStage.Failed
                    : LifecycleShutdownStage.Completed,
                Outcome = receipt.Outcome,
                CompletedAtUtc = now,
                Stages = stages
            };
            _state = receipt.Outcome is LifecycleShutdownOutcome.Failed or LifecycleShutdownOutcome.TimedOut
                ? RuntimeLifecycleState.Failed
                : RuntimeLifecycleState.StoppingHost;
            _readiness = receipt.Outcome == LifecycleShutdownOutcome.Failed
                ? RuntimeReadinessStatus.Failed
                : RuntimeReadinessStatus.Stopping;
            _activePhase = "shutdown-complete";
            _stateChangedAtUtc = now;
        }
    }

    public void SignalTermination()
    {
        try
        {
            _terminationCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            _log.Debug("Application lifecycle termination source was already disposed");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Console.CancelKeyPress -= OnConsoleCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        _externalShutdownRegistration.Dispose();
        _stopWorkCts.Dispose();
        _terminationCts.Dispose();
    }

    private ValueTask<LifecycleShutdownAcceptedDto> RequestShutdownCoreAsync(
        LifecycleShutdownRequestDto request,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        LifecycleShutdownAcceptedDto response;
        var shouldCancel = false;

        lock (_gate)
        {
            if (_activeShutdownOperation is null)
            {
                var now = DateTimeOffset.UtcNow;
                var operationId = Guid.NewGuid().ToString("N");
                _activeShutdownOperation = new LifecycleShutdownOperationDto
                {
                    OperationId = operationId,
                    Reason = request.Reason,
                    Detail = request.Detail,
                    RequestedBy = request.RequestedBy,
                    CurrentStage = LifecycleShutdownStage.Requested,
                    Outcome = LifecycleShutdownOutcome.Pending,
                    RequestedAtUtc = now,
                    DeadlineUtc = now.Add(DefaultShutdownDeadline),
                    Stages =
                    [
                        new LifecycleShutdownStageDto
                        {
                            Stage = LifecycleShutdownStage.Requested,
                            Outcome = LifecycleShutdownOutcome.Pending,
                            StartedAtUtc = now,
                            Message = request.Detail
                        }
                    ]
                };
                Interlocked.Exchange(ref _shutdownRequested, 1);
                ShutdownReason = request.Reason.ToString();
                _state = RuntimeLifecycleState.ShutdownRequested;
                _readiness = RuntimeReadinessStatus.Stopping;
                _activePhase = "shutdown-requested";
                _stateChangedAtUtc = now;
                shouldCancel = true;

                _log.Information(
                    "Application shutdown requested ({Reason}); operationId={OperationId}; requestedBy={RequestedBy}; detail={Detail}",
                    request.Reason,
                    operationId,
                    request.RequestedBy,
                    request.Detail);
            }
            else
            {
                _log.Debug(
                    "Application shutdown request resolved to active operation {OperationId} ({Reason})",
                    _activeShutdownOperation.OperationId,
                    request.Reason);
            }

            response = new LifecycleShutdownAcceptedDto
            {
                Accepted = true,
                OperationId = _activeShutdownOperation.OperationId,
                OperationUri = $"/api/system/shutdown/{_activeShutdownOperation.OperationId}",
                State = _state,
                RequestedAtUtc = _activeShutdownOperation.RequestedAtUtc
            };
        }

        if (shouldCancel)
        {
            try
            {
                _stopWorkCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                _log.Debug("Application lifecycle stop-work source was already disposed");
            }
        }

        return ValueTask.FromResult(response);
    }

    private RuntimeLifecycleSnapshotDto CreateSnapshotUnsafe()
        => new()
        {
            SessionId = _sessionId,
            State = _state,
            Readiness = _readiness,
            StartedAtUtc = StartedAtUtc,
            StateChangedAtUtc = _stateChangedAtUtc,
            ActivePhase = _activePhase,
            AcceptingWork = _readiness is RuntimeReadinessStatus.Ready or RuntimeReadinessStatus.Degraded && !IsShutdownRequested,
            ShutdownRequested = IsShutdownRequested,
            ShutdownReason = ShutdownReason,
            ActiveShutdownOperationId = _activeShutdownOperation?.OperationId,
            UptimeSeconds = Math.Max(0, (DateTimeOffset.UtcNow - StartedAtUtc).TotalSeconds),
            Checks = _checks
        };

    private void RequestShutdownFromSignal(LifecycleShutdownReason reason, string detail)
    {
        try
        {
            _ = RequestShutdownCoreAsync(
                new LifecycleShutdownRequestDto { Reason = reason, Detail = detail },
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Unable to record lifecycle shutdown signal {Reason}", reason);
        }
    }

    private void OnConsoleCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        RequestShutdownFromSignal(
            LifecycleShutdownReason.ConsoleCancel,
            "Ctrl+C or console close requested shutdown");
    }

    private void OnProcessExit(object? sender, EventArgs e)
        => RequestShutdownFromSignal(
            LifecycleShutdownReason.ProcessExit,
            "ProcessExit requested shutdown");

    private static LifecycleShutdownReason ParseCompatibilityReason(string reason)
        => reason switch
        {
            "console-cancel" => LifecycleShutdownReason.ConsoleCancel,
            "external-cancellation" => LifecycleShutdownReason.ExternalCancellation,
            "process-exit" => LifecycleShutdownReason.ProcessExit,
            "http-local-shutdown" => LifecycleShutdownReason.HttpLocalShutdown,
            "restart" => LifecycleShutdownReason.Restart,
            "supervisor" => LifecycleShutdownReason.Supervisor,
            "startup-failure" => LifecycleShutdownReason.StartupFailure,
            _ => LifecycleShutdownReason.Operator
        };

    private static string ToPhaseName(LifecycleShutdownStage stage)
        => stage switch
        {
            LifecycleShutdownStage.StopAcceptingWork => "stop-accepting-work",
            LifecycleShutdownStage.PersistingReceipt => "persisting-receipt",
            LifecycleShutdownStage.ReleasingHost => "releasing-host",
            _ => stage.ToString().ToLowerInvariant()
        };
}
