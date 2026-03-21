using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Events;

namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Represents the arrival of a new order at the venue's matching engine.
/// Captures the full state of the order at the moment it enters the book.
/// </summary>
public sealed record OrderAdd(
    /// <summary>
    /// Venue-assigned order identifier, stable for the life of the order within the session.
    /// </summary>
    string OrderId,

    /// <summary>
    /// Ticker symbol of the instrument.
    /// </summary>
    string Symbol,

    /// <summary>
    /// Buy or sell direction of the order.
    /// </summary>
    OrderSide Side,

    /// <summary>
    /// Limit price of the order.
    /// </summary>
    decimal Price,

    /// <summary>
    /// Visible (displayed) quantity available to the market.
    /// </summary>
    long DisplayedSize,

    /// <summary>
    /// Exchange-assigned priority timestamp used to determine queue position within a price level.
    /// </summary>
    DateTimeOffset PriorityTimestamp,

    /// <summary>
    /// Feed sequence number for ordering and gap detection.
    /// </summary>
    long SequenceNumber,

    /// <summary>
    /// Reserved (iceberg) quantity hidden from the public order book. Null when not applicable.
    /// </summary>
    long? HiddenSize = null,

    /// <summary>
    /// Identifier of the participant or firm that submitted the order, when disclosed by the venue.
    /// </summary>
    string? ParticipantId = null,

    /// <summary>
    /// Market-maker identifier, populated when the venue designates the order as a market-maker quote.
    /// </summary>
    string? MarketMaker = null,

    /// <summary>
    /// Stream identifier for data source tracking.
    /// </summary>
    string? StreamId = null,

    /// <summary>
    /// Trading venue or exchange identifier.
    /// </summary>
    string? Venue = null
) : MarketEventPayload;
