using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.ProviderSdk;

/// <summary>
/// Provider-facing source of exchange trading-calendar data. Implementations return data with
/// retained provider provenance; this contract is not the deterministic local scheduling calendar.
/// </summary>
public interface ITradingCalendarProvider : IProviderMetadata
{
    /// <summary>
    /// Retrieves provider-supplied sessions and closures for the requested market and date range.
    /// </summary>
    Task<ProviderTradingCalendarResponse> GetTradingCalendarAsync(
        ProviderTradingCalendarRequest request,
        CancellationToken ct = default)
        => throw new NotSupportedException(
            "This calendar provider implements the legacy GetSessionsAsync contract. " +
            "Implement GetTradingCalendarAsync to return provenance-complete calendar data.");

    /// <summary>
    /// Gets sessions through the former calendar contract.
    /// </summary>
    /// <remarks>
    /// Retained so existing provider adapters and callers can transition without a source-breaking
    /// interface change. New code must use <see cref="GetTradingCalendarAsync"/> because this
    /// projection cannot represent closures or the required provider provenance.
    /// </remarks>
    [Obsolete("Use GetTradingCalendarAsync so calendar output retains provider provenance.")]
    async Task<IReadOnlyList<TradingSession>> GetSessionsAsync(
        TradingCalendarRequest request,
        CancellationToken ct = default)
    {
        var response = await GetTradingCalendarAsync(
            new ProviderTradingCalendarRequest(request.Market, request.From, request.To),
            ct).ConfigureAwait(false);

        response.EnsureProvenanceComplete();
        return response.Sessions
            .Select(session => new TradingSession(
                session.Date,
                true,
                session.OpenTime,
                session.CloseTime,
                session.SessionType))
            .ToArray();
    }
}

/// <summary>Provider-neutral request for venue trading sessions retained for source compatibility.</summary>
[Obsolete("Use ProviderTradingCalendarRequest.")]
public sealed record TradingCalendarRequest(string Market, DateOnly From, DateOnly To, string? AssetClass = null);

/// <summary>Provider-neutral trading session retained for source compatibility.</summary>
[Obsolete("Use ProviderTradingSession and ProviderTradingCalendarResponse.")]
public sealed record TradingSession(
    DateOnly Date,
    bool IsOpen,
    DateTimeOffset? OpensAt = null,
    DateTimeOffset? ClosesAt = null,
    string? Name = null);

/// <summary>
/// Specifies the market and inclusive date range requested from a calendar provider.
/// </summary>
public sealed record ProviderTradingCalendarRequest(
    string Market,
    DateOnly StartDate,
    DateOnly EndDate)
{
    /// <summary>Validates that the request has a market and an ordered range.</summary>
    public void EnsureValid()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Market);
        if (EndDate < StartDate)
            throw new ArgumentOutOfRangeException(nameof(EndDate), "The end date must not precede the start date.");
    }
}

/// <summary>
/// A session supplied by a calendar provider.
/// </summary>
public sealed record ProviderTradingSession(
    DateOnly Date,
    string Exchange,
    string Market,
    string SessionType,
    DateTimeOffset OpenTime,
    DateTimeOffset CloseTime);

/// <summary>
/// A provider-supplied full-day or partial-day market closure.
/// </summary>
public sealed record ProviderTradingCalendarClosure(
    DateOnly Date,
    string Market,
    string Reason,
    bool IsEarlyClose = false);

/// <summary>
/// Provider calendar output and its required provenance envelope.
/// </summary>
public sealed record ProviderTradingCalendarResponse(
    IReadOnlyList<ProviderTradingSession> Sessions,
    IReadOnlyList<ProviderTradingCalendarClosure> Closures,
    ProviderDataProvenance Provenance)
{
    /// <summary>Throws when the provider output omits required shared provenance.</summary>
    public void EnsureProvenanceComplete()
    {
        ArgumentNullException.ThrowIfNull(Provenance);
        ArgumentException.ThrowIfNullOrWhiteSpace(Provenance.ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Provenance.ProviderConnectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Provenance.Entitlement);
        ArgumentException.ThrowIfNullOrWhiteSpace(Provenance.Feed);
        ArgumentException.ThrowIfNullOrWhiteSpace(Provenance.MarketDataAvailability);
        ArgumentException.ThrowIfNullOrWhiteSpace(Provenance.RequestOrSubscriptionDescriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(Provenance.ProviderNativeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Provenance.CorrelationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Provenance.StableDeduplicationKey);
        if (Provenance.SourceTimestamp == default || Provenance.ReceiptTimestamp == default)
            throw new ArgumentOutOfRangeException(nameof(Provenance), "Provider output must include source and receipt timestamps.");
    }
}
