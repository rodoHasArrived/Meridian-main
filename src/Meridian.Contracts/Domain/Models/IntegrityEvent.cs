using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Events;

namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Data integrity / continuity / anomaly event.
/// </summary>
public sealed record IntegrityEvent(
    DateTimeOffset Timestamp,
    string Symbol,
    IntegritySeverity Severity,
    string Description,
    ushort? ErrorCode,
    long SequenceNumber,
    string? StreamId = null,
    string? Venue = null
) : MarketEventPayload
{
    /// <summary>
    /// Creates a sequence gap integrity event.
    /// </summary>
    public static IntegrityEvent SequenceGap(
        DateTimeOffset ts,
        string symbol,
        long expectedNext,
        long received,
        string? streamId = null,
        string? venue = null)
        => new(ts, symbol, IntegritySeverity.Error,
            $"Sequence gap: expected {expectedNext} but received {received}.",
            ErrorCode: 1001,
            SequenceNumber: received,
            StreamId: streamId,
            Venue: venue);

    /// <summary>
    /// Creates an out-of-order integrity event.
    /// </summary>
    public static IntegrityEvent OutOfOrder(
        DateTimeOffset ts,
        string symbol,
        long last,
        long received,
        string? streamId = null,
        string? venue = null)
        => new(ts, symbol, IntegritySeverity.Warning,
            $"Out-of-order trade: last {last}, received {received}.",
            ErrorCode: 1002,
            SequenceNumber: received,
            StreamId: streamId,
            Venue: venue);

    /// <summary>
    /// Creates an invalid symbol integrity event.
    /// </summary>
    public static IntegrityEvent InvalidSymbol(
        DateTimeOffset ts,
        string symbol,
        string reason,
        long sequenceNumber,
        string? streamId = null,
        string? venue = null)
        => new(ts, symbol, IntegritySeverity.Error,
            $"Invalid symbol format: {reason}",
            ErrorCode: 1003,
            SequenceNumber: sequenceNumber,
            StreamId: streamId,
            Venue: venue);

    /// <summary>
    /// Creates an invalid sequence number integrity event.
    /// </summary>
    public static IntegrityEvent InvalidSequenceNumber(
        DateTimeOffset ts,
        string symbol,
        long sequenceNumber,
        string reason,
        string? streamId = null,
        string? venue = null)
        => new(ts, symbol, IntegritySeverity.Error,
            $"Invalid sequence number {sequenceNumber}: {reason}",
            ErrorCode: 1004,
            SequenceNumber: sequenceNumber,
            StreamId: streamId,
            Venue: venue);

    /// <summary>
    /// Creates an unresolved symbol integrity event when canonicalization cannot map a symbol.
    /// </summary>
    public static IntegrityEvent UnresolvedSymbol(
        DateTimeOffset ts,
        string symbol,
        string provider,
        long sequenceNumber,
        string? streamId = null,
        string? venue = null)
        => new(ts, symbol, IntegritySeverity.Warning,
            $"Unresolved symbol '{symbol}' from provider '{provider}': no canonical mapping found.",
            ErrorCode: 1005,
            SequenceNumber: sequenceNumber,
            StreamId: streamId,
            Venue: venue);


    /// <summary>
    /// Creates a canonicalization hard-fail integrity event when required fields are missing.
    /// </summary>
    public static IntegrityEvent CanonicalizationHardFail(
        DateTimeOffset ts,
        string symbol,
        string reason,
        long sequenceNumber,
        string? streamId = null,
        string? venue = null)
        => new(ts, symbol, IntegritySeverity.Error,
            $"Canonicalization hard failure: {reason}",
            ErrorCode: 1006,
            SequenceNumber: sequenceNumber,
            StreamId: streamId,
            Venue: venue);
}
