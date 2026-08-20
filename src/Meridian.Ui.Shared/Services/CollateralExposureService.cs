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

/// <summary>
/// Holds the current collateral picture: one row per distinct exposure, replaced when a producer
/// reports that exposure again.
///
/// <para><b>Why current state and not a log.</b> <see cref="CollateralExposureService.BuildSnapshots"/>
/// sums mark-to-market, collateral balance, and margin across every row it is handed. That is correct
/// for a set of simultaneous positions and wrong for a history of observations: a producer posting
/// periodic refreshes for the same exposure would have its numbers added together, so two identical
/// refreshes reported twice the exposure and the figure kept climbing until eviction happened to
/// change it. Reads used to drain the buffer, which hid this; once reads stopped consuming, the
/// retained history became the reported total.</para>
///
/// <para><b>Exposure identity</b> is counterparty, product type, and collateral type — the same three
/// fields <c>BuildSnapshots</c> groups and resolves haircuts by. A delivery replaces every buffered
/// row whose identity it restates and keeps the rest, so a producer may refresh one counterparty
/// without erasing the others.</para>
///
/// <para><b>Rows within one delivery are never collapsed</b>, only rows from earlier deliveries are.
/// Two positions sharing an identity in a single batch are two positions and are summed; the same
/// identity arriving in a later batch is a restatement and supersedes. That is why the API takes a
/// batch: calling it once per row would make a delivery overwrite itself and silently under-report.</para>
/// </summary>
public sealed class CollateralIngestionBuffer
{
    private const int MaxBufferedRows = 20_000;
    private readonly Lock _gate = new();
    private readonly List<CollateralInputRow> _rows = [];

    public int BufferedCount
    {
        get
        {
            lock (_gate)
            {
                return _rows.Count;
            }
        }
    }

    /// <summary>
    /// Applies one delivery: rows restating a buffered exposure replace it, the rest are added.
    /// <para>
    /// The row cap remains as a backstop against a producer that invents unbounded identities;
    /// steady state is the number of distinct exposures, not the number of messages. Ingestion never
    /// refuses — refusing past capacity would freeze exposure at whatever a deployment saw first,
    /// and with non-consuming reads nothing would ever drain it.
    /// </para>
    /// </summary>
    public void IngestBatch(IReadOnlyList<CollateralInputRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            return;
        }

        var restated = new HashSet<string>(rows.Count, StringComparer.Ordinal);
        foreach (var row in rows)
        {
            restated.Add(ExposureKey(row));
        }

        lock (_gate)
        {
            _rows.RemoveAll(existing => restated.Contains(ExposureKey(existing)));
            _rows.AddRange(rows);
            if (_rows.Count > MaxBufferedRows)
            {
                _rows.RemoveRange(0, _rows.Count - MaxBufferedRows);
            }
        }
    }

    /// <summary>
    /// The whole current picture, without consuming it.
    /// <para>
    /// Every retained row is a live exposure, and <see cref="CollateralExposureService.BuildSnapshots"/>
    /// treats what it is handed as the complete set, so there is no honest read limit: any subset
    /// silently drops counterparties out of net exposure, coverage, and breach evaluation with nothing
    /// to signal it. The window cap is what bounds this — retention and reporting are the same number
    /// by construction rather than two limits that can drift apart.
    /// </para>
    /// <para>
    /// Reading must also leave the buffer intact. Draining on read made each snapshot cover only the
    /// rows arriving since the previous reader, so two operators looking at the same moment saw
    /// different exposure and the second frequently saw none.
    /// </para>
    /// </summary>
    public IReadOnlyList<CollateralInputRow> SnapshotCurrent()
    {
        lock (_gate)
        {
            return _rows.ToArray();
        }
    }

    // Lowercased and delimited the same way the haircut lookup keys are, so identity here and
    // haircut resolution there cannot drift apart on casing.
    private static string ExposureKey(CollateralInputRow row)
        => $"{row.Counterparty}:{row.ProductType}:{row.CollateralType}".ToLowerInvariant();
}
