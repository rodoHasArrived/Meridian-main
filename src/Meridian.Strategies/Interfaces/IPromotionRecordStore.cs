using Meridian.Strategies.Promotions;

namespace Meridian.Strategies.Interfaces;

/// <summary>
/// Durable append-only store for promotion decisions and audit metadata.
/// </summary>
public interface IPromotionRecordStore
{
    /// <summary>
    /// Loads all recorded promotion decisions in append order.
    /// </summary>
    Task<IReadOnlyList<StrategyPromotionRecord>> LoadAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Atomically retains the first decision for a source run and target mode, then returns an
    /// exclusive authority lease over that decision. The lease must remain held until the caller
    /// has materialized any target-run and audit side effects derived from the retained winner.
    /// </summary>
    /// <remarks>
    /// Implementations must compare <see cref="StrategyPromotionRecord.SourceRunId"/>,
    /// <see cref="StrategyPromotionRecord.SourceRunType"/>, and
    /// <see cref="StrategyPromotionRecord.TargetRunType"/> as the decision key. When a decision
    /// already exists, its exact retained record is returned and <see cref="PromotionDecisionReservation.WasAppended"/>
    /// is <see langword="false"/>. Production implementations must serialize this operation across
    /// store instances and processes that share the same durable authority.
    /// </remarks>
    Task<PromotionDecisionReservation> ReserveFirstDecisionAsync(
        StrategyPromotionRecord record,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return ReserveFirstDecisionCompatibilityAsync(this, record, ct);

        static async Task<PromotionDecisionReservation> ReserveFirstDecisionCompatibilityAsync(
            IPromotionRecordStore store,
            StrategyPromotionRecord candidate,
            CancellationToken cancellationToken)
        {
            var records = await store.LoadAllAsync(cancellationToken).ConfigureAwait(false);
            var existing = records.FirstOrDefault(record =>
                string.Equals(record.SourceRunId, candidate.SourceRunId, StringComparison.Ordinal) &&
                record.SourceRunType == candidate.SourceRunType &&
                record.TargetRunType == candidate.TargetRunType);
            if (existing is not null)
            {
                return new PromotionDecisionReservation(existing, wasAppended: false);
            }

            await store.AppendAsync(candidate, cancellationToken).ConfigureAwait(false);
            return new PromotionDecisionReservation(candidate, wasAppended: true);
        }
    }

    /// <summary>
    /// Appends a new promotion decision to the durable history.
    /// </summary>
    Task AppendAsync(StrategyPromotionRecord record, CancellationToken ct = default);
}

/// <summary>
/// Exclusive authority over the first retained promotion decision for one source-run and target-mode
/// key. Disposing the reservation releases the authority for the next caller.
/// </summary>
public sealed class PromotionDecisionReservation : IAsyncDisposable
{
    private Func<ValueTask>? _release;

    public PromotionDecisionReservation(
        StrategyPromotionRecord record,
        bool wasAppended,
        Func<ValueTask>? release = null)
    {
        Record = record ?? throw new ArgumentNullException(nameof(record));
        WasAppended = wasAppended;
        _release = release;
    }

    /// <summary>The first durable decision retained for the key.</summary>
    public StrategyPromotionRecord Record { get; }

    /// <summary>Whether this reservation appended <see cref="Record"/> as the first decision.</summary>
    public bool WasAppended { get; }

    public ValueTask DisposeAsync()
    {
        var release = Interlocked.Exchange(ref _release, null);
        return release is null ? ValueTask.CompletedTask : release();
    }
}
