using Meridian.Contracts.Domain.Events;

namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Represents a modification to an existing order's price, size, or flags.
/// Only the fields that changed are populated; null values indicate no change.
/// </summary>
public sealed record OrderModify(
    /// <summary>
    /// Venue-assigned identifier of the order being modified.
    /// </summary>
    string OrderId,

    /// <summary>
    /// Feed sequence number for ordering and gap detection.
    /// </summary>
    long SequenceNumber,

    /// <summary>
    /// Updated limit price. Null when the price was not changed.
    /// </summary>
    decimal? NewPrice = null,

    /// <summary>
    /// Updated displayed (visible) size. Null when the displayed size was not changed.
    /// </summary>
    long? NewDisplayedSize = null,

    /// <summary>
    /// Updated hidden (iceberg) size. Null when the hidden size was not changed.
    /// </summary>
    long? NewHiddenSize = null,

    /// <summary>
    /// When true, the modification caused this order to lose its time-priority position
    /// at the price level (e.g., an upsize that resets the queue position is false; a
    /// price change is always true).
    /// </summary>
    bool LosesPriority = false,

    /// <summary>
    /// Stream identifier for data source tracking.
    /// </summary>
    string? StreamId = null,

    /// <summary>
    /// Trading venue or exchange identifier.
    /// </summary>
    string? Venue = null
) : MarketEventPayload;
