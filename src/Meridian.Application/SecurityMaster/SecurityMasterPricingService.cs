using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>Selects retained source prices using the hierarchy in force at the requested date.</summary>
public sealed class SecurityMasterPricingService : ISecurityMasterPricingService
{
    private readonly ISecurityMasterPricingStore _store;
    private readonly ISecurityMasterQueryService _queryService;
    private readonly ILogger<SecurityMasterPricingService> _logger;

    public SecurityMasterPricingService(ISecurityMasterPricingStore store,
        ISecurityMasterQueryService queryService, ILogger<SecurityMasterPricingService> logger)
        => (_store, _queryService, _logger) = (store, queryService, logger);

    public Task<SecurityPricingHierarchyDto?> GetPricingHierarchyAsync(
        Guid securityId, string? accountId, CancellationToken ct = default)
        => _store.GetHierarchyAsync(securityId, accountId, ct);

    public Task UpsertPricingHierarchyAsync(SecurityPricingHierarchyDto hierarchy, CancellationToken ct = default)
    {
        if (hierarchy.SecurityId == Guid.Empty || string.IsNullOrWhiteSpace(hierarchy.UpdatedBy)
            || hierarchy.Entries.Any(e => string.IsNullOrWhiteSpace(e.SourceId) || e.MaxDaysStale < 0)
            || hierarchy.Entries.Select(e => e.SourceId.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != hierarchy.Entries.Count
            || hierarchy.Entries.Select(e => e.Priority).Distinct().Count() != hierarchy.Entries.Count)
            throw new ArgumentException("A pricing hierarchy requires a security, actor, distinct priorities and sources, and nonnegative staleness limits.");
        return _store.UpsertHierarchyAsync(hierarchy, ct);
    }

    public Task RecordRawPriceAsync(RecordRawPriceRequest request, CancellationToken ct = default)
    {
        if (request.SecurityId == Guid.Empty || string.IsNullOrWhiteSpace(request.SourceId)
            || string.IsNullOrWhiteSpace(request.RecordedBy) || request.PriceAsOf == default
            || request.Unit == SecurityPriceUnit.Unspecified || !Enum.IsDefined(request.Unit))
            throw new ArgumentException("A price requires a security, source, observation date, actor, and explicit quote unit.");
        return _store.RecordRawPriceAsync(request with { SourceId = request.SourceId.Trim() }, ct);
    }

    public Task<SecurityPriceGoldenCopyDto?> GetGoldenCopyPriceAsync(
        Guid securityId, string? accountId, CancellationToken ct = default)
        => GetGoldenCopyPriceAsOfAsync(securityId, accountId, DateTimeOffset.UtcNow, ct);

    public async Task<SecurityPriceGoldenCopyDto?> GetGoldenCopyPriceAsOfAsync(
        Guid securityId, string? accountId, DateTimeOffset asOf, CancellationToken ct = default, DateTimeOffset? knownAt = null)
    {
        var knowledgeAsOf = knownAt ?? DateTimeOffset.UtcNow;
        if (await _queryService.GetByIdAsync(securityId, ct).ConfigureAwait(false) is null)
            return null;

        // No asset-class shortcuts: par, stable NAV and straight-line accretion require actual
        // economic evidence. They cannot silently override a configured market hierarchy.
        var hierarchy = await _store.GetHierarchyAsOfAsync(securityId, accountId, asOf, ct, knowledgeAsOf).ConfigureAwait(false);
        if (hierarchy is null || hierarchy.AsOf > asOf || hierarchy.Entries.Count == 0)
        {
            _logger.LogDebug("No eligible pricing hierarchy for security {SecurityId} at {AsOf}.", securityId, asOf);
            return null;
        }

        var priceMap = LatestEligible(await _store.GetRawPricesAsync(securityId, asOf, ct, knowledgeAsOf).ConfigureAwait(false), asOf);
        SecurityRawPriceDto? selected = null;
        var stale = false;
        var daysStale = 0;
        foreach (var entry in hierarchy.Entries.OrderBy(e => e.Priority).ThenBy(e => e.SourceId, StringComparer.Ordinal))
        {
            if (!priceMap.TryGetValue(entry.SourceId.Trim(), out var price)
                || price.Unit == SecurityPriceUnit.Unspecified || !Enum.IsDefined(price.Unit))
                continue;
            var age = (int)(asOf - price.PriceAsOf).TotalDays;
            if (age <= entry.MaxDaysStale)
            {
                selected = price;
                stale = false;
                daysStale = age;
                break;
            }
            if (selected is null)
            {
                selected = price;
                stale = true;
                daysStale = age;
            }
        }
        if (selected is null)
            return null;

        var comparisons = priceMap.Values
            .Where(p => !string.Equals(p.SourceId, selected.SourceId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.SourceId, StringComparer.Ordinal)
            .Select(p => new SecurityComparisonPriceDto(p.SourceId, p.Price, p.PriceAsOf,
                p.Unit == selected.Unit && selected.Price != 0m
                    ? Math.Round((p.Price - selected.Price) / selected.Price * 100m, 4) : null,
                p.Unit)).ToArray();
        return new SecurityPriceGoldenCopyDto(securityId, selected.Price, SecurityPriceKind.MarketGoldenCopy,
            selected.SourceId, selected.PriceAsOf, stale, daysStale, comparisons, selected.Unit, asOf, hierarchy.AsOf, knowledgeAsOf);
    }

    public async Task<IReadOnlyList<SecurityComparisonPriceDto>> GetComparisonPricesAsync(
        Guid securityId, CancellationToken ct = default)
    {
        var asOf = DateTimeOffset.UtcNow;
        var prices = LatestEligible(await _store.GetRawPricesAsync(securityId, asOf, ct).ConfigureAwait(false), asOf);
        return prices.Values.OrderBy(p => p.SourceId, StringComparer.Ordinal)
            .Select(p => new SecurityComparisonPriceDto(p.SourceId, p.Price, p.PriceAsOf, null, p.Unit)).ToArray();
    }

    private static Dictionary<string, SecurityRawPriceDto> LatestEligible(
        IReadOnlyList<SecurityRawPriceDto> prices, DateTimeOffset asOf)
        => prices.Where(p => p.PriceAsOf <= asOf)
            .GroupBy(p => p.SourceId.Trim(), StringComparer.OrdinalIgnoreCase)
            // A same-time legacy case-variant conflict is ambiguous and cannot drive valuation.
            .Select(g => g.Where(p => p.PriceAsOf == g.Max(v => v.PriceAsOf)).ToArray())
            .Where(latest => latest.Select(p => (p.Price, p.Unit)).Distinct().Count() == 1)
            .Select(latest => latest.OrderBy(p => p.SourceId, StringComparer.Ordinal).First())
            .ToDictionary(p => p.SourceId.Trim(), StringComparer.OrdinalIgnoreCase);
}
