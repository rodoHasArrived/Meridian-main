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
            // Retained so the durable store can recompute the v4 digest from the entry itself.
            // Without it the store falls back to the v3 recomputation, rejects the v4 hash, and
            // the catch below turns that into a silent loss of research lineage.
            ExecutionRealism = descriptor.ExecutionRealism
        };

        // Hash the entry we are about to persist rather than a parallel argument list, so the
        // digest is always reproducible by the store from the same retained fields.
        entry = entry with { InputHashSha256 = StrategyRunEntry.ComputeRealismBoundInputHash(entry) };

        try
        {
            await store.RecordRunAsync(entry.Complete(result), ct).ConfigureAwait(false);
            // Preserve raw lineage in storage; sanitize caller-controlled metadata only at the log boundary.
            logger.LogInformation(
                "Recorded research backtest {RunId} for strategy {StrategyId} (correlation {CorrelationId})",
                runId,
                Meridian.Execution.Logging.LogSanitizer.Sanitize(descriptor.StrategyId),
                Meridian.Execution.Logging.LogSanitizer.Sanitize(descriptor.CorrelationId));
            return runId;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Fail-open keeps a store outage from killing a researcher's script, but it must stay
            // observable: swallowing the exception type is what let a durable-store hash rejection
            // look like success while every research run silently lost its lineage.
            logger.LogWarning(
                ex,
                "Could not record research backtest for strategy {StrategyId}; the run completed but has no lineage. Failure was {FailureType}: {FailureMessage}",
                Meridian.Execution.Logging.LogSanitizer.Sanitize(descriptor.StrategyId),
                ex.GetType().Name,
                Meridian.Execution.Logging.LogSanitizer.Sanitize(ex.Message));
            return null;
        }
    }
}
