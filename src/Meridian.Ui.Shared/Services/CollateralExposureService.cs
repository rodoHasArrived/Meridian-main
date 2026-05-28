using System.Collections.Concurrent;

namespace Meridian.Ui.Shared.Services;

public enum ThresholdSeverity
{
    Normal,
    EarlyWarning,
    HardBreach
}

public sealed record ExposureSnapshot(
    DateTimeOffset AsOf,
    string Counterparty,
    decimal NetExposure,
    decimal GrossExposure,
    IReadOnlyList<ProductExposure> ProductDecomposition,
    decimal CollateralBalance,
    decimal HaircutAdjustedCollateral,
    decimal RequiredCollateral,
    decimal CollateralCoverageRatio);

public sealed record ProductExposure(string ProductType, decimal NetExposure, decimal GrossExposure);

public sealed record MarginRequirement(string Counterparty, decimal RequiredCollateral, decimal InitialMargin, decimal VariationMargin);

public sealed record HaircutRule(string Counterparty, string CollateralType, decimal HaircutPercent);

public sealed record CollateralCall(string Counterparty, decimal Amount, string Reason, DateTimeOffset CreatedAt);

public sealed record ThresholdBreach(string Counterparty, ThresholdSeverity Severity, decimal CoverageRatio, decimal EarlyWarningLevel, decimal HardBreachLevel, string Message);

public sealed record CounterpartyThresholdPolicy(string Counterparty, decimal EarlyWarningCoverageRatio, decimal HardBreachCoverageRatio);

public sealed record CollateralInputRow(
    DateTimeOffset AsOf,
    string Counterparty,
    string ProductType,
    decimal PositionNotional,
    decimal MarkToMarket,
    decimal CollateralBalance,
    string CollateralType,
    decimal InitialMargin,
    decimal VariationMargin);

public sealed class CollateralExposureService
{
    private readonly ConcurrentDictionary<string, CounterpartyThresholdPolicy> _policies = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HaircutRule> _haircuts = new(StringComparer.OrdinalIgnoreCase);

    public CollateralExposureService()
    {
        UpsertThresholdPolicy(new CounterpartyThresholdPolicy("default", 1.10m, 1.00m));
        UpsertHaircutRule(new HaircutRule("default", "cash", 0m));
    }

    public void UpsertThresholdPolicy(CounterpartyThresholdPolicy policy)
        => _policies[policy.Counterparty] = policy;

    public void UpsertHaircutRule(HaircutRule rule)
        => _haircuts[$"{rule.Counterparty}:{rule.CollateralType}".ToLowerInvariant()] = rule;

    public IReadOnlyList<ExposureSnapshot> BuildSnapshots(IReadOnlyList<CollateralInputRow> rows)
    {
        return rows.GroupBy(r => r.Counterparty, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var asOf = group.Max(x => x.AsOf);
                var net = group.Sum(x => x.MarkToMarket);
                var gross = group.Sum(x => Math.Abs(x.MarkToMarket));
                var collateralBalance = group.Sum(x => x.CollateralBalance);
                var required = group.Sum(x => Math.Abs(x.InitialMargin) + Math.Abs(x.VariationMargin));
                var haircutAdjusted = group.Sum(x => x.CollateralBalance * (1m - ResolveHaircut(group.Key, x.CollateralType)));
                var byProduct = group.GroupBy(x => x.ProductType, StringComparer.OrdinalIgnoreCase)
                    .Select(pg => new ProductExposure(pg.Key, pg.Sum(x => x.MarkToMarket), pg.Sum(x => Math.Abs(x.MarkToMarket))))
                    .OrderByDescending(p => p.GrossExposure)
                    .ToArray();
                var ratio = required <= 0m ? 999m : haircutAdjusted / required;
                return new ExposureSnapshot(asOf, group.Key, net, gross, byProduct, collateralBalance, haircutAdjusted, required, ratio);
            })
            .OrderByDescending(x => x.GrossExposure)
            .ToArray();
    }

    public IReadOnlyList<ThresholdBreach> EvaluateBreaches(IReadOnlyList<ExposureSnapshot> snapshots)
    {
        var results = new List<ThresholdBreach>();
        foreach (var snapshot in snapshots)
        {
            var policy = ResolvePolicy(snapshot.Counterparty);
            var severity = snapshot.CollateralCoverageRatio < policy.HardBreachCoverageRatio
                ? ThresholdSeverity.HardBreach
                : snapshot.CollateralCoverageRatio < policy.EarlyWarningCoverageRatio
                    ? ThresholdSeverity.EarlyWarning
                    : ThresholdSeverity.Normal;
            if (severity == ThresholdSeverity.Normal)
            {
                continue;
            }

            var message = severity == ThresholdSeverity.HardBreach
                ? "Collateral coverage is below hard-breach threshold."
                : "Collateral coverage is below early-warning threshold.";
            results.Add(new ThresholdBreach(snapshot.Counterparty, severity, snapshot.CollateralCoverageRatio, policy.EarlyWarningCoverageRatio, policy.HardBreachCoverageRatio, message));
        }

        return results;
    }

    private CounterpartyThresholdPolicy ResolvePolicy(string counterparty)
        => _policies.TryGetValue(counterparty, out var policy) ? policy : _policies["default"];

    private decimal ResolveHaircut(string counterparty, string collateralType)
    {
        var key = $"{counterparty}:{collateralType}".ToLowerInvariant();
        if (_haircuts.TryGetValue(key, out var specific))
        {
            return specific.HaircutPercent;
        }

        var fallback = $"default:{collateralType}".ToLowerInvariant();
        return _haircuts.TryGetValue(fallback, out var @default) ? @default.HaircutPercent : 0m;
    }
}

public sealed class CollateralIngestionBuffer
{
    private const int MaxBufferedRows = 20_000;
    private readonly ConcurrentQueue<CollateralInputRow> _buffer = new();

    public int BufferedCount => _buffer.Count;

    public bool TryIngest(CollateralInputRow row)
    {
        if (_buffer.Count >= MaxBufferedRows)
        {
            return false;
        }

        _buffer.Enqueue(row);
        return true;
    }

    public IReadOnlyList<CollateralInputRow> DrainBatch(int maxItems = 500)
    {
        var rows = new List<CollateralInputRow>(maxItems);
        while (rows.Count < maxItems && _buffer.TryDequeue(out var row))
        {
            rows.Add(row);
        }

        return rows;
    }
}
