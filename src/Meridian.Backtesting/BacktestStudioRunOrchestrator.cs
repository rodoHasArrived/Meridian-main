using System.Collections.Concurrent;
using Meridian.Application.Backtesting;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;

namespace Meridian.Backtesting;

/// <summary>
/// Coordinates Backtest Studio runs across multiple engines while persisting them through the shared strategy-run model.
/// </summary>
public sealed class BacktestStudioRunOrchestrator : IAsyncDisposable
{
    private readonly IStrategyRepository _repository;
    private readonly ILogger<BacktestStudioRunOrchestrator> _logger;
    private readonly IReadOnlyDictionary<StrategyRunEngine, IBacktestStudioEngine> _engines;
    private readonly ConcurrentDictionary<string, BacktestStudioRunHandle> _runHandles = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task> _monitorTasks = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();

    public BacktestStudioRunOrchestrator(
        IStrategyRepository repository,
        IEnumerable<IBacktestStudioEngine> engines,
        ILogger<BacktestStudioRunOrchestrator> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(engines);

        _engines = engines.ToDictionary(static engine => engine.Engine);
    }

    /// <summary>
    /// Starts a Backtest Studio run, records the initial shared run entry, and monitors completion in the background.
    /// </summary>
    public async Task<BacktestStudioRunHandle> StartAsync(BacktestStudioRunRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_shutdown.IsCancellationRequested, this);

        var engine = ResolveEngine(request.Engine);
        var handle = await engine.StartAsync(request, ct).ConfigureAwait(false);

        var entry = StrategyRunEntry.Start(
            request.StrategyId,
            request.StrategyName,
            RunType.Backtest,
            runId: handle.RunId,
            datasetReference: request.DatasetReference,
            feedReference: request.FeedReference,
            engine: handle.Engine.ToString(),
            parameterSet: request.Parameters);

        await _repository.RecordRunAsync(entry, ct).ConfigureAwait(false);
        _runHandles[handle.RunId] = handle;

        var monitorCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, ct);
        var monitorTask = MonitorRunCompletionAsync(entry, handle, engine, monitorCts);
        if (!_monitorTasks.TryAdd(handle.RunId, monitorTask))
        {
            monitorCts.Cancel();
            monitorCts.Dispose();
            throw new InvalidOperationException($"Backtest Studio run '{handle.RunId}' is already being monitored.");
        }

        _logger.LogInformation(
            "Backtest Studio run started: {RunId} ({StrategyId}, engine {Engine})",
            handle.RunId,
            request.StrategyId,
            handle.Engine);

        return handle;
    }

    /// <summary>
    /// Returns the current status for a previously started Backtest Studio run.
    /// </summary>
    public Task<BacktestStudioRunStatus> GetStatusAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var handle = ResolveHandle(runId);
        var engine = ResolveEngine(handle.Engine);
        return engine.GetStatusAsync(handle.EngineRunHandle, ct);
    }

    /// <summary>
    /// Returns the canonical result for a previously started Backtest Studio run.
    /// </summary>
    public Task<BacktestResult> GetCanonicalResultAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var handle = ResolveHandle(runId);
        var engine = ResolveEngine(handle.Engine);
        return engine.GetCanonicalResultAsync(handle.EngineRunHandle, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_shutdown.IsCancellationRequested)
        {
            return;
        }

        _shutdown.Cancel();

        var monitors = _monitorTasks.Values.ToArray();
        try
        {
            await Task.WhenAll(monitors).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            _shutdown.Dispose();
        }
    }

    private async Task MonitorRunCompletionAsync(
        StrategyRunEntry initialEntry,
        BacktestStudioRunHandle handle,
        IBacktestStudioEngine engine,
        CancellationTokenSource monitorCts)
    {
        var waitToken = monitorCts.Token;
        var persistenceToken = _shutdown.Token;

        try
        {
            var result = await engine.GetCanonicalResultAsync(handle.EngineRunHandle, waitToken).ConfigureAwait(false);
            await TryPersistTerminalStateAsync(
                initialEntry.Complete(result),
                handle.RunId,
                terminalState: "completed",
                persistenceToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await TryPersistTerminalStateAsync(
                initialEntry.Cancel(),
                handle.RunId,
                terminalState: "cancelled",
                persistenceToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Backtest Studio run {RunId} failed while awaiting engine completion.", handle.RunId);
            await TryPersistTerminalStateAsync(
                initialEntry.Fail(),
                handle.RunId,
                terminalState: "failed",
                persistenceToken).ConfigureAwait(false);
        }
        finally
        {
            _monitorTasks.TryRemove(handle.RunId, out _);
            monitorCts.Dispose();
        }
    }

    private async Task TryPersistTerminalStateAsync(
        StrategyRunEntry entry,
        string runId,
        string terminalState,
        CancellationToken ct)
    {
        try
        {
            await _repository.RecordRunAsync(entry, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Backtest Studio run {RunId} reached terminal state '{TerminalState}', but persistence stopped during shutdown.",
                runId,
                terminalState);
        }
    }

    private IBacktestStudioEngine ResolveEngine(StrategyRunEngine engine)
    {
        if (_engines.TryGetValue(engine, out var resolved))
            return resolved;

        throw new InvalidOperationException($"Backtest Studio engine '{engine}' is not registered.");
    }

    private BacktestStudioRunHandle ResolveHandle(string runId)
    {
        if (_runHandles.TryGetValue(runId, out var handle))
            return handle;

        throw new InvalidOperationException($"Backtest Studio run '{runId}' is not being tracked by the orchestrator.");
    }
}
