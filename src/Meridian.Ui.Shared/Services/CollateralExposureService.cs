using System.Collections.Concurrent;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;

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
    private readonly ConcurrentDictionary<(string Counterparty, string CollateralType), HaircutRule> _haircuts = new();

    public CollateralExposureService()
    {
        UpsertThresholdPolicy(new CounterpartyThresholdPolicy("default", 1.10m, 1.00m));
        UpsertHaircutRule(new HaircutRule("default", "cash", 0m));
    }

    public void UpsertThresholdPolicy(CounterpartyThresholdPolicy policy)
        => _policies[policy.Counterparty] = policy;

    // Keyed by compared fields for the same reason the ingestion buffer is: a counterparty or
    // collateral type containing the delimiter would otherwise collide with a different pair and
    // resolve the wrong haircut against real collateral.
    public void UpsertHaircutRule(HaircutRule rule)
        => _haircuts[HaircutKey(rule.Counterparty, rule.CollateralType)] = rule;

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
                var ratio = ResolveCoverageRatio(haircutAdjusted, required);
                return new ExposureSnapshot(asOf, group.Key, net, gross, byProduct, collateralBalance, haircutAdjusted, required, ratio);
            })
            .OrderByDescending(x => x.GrossExposure)
            .ToArray();
    }

    /// <summary>
    /// Collateral coverage, saturating at <see cref="MaxReportedCoverageRatio"/> instead of dividing
    /// without bound.
    /// <para>
    /// The quotient is the one place in <see cref="BuildSnapshots"/> that the ingest value cap cannot
    /// bound: a requirement small enough relative to posted collateral drives it past
    /// <see cref="decimal.MaxValue"/> and throws, and because the ingestion buffer is non-consuming
    /// the row that caused it stays current and takes the tenant's exposure read down with it on
    /// every later request.
    /// </para>
    /// <para>
    /// Saturating loses nothing: the ratio is only ever compared against coverage thresholds near
    /// 1.0, so every value above the ceiling classifies identically -- which is why the
    /// no-requirement case already reports the same number. The comparison is written as a product
    /// rather than a quotient because the multiplication is bounded by the ingest cap and the
    /// buffer's row cap, and the division is not.
    /// </para>
    /// </summary>
    private static decimal ResolveCoverageRatio(decimal haircutAdjusted, decimal required)
    {
        if (required <= 0m)
        {
            return MaxReportedCoverageRatio;
        }

        return haircutAdjusted >= required * MaxReportedCoverageRatio
            ? MaxReportedCoverageRatio
            : haircutAdjusted / required;
    }

    /// <summary>
    /// The coverage figure reported for an exposure whose requirement is zero or negligible against
    /// the collateral posted. Retained at the value the zero-requirement case has always used, so
    /// the ceiling does not change how any existing threshold classifies.
    /// </summary>
    private const decimal MaxReportedCoverageRatio = 999m;

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
        if (_haircuts.TryGetValue(HaircutKey(counterparty, collateralType), out var specific))
        {
            return specific.HaircutPercent;
        }

        return _haircuts.TryGetValue(HaircutKey("default", collateralType), out var @default)
            ? @default.HaircutPercent
            : 0m;
    }

    private static (string Counterparty, string CollateralType) HaircutKey(string? counterparty, string? collateralType)
        => ((counterparty ?? string.Empty).ToLowerInvariant(), (collateralType ?? string.Empty).ToLowerInvariant());
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
/// <para><b>Simultaneous positions are never collapsed.</b> Rows sharing an identity <em>and</em> an
/// <c>AsOf</c> are two positions observed at one moment and are summed. Rows sharing an identity at
/// different times are a restatement and a straggler, and only the newest observation survives —
/// whether the straggler arrives in a later delivery or in the same one. That is why the API takes a
/// batch: calling it once per row would make a delivery overwrite itself and silently under-report.</para>
///
/// <para><b>An observation may arrive in pieces.</b> The ingest route caps a single request, so an
/// observation with more rows than that cap has to be split across requests. A later delivery at the
/// same <c>AsOf</c> therefore continues the observation already held rather than replacing it, and
/// incoming rows are matched one-for-one against what is held so a redelivered chunk adds nothing.
/// Only a strictly newer <c>AsOf</c> replaces.</para>
///
/// <para><b>State is partitioned by tenant scope.</b> The buffer is a process-wide singleton, so
/// without a scope key one tenant's ingest would appear in another's exposure and a same-identity
/// restatement from either would overwrite the other's current reading. The scope is resolved
/// server-side from the request, never from the payload. A deployment with no tenancy resolves one
/// empty scope and behaves exactly as before.</para>
/// </summary>
/// <summary>
/// The tenant partition a collateral reading belongs to, resolved server-side from the request rather
/// than taken from the payload. An unscoped deployment resolves a single empty scope.
/// </summary>
public readonly record struct CollateralTenantScope(string TenantId, string CompanyId)
{
    public static readonly CollateralTenantScope Unscoped = new(string.Empty, string.Empty);

    public static CollateralTenantScope For(string? tenantId, string? companyId)
        => new(Fold(tenantId), Fold(companyId));

    /// <summary>
    /// Resolves the scope from the request, mirroring <see cref="WorkstationWorkflowReadScope.ForRequest"/>.
    /// The tenant comes from what the server resolved for the caller, never from the ingest payload --
    /// a payload-supplied tenant would let a producer write into another tenant's exposure.
    /// </summary>
    public static CollateralTenantScope ForRequest(HttpContext context)
    {
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        return For(tenant.TenantId, tenant.CompanyId);
    }

    private static string Fold(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}

public sealed class CollateralIngestionBuffer
{
    private const int MaxBufferedRows = 20_000;
    private readonly Lock _gate = new();
    private readonly Dictionary<CollateralTenantScope, List<CollateralInputRow>> _byScope = [];

    /// <summary>
    /// Rows currently held for one tenant scope. Scoped rather than global because a count spanning
    /// tenants would be a number no operator can act on.
    /// </summary>
    public int BufferedCount(CollateralTenantScope scope)
    {
        lock (_gate)
        {
            return _byScope.TryGetValue(scope, out var rows) ? rows.Count : 0;
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
    public void IngestBatch(CollateralTenantScope scope, IReadOnlyList<CollateralInputRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            return;
        }

        // Arrival order is not observation order -- a delayed retry can land after a newer refresh --
        // so the winner is decided by AsOf, not by which delivery arrived last. Without this a stale
        // redelivery would reinstate old exposure and regress coverage and breach state.
        var offered = new Dictionary<ExposureIdentity, DateTimeOffset>();
        foreach (var row in rows)
        {
            var key = ExposureKey(row);
            if (!offered.TryGetValue(key, out var seen) || row.AsOf > seen)
            {
                offered[key] = row.AsOf;
            }
        }

        lock (_gate)
        {
            if (!_byScope.TryGetValue(scope, out var held0))
            {
                held0 = [];
                _byScope[scope] = held0;
            }

            var held = new Dictionary<ExposureIdentity, DateTimeOffset>();
            foreach (var existing in held0)
            {
                var key = ExposureKey(existing);
                if (offered.ContainsKey(key) &&
                    (!held.TryGetValue(key, out var seen) || existing.AsOf > seen))
                {
                    held[key] = existing.AsOf;
                }
            }

            // Three outcomes per identity, decided by observation time against what is already held.
            // Strictly newer is a restatement and replaces; strictly older is a straggler and is
            // dropped; equal continues the observation already held rather than replacing it.
            //
            // Equal used to replace, on the reading that a same-timestamp delivery is a redelivery of
            // the same rows. That is one of two things it can be, and the other one loses data: an
            // observation with more rows than the ingest route accepts in one request must be split
            // across requests, and each later chunk deleted the chunks before it, leaving the snapshot
            // reporting only the last -- silently short rather than visibly missing. Continuing is
            // correct for the chunk case and no worse for the redelivery case, because incoming rows
            // are matched against what is held and only the unmatched remainder is added.
            var stale = new HashSet<ExposureIdentity>();
            var continued = new HashSet<ExposureIdentity>();
            foreach (var (key, asOf) in offered)
            {
                if (!held.TryGetValue(key, out var current))
                {
                    continue;
                }

                if (current > asOf)
                {
                    stale.Add(key);
                }
                else if (current == asOf)
                {
                    continued.Add(key);
                }
            }

            held0.RemoveAll(existing =>
            {
                var key = ExposureKey(existing);
                return offered.ContainsKey(key) && !stale.Contains(key) && !continued.Contains(key);
            });

            // Rows already standing for a continued observation, counted rather than set-tested: a
            // producer may legitimately report two identical simultaneous positions, so a second copy
            // within one delivery is a real position while a copy of a row already held is a
            // redelivered one. Matching by count keeps both cases right.
            var alreadyHeld = new Dictionary<CollateralInputRow, int>();
            if (continued.Count > 0)
            {
                foreach (var existing in held0)
                {
                    if (!continued.Contains(ExposureKey(existing)))
                    {
                        continue;
                    }

                    alreadyHeld[existing] = alreadyHeld.TryGetValue(existing, out var count) ? count + 1 : 1;
                }
            }

            foreach (var row in rows)
            {
                var key = ExposureKey(row);

                // Per exposure, not per batch: a delivery restating one exposure with a stale reading
                // and another with a fresh one keeps the fresh half rather than being dropped whole.
                if (stale.Contains(key))
                {
                    continue;
                }

                // And within the winning identity, only the winning observation. A batch carrying rows
                // for one identity at two different times is not two simultaneous positions -- it is a
                // restatement plus a straggler, and summing both would fold a superseded position back
                // into exposure. Rows sharing the winning AsOf are the simultaneous case and all survive.
                if (row.AsOf != offered[key])
                {
                    continue;
                }

                if (alreadyHeld.TryGetValue(row, out var remaining) && remaining > 0)
                {
                    alreadyHeld[row] = remaining - 1;
                    continue;
                }

                held0.Add(row);
            }

            EvictWholeObservations(held0);
        }
    }

    /// <summary>
    /// Drops entire observations from the oldest end until the window fits.
    /// <para>
    /// Whole observations, not whole rows: an observation may be several simultaneous positions
    /// sharing an identity and an <c>AsOf</c>, and
    /// <see cref="CollateralExposureService.BuildSnapshots"/> treats whatever survives as the complete
    /// current exposure. Trimming by row count could cut through such a set — an overage of one leaves
    /// one of two positions standing — and the snapshot would silently understate mark-to-market,
    /// collateral and margin rather than reporting a counterparty as missing.
    /// </para>
    /// </summary>
    private static void EvictWholeObservations(List<CollateralInputRow> rows)
    {
        if (rows.Count <= MaxBufferedRows)
        {
            return;
        }

        // One pass for the whole overage, not one per evicted observation. A full batch arriving at the
        // cap would otherwise drive a full minimum scan and a full RemoveAll per excess observation --
        // tens of millions of comparisons while holding the gate, which blocks every snapshot and
        // ingest in the process.
        var sizes = new Dictionary<(ExposureIdentity Identity, DateTimeOffset AsOf), int>();
        foreach (var row in rows)
        {
            var key = (ExposureKey(row), row.AsOf);
            sizes[key] = sizes.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        // Oldest by observation time, not by insertion order: a delayed first reading can be inserted
        // after a current one, and trimming from the head would discard the current exposure and keep
        // the stale arrival.
        var doomed = new HashSet<(ExposureIdentity Identity, DateTimeOffset AsOf)>();
        var surplus = rows.Count - MaxBufferedRows;
        foreach (var entry in sizes.OrderBy(static entry => entry.Key.AsOf))
        {
            if (surplus <= 0)
            {
                break;
            }

            doomed.Add(entry.Key);
            surplus -= entry.Value;
        }

        rows.RemoveAll(row => doomed.Contains((ExposureKey(row), row.AsOf)));
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
    public IReadOnlyList<CollateralInputRow> SnapshotCurrent(CollateralTenantScope scope)
    {
        lock (_gate)
        {
            return _byScope.TryGetValue(scope, out var rows) ? rows.ToArray() : [];
        }
    }

    private static ExposureIdentity ExposureKey(CollateralInputRow row)
        => ExposureIdentity.For(row.Counterparty, row.ProductType, row.CollateralType);
}

/// <summary>
/// Counterparty, product type, and collateral type as three compared fields rather than one joined
/// string. Joining with a delimiter is not injective when a component may contain it: the ingest route
/// accepts these values verbatim, so <c>("A:B", "C", "cash")</c> and <c>("A", "B:C", "cash")</c> would
/// share a key, and a delivery for either would evict the other — silently understating exposure.
/// <para>
/// Case is folded once here because <see cref="CollateralExposureService.BuildSnapshots"/> groups
/// case-insensitively; comparing the folded fields keeps identity and aggregation in agreement.
/// </para>
/// </summary>
internal readonly record struct ExposureIdentity(string Counterparty, string ProductType, string CollateralType)
{
    public static ExposureIdentity For(string? counterparty, string? productType, string? collateralType)
        => new(Fold(counterparty), Fold(productType), Fold(collateralType));

    private static string Fold(string? value) => (value ?? string.Empty).ToLowerInvariant();
}
