using Meridian.Execution.Services;

namespace Meridian.Strategies.Services;

/// <summary>
/// Implements <see cref="IAggregatePortfolioService"/> by querying the live
/// <see cref="PortfolioRegistry"/> for all active paper/live strategy runs.
/// </summary>
public sealed class AggregatePortfolioService : IAggregatePortfolioService
{
    private readonly PortfolioRegistry _registry;

    public AggregatePortfolioService(PortfolioRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <inheritdoc />
    public IReadOnlyList<AggregatedPosition> GetAggregatedPositions(IReadOnlyList<string>? runIds = null)
    {
        var all = _registry.GetAll();

        // Optionally filter to the requested run IDs.
        var entries = runIds is { Count: > 0 }
            ? all.Where(kv => runIds.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
            : all;

        // Accumulate per-symbol contributions.
        var symbolMap = new Dictionary<string, List<RunPositionContribution>>(
            StringComparer.OrdinalIgnoreCase);
        var symbolCostBasis = new Dictionary<string, (decimal totalQty, decimal totalWeightedCost, decimal totalUnrealised)>(
            StringComparer.OrdinalIgnoreCase);

        // The registry is keyed by run id, but several runs can share one portfolio
        // instance (the workstation host state is registered for the host and again for
        // each run that trades it). Counting that instance once per key would multiply
        // every position and, through the exposure snapshot, invent risk that does not exist.
        //
        // A shared instance is one book with no per-run split available, so its
        // contributions can only be labelled with one of the run ids sharing it. Ordering
        // the registry first makes that label deterministic rather than dependent on
        // dictionary enumeration order, so the same composition always reports the same
        // attribution. Splitting a shared book by owning run needs fill-level ownership the
        // portfolio does not carry.
        var seenPortfolios = new HashSet<Meridian.Execution.Models.IMultiAccountPortfolioState>(
            ReferenceEqualityComparer.Instance);
        foreach (var (runId, portfolio) in entries.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
        {
            if (!seenPortfolios.Add(portfolio))
            {
                continue;
            }

            foreach (var account in portfolio.Accounts)
            {
                foreach (var (symbol, pos) in account.Positions)
                {
                    var contribution = new RunPositionContribution(
                        runId, account.AccountId, pos.Quantity, pos.AverageCostBasis, pos.UnrealizedPnl);

                    if (!symbolMap.TryGetValue(symbol, out var list))
                    {
                        list = [];
                        symbolMap[symbol] = list;
                    }

                    list.Add(contribution);

                    if (!symbolCostBasis.TryGetValue(symbol, out var agg))
                        agg = (0m, 0m, 0m);

                    symbolCostBasis[symbol] = (
                        agg.totalQty + pos.Quantity,
                        agg.totalWeightedCost + pos.Quantity * pos.AverageCostBasis,
                        agg.totalUnrealised + pos.UnrealizedPnl);
                }
            }
        }

        return symbolMap.Select(kv =>
        {
            var symbol = kv.Key;
            var contributions = kv.Value;
            var (totalQty, totalWeightedCost, totalUnrealised) = symbolCostBasis[symbol];
            var longQty = contributions.Where(static c => c.Quantity > 0).Sum(static c => c.Quantity);
            var shortQty = contributions.Where(static c => c.Quantity < 0).Sum(static c => Math.Abs(c.Quantity));
            var wac = totalQty != 0m ? totalWeightedCost / totalQty : 0m;

            return new AggregatedPosition(
                Symbol: symbol,
                TotalQuantity: totalQty,
                LongQuantity: longQty,
                ShortQuantity: shortQty,
                WeightedAverageCost: wac,
                TotalUnrealisedPnl: totalUnrealised,
                Contributions: contributions);
        }).ToArray();
    }

    /// <inheritdoc />
    public CrossStrategyExposureReport GetCrossStrategyExposure()
    {
        var positions = GetAggregatedPositions();

        var longExposure = positions.Sum(static p => p.LongQuantity * p.WeightedAverageCost);
        var shortExposure = positions.Sum(static p => p.ShortQuantity * p.WeightedAverageCost);
        var grossExposure = longExposure + shortExposure;
        var netExposure = longExposure - shortExposure;

        var top5 = positions
            .OrderByDescending(static p => (p.LongQuantity + p.ShortQuantity) * p.WeightedAverageCost)
            .Take(5)
            .Select(static p => p.Symbol)
            .ToArray();

        return new CrossStrategyExposureReport(
            GrossExposure: grossExposure,
            NetExposure: netExposure,
            Top5Concentrations: top5,
            AsOf: DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public NetSymbolPosition GetNetPositionForSymbol(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        var all = _registry.GetAll();
        decimal net = 0m;
        decimal gross = 0m;

        foreach (var portfolio in all.Values)
        {
            foreach (var account in portfolio.Accounts)
            {
                if (account.Positions.TryGetValue(symbol, out var pos))
                {
                    net += pos.Quantity;
                    gross += Math.Abs(pos.Quantity);
                }
            }
        }

        return new NetSymbolPosition(symbol, net, gross);
    }
}
