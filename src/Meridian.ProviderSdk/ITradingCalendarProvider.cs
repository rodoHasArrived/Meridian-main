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
            "This calendar provider does not implement GetTradingCalendarAsync. " +
            "Implement it to return provenance-complete calendar data.");
}

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
        EnsureTraceable(Provenance.ProviderId, nameof(Provenance.ProviderId));
        EnsureTraceable(Provenance.ProviderConnectionId, nameof(Provenance.ProviderConnectionId));
        EnsureTraceable(Provenance.Entitlement, nameof(Provenance.Entitlement));
        EnsureTraceable(Provenance.Feed, nameof(Provenance.Feed));
        EnsureTraceable(Provenance.MarketDataAvailability, nameof(Provenance.MarketDataAvailability));
        EnsureTraceable(Provenance.RequestOrSubscriptionDescriptor, nameof(Provenance.RequestOrSubscriptionDescriptor));
        EnsureTraceable(Provenance.ProviderNativeId, nameof(Provenance.ProviderNativeId));
        EnsureTraceable(Provenance.CorrelationId, nameof(Provenance.CorrelationId));
        EnsureTraceable(Provenance.StableDeduplicationKey, nameof(Provenance.StableDeduplicationKey));
        if (Provenance.SourceTimestamp == default || Provenance.ReceiptTimestamp == default)
            throw new ArgumentOutOfRangeException(nameof(Provenance), "Provider output must include source and receipt timestamps.");
        if (Provenance.DataProvenance is not { } dataProvenance || !Enum.IsDefined(dataProvenance))
            throw new ArgumentOutOfRangeException(nameof(Provenance), "Provider output must include a recognized data provenance.");
    }

    private static void EnsureTraceable(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Provider output must include traceable provenance values.", parameterName);
    }
}
