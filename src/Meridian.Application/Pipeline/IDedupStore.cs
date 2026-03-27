using Meridian.Domain.Events;

namespace Meridian.Application.Pipeline;

/// <summary>
/// Abstraction over the persistent event deduplication ledger.
/// Implementations must be thread-safe and survive application restarts.
/// </summary>
public interface IDedupStore
{
    /// <summary>Gets the total number of events checked for duplicates.</summary>
    long TotalChecked { get; }

    /// <summary>Gets the total number of duplicate events detected.</summary>
    long TotalDuplicates { get; }

    /// <summary>Loads persisted dedup state from durable storage on startup.</summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns <c>true</c> if <paramref name="evt"/> has been seen before and records it if new.
    /// </summary>
    ValueTask<bool> IsDuplicateAsync(MarketEvent evt, CancellationToken ct = default);

    /// <summary>Flushes in-memory state to durable backing storage.</summary>
    Task FlushAsync(CancellationToken ct = default);

    /// <summary>Compacts the backing store by discarding expired entries.</summary>
    Task CompactAsync(CancellationToken ct = default);
}
