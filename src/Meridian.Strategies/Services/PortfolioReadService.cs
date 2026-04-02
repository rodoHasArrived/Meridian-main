using Meridian.Backtesting.Sdk;
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

        var lookup = await ResolveSecuritiesAsync(
                summary.Positions
                    .Select(static position => position.Symbol)
                    .Where(static symbol => !string.IsNullOrWhiteSpace(symbol)),
                ct)
            .ConfigureAwait(false);

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

    /// <summary>
    /// Builds a <see cref="RunLotSummary"/> from the most-recent snapshot's lot data,
    /// optionally filtered to a single <paramref name="symbol"/>.
    /// Returns <see langword="null"/> when the run has no recorded snapshots.
    /// </summary>
    public RunLotSummary? BuildLotSummary(StrategyRunEntry entry, string? symbol = null, DateTimeOffset? asOf = null)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var result = entry.Metrics;
        var latestSnapshot = result?.Snapshots.LastOrDefault();
        if (latestSnapshot is null)
        {
            return null;
        }

        var openLots = (latestSnapshot.OpenLots ?? [])
            .Where(l => symbol is null || string.Equals(l.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
            .Select(l => new OpenLotSummary(
                LotId: l.LotId,
                Symbol: l.Symbol,
                Quantity: l.Quantity,
                EntryPrice: l.EntryPrice,
                OpenedAt: l.OpenedAt,
                CurrentUnrealizedPnl: 0m,  // price not available in summary context
                IsLongTerm: l.IsLongTerm(asOf ?? latestSnapshot.Timestamp),
                AccountId: l.AccountId))
            .ToArray();

        var closedLots = (latestSnapshot.ClosedLots ?? [])
            .Where(l => symbol is null || string.Equals(l.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
            .Select(static l => new ClosedLotSummary(
                LotId: l.LotId,
                Symbol: l.Symbol,
                Quantity: l.Quantity,
                EntryPrice: l.EntryPrice,
                ClosePrice: l.ClosePrice,
                OpenedAt: l.OpenedAt,
                ClosedAt: l.ClosedAt,
                RealizedPnl: l.RealizedPnl,
                IsLongTerm: l.IsLongTerm,
                AccountId: l.AccountId))
            .ToArray();

        return new RunLotSummary(
            RunId: entry.RunId,
            TotalOpenLots: openLots.Length,
            TotalClosedLots: closedLots.Length,
            TotalRealizedPnl: closedLots.Sum(static cl => cl.RealizedPnl),
            OpenLots: openLots,
            ClosedLots: closedLots);
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

        // Include lot summaries when available in the snapshot.
        OpenLotSummary[]? openLotSummaries = null;
        ClosedLotSummary[]? closedLotSummaries = null;

        if (latestSnapshot.OpenLots is { Count: > 0 } openLots)
        {
            openLotSummaries = openLots
                .Select(l => new OpenLotSummary(
                    LotId: l.LotId,
                    Symbol: l.Symbol,
                    Quantity: l.Quantity,
                    EntryPrice: l.EntryPrice,
                    OpenedAt: l.OpenedAt,
                    CurrentUnrealizedPnl: 0m,
                    IsLongTerm: l.IsLongTerm(latestSnapshot.Timestamp),
                    AccountId: l.AccountId))
                .ToArray();
        }

        if (latestSnapshot.ClosedLots is { Count: > 0 } closedLots)
        {
            closedLotSummaries = closedLots
                .Select(static l => new ClosedLotSummary(
                    LotId: l.LotId,
                    Symbol: l.Symbol,
                    Quantity: l.Quantity,
                    EntryPrice: l.EntryPrice,
                    ClosePrice: l.ClosePrice,
                    OpenedAt: l.OpenedAt,
                    ClosedAt: l.ClosedAt,
                    RealizedPnl: l.RealizedPnl,
                    IsLongTerm: l.IsLongTerm,
                    AccountId: l.AccountId))
                .ToArray();
        }

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
            Positions: positions,
            FundProfileId: entry.FundProfileId,
            OpenLots: openLotSummaries,
            ClosedLots: closedLotSummaries);
    }

    private async Task<Dictionary<string, WorkstationSecurityReference?>> ResolveSecuritiesAsync(
        IEnumerable<string> symbols,
        CancellationToken ct)
    {
        var lookup = new Dictionary<string, WorkstationSecurityReference?>(StringComparer.OrdinalIgnoreCase);
        if (_securityReferenceLookup is null)
        {
            return lookup;
        }

        foreach (var symbol in symbols.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            lookup[symbol] = await _securityReferenceLookup
                .GetBySymbolAsync(symbol, ct)
                .ConfigureAwait(false);
        }

        return lookup;
    }
}
