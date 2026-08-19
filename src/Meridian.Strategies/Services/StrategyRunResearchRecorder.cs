using Meridian.Backtesting.Sdk;
using Meridian.Strategies.Models;
using Meridian.Strategies.Storage;
using Microsoft.Extensions.Logging;

namespace Meridian.Strategies.Services;

/// <summary>
/// Records research-originated backtests into <see cref="StrategyRunStore"/> so that a run produced
/// in a scripting surface carries the same lineage as one launched from the Studio.
/// </summary>
/// <remarks>
/// <para>
/// Backtests executed from a script previously left no trace in the run store, so research done in
/// the Quant Lab was invisible to the promotion path and could not be compared against Studio runs.
/// </para>
/// <para>
/// Recording is deliberately host-side. A script executes inside the QuantScript worker sandbox,
/// whose only sanctioned route to host state is the typed market-data RPC seam; giving that process
/// a writable path into the strategy-run store would widen the isolation boundary the worker exists
/// to enforce. The host records the results the worker returns instead.
/// </para>
/// </remarks>
public sealed class StrategyRunResearchRecorder(
    StrategyRunStore store,
    ILogger<StrategyRunResearchRecorder> logger) : IResearchRunRecorder
{
    /// <inheritdoc />
    /// <remarks>
    /// Fail-open for research: a store outage returns <see langword="null"/> rather than throwing,
    /// so a recording problem never destroys a researcher's completed work. Fail-closed for
    /// promotion follows from the same return: a null run id means no lineage exists, and callers
    /// must not present such a run as promotion-eligible.
    /// </remarks>
    public async Task<string?> RecordAsync(
        ResearchRunDescriptor descriptor,
        BacktestResult result,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.StrategyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.StrategyName);

        var runId = Guid.NewGuid().ToString("N");
        const string Engine = "MeridianNative";

        var inputHash = StrategyRunEntry.ComputeRealismBoundInputHash(
            descriptor.StrategyId,
            descriptor.StrategyName,
            RunType.Backtest,
            descriptor.DatasetReference,
            feedReference: null,
            Engine,
            descriptor.ParameterSet,
            descriptor.ExecutionRealism);

        var entry = StrategyRunEntry.Start(
            descriptor.StrategyId,
            descriptor.StrategyName,
            RunType.Backtest,
            runId,
            descriptor.DatasetReference,
            feedReference: null,
            Engine,
            descriptor.ParameterSet) with
        {
            CorrelationId = descriptor.CorrelationId,
            InputHashSha256 = inputHash
        };

        try
        {
            await store.RecordRunAsync(entry.Complete(result), ct).ConfigureAwait(false);
            logger.LogInformation(
                "Recorded research backtest {RunId} for strategy {StrategyId} (correlation {CorrelationId})",
                runId,
                descriptor.StrategyId,
                descriptor.CorrelationId);
            return runId;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not record research backtest for strategy {StrategyId}; the run completed but has no lineage",
                descriptor.StrategyId);
            return null;
        }
    }
}
