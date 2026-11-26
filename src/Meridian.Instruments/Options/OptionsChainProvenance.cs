using System.Collections.Immutable;
using Meridian.Contracts.Operations;
using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Instruments.Options;

/// <summary>Run-local options route evidence for a sequential chain acquisition.</summary>
public sealed class OptionsChainProvenance
{
    private readonly ProviderProvenanceSource?[] _sources;
    private readonly int _primaryIndex;

    internal OptionsChainProvenance(IReadOnlyList<IOptionsChainProvider> providers, string? tenantId, int strikeRange)
    {
        StrikeRange = strikeRange;
        _sources = providers.Select(provider =>
        {
            var id = ProviderProvenanceLabels.ProviderId(provider.ProviderId);
            var connection = ProviderRuntimeProvenance.Find(provider);
            if (connection?.TenantId is { } owner && owner != tenantId)
            {
                if (tenantId is not null)
                    return null;
                connection = null;
            }
            return new ProviderProvenanceSource(id, connection?.Id,
                id == "synthetic" ? "Local" : connection?.Environment ?? ProviderProvenanceLabels.Unknown,
                id == "alpaca" ? "Indicative options feed" : "Options chain snapshot",
                id == "synthetic" ? "Local simulation" : "Entitlement not reported",
                id == "synthetic" ? "Synthetic option chain" : "Current options data", provider.ProviderPriority);
        }).ToArray();
        _primaryIndex = Array.FindIndex(_sources, source => source is not null);
    }

    public int StrikeRange { get; }
    public bool FallbackOccurred { get; private set; }
    public ImmutableArray<ProviderProvenanceSource> Sources => _sources.OfType<ProviderProvenanceSource>().ToImmutableArray();

    internal bool CanQuery(int index) => _sources[index] is not null;

    internal void Record(int index, bool requested, bool? hasData)
    {
        if (_sources[index] is not { } source)
            return;
        FallbackOccurred |= index != _primaryIndex;
        _sources[index] = source with
        {
            Requests = source.Requests + (requested ? 1 : 0),
            ResponsesWithData = source.ResponsesWithData + (hasData == true ? 1 : 0),
            Unavailable = source.Unavailable + (hasData is null ? 1 : 0),
            EmptyResponses = source.EmptyResponses + (hasData == false ? 1 : 0)
        };
    }
}
