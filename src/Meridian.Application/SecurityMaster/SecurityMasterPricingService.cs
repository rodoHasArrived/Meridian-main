using System.Collections.Generic;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;



namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Implements the Clearwater pricing hierarchy model: golden copy selection, stale-price
/// fallback, calculated-price rules for specific asset classes, and multi-source comparison.
/// </summary>
public sealed class SecurityMasterPricingService : ISecurityMasterPricingService
{
    private readonly ISecurityMasterPricingStore _store;
    private readonly ISecurityMasterQueryService _queryService;
    private readonly ILogger<SecurityMasterPricingService> _logger;

    public SecurityMasterPricingService(
        ISecurityMasterPricingStore store,
        ISecurityMasterQueryService queryService,
        ILogger<SecurityMasterPricingService> logger)
    {
        _store = store;
        _queryService = queryService;
        _logger = logger;
    }

    public Task<SecurityPricingHierarchyDto?> GetPricingHierarchyAsync(
        Guid securityId, string? accountId, CancellationToken ct = default)
        => _store.GetHierarchyAsync(securityId, accountId, ct);

    public Task UpsertPricingHierarchyAsync(
        SecurityPricingHierarchyDto hierarchy, CancellationToken ct = default)
        => _store.UpsertHierarchyAsync(hierarchy, ct);

    public Task RecordRawPriceAsync(RecordRawPriceRequest request, CancellationToken ct = default)
        => _store.RecordRawPriceAsync(
            request.SecurityId, request.SourceId, request.Price, request.PriceAsOf, request.RecordedBy, ct);

    public async Task<SecurityPriceGoldenCopyDto?> GetGoldenCopyPriceAsync(
        Guid securityId, string? accountId, CancellationToken ct = default)
    {
        var security = await _queryService.GetByIdAsync(securityId, ct).ConfigureAwait(false);
        if (security is null)
            return null;

        // Calculated-price rules: certain asset classes are priced without external sources.
        if (TryGetCalculatedPrice(security.AssetClass, out var calculatedKind, out var calculatedPrice))
        {
            return new SecurityPriceGoldenCopyDto(
                securityId, calculatedPrice, calculatedKind, "calculated",
                DateTimeOffset.UtcNow, false, null, []);
        }

        var hierarchy = await _store.GetHierarchyAsync(securityId, accountId, ct).ConfigureAwait(false);
        if (hierarchy is null || hierarchy.Entries.Count == 0)
        {
            _logger.LogDebug("No pricing hierarchy configured for security {SecurityId}.", securityId);
            return null;
        }

        var rawPrices = await _store.GetRawPricesAsync(securityId, ct).ConfigureAwait(false);
        var priceMap = rawPrices.ToDictionary(
            p => p.SourceId, p => (p.Price, p.PriceAsOf), StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        var orderedEntries = hierarchy.Entries.OrderBy(e => e.Priority);

        (string SourceId, decimal Price, DateTimeOffset PriceAsOf, bool IsStaleFallback, int? DaysStale)? selected = null;

        foreach (var entry in orderedEntries)
        {
            if (!priceMap.TryGetValue(entry.SourceId, out var rawPrice))
                continue;

            var age = (int)(now - rawPrice.PriceAsOf).TotalDays;
            if (age <= entry.MaxDaysStale)
            {
                selected = (entry.SourceId, rawPrice.Price, rawPrice.PriceAsOf, false, age);
                break;
            }

            // Source is stale; carry as fallback candidate if nothing better found.
            if (selected is null)
                selected = (entry.SourceId, rawPrice.Price, rawPrice.PriceAsOf, true, age);
        }

        if (selected is null)
            return null;

        var comparison = BuildComparisons(selected.Value.SourceId, selected.Value.Price, priceMap);

        return new SecurityPriceGoldenCopyDto(
            securityId,
            selected.Value.Price,
            SecurityPriceKind.MarketGoldenCopy,
            selected.Value.SourceId,
            selected.Value.PriceAsOf,
            selected.Value.IsStaleFallback,
            selected.Value.DaysStale,
            comparison);
    }

    public async Task<IReadOnlyList<SecurityComparisonPriceDto>> GetComparisonPricesAsync(
        Guid securityId, CancellationToken ct = default)
    {
        var rawPrices = await _store.GetRawPricesAsync(securityId, ct).ConfigureAwait(false);
        return rawPrices
            .Select(p => new SecurityComparisonPriceDto(p.SourceId, p.Price, p.PriceAsOf, null))
            .ToList();
    }

    private static bool TryGetCalculatedPrice(
        string assetClass, out SecurityPriceKind kind, out decimal price)
    {
        // Repo and auction-rate: priced at par per Clearwater calculated-price rules.
        if (string.Equals(assetClass, "Repo", StringComparison.OrdinalIgnoreCase))
        {
            kind = SecurityPriceKind.CalculatedPar;
            price = 100m;
            return true;
        }

        // Commercial paper and certificates of deposit use straight-line accretion to par.
        // The service returns par here; callers requiring full accretion use the ledger projector.
        if (string.Equals(assetClass, "CommercialPaper", StringComparison.OrdinalIgnoreCase)
            || string.Equals(assetClass, "CertificateOfDeposit", StringComparison.OrdinalIgnoreCase))
        {
            kind = SecurityPriceKind.CalculatedStraightLine;
            price = 100m;
            return true;
        }

        // Non-institutional-prime money-market funds: priced at 1.0 (stable NAV).
        if (string.Equals(assetClass, "MoneyMarketFund", StringComparison.OrdinalIgnoreCase))
        {
            kind = SecurityPriceKind.CalculatedPar;
            price = 1m;
            return true;
        }

        kind = default;
        price = default;
        return false;
    }

    private static IReadOnlyList<SecurityComparisonPriceDto> BuildComparisons(
        string goldenSourceId,
        decimal goldenPrice,
        Dictionary<string, (decimal Price, DateTimeOffset PriceAsOf)> priceMap)
    {
        var result = new List<SecurityComparisonPriceDto>();
        foreach (var (sourceId, (price, asOf)) in priceMap)
        {
            if (string.Equals(sourceId, goldenSourceId, StringComparison.OrdinalIgnoreCase))
                continue;

            decimal? pctDiff = goldenPrice != 0m
                ? Math.Round((price - goldenPrice) / goldenPrice * 100m, 4)
                : null;

            result.Add(new SecurityComparisonPriceDto(sourceId, price, asOf, pctDiff));
        }

        return result;
    }
}
