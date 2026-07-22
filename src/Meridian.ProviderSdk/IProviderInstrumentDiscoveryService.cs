using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.ProviderSdk;

/// <summary>Provider-neutral request for discoverable instruments and contracts.</summary>
public sealed record ProviderInstrumentDiscoveryRequest(
    string Query,
    string? AssetClass = null,
    string? Market = null,
    int? Limit = null);

/// <summary>Provider-neutral instrument identity returned by discovery.</summary>
public sealed record ProviderInstrument(
    string Symbol,
    string Name,
    string AssetClass,
    string? Exchange = null,
    string? Currency = null,
    string? ProviderInstrumentId = null);

/// <summary>Optional provider capability for instrument and contract discovery.</summary>
public interface IProviderInstrumentDiscoveryService : IProviderMetadata
{
    Task<IReadOnlyList<ProviderInstrument>> DiscoverAsync(ProviderInstrumentDiscoveryRequest request, CancellationToken ct = default);
}
