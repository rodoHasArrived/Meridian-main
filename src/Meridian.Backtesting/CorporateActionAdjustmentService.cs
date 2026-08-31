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
        _queryService = queryService;
        _resolver = resolver;
        _logger = logger;
        _factorPaydownProjector = factorPaydownProjector ?? new FactorPaydownProjectionService();
    }

    public async Task<IReadOnlyList<HistoricalBar>> AdjustAsync(
        IReadOnlyList<HistoricalBar> bars,
        string ticker,
        CancellationToken ct = default)
    {
        if (bars.Count == 0)
            return bars;

        var sortedActions = await GetSortedCorporateActionsAsync(ticker, ct).ConfigureAwait(false);
        if (sortedActions is null || sortedActions.Count == 0)
            return bars;

        var dividendFactors = BuildDividendFactors(bars, sortedActions);
        var adjustedBars = new List<HistoricalBar>(bars.Count);
        foreach (var bar in bars)
        {
            adjustedBars.Add(ApplyAdjustments(bar, sortedActions, dividendFactors));
        }

        return adjustedBars;
    }

    public async Task<HistoricalBar> AdjustBarAsync(
        HistoricalBar bar,
        string ticker,
        CancellationToken ct = default)
    {
        var sortedActions = await GetSortedCorporateActionsAsync(ticker, ct).ConfigureAwait(false);
        return sortedActions is null || sortedActions.Count == 0
            ? bar
            : ApplyAdjustments(bar, sortedActions, BuildDividendFactors([bar], sortedActions));
    }

    public async IAsyncEnumerable<HistoricalBar> AdjustAsync(
        IAsyncEnumerable<HistoricalBar> bars,
        string ticker,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var sortedActions = await GetSortedCorporateActionsAsync(ticker, ct).ConfigureAwait(false);
        if (sortedActions is null || sortedActions.Count == 0)
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

        var dividendFactors = BuildDividendFactors(buffered, sortedActions);
        foreach (var bar in buffered)
        {
            yield return ApplyAdjustments(bar, sortedActions, dividendFactors);
        }
    }

    private async Task<List<CorporateActionDto>?> GetSortedCorporateActionsAsync(string ticker, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            return null;

        return await LoadSortedCorporateActionsAsync(ticker.Trim(), ct).ConfigureAwait(false);
    }

    private async Task<List<CorporateActionDto>?> LoadSortedCorporateActionsAsync(string ticker, CancellationToken ct)
    {
        var securityId = await _resolver.ResolveAsync(
            new ResolveSecurityRequest(
                IdentifierKind: SecurityIdentifierKind.Ticker,
                IdentifierValue: ticker,
                Provider: null,
                AsOfUtc: null),
            ct).ConfigureAwait(false);

        if (securityId is null)
        {
            _logger.LogWarning("Security not found in master for ticker {Ticker}", ticker);
            return null;
        }

        var actions = await _queryService.GetCorporateActionsAsync(securityId.Value, ct)
            .ConfigureAwait(false);

        // Fold supersede chains and drop cancellations so adjustment math sees each
        // action's latest terms exactly once.
        return CorporateActionEffectiveStateProjector
            .ProjectEffectiveActions(actions, DateTimeOffset.UtcNow)
            .OrderBy(static a => a.ExDate)
            .ToList();
    }

    private static HistoricalBar ApplyAdjustments(
        HistoricalBar bar,
        IReadOnlyList<CorporateActionDto> sortedActions,
        IReadOnlyDictionary<DateOnly, decimal> dividendFactorsByExDate)
    {
        var barDate = bar.SessionDate;
        // Accumulate the split divisor and divide once at the end: dividing the running
        // factor per split (1/2, then /3, ...) leaves a repeating-decimal residue that
        // makes exact chains like 600 / 6 come out as 100.000...02.
        var splitDivisor = 1m;
        var dividendFactor = 1m;

        foreach (var action in sortedActions)
        {
            if (action.ExDate <= barDate)
                continue;

            if (!CorporateActionTypeDescriptorCatalog.TryNormalize(action.EventType, out var descriptor))
                continue;

            if (descriptor.AdjustmentBehavior == CorporateActionAdjustmentBehavior.PriceScaling &&
                action.SplitRatio is > 0m)
            {
                splitDivisor *= action.SplitRatio.Value;
            }
            else if (descriptor.AdjustmentBehavior == CorporateActionAdjustmentBehavior.CashDistribution &&
                     dividendFactorsByExDate.TryGetValue(action.ExDate, out var exDateFactor))
            {
                dividendFactor *= exDateFactor;
            }
        }

        return new HistoricalBar(
            Symbol: bar.Symbol,
            SessionDate: bar.SessionDate,
            Open: bar.Open * dividendFactor / splitDivisor,
            High: bar.High * dividendFactor / splitDivisor,
            Low: bar.Low * dividendFactor / splitDivisor,
            Close: bar.Close * dividendFactor / splitDivisor,
            Volume: (long)Math.Round(bar.Volume * splitDivisor, MidpointRounding.AwayFromZero),
            Source: bar.Source,
            SequenceNumber: bar.SequenceNumber,
            // Only a bar an actual factor rewrote is claimed as adjusted; a bar the factor
            // computation skipped (CA_DEF_001: missing prior close) keeps its input regime, so
            // the flag can never assert an adjustment that silently did not happen.
            IsAdjusted: dividendFactor != 1m || splitDivisor != 1m ? true : bar.IsAdjusted);
    }

    private static IReadOnlyDictionary<DateOnly, decimal> BuildDividendFactors(
        IReadOnlyList<HistoricalBar> bars,
        IReadOnlyList<CorporateActionDto> sortedActions)
    {
        if (bars.Count == 0)
            return new Dictionary<DateOnly, decimal>();

        var barsByDate = bars
            .OrderBy(static bar => bar.SessionDate)
            .ToArray();
        var factors = new Dictionary<DateOnly, decimal>();

        foreach (var dividendGroup in sortedActions
                     .Where(static action =>
                         CorporateActionTypeDescriptorCatalog.TryNormalize(action.EventType, out var descriptor) &&
                         descriptor.AdjustmentBehavior == CorporateActionAdjustmentBehavior.CashDistribution &&
                         action.DividendPerShare is > 0m)
                     .GroupBy(static action => action.ExDate))
        {
            var previousClose = barsByDate
                .Where(bar => bar.SessionDate < dividendGroup.Key)
                .Select(static bar => (decimal?)bar.Close)
                .LastOrDefault();
            var dividendAmount = dividendGroup.Sum(static action => action.DividendPerShare!.Value);

            if (previousClose is not > 0m)
                continue;

            var factor = 1m - (dividendAmount / previousClose.Value);
            if (factor is > 0m and <= 1m)
                factors[dividendGroup.Key] = factor;
        }

        return factors;
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
