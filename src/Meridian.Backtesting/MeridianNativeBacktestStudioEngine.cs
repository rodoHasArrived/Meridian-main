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
    private readonly BacktestEngine _engine;
    private readonly ILogger<MeridianNativeBacktestStudioEngine> _logger;
    private readonly ConcurrentDictionary<string, NativeRunRegistration> _runs = new(StringComparer.Ordinal);

    public MeridianNativeBacktestStudioEngine(
        BacktestEngine engine,
        ILogger<MeridianNativeBacktestStudioEngine> logger)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public StrategyRunEngine Engine => StrategyRunEngine.MeridianNative;

    public Task<BacktestStudioRunHandle> StartAsync(BacktestStudioRunRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

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

        var registration = Resolve(runHandle);
        return Task.FromResult(registration.ToStatus());
    }

    public Task<BacktestResult> GetCanonicalResultAsync(string runHandle, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runHandle);

        var registration = Resolve(runHandle);
        return registration.Result.Task.WaitAsync(ct);
    }

    private async Task ExecuteAsync(BacktestStudioRunRequest request, NativeRunRegistration registration)
    {
        var progress = new Progress<BacktestProgressEvent>(evt => registration.UpdateProgress(evt));

        try
        {
            var result = await _engine.RunAsync(
                    request.NativeRequest,
                    request.Strategy!,
                    progress,
                    CancellationToken.None)
                .ConfigureAwait(false);

            registration.Complete(result with { EngineMetadata = new BacktestEngineMetadata("MeridianNative") });
        }
        catch (OperationCanceledException)
        {
            registration.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Native Backtest Studio run {RunId} failed.", registration.RunId);
            registration.Fail(ex);
        }
    }

    private NativeRunRegistration Resolve(string runHandle)
    {
        if (_runs.TryGetValue(runHandle, out var registration))
            return registration;

        throw new InvalidOperationException($"Native Backtest Studio run '{runHandle}' was not found.");
    }

    private sealed class NativeRunRegistration
    {
        private readonly object _gate = new();
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

        public void Cancel()
        {
            lock (_gate)
            {
                _status = StrategyRunStatus.Cancelled;
                _message = "Cancelled";
            }

            Result.TrySetCanceled();
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
    }
}
