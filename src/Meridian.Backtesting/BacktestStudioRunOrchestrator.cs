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
public sealed class BacktestStudioRunOrchestrator
{
    private readonly IStrategyRepository _repository;
    private readonly ILogger<BacktestStudioRunOrchestrator> _logger;
    private readonly IReadOnlyDictionary<StrategyRunEngine, IBacktestStudioEngine> _engines;
    private readonly ConcurrentDictionary<string, BacktestStudioRunHandle> _runHandles = new(StringComparer.Ordinal);

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

        _ = MonitorRunCompletionAsync(entry, handle, engine);

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

    private async Task MonitorRunCompletionAsync(
        StrategyRunEntry initialEntry,
        BacktestStudioRunHandle handle,
        IBacktestStudioEngine engine)
    {
        try
        {
            var result = await engine.GetCanonicalResultAsync(handle.EngineRunHandle, CancellationToken.None).ConfigureAwait(false);
            var completed = initialEntry.Complete(result);
            await _repository.RecordRunAsync(completed, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await _repository.RecordRunAsync(initialEntry.Cancel(), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Backtest Studio run {RunId} failed while awaiting engine completion.", handle.RunId);
            await _repository.RecordRunAsync(initialEntry.Fail(), CancellationToken.None).ConfigureAwait(false);
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
