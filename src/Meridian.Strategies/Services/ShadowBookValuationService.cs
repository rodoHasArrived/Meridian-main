using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

public interface IShadowBookValuationService
{
    Task<ShadowBookValuationResult> RunAsync(ShadowBookValuationRequest request, CancellationToken ct = default);
}

public sealed record ShadowBookValuationRequest(
    string AccountId,
    string StrategyProfile,
    DateOnly PeriodEnd,
    IReadOnlyList<ShadowBookPositionInput> Positions,
    IReadOnlyDictionary<string, decimal> PricesBySymbol,
    IReadOnlyDictionary<string, decimal> FxRatesByCurrency,
    decimal Fees,
    decimal Accruals,
    decimal ExternalReferenceNav,
    bool BlockCloseOnBreach,
    string OperatorId);

public sealed record ShadowBookPositionInput(string Symbol, decimal Quantity, string PriceCurrency);
public sealed record ShadowBookVarianceThresholds(decimal AbsoluteMateriality, decimal BpsMateriality);
public sealed record ShadowBookRunSnapshot(string RunId, string VersionHash, DateTimeOffset CreatedAt, string AccountId, DateOnly PeriodEnd, decimal ShadowNav, decimal ExternalNav, decimal VarianceAmount, decimal VarianceBps, bool Breached);
public sealed record ShadowBookValuationResult(ShadowBookRunSnapshot Snapshot, bool CloseBlocked, ReconciliationBreakQueueItem? EmittedBreak);

public sealed class ShadowBookValuationService : IShadowBookValuationService
{
    private readonly IReconciliationBreakQueueRepository _breakQueueRepository;
    private readonly Dictionary<string, ShadowBookVarianceThresholds> _thresholds;
    private readonly List<ShadowBookRunSnapshot> _snapshots = [];

    public ShadowBookValuationService(IReconciliationBreakQueueRepository breakQueueRepository, IReadOnlyDictionary<string, ShadowBookVarianceThresholds>? thresholds = null)
    {
        _breakQueueRepository = breakQueueRepository;
        _thresholds = new(StringComparer.OrdinalIgnoreCase)
        {
            ["default"] = new ShadowBookVarianceThresholds(100m, 25m)
        };

        if (thresholds is not null)
        {
            foreach (var pair in thresholds)
            {
                _thresholds[pair.Key] = pair.Value;
            }
        }
    }

    public Task<ShadowBookValuationResult> RunAsync(ShadowBookValuationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var threshold = _thresholds.TryGetValue(request.StrategyProfile, out var configured)
            ? configured
            : _thresholds["default"];

        decimal gross = 0m;
        foreach (var position in request.Positions.OrderBy(p => p.Symbol, StringComparer.Ordinal))
        {
            var px = request.PricesBySymbol[position.Symbol];
            var fx = request.FxRatesByCurrency.GetValueOrDefault(position.PriceCurrency, 1m);
            gross += position.Quantity * px * fx;
        }

        var shadowNav = gross - request.Fees + request.Accruals;
        var varianceAmount = shadowNav - request.ExternalReferenceNav;
        var varianceBps = request.ExternalReferenceNav == 0m ? 0m : varianceAmount / request.ExternalReferenceNav * 10_000m;
        var breached = Math.Abs(varianceAmount) >= threshold.AbsoluteMateriality || Math.Abs(varianceBps) >= threshold.BpsMateriality;

        var hash = ComputeHash(request, shadowNav, varianceAmount, varianceBps);
        var snapshot = new ShadowBookRunSnapshot(Guid.NewGuid().ToString("N"), hash, DateTimeOffset.UtcNow, request.AccountId, request.PeriodEnd, shadowNav, request.ExternalReferenceNav, varianceAmount, varianceBps, breached);
        _snapshots.Add(snapshot);

        ReconciliationBreakQueueItem? emittedBreak = null;
        if (breached)
        {
            emittedBreak = new ReconciliationBreakQueueItem(
                BreakId: $"shadow-nav:{request.AccountId}:{request.PeriodEnd:yyyyMMdd}:{hash[..12]}",
                RunId: $"shadow-nav-{request.PeriodEnd:yyyyMMdd}",
                StrategyName: request.StrategyProfile,
                Category: ReconciliationBreakCategory.AmountMismatch,
                Status: ReconciliationBreakQueueStatus.Open,
                Variance: varianceAmount,
                Reason: $"Shadow NAV variance breached ({varianceAmount.ToString("F2", CultureInfo.InvariantCulture)} / {varianceBps.ToString("F1", CultureInfo.InvariantCulture)} bps)",
                DetectedAt: DateTimeOffset.UtcNow,
                LastUpdatedAt: DateTimeOffset.UtcNow,
                Severity: Math.Abs(varianceBps) >= threshold.BpsMateriality * 2 ? ReconciliationBreakSeverity.Critical : ReconciliationBreakSeverity.High,
                ExceptionRoute: "accounting/reconciliation/shadow-nav",
                ToleranceBand: threshold.AbsoluteMateriality,
                RequiredSignoffRole: "Fund accounting",
                SignoffStatus: "pending-signoff",
                FundAccountId: request.AccountId,
                ExplainabilitySummary: $"version={snapshot.VersionHash[..12]}, shadowNav={shadowNav.ToString("F2", CultureInfo.InvariantCulture)}, externalNav={request.ExternalReferenceNav.ToString("F2", CultureInfo.InvariantCulture)}",
                CustodianId: null,
                UpstreamSyncCursor: snapshot.VersionHash,
                AssignedTo: request.OperatorId,
                ReviewedBy: null,
                ReviewedAt: null,
                ResolvedBy: null,
                ResolvedAt: null,
                ResolutionNote: $"Policy threshold breached for strategy profile '{request.StrategyProfile}'.",
                LifecycleState: ReconciliationCaseLifecycleState.Detected,
                LifecycleRationale: "Auto-generated from shadow-book valuation variance.",
                StateTransitions: []);
        }

        return PersistAndReturnAsync(emittedBreak, snapshot, request.BlockCloseOnBreach && breached, ct);
    }

    private async Task<ShadowBookValuationResult> PersistAndReturnAsync(ReconciliationBreakQueueItem? emittedBreak, ShadowBookRunSnapshot snapshot, bool closeBlocked, CancellationToken ct)
    {
        if (emittedBreak is not null)
        {
            await _breakQueueRepository.CreateIfMissingAsync(emittedBreak, ct).ConfigureAwait(false);
        }

        return new ShadowBookValuationResult(snapshot, closeBlocked, emittedBreak);
    }

    private static string ComputeHash(ShadowBookValuationRequest request, decimal nav, decimal varianceAmount, decimal varianceBps)
    {
        var builder = new StringBuilder();
        builder.Append(request.AccountId).Append('|').Append(request.StrategyProfile).Append('|').Append(request.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('|');
        foreach (var p in request.Positions.OrderBy(x => x.Symbol, StringComparer.Ordinal))
        {
            builder.Append(p.Symbol).Append(':').Append(p.Quantity.ToString("G29", CultureInfo.InvariantCulture)).Append(':').Append(p.PriceCurrency).Append('|');
        }

        foreach (var px in request.PricesBySymbol.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            builder.Append("px:").Append(px.Key).Append('=').Append(px.Value.ToString("G29", CultureInfo.InvariantCulture)).Append('|');
        }

        foreach (var fx in request.FxRatesByCurrency.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            builder.Append("fx:").Append(fx.Key).Append('=').Append(fx.Value.ToString("G29", CultureInfo.InvariantCulture)).Append('|');
        }

        builder.Append(request.Fees.ToString("G29", CultureInfo.InvariantCulture)).Append('|')
            .Append(request.Accruals.ToString("G29", CultureInfo.InvariantCulture)).Append('|')
            .Append(request.ExternalReferenceNav.ToString("G29", CultureInfo.InvariantCulture)).Append('|')
            .Append(nav.ToString("G29", CultureInfo.InvariantCulture)).Append('|')
            .Append(varianceAmount.ToString("G29", CultureInfo.InvariantCulture)).Append('|')
            .Append(varianceBps.ToString("G29", CultureInfo.InvariantCulture));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }
}
