using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Backtesting;

/// <summary>
/// Adjusts historical bar prices and volumes for corporate actions (stock splits and dividends)
/// using Security Master data.
/// </summary>
public sealed class CorporateActionAdjustmentService : ICorporateActionAdjustmentService
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
}
