using Meridian.Contracts.Domain.Events;

namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Represents a venue-side order replace, where the venue cancels the original order
/// and immediately re-enters it under a new order identifier.
/// This is distinct from <see cref="OrderModify"/> because the venue reassigns the
/// order ID, which breaks continuity of the original order's life cycle.
/// </summary>
public sealed record OrderReplace(
    /// <summary>
    /// Venue-assigned identifier of the original order being replaced (cancelled side).
    /// </summary>
    string OldOrderId,

    /// <summary>
    /// Venue-assigned identifier of the replacement order (new entry).
    /// </summary>
    string NewOrderId,

    /// <summary>
    /// Feed sequence number for ordering and gap detection.
    /// </summary>
    long SequenceNumber,

    /// <summary>
    /// New limit price for the replacement order. Null when unchanged from the original.
    /// </summary>
    decimal? NewPrice = null,

    /// <summary>
    /// New displayed (visible) size. Null when unchanged from the original.
    /// </summary>
    long? NewDisplayedSize = null,

    /// <summary>
    /// New hidden (iceberg) size. Null when unchanged from the original.
    /// </summary>
    long? NewHiddenSize = null,

    /// <summary>
    /// Indicates whether the replacement order loses its time-priority position.
    /// Defaults to true because a venue ID reassignment typically resets queue position.
    /// </summary>
    bool LosesPriority = true,

    /// <summary>
    /// Stream identifier for data source tracking.
    /// </summary>
    string? StreamId = null,

    /// <summary>
    /// Trading venue or exchange identifier.
    /// </summary>
    string? Venue = null
) : MarketEventPayload;
