using System.Globalization;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Operations;
using Meridian.Strategies.Interfaces;
using System.Text.Json;
using Meridian.Strategies.Serialization;
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
            var completed = entry.Complete(result);
            var startedAt = completed.EndedAt is { } endedAt && result.ElapsedTime > TimeSpan.Zero
                ? endedAt - result.ElapsedTime
                : completed.StartedAt;
            completed = completed with { StartedAt = startedAt };

            // Trim only if the snapshot would actually breach the store's cap. Trimming every run
            // that merely has a fill would zero out the fill counts that StrategyRunReadService and
            // StrategyRunContinuityService read from Metrics.Fills, reporting a missing fill seam
            // for runs that executed perfectly well.
            if (ExceedsRetentionLimit(completed))
            {
                completed = completed with
                {
                    Metrics = BoundResultForRetention(result),
                    OutputMetadata = DescribeTrimmedDetail(result)
                };
            }

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
    /// Reports whether the entry would breach the durable store's per-snapshot character cap.
    /// Serializing to measure costs one pass, which is far cheaper than the alternative: the store
    /// throws past the cap and the fail-open catch converts that into a silently unrecorded run.
    /// </summary>
    private static readonly int RetentionSafetyLimit =
        (int)(OperationalCaseHistoryHashing.MaxDataValueLength * 0.95);

    private static bool ExceedsRetentionLimit(StrategyRunEntry entry)
    {
        try
        {
            var json = JsonSerializer.Serialize(entry, StrategyRunPersistenceJson.Options);

            // Measured against a margin rather than the exact cap: the store measures after its own
            // normalization pass, so this estimate is close but not identical. Erring toward
            // trimming costs some detail; erring the other way costs the run its lineage entirely.
            return json.Length > RetentionSafetyLimit;
        }
        catch (NotSupportedException)
        {
            // Unmeasurable rather than oversized. Trim so a serialization quirk cannot cost the
            // run its lineage.
            return true;
        }
    }

    /// <summary>
    /// Drops per-event detail from an oversized result, keeping what lineage and run comparison
    /// read: metrics, universe, request, and bias disclosure. The ledger is bounded too — its
    /// journal is serialized in full by the retention converter, so a journal-heavy run can breach
    /// the cap even with no fills at all.
    /// </summary>
    private static BacktestResult BoundResultForRetention(BacktestResult result) => result with
    {
        Snapshots = [],
        CashFlows = [],
        Fills = [],
        TradeTickets = null,
        Ledger = new Meridian.Ledger.Ledger()
    };

    /// <summary>
    /// Records what <see cref="BoundResultForRetention"/> dropped, so a trimmed run reports real
    /// counts rather than appearing to have produced nothing.
    /// </summary>
    private static Dictionary<string, string> DescribeTrimmedDetail(BacktestResult result) => new(StringComparer.Ordinal)
    {
        ["retainedDetail"] = "summary",
        ["snapshotCount"] = result.Snapshots.Count.ToString(CultureInfo.InvariantCulture),
        ["cashFlowCount"] = result.CashFlows.Count.ToString(CultureInfo.InvariantCulture),
        ["fillCount"] = result.Fills.Count.ToString(CultureInfo.InvariantCulture),
        ["journalEntryCount"] = result.Ledger.JournalEntryCount.ToString(CultureInfo.InvariantCulture)
    };
}
