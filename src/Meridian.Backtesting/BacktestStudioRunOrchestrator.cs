using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

        if (!request.TryCreateRequiredEvidenceLoop(out var evidenceLoop, out var validationError))
        {
            throw new ArgumentException(validationError, nameof(request));
        }

        var computedSweepHash = ComputeSweepDefinitionHash(request);
        if (!string.IsNullOrWhiteSpace(request.SweepDefinitionHash) &&
            !string.Equals(request.SweepDefinitionHash, computedSweepHash, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Backtest Studio run request {StrategyId} supplied a sweep definition hash that did not match the canonical value. Persisting canonical hash.",
                request.StrategyId);
        }

        var engine = ResolveEngine(request.Engine);
        var handle = await engine.StartAsync(request, ct).ConfigureAwait(false);

        var entry = StrategyRunEntry.StartWithEvidence(
            request.StrategyId,
            request.StrategyName,
            RunType.Backtest,
            runId: handle.RunId,
            datasetReference: request.DatasetReference,
            feedReference: request.FeedReference,
            engine: handle.Engine.ToString(),
            parameterSet: request.Parameters,
            operatorAcceptanceCriteria: evidenceLoop.OperatorAcceptanceCriteria,
            retainedEvidenceReferences: evidenceLoop.RetainedEvidenceReferences,
            accountingRecordReferences: evidenceLoop.AccountingRecordReferences,
            approvalReferences: evidenceLoop.ApprovalReferences,
            paperValidationReferences: evidenceLoop.PaperValidationReferences,
            governedReportReferences: evidenceLoop.GovernedReportReferences) with
        {
            SweepId = request.SweepId,
            SweepDefinitionHash = computedSweepHash,
            SweepObjective = request.SweepObjective
        };

        await _repository.RecordRunAsync(entry, ct).ConfigureAwait(false);
        _runHandles[handle.RunId] = handle;

        var monitorCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
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

    /// <summary>
    /// Requests cancellation for a previously started Backtest Studio run.
    /// </summary>
    public Task CancelAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var handle = ResolveHandle(runId);
        var engine = ResolveEngine(handle.Engine);
        return engine.CancelAsync(handle.EngineRunHandle, ct);
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

    private static string? ComputeSweepDefinitionHash(BacktestStudioRunRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SweepId))
        {
            return null;
        }

        var builder = new StringBuilder();
        // A sweep definition binds invariant assumptions. Point values belong to ParameterSet
        // and its retained input hash, not the experiment key: including them split every grid.
        void Append(string? value) => builder.Append(value?.Length ?? 0).Append(':').Append(value).Append(';');
        Append(request.StrategyId);
        Append(request.Engine.ToString());
        Append(request.SweepObjective?.Trim());
        Append(request.DatasetReference);
        Append(request.FeedReference);
        Append(request.BenchmarkSymbol);
        Append(JsonSerializer.Serialize(request.NativeRequest, BacktestSweepFingerprintJsonContext.Default.BacktestRequest));
        Append("external-options");
        Append((request.ExternalEngineOptions?.Count ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (request.ExternalEngineOptions is not null)
        {
            foreach (var pair in request.ExternalEngineOptions.OrderBy(static p => p.Key, StringComparer.Ordinal))
            {
                Append(pair.Key);
                Append(pair.Value);
            }
        }
        Append("parameter-dimensions");
        Append((request.Parameters?.Count ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (request.Parameters is not null)
        {
            foreach (var key in request.Parameters.Keys.OrderBy(static key => key, StringComparer.Ordinal))
            {
                Append(key);
            }
        }

        // Versioned deliberately: legacy point hashes remain inspectable but never gain a
        // claim of comparable window/cost assumptions retroactively.
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return "sweep-v2:" + Convert.ToHexString(bytes);
    }

}
