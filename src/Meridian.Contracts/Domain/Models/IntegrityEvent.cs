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
    /// Creates a missing-source integrity event for an ingress update that carried no
    /// provider identity. The update is rejected rather than being silently attributed
    /// to a default vendor, so this event is the loud mark the tape keeps instead.
    /// </summary>
    public static IntegrityEvent MissingSource(
        DateTimeOffset ts,
        string symbol,
        string updateKind,
        long sequenceNumber,
        string? streamId = null,
        string? venue = null)
        => new(ts, symbol, IntegritySeverity.Error,
            $"Rejected {updateKind} update without a provider source: adapters must stamp their real provider identity at origin.",
            ErrorCode: 1008,
            SequenceNumber: sequenceNumber,
            StreamId: streamId,
            Venue: venue);

    /// <summary>
    /// Creates a coverage-hole integrity event for a known data gap that was deliberately left
    /// unremediated (e.g. a reconnect window below the automatic remediation floor). The gap is
    /// disclosed on the tape instead of disappearing into a Debug log, so a stored day with a
    /// skipped outage no longer reads back as continuous coverage.
    /// </summary>
    public static IntegrityEvent UnremediatedCoverageGap(
        DateTimeOffset ts,
        string symbol,
        string provider,
        DateTimeOffset gapStart,
        DateTimeOffset gapEnd,
        string reason,
        string? streamId = null,
        string? venue = null)
        => new(ts, symbol, IntegritySeverity.Warning,
            $"Unremediated coverage gap on provider '{provider}' from {gapStart:O} to {gapEnd:O}: {reason}.",
            ErrorCode: 1009,
            SequenceNumber: 0,
            StreamId: streamId,
            Venue: venue);

    /// <summary>
    /// Creates a provider-failover integrity event marking the coverage-uncertain window while a
    /// composite streaming client hands off from one provider to another (losing the old feed
    /// through completing re-subscription on the new one).
    /// </summary>
    public static IntegrityEvent ProviderFailover(
        DateTimeOffset ts,
        string symbol,
        string fromProvider,
        string toProvider,
        string reason,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        string? streamId = null,
        string? venue = null)
        => new(ts, symbol, IntegritySeverity.Warning,
            $"Provider failover '{fromProvider}' -> '{toProvider}' ({reason}); coverage uncertain from {windowStart:O} to {windowEnd:O}.",
            ErrorCode: 1010,
            SequenceNumber: 0,
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
