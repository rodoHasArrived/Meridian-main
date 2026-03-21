using Meridian.Contracts.Domain.Events;

namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Represents the full or partial cancellation of a resting order.
/// A CanceledSize equal to the original order size indicates a full cancel.
/// </summary>
public sealed record OrderCancel(
    /// <summary>
    /// Venue-assigned identifier of the order being cancelled.
    /// </summary>
    string OrderId,

    /// <summary>
    /// Quantity removed from the book. For a full cancel this equals the remaining order size;
    /// for a partial cancel it is less than the remaining size.
    /// </summary>
    long CanceledSize,

    /// <summary>
    /// Feed sequence number for ordering and gap detection.
    /// </summary>
    long SequenceNumber,

    /// <summary>
    /// Stream identifier for data source tracking.
    /// </summary>
    string? StreamId = null,

    /// <summary>
    /// Trading venue or exchange identifier.
    /// </summary>
    string? Venue = null
) : MarketEventPayload;
