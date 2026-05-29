using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, Task<List<CorporateActionDto>?>> _sortedActionsByTicker = new(StringComparer.OrdinalIgnoreCase);

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
        if (sortedActions is null)
            return bars;

        var adjustedBars = new List<HistoricalBar>(bars.Count);
        foreach (var bar in bars)
        {
            adjustedBars.Add(ApplyAdjustments(bar, sortedActions));
        }

        return adjustedBars;
    }

    public async Task<HistoricalBar> AdjustBarAsync(
        HistoricalBar bar,
        string ticker,
        CancellationToken ct = default)
    {
        var sortedActions = await GetSortedCorporateActionsAsync(ticker, ct).ConfigureAwait(false);
        return sortedActions is null ? bar : ApplyAdjustments(bar, sortedActions);
    }

    public async IAsyncEnumerable<HistoricalBar> AdjustAsync(
        IAsyncEnumerable<HistoricalBar> bars,
        string ticker,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var sortedActions = await GetSortedCorporateActionsAsync(ticker, ct).ConfigureAwait(false);
        if (sortedActions is null)
        {
            await foreach (var bar in bars.WithCancellation(ct).ConfigureAwait(false))
            {
                yield return bar;
            }

            yield break;
        }

        await foreach (var bar in bars.WithCancellation(ct).ConfigureAwait(false))
        {
            yield return ApplyAdjustments(bar, sortedActions);
        }
    }

    private Task<List<CorporateActionDto>?> GetSortedCorporateActionsAsync(string ticker, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            return Task.FromResult<List<CorporateActionDto>?>(null);

        return _sortedActionsByTicker.GetOrAdd(ticker.Trim(), key => LoadSortedCorporateActionsAsync(key, ct));
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

        if (actions.Count == 0)
            return null;

        return actions.OrderBy(a => a.ExDate).ToList();
    }

    private static HistoricalBar ApplyAdjustments(HistoricalBar bar, IReadOnlyList<CorporateActionDto> sortedActions)
    {
        var barDate = bar.SessionDate;
        decimal splitFactor = 1m;
        decimal dividendAdjustment = 0m;

        foreach (var action in sortedActions)
        {
            if (action.ExDate <= barDate)
                continue;

            if (action.EventType == "StockSplit" && action.SplitRatio.HasValue)
                splitFactor *= action.SplitRatio.Value;
            else if (action.EventType == "Dividend" && action.DividendPerShare.HasValue)
                dividendAdjustment += action.DividendPerShare.Value;
        }

        return new HistoricalBar(
            Symbol: bar.Symbol,
            SessionDate: bar.SessionDate,
            Open: (bar.Open - dividendAdjustment) / splitFactor,
            High: (bar.High - dividendAdjustment) / splitFactor,
            Low: (bar.Low - dividendAdjustment) / splitFactor,
            Close: (bar.Close - dividendAdjustment) / splitFactor,
            Volume: (long)(bar.Volume * splitFactor),
            Source: bar.Source,
            SequenceNumber: bar.SequenceNumber);
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
