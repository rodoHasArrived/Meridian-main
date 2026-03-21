using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Events;

namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Represents the execution of a resting order (full or partial fill).
/// The resting order is always identified; the aggressor order ID is provided
/// when the venue includes it in the execution message.
/// </summary>
public sealed record OrderExecute(
    /// <summary>
    /// Venue-assigned identifier of the resting (passive/maker) order that was filled.
    /// </summary>
    string RestingOrderId,

    /// <summary>
    /// Execution price at which the match occurred.
    /// </summary>
    decimal ExecPrice,

    /// <summary>
    /// Number of shares/contracts matched in this execution.
    /// </summary>
    long ExecSize,

    /// <summary>
    /// Side that initiated the trade (aggressor/taker side).
    /// </summary>
    AggressorSide AggressorSide,

    /// <summary>
    /// Feed sequence number for ordering and gap detection.
    /// </summary>
    long SequenceNumber,

    /// <summary>
    /// Venue-assigned identifier of the aggressor (taker/initiating) order,
    /// when included in the execution message.
    /// </summary>
    string? TakerOrderId = null,

    /// <summary>
    /// Venue-assigned trade identifier that links this execution to the corresponding
    /// trade print (time and sales). Null when not provided by the venue.
    /// </summary>
    string? TradeId = null,

    /// <summary>
    /// Stream identifier for data source tracking.
    /// </summary>
    string? StreamId = null,

    /// <summary>
    /// Trading venue or exchange identifier.
    /// </summary>
    string? Venue = null
) : MarketEventPayload;
