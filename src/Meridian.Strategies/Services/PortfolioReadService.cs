using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Models;

namespace Meridian.Strategies.Services;

/// <summary>
/// Builds workstation-facing portfolio read models from recorded run results.
/// </summary>
public sealed class PortfolioReadService
{
    private readonly ISecurityReferenceLookup? _securityReferenceLookup;

    public PortfolioReadService()
    {
    }

    public PortfolioReadService(ISecurityReferenceLookup securityReferenceLookup)
    {
        _securityReferenceLookup = securityReferenceLookup ?? throw new ArgumentNullException(nameof(securityReferenceLookup));
    }

    public PortfolioSummary? BuildSummary(StrategyRunEntry entry)
        => BuildBaseSummary(entry);

    public async Task<PortfolioSummary?> BuildSummaryAsync(StrategyRunEntry entry, CancellationToken ct = default)
    {
        var summary = BuildBaseSummary(entry);
        if (summary is null || _securityReferenceLookup is null || summary.Positions.Count == 0)
        {
            return summary;
        }

        var lookupRequests = BuildLookupRequests(entry, summary);
        var lookup = await ResolveSecuritiesAsync(lookupRequests, ct).ConfigureAwait(false);

        var positions = summary.Positions
            .Select(position => position with
            {
                Security = lookup.GetValueOrDefault(position.Symbol)
            })
            .ToArray();

        var resolvedCount = lookup.Values.Count(static value => value is not null);
        var missingCount = lookup.Count - resolvedCount;

        return summary with
        {
            Positions = positions,
            SecurityResolvedCount = resolvedCount,
            SecurityMissingCount = missingCount
        };
    }

    private static PortfolioSummary? BuildBaseSummary(StrategyRunEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var result = entry.Metrics;
        var latestSnapshot = result?.Snapshots.LastOrDefault();
        if (latestSnapshot is null)
        {
            return null;
        }

        var positions = latestSnapshot.Positions.Values
            .OrderBy(static position => position.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(static position => new PortfolioPositionSummary(
                Symbol: position.Symbol,
                Quantity: position.Quantity,
                AverageCostBasis: position.AverageCostBasis,
                RealizedPnl: position.RealizedPnl,
                UnrealizedPnl: position.UnrealizedPnl,
                IsShort: position.IsShort))
            .ToArray();

        var longMarketValue = latestSnapshot.LongMarketValue;
        var shortMarketValue = latestSnapshot.ShortMarketValue;
        var grossExposure = longMarketValue + Math.Abs(shortMarketValue);
        var netExposure = longMarketValue + shortMarketValue;
        var realizedPnl = positions.Sum(static position => position.RealizedPnl);
        var unrealizedPnl = positions.Sum(static position => position.UnrealizedPnl);
        var financing = result!.Metrics.TotalMarginInterest - result.Metrics.TotalShortRebates;

        return new PortfolioSummary(
            PortfolioId: entry.PortfolioId ?? entry.RunId,
            RunId: entry.RunId,
            AsOf: latestSnapshot.Timestamp,
            Cash: latestSnapshot.Cash,
            LongMarketValue: longMarketValue,
            ShortMarketValue: shortMarketValue,
            GrossExposure: grossExposure,
            NetExposure: netExposure,
            TotalEquity: latestSnapshot.TotalEquity,
            RealizedPnl: realizedPnl,
            UnrealizedPnl: unrealizedPnl,
            Commissions: result.Metrics.TotalCommissions,
            Financing: financing,
            Positions: positions);
    }

    private static IReadOnlyDictionary<string, SecurityReferenceLookupRequest> BuildLookupRequests(
        StrategyRunEntry entry,
        PortfolioSummary summary)
    {
        var symbolsToSecurityId = entry.Metrics?.Ledger?.Journal
            .Where(static item => !string.IsNullOrWhiteSpace(item.Metadata.Symbol))
            .GroupBy(
                static item => item.Metadata.Symbol!,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group =>
                {
                    var candidates = group
                        .Select(static entry => entry.Metadata.SecurityId)
                        .Where(static id => id.HasValue)
                        .Select(static id => id!.Value)
                        .Distinct()
                        .ToArray();
                    return candidates.Length == 1 ? candidates[0] : (Guid?)null;
                },
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);

        return summary.Positions
            .Where(static position => !string.IsNullOrWhiteSpace(position.Symbol))
            .GroupBy(static position => position.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                group =>
                {
                    var symbol = group.Key;
                    var resolvedSecurityId = symbolsToSecurityId.GetValueOrDefault(symbol);
                    var source = resolvedSecurityId is null ? "portfolio-position-symbol" : "ledger-metadata-security-id";
                    return new SecurityReferenceLookupRequest(
                        SecurityId: resolvedSecurityId,
                        IdentifierKind: SecurityIdentifierKind.Ticker.ToString(),
                        IdentifierValue: symbol,
                        Symbol: symbol,
                        Source: source);
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, WorkstationSecurityReference?>> ResolveSecuritiesAsync(
        IReadOnlyDictionary<string, SecurityReferenceLookupRequest> requests,
        CancellationToken ct)
    {
        var lookup = new Dictionary<string, WorkstationSecurityReference?>(StringComparer.OrdinalIgnoreCase);
        if (_securityReferenceLookup is null)
        {
            return lookup;
        }

        foreach (var (symbol, request) in requests)
        {
            var resolved = await _securityReferenceLookup.GetByCanonicalAsync(request, ct).ConfigureAwait(false)
                ?? await _securityReferenceLookup.GetBySymbolAsync(symbol, ct).ConfigureAwait(false);

            lookup[symbol] = resolved is null
                ? null
                : resolved with
                {
                    LookupSource = request.Source,
                    LookupPath = resolved.LookupPath ?? (request.SecurityId is null ? "symbol" : "security-id"),
                    IsInferredMatch = request.SecurityId is null && string.IsNullOrWhiteSpace(request.IdentifierValue)
                };
        }

        return lookup;
    }
}
