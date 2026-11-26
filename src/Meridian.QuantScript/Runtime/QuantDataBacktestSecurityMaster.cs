using Meridian.Contracts.SecurityMaster;
using Meridian.QuantScript.Api;

namespace Meridian.QuantScript.Runtime;

/// <summary>Canonical replay queries use the same bounded typed host-data channel as scripts.</summary>
internal sealed class QuantDataBacktestSecurityMaster(IQuantDataContext data)
    : ISecurityIdentityQueryService, ISecurityCorporateActionsQueryService
{
    private readonly Dictionary<Guid, (string Symbol, DateTimeOffset Cutoff)> _resolved = [];

    public async Task<SecurityDetailDto?> GetByIdentifierAsync(
        SecurityIdentifierKind identifierKind, string identifierValue, string? provider,
        CancellationToken ct = default, DateTimeOffset? asOfUtc = null)
    {
        if (identifierKind != SecurityIdentifierKind.Ticker || asOfUtc is null || provider is not null)
            throw new NotSupportedException("Canonical replay requires ticker identity and an explicit effective cutoff.");
        var security = await data.CanonicalSecurityMasterAsync(identifierValue, asOfUtc.Value, ct).ConfigureAwait(false);
        _resolved[security.SecurityId] = (identifierValue, asOfUtc.Value);
        return security;
    }

    public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
    {
        if (!_resolved.TryGetValue(securityId, out var resolved))
            throw new InvalidOperationException("Canonical action query requires a resolved replay security.");
        return data.CanonicalCorporateActionsAsync(resolved.Symbol, securityId, resolved.Cutoff, ct);
    }
}
