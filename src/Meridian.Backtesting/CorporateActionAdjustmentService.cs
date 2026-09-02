using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.AssetOperations;
using ISecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Backtesting;

/// <summary>
/// Adjusts historical bar prices and volumes for corporate actions (stock splits and dividends)
/// using Security Master data.
/// </summary>
public sealed class CorporateActionAdjustmentService : ICorporateActionAdjustmentService, ILivePositionCorporateActionAdjuster
{
    private readonly Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService _queryService;
    private readonly ISecurityResolver _resolver;
    private readonly ILogger<CorporateActionAdjustmentService> _logger;
    private readonly IFactorPaydownProjectionService _factorPaydownProjector;

    public CorporateActionAdjustmentService(
        Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService queryService,
        ISecurityResolver resolver,
        ILogger<CorporateActionAdjustmentService> logger,
        IFactorPaydownProjectionService? factorPaydownProjector = null)
    {
        ArgumentNullException.ThrowIfNull(queryService);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(logger);

        _queryService = queryService;
        _resolver = resolver;
        _logger = logger;
        _factorPaydownProjector = factorPaydownProjector ?? new FactorPaydownProjectionService();
    }

    public async Task<CorporateActionAdjustmentPlan> PrepareAsync(
        IReadOnlyList<HistoricalBar> bars,
        string ticker,
        DateTimeOffset effectiveThroughUtc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);
        ct.ThrowIfCancellationRequested();

        var normalizedTicker = ticker.Trim().ToUpperInvariant();
        var effectiveThrough = effectiveThroughUtc.ToUniversalTime();
        var sortedActions = await GetSortedCorporateActionsAsync(normalizedTicker, effectiveThrough, ct)
            .ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return PreparePlan(bars, normalizedTicker, effectiveThrough, sortedActions, ct);
    }

    public async Task<IReadOnlyList<HistoricalBar>> AdjustAsync(
        IReadOnlyList<HistoricalBar> bars,
        string ticker,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bars);
        if (bars.Count == 0)
            return bars;

        var plan = await PrepareAsync(bars, ticker, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        var adjustedBars = new List<HistoricalBar>(bars.Count);
        foreach (var bar in bars)
        {
            ct.ThrowIfCancellationRequested();
            adjustedBars.Add(plan.Apply(bar));
        }

        return adjustedBars;
    }

    public async Task<HistoricalBar> AdjustBarAsync(
        HistoricalBar bar,
        string ticker,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bar);
        var plan = await PrepareAsync([bar], ticker, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        return plan.Apply(bar);
    }

    public async IAsyncEnumerable<HistoricalBar> AdjustAsync(
        IAsyncEnumerable<HistoricalBar> bars,
        string ticker,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);

        var normalizedTicker = ticker.Trim().ToUpperInvariant();
        var effectiveThrough = DateTimeOffset.UtcNow;
        var sortedActions = await GetSortedCorporateActionsAsync(normalizedTicker, effectiveThrough, ct)
            .ConfigureAwait(false);
        if (sortedActions.Count == 0)
        {
            await foreach (var bar in bars.WithCancellation(ct).ConfigureAwait(false))
                yield return bar;
            yield break;
        }

        var buffered = new List<HistoricalBar>();
        await foreach (var bar in bars.WithCancellation(ct).ConfigureAwait(false))
        {
            buffered.Add(bar);
        }

        var plan = PreparePlan(buffered, normalizedTicker, effectiveThrough, sortedActions, ct);
        foreach (var bar in buffered)
        {
            ct.ThrowIfCancellationRequested();
            yield return plan.Apply(bar);
        }
    }

    private async Task<IReadOnlyList<CorporateActionDto>> GetSortedCorporateActionsAsync(
        string ticker,
        DateTimeOffset effectiveThroughUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            return [];

        var securityId = await _resolver.ResolveAsync(
            new ResolveSecurityRequest(
                IdentifierKind: SecurityIdentifierKind.Ticker,
                IdentifierValue: ticker,
                Provider: null,
                AsOfUtc: effectiveThroughUtc),
            ct).ConfigureAwait(false);

        if (securityId is null)
        {
            _logger.LogWarning(
                "Security not found in master for ticker {Ticker} at effective-through {EffectiveThroughUtc}",
                ticker,
                effectiveThroughUtc);
            return [];
        }

        var actions = await _queryService.GetCorporateActionsAsync(securityId.Value, ct)
            .ConfigureAwait(false);

        // Freeze one query result, then accept only authoritative (confirmed/legacy) terms that
        // are economically effective by the requested cutoff. The query contract does not expose
        // storage transaction time, so this deliberately makes no claim about which revisions
        // existed historically at that cutoff. An announcement alone is never execution authority.
        var effectiveThrough = DateOnly.FromDateTime(effectiveThroughUtc.UtcDateTime);
        return CorporateActionEffectiveStateProjector
            .Project(actions, effectiveThroughUtc)
            .Where(static state =>
                !state.IsCancelled &&
                state.Effective.LifecycleState is null or CorporateActionLifecycleStates.Confirmed)
            .Select(static state => state.Effective)
            .Where(action => action.ExDate <= effectiveThrough)
            .OrderBy(static a => a.ExDate)
            .ThenBy(static a => a.CorpActId)
            .ToArray();
    }

    private static CorporateActionAdjustmentPlan PreparePlan(
        IReadOnlyList<HistoricalBar> bars,
        string normalizedTicker,
        DateTimeOffset effectiveThroughUtc,
        IReadOnlyList<CorporateActionDto> sortedActions,
        CancellationToken ct)
    {
        var dividendFactors = BuildDividendFactors(bars, sortedActions, ct);
        var steps = new List<(DateOnly ExDate, decimal SplitDivisor, decimal DividendFactor)>();
        foreach (var group in sortedActions.GroupBy(static action => action.ExDate))
        {
            ct.ThrowIfCancellationRequested();
            var splitDivisor = group
                .Where(static action =>
                    CorporateActionTypeDescriptorCatalog.TryNormalize(action.EventType, out var descriptor) &&
                    descriptor.AdjustmentBehavior == CorporateActionAdjustmentBehavior.PriceScaling &&
                    action.SplitRatio is > 0m)
                .Aggregate(1m, static (product, action) => product * action.SplitRatio!.Value);
            var dividendFactor = dividendFactors.GetValueOrDefault(group.Key, 1m);
            if (splitDivisor != 1m || dividendFactor != 1m)
                steps.Add((group.Key, splitDivisor, dividendFactor));
        }

        return new CorporateActionAdjustmentPlan(
            normalizedTicker,
            effectiveThroughUtc,
            HashPlanContent(normalizedTicker, effectiveThroughUtc, bars, sortedActions, ct),
            bars.Count,
            steps);
    }

    private static IReadOnlyDictionary<DateOnly, decimal> BuildDividendFactors(
        IReadOnlyList<HistoricalBar> bars,
        IReadOnlyList<CorporateActionDto> sortedActions,
        CancellationToken ct)
    {
        if (bars.Count == 0)
            return new Dictionary<DateOnly, decimal>();

        var barsByDate = bars
            .OrderBy(static bar => bar.SessionDate)
            .ToArray();
        var factors = new Dictionary<DateOnly, decimal>();

        var dividendGroups = sortedActions
            .Where(static action =>
                CorporateActionTypeDescriptorCatalog.TryNormalize(action.EventType, out var descriptor) &&
                descriptor.AdjustmentBehavior == CorporateActionAdjustmentBehavior.CashDistribution &&
                action.DividendPerShare is > 0m)
            .GroupBy(static action => action.ExDate)
            .OrderBy(static group => group.Key);
        var barIndex = 0;
        decimal? previousClose = null;

        foreach (var dividendGroup in dividendGroups)
        {
            ct.ThrowIfCancellationRequested();
            while (barIndex < barsByDate.Length && barsByDate[barIndex].SessionDate < dividendGroup.Key)
            {
                if ((barIndex & 1023) == 0)
                    ct.ThrowIfCancellationRequested();
                previousClose = barsByDate[barIndex].Close;
                barIndex++;
            }
            var dividendAmount = dividendGroup.Sum(static action => action.DividendPerShare!.Value);

            if (previousClose is not > 0m)
                continue;

            var factor = 1m - (dividendAmount / previousClose.Value);
            if (factor is > 0m and <= 1m)
                factors[dividendGroup.Key] = factor;
        }

        return factors;
    }

    private static string HashPlanContent(
        string ticker,
        DateTimeOffset effectiveThroughUtc,
        IReadOnlyList<HistoricalBar> bars,
        IReadOnlyList<CorporateActionDto> actions,
        CancellationToken ct)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        using var hash = CorporateActionContentHasher.Create();
        CorporateActionContentHasher.AppendValue(hash, ticker);
        CorporateActionContentHasher.AppendValue(hash, effectiveThroughUtc.ToUniversalTime().Ticks.ToString(invariant));
        CorporateActionContentHasher.AppendValue(hash, bars.Count.ToString(invariant));
        CorporateActionContentHasher.AppendValue(hash, actions.Count.ToString(invariant));

        foreach (var bar in bars)
        {
            ct.ThrowIfCancellationRequested();
            CorporateActionContentHasher.AppendBar(hash, bar);
        }

        foreach (var action in actions)
        {
            ct.ThrowIfCancellationRequested();
            CorporateActionContentHasher.AppendValue(hash, action.CorpActId.ToString("N"));
            CorporateActionContentHasher.AppendValue(hash, action.SupersedesCorpActId?.ToString("N"));
            CorporateActionContentHasher.AppendValue(hash, action.LifecycleState);
            CorporateActionContentHasher.AppendValue(hash, CorporateActionEconomicFingerprint.Compute(action));
        }

        return CorporateActionContentHasher.Complete(hash);
    }

    /// <inheritdoc />
    public async Task<PositionCorporateActionAdjustment> AdjustPositionAsync(
        string ticker,
        decimal quantity,
        decimal costBasis,
        DateTimeOffset positionOpenedAt,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ticker) || quantity == 0m)
        {
            return new PositionCorporateActionAdjustment(
                ticker ?? string.Empty, quantity, quantity, costBasis, costBasis, ActionCount: 0);
        }

        var securityId = await _resolver.ResolveAsync(
            new ResolveSecurityRequest(
                IdentifierKind: SecurityIdentifierKind.Ticker,
                IdentifierValue: ticker,
                Provider: null,
                AsOfUtc: null),
            ct).ConfigureAwait(false);

        if (securityId is null)
        {
            _logger.LogDebug(
                "AdjustPositionAsync: symbol '{Ticker}' not found in Security Master — no adjustment applied",
                ticker);
            return new PositionCorporateActionAdjustment(ticker, quantity, quantity, costBasis, costBasis, ActionCount: 0);
        }

        var actions = await _queryService.GetCorporateActionsAsync(securityId.Value, ct)
            .ConfigureAwait(false);

        var relevantActions = CorporateActionEffectiveStateProjector
            .ProjectEffectiveActions(actions, DateTimeOffset.UtcNow)
            .Where(a => a.ExDate > DateOnly.FromDateTime(positionOpenedAt.UtcDateTime))
            .OrderBy(static a => a.ExDate)
            .ToList();

        if (relevantActions.Count == 0)
        {
            return new PositionCorporateActionAdjustment(ticker, quantity, quantity, costBasis, costBasis, ActionCount: 0);
        }

        var adjustedQuantity = quantity;
        var adjustedCostBasis = costBasis;
        var appliedActions = 0;

        foreach (var action in relevantActions)
        {
            var eventType = CorporateActionEventTypes.Normalize(action.EventType);
            if (CorporateActionTypeDescriptorCatalog.TryNormalize(action.EventType, out var descriptor) &&
                descriptor.AdjustmentBehavior == CorporateActionAdjustmentBehavior.PriceScaling &&
                action.SplitRatio.HasValue &&
                action.SplitRatio.Value != 0m)
            {
                adjustedQuantity *= action.SplitRatio.Value;
                adjustedCostBasis /= action.SplitRatio.Value;
                appliedActions++;
            }
            else if (eventType == CorporateActionEventTypes.MergerAbsorption &&
                     action.ExchangeRatio is > 0m)
            {
                adjustedQuantity *= action.ExchangeRatio.Value;
                appliedActions++;
            }
            else if (eventType == CorporateActionEventTypes.PrincipalPaydown &&
                     action.DistributionRatio is > 0m)
            {
                var heldFace = Math.Abs(adjustedQuantity);
                var positionId = DeriveCompatibilityPositionId(securityId.Value, ticker);
                var projection = _factorPaydownProjector.Project(new FactorPaydownProjectionRequest(
                    securityId.Value,
                    positionId,
                    PositionVersion: 1,
                    ExpectedPositionVersion: 1,
                    HeldFace: heldFace,
                    PriorFactor: 1m,
                    CurrentFactor: 1m - action.DistributionRatio.Value,
                    Currency: action.Currency ?? string.Empty,
                    EffectiveDate: action.ExDate,
                    OccurredAtUtc: new DateTimeOffset(action.ExDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                    SourceDomain: "SecurityMaster",
                    SourceEntityId: action.CorpActId.ToString("D"),
                    SourceContentHash: HashCorporateAction(action),
                    EvidenceLinks: [$"security-master://corporate-actions/{action.CorpActId:D}"]));
                if (projection.ProducesPostingCandidate && heldFace > 0m)
                {
                    var totalBasis = heldFace * adjustedCostBasis;
                    var adjustedTotalBasis = Math.Max(0m, totalBasis - projection.PrincipalPaydown!.Value);
                    adjustedCostBasis = adjustedTotalBasis / heldFace;
                    appliedActions++;
                }
                else
                {
                    _logger.LogWarning(
                        "AdjustPositionAsync skipped principal paydown {CorporateActionId} for {Ticker}: {Issues}",
                        action.CorpActId,
                        ticker,
                        string.Join("; ", projection.Issues.Select(static issue => issue.Message)));
                }
            }
        }

        _logger.LogInformation(
            "AdjustPositionAsync: applied {ActionCount} corporate action(s) to {Ticker} position; " +
            "quantity {OrigQty} → {AdjQty}, cost basis {OrigCb:F4} → {AdjCb:F4}",
            appliedActions, ticker, quantity, adjustedQuantity, costBasis, adjustedCostBasis);

        return new PositionCorporateActionAdjustment(
            ticker, quantity, adjustedQuantity, costBasis, adjustedCostBasis, appliedActions);
    }

    private static Guid DeriveCompatibilityPositionId(Guid securityId, string ticker)
        => new(System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{securityId:N}:factor-position:{ticker.Trim().ToUpperInvariant()}")));

    private static string HashCorporateAction(CorporateActionDto action)
    {
        var source = string.Join(
            '|',
            action.CorpActId.ToString("N"),
            action.SecurityId.ToString("N"),
            action.EventType,
            action.ExDate.ToString("yyyy-MM-dd"),
            action.DistributionRatio?.ToString("G29", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            action.Currency ?? string.Empty,
            action.LifecycleState ?? string.Empty);
        return $"sha256:{Sha256Digest.ComputeUtf8(source)}";
    }
}
