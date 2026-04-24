using System.Collections.Concurrent;
using Meridian.Application.Backtesting;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Workstation;

namespace Meridian.Backtesting;

/// <summary>
/// Backtest Studio engine implementation backed by the native Meridian backtest engine.
/// </summary>
public sealed class MeridianNativeBacktestStudioEngine : IBacktestStudioEngine
{
    private static readonly TimeSpan DefaultTerminalRetention = TimeSpan.FromMinutes(10);
    private const int DefaultMaxTerminalHistory = 256;

    private readonly BacktestEngine _engine;
    private readonly ILogger<MeridianNativeBacktestStudioEngine> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _terminalRetention;
    private readonly int _maxTerminalHistory;
    private readonly ConcurrentDictionary<string, NativeRunRegistration> _runs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TerminalRunSnapshot> _terminalRuns = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<(string Handle, DateTimeOffset TerminalAt)> _terminalOrder = new();

    public MeridianNativeBacktestStudioEngine(
        BacktestEngine engine,
        ILogger<MeridianNativeBacktestStudioEngine> logger,
        TimeProvider? timeProvider = null,
        TimeSpan? terminalRetention = null,
        int? maxTerminalHistory = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _terminalRetention = terminalRetention ?? DefaultTerminalRetention;
        _maxTerminalHistory = maxTerminalHistory ?? DefaultMaxTerminalHistory;

        if (_terminalRetention < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(terminalRetention), "Terminal retention must be non-negative.");

        if (_maxTerminalHistory < 1)
            throw new ArgumentOutOfRangeException(nameof(maxTerminalHistory), "Terminal history size must be at least 1.");
    }

    public StrategyRunEngine Engine => StrategyRunEngine.MeridianNative;

    public Task<BacktestStudioRunHandle> StartAsync(BacktestStudioRunRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        if (request.Strategy is null)
            throw new InvalidOperationException("A native Backtest Studio run requires an IBacktestStrategy instance.");

        var runId = Guid.NewGuid().ToString("N");
        var engineRunHandle = Guid.NewGuid().ToString("N");
        var registration = new NativeRunRegistration(runId, engineRunHandle, DateTimeOffset.UtcNow);

        if (!_runs.TryAdd(engineRunHandle, registration))
            throw new InvalidOperationException($"Unable to track native backtest run '{engineRunHandle}'.");

        registration.SetRunning();
        _ = ExecuteAsync(request, registration);

        return Task.FromResult(new BacktestStudioRunHandle(runId, engineRunHandle, Engine));
    }

    public Task<BacktestStudioRunStatus> GetStatusAsync(string runHandle, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runHandle);
        PruneTerminalHistory();

        if (_runs.TryGetValue(runHandle, out var registration))
            return Task.FromResult(registration.ToStatus());

        if (_terminalRuns.TryGetValue(runHandle, out var terminal))
            return Task.FromResult(terminal.Status);

        throw new InvalidOperationException($"Native Backtest Studio run '{runHandle}' was not found.");
    }

    public Task<BacktestResult> GetCanonicalResultAsync(string runHandle, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runHandle);
        PruneTerminalHistory();

        if (_runs.TryGetValue(runHandle, out var registration))
            return registration.Result.Task.WaitAsync(ct);

        if (_terminalRuns.TryGetValue(runHandle, out var terminal))
            return terminal.Result(ct);

        throw new InvalidOperationException($"Native Backtest Studio run '{runHandle}' was not found.");
    }

    public Task CancelAsync(string runHandle, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runHandle);
        ct.ThrowIfCancellationRequested();

        var registration = Resolve(runHandle);
        registration.RequestCancellation();
        return Task.CompletedTask;
    }

    private async Task ExecuteAsync(BacktestStudioRunRequest request, NativeRunRegistration registration)
    {
        var progress = new Progress<BacktestProgressEvent>(evt => registration.UpdateProgress(evt));
        var runToken = registration.Token;

        try
        {
            var result = await _engine.RunAsync(
                    request.NativeRequest,
                    request.Strategy!,
                    progress,
                    runToken)
                .ConfigureAwait(false);

            registration.Complete(result with { EngineMetadata = new BacktestEngineMetadata("MeridianNative") });
            FinalizeTerminalRun(registration);
        }
        catch (OperationCanceledException ex)
        {
            registration.Cancel(ex.CancellationToken.CanBeCanceled ? ex.CancellationToken : runToken);
            FinalizeTerminalRun(registration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Native Backtest Studio run {RunId} failed.", registration.RunId);
            registration.Fail(ex);
            FinalizeTerminalRun(registration);
        }
    }

    private void FinalizeTerminalRun(NativeRunRegistration registration)
    {
        var terminalAt = _timeProvider.GetUtcNow();
        var snapshot = registration.ToTerminalSnapshot(terminalAt);

        _terminalRuns[registration.EngineRunHandle] = snapshot;
        _terminalOrder.Enqueue((registration.EngineRunHandle, terminalAt));
        _runs.TryRemove(registration.EngineRunHandle, out _);
        PruneTerminalHistory();
    }

    private void PruneTerminalHistory()
    {
        var now = _timeProvider.GetUtcNow();
        while (_terminalOrder.TryPeek(out var entry))
        {
            if (_terminalRuns.Count <= _maxTerminalHistory && now - entry.TerminalAt <= _terminalRetention)
                break;

            _terminalOrder.TryDequeue(out var dequeued);
            _terminalRuns.TryGetValue(dequeued.Handle, out var current);

            if (current is null || current.TerminalAt > dequeued.TerminalAt)
                continue;

            _terminalRuns.TryRemove(dequeued.Handle, out _);
        }
    }

    private sealed record TerminalRunSnapshot(
        BacktestStudioRunStatus Status,
        DateTimeOffset TerminalAt,
        Func<CancellationToken, Task<BacktestResult>> Result);

    private static Task<BacktestResult> CanceledResult(CancellationToken ct)
        => Task.FromCanceled<BacktestResult>(ct.CanBeCanceled ? ct : new CancellationToken(canceled: true));

    private static Task<BacktestResult> FailedResult(Exception ex)
        => Task.FromException<BacktestResult>(ex);

    private static Task<BacktestResult> CompletedResult(BacktestResult result)
        => Task.FromResult(result);

    private static Task<BacktestResult> CloneResultForCancellation(Task<BacktestResult> resultTask, CancellationToken ct)
        => ct.IsCancellationRequested
            ? Task.FromCanceled<BacktestResult>(ct)
            : resultTask;

    private static Func<CancellationToken, Task<BacktestResult>> CreateResultAccessor(Task<BacktestResult> resultTask)
    {
        if (resultTask.IsCanceled)
            return CanceledResult;

        if (resultTask.IsFaulted)
        {
            var exception = resultTask.Exception?.InnerException ?? resultTask.Exception ?? new InvalidOperationException("Run failed.");
            return _ => FailedResult(exception);
        }

        if (resultTask.IsCompletedSuccessfully)
        {
            var result = resultTask.Result;
            return _ => CompletedResult(result);
        }

        return ct => CloneResultForCancellation(resultTask, ct);
    }

    private sealed class NativeRunRegistration
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _runCancellation = new();
        private StrategyRunStatus _status = StrategyRunStatus.Pending;
        private double _progress;
        private string? _message;

        public NativeRunRegistration(string runId, string engineRunHandle, DateTimeOffset startedAt)
        {
            RunId = runId;
            EngineRunHandle = engineRunHandle;
            StartedAt = startedAt;
            Result = new TaskCompletionSource<BacktestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public string RunId { get; }

        public string EngineRunHandle { get; }

        public DateTimeOffset StartedAt { get; }

        public TaskCompletionSource<BacktestResult> Result { get; }

        public CancellationToken Token => _runCancellation.Token;

        public void SetRunning()
        {
            lock (_gate)
            {
                _status = StrategyRunStatus.Running;
                _progress = 0d;
                _message = "Running";
            }
        }

        public void UpdateProgress(BacktestProgressEvent evt)
        {
            lock (_gate)
            {
                _status = StrategyRunStatus.Running;
                _progress = Math.Clamp(evt.ProgressFraction, 0d, 1d);
                _message = evt.Message;
            }
        }

        public void Complete(BacktestResult result)
        {
            lock (_gate)
            {
                _status = StrategyRunStatus.Completed;
                _progress = 1d;
                _message = "Completed";
            }

            Result.TrySetResult(result);
        }

        public void RequestCancellation() => _runCancellation.Cancel();

        public void Cancel(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _status = StrategyRunStatus.Cancelled;
                _message = "Cancelled";
            }

            Result.TrySetCanceled(cancellationToken);
        }

        public void Fail(Exception ex)
        {
            lock (_gate)
            {
                _status = StrategyRunStatus.Failed;
                _message = ex.Message;
            }

            Result.TrySetException(ex);
        }

        public BacktestStudioRunStatus ToStatus()
        {
            lock (_gate)
            {
                return new BacktestStudioRunStatus(
                    RunId,
                    _status,
                    _progress,
                    StartedAt,
                    EstimatedCompletionAt: null,
                    Message: _message);
            }
        }

        public TerminalRunSnapshot ToTerminalSnapshot(DateTimeOffset terminalAt)
            => new(ToStatus(), terminalAt, CreateResultAccessor(Result.Task));
    }
}
