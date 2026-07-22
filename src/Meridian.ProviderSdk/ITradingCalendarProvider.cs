using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.ProviderSdk;

/// <summary>Provider-neutral request for venue trading sessions.</summary>
public sealed record TradingCalendarRequest(string Market, DateOnly From, DateOnly To, string? AssetClass = null);

/// <summary>One provider-neutral trading session, including closed or shortened sessions.</summary>
public sealed record TradingSession(
    DateOnly Date,
    bool IsOpen,
    DateTimeOffset? OpensAt = null,
    DateTimeOffset? ClosesAt = null,
    string? Name = null);

/// <summary>Optional provider capability for venue trading calendars.</summary>
public interface ITradingCalendarProvider : IProviderMetadata
{
    Task<IReadOnlyList<TradingSession>> GetSessionsAsync(TradingCalendarRequest request, CancellationToken ct = default);
}
