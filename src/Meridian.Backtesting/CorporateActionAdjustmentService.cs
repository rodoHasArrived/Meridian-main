using Meridian.Application.SecurityMaster;
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
    private readonly int _maxCachedPlans;
    private readonly object _planCacheGate = new();
    private readonly Dictionary<string, CorporateActionAdjustmentPlan> _planCache = new(StringComparer.Ordinal);
    private readonly Queue<string> _planCacheOrder = new();

    public CorporateActionAdjustmentService(
        Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService queryService,
        ISecurityResolver resolver,
        ILogger<CorporateActionAdjustmentService> logger,
        IFactorPaydownProjectionService? factorPaydownProjector = null,
        int maxCachedPlans = 128)
    {
        ArgumentNullException.ThrowIfNull(queryService);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCachedPlans, 1);

        _queryService = queryService;
        _resolver = resolver;
        _logger = logger;
        _factorPaydownProjector = factorPaydownProjector ?? new FactorPaydownProjectionService();
        _maxCachedPlans = maxCachedPlans;
    }

    internal int CachedPlanCount
    {
        get
        {
            lock (_planCacheGate)
                return _planCache.Count;
        }
    }

    public async Task<CorporateActionAdjustmentPlan> PrepareAsync(
        IReadOnlyList<HistoricalBar> bars,
        string ticker,
        DateTimeOffset asOfUtc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);
        ct.ThrowIfCancellationRequested();

        var normalizedTicker = ticker.Trim().ToUpperInvariant();
        var pinnedAsOf = asOfUtc.ToUniversalTime();
        var sortedActions = await GetSortedCorporateActionsAsync(normalizedTicker, pinnedAsOf, ct)
            .ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return PreparePlan(bars, normalizedTicker, pinnedAsOf, sortedActions, ct);
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
        var pinnedAsOf = DateTimeOffset.UtcNow;
        var sortedActions = await GetSortedCorporateActionsAsync(normalizedTicker, pinnedAsOf, ct)
            .ConfigureAwait(false);
        if (sortedActions.Count == 0)
        {
            await foreach (var bar in bars.WithCancellation(ct).ConfigureAwait(false))
            {
                yield return bar;
            }

            yield break;
        }

        var buffered = new List<HistoricalBar>();
        await foreach (var bar in bars.WithCancellation(ct).ConfigureAwait(false))
        {
            buffered.Add(bar);
        }

        var plan = PreparePlan(buffered, normalizedTicker, pinnedAsOf, sortedActions, ct);
        foreach (var bar in buffered)
        {
            ct.ThrowIfCancellationRequested();
            yield return plan.Apply(bar);
        }
    }

    private async Task<IReadOnlyList<CorporateActionDto>> GetSortedCorporateActionsAsync(
        string ticker,
        DateTimeOffset asOfUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            return [];

        var securityId = await _resolver.ResolveAsync(
            new ResolveSecurityRequest(
                IdentifierKind: SecurityIdentifierKind.Ticker,
                IdentifierValue: ticker,
                Provider: null,
                AsOfUtc: asOfUtc),
            ct).ConfigureAwait(false);

        if (securityId is null)
        {
            securityId = await _resolver.ResolveAsync(
                new ResolveSecurityRequest(
                    IdentifierKind: SecurityIdentifierKind.Ticker,
                    IdentifierValue: ticker,
                    Provider: null,
                    AsOfUtc: DateTimeOffset.UtcNow),
                ct).ConfigureAwait(false);

            if (securityId is null)
            {
                _logger.LogWarning("Security not found in master for ticker {Ticker}", ticker);
                return [];
            }
        }

        var actions = await _queryService.GetCorporateActionsAsync(securityId.Value, ct)
            .ConfigureAwait(false);

        // Fold supersede chains and drop cancellations so adjustment math sees each
        // action's latest terms exactly once.
        return CorporateActionEffectiveStateProjector
            .ProjectEffectiveActions(actions, asOfUtc)
            .OrderBy(static a => a.ExDate)
            .ThenBy(static a => a.CorpActId)
            .ToArray();
    }

    private CorporateActionAdjustmentPlan PreparePlan(
        IReadOnlyList<HistoricalBar> bars,
        string normalizedTicker,
        DateTimeOffset asOfUtc,
        IReadOnlyList<CorporateActionDto> sortedActions,
        CancellationToken ct)
    {
        var contentVersion = HashPlanContent(normalizedTicker, asOfUtc, bars, sortedActions, ct);
        lock (_planCacheGate)
        {
            if (_planCache.TryGetValue(contentVersion, out var cached))
                return cached;
        }

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
        var plan = new CorporateActionAdjustmentPlan(
            normalizedTicker,
            asOfUtc,
            contentVersion,
            bars.Count,
            steps);

        lock (_planCacheGate)
        {
            if (_planCache.TryGetValue(contentVersion, out var cached))
                return cached;

            while (_planCache.Count >= _maxCachedPlans && _planCacheOrder.TryDequeue(out var evicted))
                _planCache.Remove(evicted);

            _planCache[contentVersion] = plan;
            _planCacheOrder.Enqueue(contentVersion);
            return plan;
        }
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
        DateTimeOffset asOfUtc,
        IReadOnlyList<HistoricalBar> bars,
        IReadOnlyList<CorporateActionDto> actions,
        CancellationToken ct)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        using var hash = CorporateActionContentHasher.Create();
        CorporateActionContentHasher.AppendValue(hash, ticker);
        CorporateActionContentHasher.AppendValue(
            hash,
            asOfUtc.ToUniversalTime().Ticks.ToString(invariant));
        CorporateActionContentHasher.AppendValue(hash, bars.Count.ToString(invariant));
        CorporateActionContentHasher.AppendValue(hash, actions.Count.ToString(invariant), endRecord: true);

        foreach (var bar in bars)
        {
            ct.ThrowIfCancellationRequested();
            CorporateActionContentHasher.AppendBar(hash, bar);
        }

        foreach (var action in actions)
        {
            ct.ThrowIfCancellationRequested();
            CorporateActionContentHasher.AppendValue(hash, action.CorpActId.ToString("N"));
            CorporateActionContentHasher.AppendValue(hash, action.SecurityId.ToString("N"));
            CorporateActionContentHasher.AppendValue(hash, action.EventType);
            CorporateActionContentHasher.AppendValue(hash, action.ExDate.ToString("yyyy-MM-dd", invariant));
            CorporateActionContentHasher.AppendValue(hash, action.PayDate?.ToString("yyyy-MM-dd", invariant));
            CorporateActionContentHasher.AppendValue(hash, action.DividendPerShare?.ToString("G29", invariant));
            CorporateActionContentHasher.AppendValue(hash, action.Currency);
            CorporateActionContentHasher.AppendValue(hash, action.SplitRatio?.ToString("G29", invariant));
            CorporateActionContentHasher.AppendValue(hash, action.NewSecurityId?.ToString("N"));
            CorporateActionContentHasher.AppendValue(hash, action.DistributionRatio?.ToString("G29", invariant));
            CorporateActionContentHasher.AppendValue(hash, action.AcquirerSecurityId?.ToString("N"));
            CorporateActionContentHasher.AppendValue(hash, action.ExchangeRatio?.ToString("G29", invariant));
            CorporateActionContentHasher.AppendValue(hash, action.SubscriptionPricePerShare?.ToString("G29", invariant));
            CorporateActionContentHasher.AppendValue(hash, action.RightsPerShare?.ToString("G29", invariant));
            CorporateActionContentHasher.AppendValue(hash, action.RecordDate?.ToString("yyyy-MM-dd", invariant));
            CorporateActionContentHasher.AppendValue(hash, action.LifecycleState);
            CorporateActionContentHasher.AppendValue(hash, action.SupersedesCorpActId?.ToString("N"));
            CorporateActionContentHasher.AppendValue(
                hash,
                action.RedemptionPricePercentOfPar?.ToString("G29", invariant),
                endRecord: true);
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
        return $"sha256:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source))).ToLowerInvariant()}";
    }
}
