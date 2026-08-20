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

    /// <summary>
    /// Buffers a row, evicting the oldest once the window is full. The buffer is a bounded
    /// most-recent window rather than a queue awaiting a consumer: exposure is an aggregate of what
    /// is buffered, so the newest rows are the ones that must survive, and refusing new rows to
    /// preserve old ones would freeze exposure at whatever the deployment happened to see first.
    /// <para>
    /// Ingestion therefore never stalls. The previous refusal-past-capacity behaviour paired with a
    /// consuming read; with a non-consuming read it would have made every ingest fail permanently
    /// once the window filled.
    /// </para>
    /// </summary>
    public void Ingest(CollateralInputRow row)
    {
        _buffer.Enqueue(row);

        // Count is approximate under concurrency, which is fine: the loop self-corrects on the next
        // ingest and the window is a bound, not an exact size.
        while (_buffer.Count > MaxBufferedRows && _buffer.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    /// The most recent buffered rows, without consuming them. Exposure is an aggregate of what is
    /// buffered, so reading must leave the buffer intact: draining on read made each snapshot cover
    /// only the rows arriving since the previous reader, so two operators looking at the same moment
    /// saw different exposure and the second frequently saw none.
    /// <para>
    /// Most recent, not first buffered. Taking from the head would pin the snapshot to the oldest
    /// rows and silently exclude everything after the first <paramref name="maxItems"/>, so exposure
    /// would stop tracking current collateral the moment the window exceeded the read limit.
    /// </para>
    /// </summary>
    public IReadOnlyList<CollateralInputRow> SnapshotRows(int maxItems = 500)
    {
        if (maxItems <= 0)
        {
            return [];
        }

        // Point-in-time snapshot, oldest first; the window is bounded, so materializing is cheap.
        var buffered = _buffer.ToArray();
        return buffered.Length <= maxItems ? buffered : buffered[^maxItems..];
    }
}
