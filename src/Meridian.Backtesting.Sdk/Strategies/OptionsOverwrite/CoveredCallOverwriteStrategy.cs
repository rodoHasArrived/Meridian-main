using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite;

/// <summary>
/// Conservative covered-call (options overwrite) backtest strategy.
/// </summary>
/// <remarks>
/// <para>
/// The strategy assumes the user already holds a long position in <see cref="UnderlyingSymbol"/>
/// at the start of the backtest (either entered prior to the backtest window or at the first bar).
/// On each trading day, it:
/// <list type="number">
///   <item>Marks all open short-call positions to market using Black-Scholes.</item>
///   <item>Manages open positions (take-profit roll, delta-risk roll, dividend-risk roll, assignment at expiry).</item>
///   <item>When no short call is open and conditions are met, scans the option chain and sells the best candidate.</item>
/// </list>
/// </para>
/// <para>
/// <b>Option fills are simulated internally</b> rather than routed through the backtest engine's
/// order system, because standard OHLCV bar data does not contain option chain snapshots.
/// The underlying stock position <em>is</em> tracked via the engine (placed via
/// <see cref="IBacktestContext.PlaceMarketOrder"/>).
/// </para>
/// <para>
/// Provide an <see cref="IOptionChainProvider"/> implementation that returns
/// <see cref="OptionCandidateInfo"/> objects for each scan date.  A test double
/// (e.g. a lambda-based stub) is sufficient for unit tests.
/// </para>
/// </remarks>
public sealed class CoveredCallOverwriteStrategy : IBacktestStrategy
{
    // ------------------------------------------------------------------ //
    //  Public surface                                                     //
    // ------------------------------------------------------------------ //

    /// <inheritdoc/>
    public string Name => $"CoveredCallOverwrite({UnderlyingSymbol})";

    /// <summary>The underlying symbol being overwritten (e.g. "SPY").</summary>
    public string UnderlyingSymbol { get; }

    /// <summary>Strategy tuning parameters.</summary>
    public OptionsOverwriteParams Params { get; }

    /// <summary>
    /// Completed option trade records (populated after each closed position).
    /// Available after the strategy finishes via <see cref="OnFinished"/>.
    /// </summary>
    public IReadOnlyList<OptionsOverwriteTradeRecord> CompletedTrades => _completedTrades;

    /// <summary>
    /// Currently open short-call positions.  Typically zero or one per underlying
    /// in a conservative full-overwrite scenario.
    /// </summary>
    public IReadOnlyList<ShortCallPosition> OpenPositions => _openPositions;

    /// <summary>
    /// Computed performance metrics — available after <see cref="OnFinished"/> is called.
    /// </summary>
    public OptionsOverwriteMetrics? Metrics { get; private set; }

    // ------------------------------------------------------------------ //
    //  Private state                                                      //
    // ------------------------------------------------------------------ //

    private readonly IOptionChainProvider _chainProvider;
    private readonly ILogger _logger;

    // Option position tracking
    private readonly List<ShortCallPosition> _openPositions = [];
    private readonly List<OptionsOverwriteTradeRecord> _completedTrades = [];

    // Equity curve (for metrics)
    private readonly List<(DateOnly Date, decimal StrategyEquity, decimal UnderlyingEquity)> _equityCurve = [];

    // Underlying state
    private decimal _underlyingPrice;
    private decimal _initialUnderlyingPrice;
    private bool _underlyingBought;

    // Dividend state (updated via OnDayEnd when chain provider returns data)
    private int? _daysToNextExDiv;
    private decimal? _nextDividendAmount;

    // Running option P&L (separate from the engine's equity tracking)
    private decimal _cumulativeOptionPnl;

    // ------------------------------------------------------------------ //
    //  Construction                                                       //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Creates a new covered-call overwrite strategy.
    /// </summary>
    /// <param name="underlyingSymbol">The underlying asset symbol (must be in the backtest universe).</param>
    /// <param name="parameters">Strategy tuning parameters.</param>
    /// <param name="chainProvider">Option chain data source.</param>
    /// <param name="logger">Optional logger. Defaults to <see cref="NullLogger"/>.</param>
    public CoveredCallOverwriteStrategy(
        string underlyingSymbol,
        OptionsOverwriteParams parameters,
        IOptionChainProvider chainProvider,
        ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(underlyingSymbol))
            throw new ArgumentException("Underlying symbol must not be empty.", nameof(underlyingSymbol));

        UnderlyingSymbol = underlyingSymbol.Trim().ToUpperInvariant();
        Params = parameters ?? throw new ArgumentNullException(nameof(parameters));
        _chainProvider = chainProvider ?? throw new ArgumentNullException(nameof(chainProvider));
        _logger = logger ?? NullLogger.Instance;
    }

    // ------------------------------------------------------------------ //
    //  IBacktestStrategy callbacks                                        //
    // ------------------------------------------------------------------ //

    /// <inheritdoc/>
    public void Initialize(IBacktestContext ctx)
    {
        _logger.LogInformation(
            "[{Strategy}] Initialised. Underlying={Symbol} MinStrike={MinStrike} MaxDelta={MaxDelta} OverwriteRatio={Ratio}",
            Name, UnderlyingSymbol,
            Params.MinStrike, Params.MaxDelta, Params.OverwriteRatio);
    }

    /// <inheritdoc/>
    public void OnTrade(Trade trade, IBacktestContext ctx) { /* not used by this strategy */ }

    /// <inheritdoc/>
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { /* not used */ }

    /// <inheritdoc/>
    public void OnBar(HistoricalBar bar, IBacktestContext ctx)
    {
        if (!bar.Symbol.Equals(UnderlyingSymbol, StringComparison.OrdinalIgnoreCase))
            return;

        _underlyingPrice = bar.Close;

        // Buy the underlying on the first bar if not already held
        if (!_underlyingBought)
        {
            _initialUnderlyingPrice = bar.Close;
            _underlyingBought = true;
            // Underlying is assumed pre-held; strategy manages the option layer on top.
            // If the caller wants the engine to also buy the underlying, they can set
            // BuyUnderlyingOnFirstBar = true in a future extension.
        }
    }

    /// <inheritdoc/>
    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { /* not used */ }

    /// <inheritdoc/>
    public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { /* underlying fills handled by engine */ }

    /// <inheritdoc/>
    public void OnDayEnd(DateOnly date, IBacktestContext ctx)
    {
        if (_underlyingPrice <= 0)
            return;

        // 1. Mark all open positions to market
        MarkOpenPositions(date);

        // 2. Manage open positions (take profit / roll / assignment)
        ManageOpenPositions(date, ctx);

        // 3. If no open position, scan chain and open a new one
        if (_openPositions.Count == 0)
        {
            TryScanAndOpen(date, ctx);
        }

        // 4. Record equity snapshot
        RecordEquitySnapshot(date, ctx);
    }

    /// <inheritdoc/>
    public void OnFinished(IBacktestContext ctx)
    {
        // Force-close any remaining open positions at current underlying price
        var today = ctx.CurrentDate;
        foreach (var pos in _openPositions.ToList())
        {
            ClosePosition(pos, today, ShortCallExitReason.ForcedClose);
        }

        // Compute metrics
        Metrics = OptionsOverwriteMetricsCalculator.Calculate(
            _completedTrades,
            _equityCurve,
            Params.RiskFreeRate);

        _logger.LogInformation(
            "[{Strategy}] Finished. Trades={Trades} WinRate={WinRate:P1} TotalOptionPnl={Pnl:C2}",
            Name,
            _completedTrades.Count,
            Metrics.WinRate,
            Metrics.TotalOptionPnl);
    }

    // ------------------------------------------------------------------ //
    //  Mark-to-market                                                     //
    // ------------------------------------------------------------------ //

    private void MarkOpenPositions(DateOnly asOf)
    {
        foreach (var pos in _openPositions)
        {
            int dte = pos.Expiration.DayNumber - asOf.DayNumber;
            pos.CurrentDte = Math.Max(0, dte);

            if (dte <= 0)
            {
                // Expired — intrinsic value
                pos.MarkToClose = Math.Max(0m, _underlyingPrice - pos.Strike);
                pos.CurrentDelta = pos.MarkToClose > 0 ? 1.0 : 0.0;
                continue;
            }

            // Use entry IV for mark (a flat vol assumption; inject a vol model via chain provider for refinement)
            double iv = pos.EntryImpliedVolatility ?? 0.20;
            pos.MarkToClose = BlackScholesCalculator.MarkToClose(
                _underlyingPrice,
                pos.Strike,
                asOf,
                pos.Expiration,
                iv,
                Params.RiskFreeRate);

            double t = dte / 365.0;
            pos.CurrentDelta = BlackScholesCalculator.CallDelta(
                (double)_underlyingPrice,
                (double)pos.Strike,
                Params.RiskFreeRate,
                iv,
                t);
        }
    }

    // ------------------------------------------------------------------ //
    //  Position management                                                //
    // ------------------------------------------------------------------ //

    private void ManageOpenPositions(DateOnly date, IBacktestContext ctx)
    {
        // Iterate over a copy so we can modify the list inside the loop
        foreach (var pos in _openPositions.ToList())
        {
            string action = DeterminePositionAction(pos, date);

            _logger.LogDebug(
                "[{Strategy}] {Date} Position {Strike} {Expiry} DTE={DTE} Delta={Delta:F2} Capture={Capture:P0} → {Action}",
                Name, date, pos.Strike, pos.Expiration, pos.CurrentDte,
                pos.CurrentDelta, pos.PremiumCaptured, action);

            switch (action)
            {
                case "expire_worthless":
                    ClosePosition(pos, date, ShortCallExitReason.ExpiredWorthless);
                    break;

                case "assign":
                    ClosePosition(pos, date, ShortCallExitReason.Assigned);
                    break;

                case "take_profit_roll":
                    ClosePosition(pos, date, ShortCallExitReason.TakeProfitRoll);
                    break;

                case "risk_roll":
                    ClosePosition(pos, date, ShortCallExitReason.RiskRoll);
                    break;

                case "dividend_risk_roll":
                    ClosePosition(pos, date, ShortCallExitReason.DividendRiskRoll);
                    break;

                case "hold":
                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Determines what to do with an open short call.
    /// Returns an action string: "hold", "expire_worthless", "assign",
    /// "take_profit_roll", "risk_roll", or "dividend_risk_roll".
    /// </summary>
    private string DeterminePositionAction(ShortCallPosition pos, DateOnly asOf)
    {
        // Expiration handling
        if (asOf >= pos.Expiration)
        {
            return pos.MarkToClose > 0m
                ? "assign"          // In-the-money at expiry → assignment
                : "expire_worthless"; // Out-of-the-money at expiry → worthless
        }

        // Take-profit trigger (only with sufficient DTE remaining to avoid gamma risk)
        if (pos.PremiumCaptured >= Params.TakeProfitCapture && pos.CurrentDte > 5)
            return "take_profit_roll";

        // Delta-risk trigger (option is running too deep ITM)
        if (pos.CurrentDelta >= Params.RollDelta)
            return "risk_roll";

        // Dividend-assignment-risk trigger
        if (OptionsOverwriteFilters.OpenPositionHasDividendRisk(
                pos, Params, asOf, _underlyingPrice,
                _daysToNextExDiv, _nextDividendAmount))
            return "dividend_risk_roll";

        return "hold";
    }

    private void ClosePosition(ShortCallPosition pos, DateOnly closeDate, ShortCallExitReason reason)
    {
        decimal exitDebit = reason == ShortCallExitReason.ExpiredWorthless
            ? 0m
            : pos.MarkToClose;

        // Realise P&L
        decimal pnl = (pos.EntryCredit - exitDebit) * pos.Contracts * pos.Multiplier;
        _cumulativeOptionPnl += pnl;

        var record = new OptionsOverwriteTradeRecord(
            UnderlyingSymbol: pos.UnderlyingSymbol,
            Strike: pos.Strike,
            Expiration: pos.Expiration,
            Contracts: pos.Contracts,
            Multiplier: pos.Multiplier,
            EntryDate: pos.EntryDate,
            EntryCredit: pos.EntryCredit,
            ExitDate: closeDate,
            ExitDebit: exitDebit,
            ExitReason: reason,
            EntryImpliedVolatility: pos.EntryImpliedVolatility);

        _completedTrades.Add(record);
        _openPositions.Remove(pos);

        _logger.LogInformation(
            "[{Strategy}] Closed {Strike} {Expiry} Reason={Reason} Pnl={Pnl:C2}",
            Name, pos.Strike, pos.Expiration, reason, pnl);
    }

    // ------------------------------------------------------------------ //
    //  Chain scan and entry                                              //
    // ------------------------------------------------------------------ //

    private void TryScanAndOpen(DateOnly date, IBacktestContext ctx)
    {
        // Determine underlying share count (from engine's tracked positions)
        long underlyingShares = ctx.Positions.TryGetValue(UnderlyingSymbol, out var underlyingPos)
            ? underlyingPos.Quantity
            : 0;

        if (underlyingShares <= 0)
        {
            _logger.LogDebug("[{Strategy}] {Date} No underlying position — skipping chain scan.", Name, date);
            return;
        }

        int contracts = OptionsOverwriteScoring.PositionSize(underlyingShares, Params);
        if (contracts <= 0)
        {
            _logger.LogDebug("[{Strategy}] {Date} Position size rounds to zero contracts — skipping.", Name, date);
            return;
        }

        // Get option chain
        var chain = _chainProvider.GetCalls(UnderlyingSymbol, date, _underlyingPrice);
        if (chain.Count == 0)
        {
            _logger.LogDebug("[{Strategy}] {Date} Option chain provider returned no calls.", Name, date);
            return;
        }

        // Update dividend state from chain (chain provider may embed it)
        UpdateDividendState(chain);

        // Score and select the best call
        var best = OptionsOverwriteScoring.ChooseBestCall(
            chain, Params, _underlyingPrice,
            _daysToNextExDiv, _nextDividendAmount);

        if (best is null)
        {
            _logger.LogDebug("[{Strategy}] {Date} No call survived filters.", Name, date);
            return;
        }

        // Open the short call position
        double? iv = best.ImpliedVolatility
            ?? BlackScholesCalculator.ImpliedVolatility(
                (double)best.Mid,
                (double)_underlyingPrice,
                (double)best.Strike,
                Params.RiskFreeRate,
                best.DaysToExpiration / 365.0);

        var position = new ShortCallPosition
        {
            UnderlyingSymbol = UnderlyingSymbol,
            Strike = best.Strike,
            Expiration = best.Expiration,
            Contracts = contracts,
            Multiplier = best.Multiplier,
            Style = best.Style,
            EntryDate = date,
            EntryCredit = best.Bid, // conservative: sell at bid
            EntryImpliedVolatility = iv,
            MarkToClose = best.Bid,
            CurrentDelta = best.Delta,
            CurrentDte = best.DaysToExpiration
        };

        _openPositions.Add(position);

        _logger.LogInformation(
            "[{Strategy}] Opened short call {Strike} {Expiry} x{Contracts} Credit={Credit:C2} IV={IV:P1} Delta={Delta:F2}",
            Name, best.Strike, best.Expiration, contracts,
            best.Bid, iv ?? double.NaN, best.Delta);
    }

    private void UpdateDividendState(IReadOnlyList<OptionCandidateInfo> chain)
    {
        // Use the first entry that has dividend information
        foreach (var opt in chain)
        {
            if (opt.DaysToNextExDiv.HasValue)
            {
                _daysToNextExDiv = opt.DaysToNextExDiv;
                _nextDividendAmount = opt.NextDividendAmount;
                return;
            }
        }
    }

    // ------------------------------------------------------------------ //
    //  Equity curve recording                                             //
    // ------------------------------------------------------------------ //

    private void RecordEquitySnapshot(DateOnly date, IBacktestContext ctx)
    {
        // Strategy equity = engine-tracked equity (underlying + cash) + cumulative option P&L
        // + unrealised option P&L on open positions
        decimal unrealisedOptionPnl = _openPositions.Sum(p => p.UnrealisedPnl);
        decimal strategyEquity = ctx.PortfolioValue + _cumulativeOptionPnl + unrealisedOptionPnl;

        // Underlying-only equity = hypothetical portfolio holding only the underlying
        decimal underlyingEquity = _initialUnderlyingPrice > 0 && _underlyingPrice > 0
            ? ctx.PortfolioValue * (_underlyingPrice / _initialUnderlyingPrice)
            : ctx.PortfolioValue;

        _equityCurve.Add((date, strategyEquity, underlyingEquity));
    }
}
