using System.Text.Json.Serialization;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Events;

namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Integrity event specific to market depth streams.
/// </summary>
public sealed record DepthIntegrityEvent(
    DateTimeOffset Timestamp,
    string Symbol,
    [property: JsonPropertyName("integrityKind")] DepthIntegrityKind Kind,
    string Description,
    ushort Position,
    DepthOperation Operation,
    OrderBookSide Side,
    long SequenceNumber,
    string? StreamId = null,
    string? Venue = null
) : MarketEventPayload;
