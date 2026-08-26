using System.Globalization;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Operations;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Storage;
using Microsoft.Extensions.Logging;

namespace Meridian.Strategies.Services;

/// <summary>
/// Records research-originated backtests into the configured <see cref="IStrategyRepository"/> so a run produced
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
    IStrategyRepository store,
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
            // The engine has already finished by the time the host records it, so Start/Complete
            // would otherwise stamp a multi-minute backtest as near-zero duration and sort it by
            // persistence time. Derive the real start from the run's own elapsed time.
            var completed = entry.Complete(BoundResultForRetention(result));
            var startedAt = completed.EndedAt is { } endedAt && result.ElapsedTime > TimeSpan.Zero
                ? endedAt - result.ElapsedTime
                : completed.StartedAt;
            completed = completed with
            {
                StartedAt = startedAt,
                OutputMetadata = DescribeTrimmedDetail(result)
            };

            await store.RecordRunAsync(completed, ct).ConfigureAwait(false);
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

    /// <summary>
    /// Trims the per-event detail from a result before it is retained. The durable store caps a run
    /// snapshot at <see cref="OperationalCaseHistoryHashing.MaxDataValueLength"/> characters, and a
    /// long or fill-dense backtest serializes every snapshot, cash flow, fill, and ledger entry —
    /// so the runs most worth tracking were exactly the ones whose recording failed and was
    /// swallowed. Metrics, universe, and bias disclosure are what lineage and run comparison read,
    /// so they are kept; the bulk collections are dropped and their sizes recorded instead.
    /// </summary>
    private static BacktestResult BoundResultForRetention(BacktestResult result)
    {
        if (result.Snapshots.Count == 0 &&
            result.CashFlows.Count == 0 &&
            result.Fills.Count == 0 &&
            (result.Ledger?.JournalEntryCount ?? 0) == 0)
        {
            return result;
        }

        return result with
        {
            Snapshots = [],
            CashFlows = [],
            Fills = [],
            TradeTickets = null,
            // The ledger is the other unbounded collection: its JSON converter writes the complete
            // journal, so a fill-dense run that posts entries could still blow past the store's
            // snapshot cap after the lists above were cleared — losing lineage for exactly the
            // runs this bounding exists to keep.
            Ledger = new Meridian.Ledger.Ledger()
        };
    }

    /// <summary>
    /// Counts of the detail dropped by <see cref="BoundResultForRetention"/>. Consumers read them
    /// back through <see cref="StrategyRunEntry.RetainedFillCount"/> and
    /// <see cref="StrategyRunEntry.RetainedJournalEntryCount"/>, so a bounded run still reports
    /// what its simulation actually produced.
    /// </summary>
    private static Dictionary<string, string> DescribeTrimmedDetail(BacktestResult result) => new(StringComparer.Ordinal)
    {
        ["retainedDetail"] = "summary",
        ["snapshotCount"] = result.Snapshots.Count.ToString(CultureInfo.InvariantCulture),
        ["cashFlowCount"] = result.CashFlows.Count.ToString(CultureInfo.InvariantCulture),
        [StrategyRunEntry.RetainedFillCountMetadataKey] = result.Fills.Count.ToString(CultureInfo.InvariantCulture),
        [StrategyRunEntry.RetainedJournalEntryCountMetadataKey] =
            (result.Ledger?.JournalEntryCount ?? 0).ToString(CultureInfo.InvariantCulture)
    };
}
