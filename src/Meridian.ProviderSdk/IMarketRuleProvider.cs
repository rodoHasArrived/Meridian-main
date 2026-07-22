using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.ProviderSdk;

/// <summary>Provider-neutral request for price increment rules.</summary>
public sealed record MarketRuleRequest(string MarketRuleId, string? Symbol = null, string? Exchange = null);

/// <summary>Provider-neutral market rule with ordered price-band increments.</summary>
public sealed record ProviderMarketRule(string MarketRuleId, IReadOnlyList<ProviderMarketRuleIncrement> Increments, ProviderDataProvenance Provenance);

/// <summary>Optional provider capability for market-rule and price-increment retrieval.</summary>
public interface IMarketRuleProvider : IProviderMetadata
{
    Task<ProviderMarketRule?> GetMarketRuleAsync(MarketRuleRequest request, CancellationToken ct = default);
}
