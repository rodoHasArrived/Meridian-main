using Meridian.Contracts.Operations;
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
        CancellationToken ct = default);
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
/// Required provenance for provider calendar output. <see cref="DataProvenance"/> carries the
/// shared real/simulated/seeded/sample classification used throughout Meridian.
/// </summary>
public sealed record ProviderCalendarProvenance(
    string ProviderId,
    string SourceReference,
    DateTimeOffset RetrievedAtUtc,
    DateTimeOffset? SourceAsOfUtc,
    DataProvenance DataProvenance)
{
    /// <summary>Throws when provider output cannot be traced to its source and observation time.</summary>
    public void EnsureComplete()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceReference);
        if (RetrievedAtUtc == default)
            throw new ArgumentOutOfRangeException(nameof(RetrievedAtUtc), "Provider output must include its retrieval time.");
        if (!Enum.IsDefined(DataProvenance))
            throw new ArgumentOutOfRangeException(nameof(DataProvenance), "Provider output must include a recognized data provenance.");
    }
}

/// <summary>
/// Provider calendar output and its required provenance envelope.
/// </summary>
public sealed record ProviderTradingCalendarResponse(
    IReadOnlyList<ProviderTradingSession> Sessions,
    IReadOnlyList<ProviderTradingCalendarClosure> Closures,
    ProviderCalendarProvenance Provenance)
{
    /// <summary>Throws when the provider output omits required shared provenance.</summary>
    public void EnsureProvenanceComplete()
    {
        ArgumentNullException.ThrowIfNull(Provenance);
        Provenance.EnsureComplete();
    }
}
