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

        // Resolve ticker to security ID
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
            return bars;
        }

        // Get corporate actions
        var actions = await _queryService.GetCorporateActionsAsync(securityId.Value, ct)
            .ConfigureAwait(false);

        if (actions.Count == 0)
            return bars;

        _logger.LogInformation(
            "Adjusted {Count} bars for {Ticker} using {ActionCount} corporate actions",
            bars.Count, ticker, actions.Count);

        // Sort actions by ExDate ascending
        var sortedActions = actions.OrderBy(a => a.ExDate).ToList();

        var adjustedBars = new List<HistoricalBar>(bars.Count);

        // Process bars backward to forward through time
        foreach (var bar in bars)
        {
            var barDate = bar.SessionDate;

            // Accumulate split and dividend adjustments for all actions that occurred after this bar
            decimal splitFactor = 1m;
            decimal dividendAdjustment = 0m;

            foreach (var action in sortedActions)
            {
                // Only apply actions with ExDate strictly after the bar date
                if (action.ExDate <= barDate)
                    continue;

                if (action.EventType == "StockSplit" && action.SplitRatio.HasValue)
                {
                    splitFactor *= action.SplitRatio.Value;
                }
                else if (action.EventType == "Dividend" && action.DividendPerShare.HasValue)
                {
                    dividendAdjustment += action.DividendPerShare.Value;
                }
            }

            // Apply adjustments
            var adjustedOpen = (bar.Open - dividendAdjustment) / splitFactor;
            var adjustedHigh = (bar.High - dividendAdjustment) / splitFactor;
            var adjustedLow = (bar.Low - dividendAdjustment) / splitFactor;
            var adjustedClose = (bar.Close - dividendAdjustment) / splitFactor;
            var adjustedVolume = (long)(bar.Volume * splitFactor);

            var adjustedBar = new HistoricalBar(
                Symbol: bar.Symbol,
                SessionDate: bar.SessionDate,
                Open: adjustedOpen,
                High: adjustedHigh,
                Low: adjustedLow,
                Close: adjustedClose,
                Volume: adjustedVolume,
                Source: bar.Source,
                SequenceNumber: bar.SequenceNumber);

            adjustedBars.Add(adjustedBar);
        }

        return adjustedBars;
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

        foreach (var action in relevantActions)
        {
            if (action.EventType == "StockSplit" && action.SplitRatio.HasValue && action.SplitRatio.Value != 0m)
            {
                // Split: quantity multiplies by ratio, cost basis divides by ratio.
                adjustedQuantity *= action.SplitRatio.Value;
                adjustedCostBasis /= action.SplitRatio.Value;
            }
            else if (action.EventType == "Dividend" && action.DividendPerShare.HasValue)
            {
                // Dividend: reduce cost basis by the dividend per share (return of capital view).
                adjustedCostBasis -= action.DividendPerShare.Value;
            }
        }

        _logger.LogInformation(
            "AdjustPositionAsync: applied {ActionCount} corporate action(s) to {Ticker} position; " +
            "quantity {OrigQty} → {AdjQty}, cost basis {OrigCb:F4} → {AdjCb:F4}",
            relevantActions.Count, ticker, quantity, adjustedQuantity, costBasis, adjustedCostBasis);

        return new PositionCorporateActionAdjustment(
            ticker, quantity, adjustedQuantity, costBasis, adjustedCostBasis, relevantActions.Count);
    }
}
