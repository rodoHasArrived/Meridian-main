using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
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

    public CorporateActionAdjustmentService(
        Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService queryService,
        ISecurityResolver resolver,
        ILogger<CorporateActionAdjustmentService> logger)
    {
        _queryService = queryService;
        _resolver = resolver;
        _logger = logger;
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

        return actions.OrderBy(a => a.ExDate).ToList();
    }

    private static HistoricalBar ApplyAdjustments(
        HistoricalBar bar,
        IReadOnlyList<CorporateActionDto> sortedActions,
        IReadOnlyDictionary<DateOnly, decimal> dividendFactorsByExDate)
    {
        var barDate = bar.SessionDate;
        var priceFactor = 1m;
        var volumeFactor = 1m;

        foreach (var action in sortedActions)
        {
            if (action.ExDate <= barDate)
                continue;

            var eventType = CorporateActionEventTypes.Normalize(action.EventType);
            if ((eventType == CorporateActionEventTypes.StockSplit ||
                    eventType == CorporateActionEventTypes.ReverseStockSplit) &&
                action.SplitRatio is > 0m)
            {
                priceFactor /= action.SplitRatio.Value;
                volumeFactor *= action.SplitRatio.Value;
            }
            else if (eventType == CorporateActionEventTypes.Dividend &&
                     dividendFactorsByExDate.TryGetValue(action.ExDate, out var dividendFactor))
            {
                priceFactor *= dividendFactor;
            }
        }

        return new HistoricalBar(
            Symbol: bar.Symbol,
            SessionDate: bar.SessionDate,
            Open: bar.Open * priceFactor,
            High: bar.High * priceFactor,
            Low: bar.Low * priceFactor,
            Close: bar.Close * priceFactor,
            Volume: (long)Math.Round(bar.Volume * volumeFactor, MidpointRounding.AwayFromZero),
            Source: bar.Source,
            SequenceNumber: bar.SequenceNumber);
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
                         CorporateActionEventTypes.Normalize(action.EventType) == CorporateActionEventTypes.Dividend &&
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

        var relevantActions = actions
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
            if ((eventType == CorporateActionEventTypes.StockSplit ||
                    eventType == CorporateActionEventTypes.ReverseStockSplit) &&
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
                adjustedCostBasis = Math.Max(0m, adjustedCostBasis - action.DistributionRatio.Value);
                appliedActions++;
            }
        }

        _logger.LogInformation(
            "AdjustPositionAsync: applied {ActionCount} corporate action(s) to {Ticker} position; " +
            "quantity {OrigQty} → {AdjQty}, cost basis {OrigCb:F4} → {AdjCb:F4}",
            appliedActions, ticker, quantity, adjustedQuantity, costBasis, adjustedCostBasis);

        return new PositionCorporateActionAdjustment(
            ticker, quantity, adjustedQuantity, costBasis, adjustedCostBasis, appliedActions);
    }
}
