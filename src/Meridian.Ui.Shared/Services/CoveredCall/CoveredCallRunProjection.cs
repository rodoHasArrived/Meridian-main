using Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite;
using Meridian.Ui.Shared.Contracts;

namespace Meridian.Ui.Shared.Services.CoveredCall;

/// <summary>
/// Pure mapping between covered-call DTOs and strategy types. No DI, no side effects.
/// </summary>
internal static class CoveredCallRunProjection
{
    /// <summary>Maps an operator request to strategy <see cref="OptionsOverwriteParams"/>.</summary>
    public static OptionsOverwriteParams ToParams(CoveredCallBacktestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.MinStrike <= 0m)
            throw new ArgumentException("MinStrike must be greater than zero.", nameof(request));
        if (request.OverwriteRatio is <= 0 or > 1.0)
            throw new ArgumentException("OverwriteRatio must be in (0, 1].", nameof(request));
        if (request.MaxDelta is < 0 or > 1.0)
            throw new ArgumentException("MaxDelta must be in [0, 1].", nameof(request));
        if (request.MinDte < 0)
            throw new ArgumentException("MinDte must be non-negative.", nameof(request));
        if (request.MaxDte is < 0)
            throw new ArgumentException("MaxDte must be non-negative when supplied.", nameof(request));
        if (request.MinIvPercentile is < 0 or > 100)
            throw new ArgumentException("MinIvPercentile must be in [0, 100].", nameof(request));
        if (request.MaxSpreadPct < 0)
            throw new ArgumentException("MaxSpreadPct must be non-negative.", nameof(request));
        if (request.TakeProfitCapture is <= 0 or > 1.0)
            throw new ArgumentException("TakeProfitCapture must be in (0, 1].", nameof(request));
        if (request.RollDelta is <= 0 or > 1.0)
            throw new ArgumentException("RollDelta must be in (0, 1].", nameof(request));
        if (request.ExDivWindowDays < 0)
            throw new ArgumentException("ExDivWindowDays must be non-negative.", nameof(request));

        return new OptionsOverwriteParams
        {
            MinStrike = request.MinStrike,
            OverwriteRatio = request.OverwriteRatio,
            MaxDelta = request.MaxDelta,
            MinDte = request.MinDte,
            MaxDte = request.MaxDte,
            MinIvPercentile = request.MinIvPercentile,
            MinOpenInterest = request.MinOpenInterest,
            MinVolume = request.MinVolume,
            MaxSpreadPct = request.MaxSpreadPct,
            TakeProfitCapture = request.TakeProfitCapture,
            RollDelta = request.RollDelta,
            ExDivWindowDays = request.ExDivWindowDays,
            ScoringMode = request.ScoringMode == OverwriteScoringModeDto.Basic
                ? OverwriteScoringMode.Basic
                : OverwriteScoringMode.Relative,
            DepthBonusWeight = request.DepthBonusWeight,
            RiskFreeRate = request.RiskFreeRate
        };
    }

    /// <summary>Maps the strategy's metrics record to its DTO mirror.</summary>
    public static CoveredCallMetricsDto ToMetrics(OptionsOverwriteMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        return new CoveredCallMetricsDto(
            Cagr: metrics.Cagr,
            AnnualizedVolatility: metrics.AnnualizedVolatility,
            SharpeRatio: metrics.SharpeRatio,
            SortinoRatio: metrics.SortinoRatio,
            CalmarRatio: metrics.CalmarRatio,
            MaxDrawdownPct: metrics.MaxDrawdownPct,
            WinRate: metrics.WinRate,
            AssignmentRate: metrics.AssignmentRate,
            AverageHoldingDays: metrics.AverageHoldingDays,
            TotalOptionTrades: metrics.TotalOptionTrades,
            AssignedTrades: metrics.AssignedTrades,
            TotalPremiumCollected: metrics.TotalPremiumCollected,
            TotalOptionPnl: metrics.TotalOptionPnl,
            UpCapture: metrics.UpCapture,
            DownCapture: metrics.DownCapture,
            MonthlyVar1Pct: metrics.MonthlyVar1Pct,
            MonthlyVar5Pct: metrics.MonthlyVar5Pct,
            MonthlyCVar5Pct: metrics.MonthlyCVar5Pct,
            ReturnSkewness: metrics.ReturnSkewness,
            ReturnKurtosis: metrics.ReturnKurtosis,
            AnnualizedTurnover: metrics.AnnualizedTurnover);
    }

    /// <summary>Maps an equity curve tuple to its DTO mirror.</summary>
    public static IReadOnlyList<CoveredCallEquityPoint> ToEquityCurve(
        IReadOnlyList<(DateOnly Date, decimal StrategyEquity, decimal UnderlyingEquity)> curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        var result = new CoveredCallEquityPoint[curve.Count];
        for (var i = 0; i < curve.Count; i++)
        {
            var (date, strat, under) = curve[i];
            result[i] = new CoveredCallEquityPoint(date, strat, under);
        }
        return result;
    }

    /// <summary>Maps a completed trade record to its DTO.</summary>
    public static CoveredCallTradeDto ToTrade(OptionsOverwriteTradeRecord trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        return new CoveredCallTradeDto(
            Strike: trade.Strike,
            Expiration: trade.Expiration,
            Contracts: trade.Contracts,
            Multiplier: trade.Multiplier,
            EntryDate: trade.EntryDate,
            EntryCredit: trade.EntryCredit,
            ExitDate: trade.ExitDate,
            ExitDebit: trade.ExitDebit,
            ExitReason: trade.ExitReason.ToString(),
            EntryImpliedVolatility: trade.EntryImpliedVolatility,
            NetPnlPerContract: trade.NetPnlPerContract,
            TotalNetPnl: trade.TotalNetPnl,
            HoldingDays: trade.HoldingDays,
            IsWin: trade.IsWin,
            WasAssigned: trade.WasAssigned);
    }

    /// <summary>Maps an open position to its DTO.</summary>
    public static CoveredCallOpenPositionDto ToOpenPosition(ShortCallPosition pos)
    {
        ArgumentNullException.ThrowIfNull(pos);
        return new CoveredCallOpenPositionDto(
            PositionId: pos.PositionId,
            Strike: pos.Strike,
            Expiration: pos.Expiration,
            Contracts: pos.Contracts,
            Multiplier: pos.Multiplier,
            EntryDate: pos.EntryDate,
            EntryCredit: pos.EntryCredit,
            MarkToClose: pos.MarkToClose,
            CurrentDelta: pos.CurrentDelta,
            CurrentDte: pos.CurrentDte,
            UnrealisedPnl: pos.UnrealisedPnl,
            PremiumCaptured: pos.PremiumCaptured);
    }

    /// <summary>Assembles the public-facing result for a completed run.</summary>
    public static CoveredCallRunResult ToResult(
        string runId,
        CoveredCallBacktestRequest request,
        CoveredCallOverwriteStrategy strategy,
        IReadOnlyList<(DateOnly, decimal, decimal)> equityCurve)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(strategy);

        var metrics = strategy.Metrics ?? throw new InvalidOperationException(
            "Strategy.Metrics is null — CoveredCallOverwriteStrategy.OnFinished must run before projection.");

        var trades = strategy.CompletedTrades.Select(ToTrade).ToArray();
        var openPositions = strategy.OpenPositions.Select(ToOpenPosition).ToArray();

        return new CoveredCallRunResult(
            RunId: runId,
            UnderlyingSymbol: request.UnderlyingSymbol,
            From: request.From,
            To: request.To,
            Label: request.Label,
            Metrics: ToMetrics(metrics),
            EquityCurve: ToEquityCurve(equityCurve),
            Trades: trades,
            OpenPositionsAtEnd: openPositions);
    }

    /// <summary>Maps an <see cref="OptionCandidateInfo"/> + filter outcome into a UI row.</summary>
    public static CoveredCallChainRow ToChainRow(OptionCandidateInfo candidate, bool meetsAllFilters, string? rejectReason) =>
        new(
            Strike: candidate.Strike,
            Expiration: candidate.Expiration,
            DaysToExpiration: candidate.DaysToExpiration,
            Bid: candidate.Bid,
            Ask: candidate.Ask,
            Delta: candidate.Delta,
            ImpliedVolatility: candidate.ImpliedVolatility,
            OpenInterest: candidate.OpenInterest,
            Volume: candidate.Volume,
            MeetsAllFilters: meetsAllFilters,
            RejectReason: rejectReason);
}
