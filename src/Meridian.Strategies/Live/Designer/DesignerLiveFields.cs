using Meridian.Backtesting.Sdk;

namespace Meridian.Strategies.Live.Designer;

/// <summary>
/// The subset of the Strategy Designer field catalog that has a real live source, and the
/// per-symbol rolling window that computes it.
/// </summary>
/// <remarks>
/// Membership here is the honest boundary between "this document can trade" and "this document
/// cannot". The catalog also publishes Security Master reference fields (CUSIP, MATURITY, COUPON,
/// YIELD, SPREAD, RATING, DURATION) and options-chain fields (OPTION_DELTA, OPTION_EXPIRATION).
/// No live feed in the trading engine resolves those per bar, so a document that filters on one
/// is refused at compile time rather than activated against a fabricated value.
/// </remarks>
internal static class DesignerLiveFields
{
    public const string Price = "PRICE";
    public const string Momentum63D = "MOMENTUM_63D";
    public const string Volatility20D = "VOLATILITY_20D";
    public const string PortfolioWeight = "PORTFOLIO_WEIGHT";

    /// <summary>Catalog fields the live evaluator can resolve.</summary>
    public static readonly IReadOnlySet<string> Supported =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Price,
            Momentum63D,
            Volatility20D,
            PortfolioWeight
        };

    /// <summary>Why a catalog field that exists has no live source, keyed by field id.</summary>
    public static readonly IReadOnlyDictionary<string, string> UnsupportedReasons =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CUSIP"] = "Security Master reference identifier; the live trading feed carries no per-bar CUSIP.",
            ["MATURITY"] = "Security Master fixed-income reference date; not resolvable from the live market feed.",
            ["COUPON"] = "Security Master fixed-income reference value; not resolvable from the live market feed.",
            ["YIELD"] = "Security Master mapped yield; not resolvable from the live market feed.",
            ["SPREAD"] = "Security Master mapped spread; not resolvable from the live market feed.",
            ["RATING"] = "Security Master rating bucket is a string; designer expressions are numeric.",
            ["DURATION"] = "Security Master effective duration; not resolvable from the live market feed.",
            ["OPTION_DELTA"] = "Options-chain greek; the live trading engine consumes equity trades, quotes, and bars only.",
            ["OPTION_EXPIRATION"] = "Options-chain expiration date; not resolvable from the live market feed.",

            // Live strategies are fed from the market-event hub, and the trading engine's event tap
            // deliberately withholds HistoricalBar from it -- bars feed the paper-matching price
            // envelope instead. Session closes can be derived from trade and quote timestamps, but
            // session *volume* cannot: the ticks one process observes are not the session's traded
            // volume, and a liquidity filter fed an undercount would silently exclude names it was
            // written to admit.
            ["VOLUME_AVG_20D"] =
                "The live strategy feed carries trades and quotes, not session bars, so traded volume "
                + "per session is not observable on this path.",

            // The catalog defines LEDGER_CASH as cash from Meridian ledger projections. The live
            // strategy context exposes only the brokerage/session cash balance, which is a
            // different authority: a ledger-based liquidity guard could pass on broker cash while
            // the governed ledger balance says otherwise. Refusing is the honest answer until a
            // ledger projection is wired into the live context.
            ["LEDGER_CASH"] =
                "The catalog sources this from Meridian ledger projections, but the live strategy context "
                + "exposes only brokerage cash. Resolving it from the wrong authority could let a ledger "
                + "guard pass against a balance it does not govern."
        };

    /// <summary>Trading sessions the longest supported window needs before it is warm.</summary>
    public const int MaxWindow = 64;

    /// <summary>
    /// Rolling per-symbol session closes plus the latest spot price.
    /// </summary>
    /// <remarks>
    /// The two are deliberately separate. <c>PRICE</c> is a spot value and tracks every event, but
    /// <c>MOMENTUM_63D</c> and <c>VOLATILITY_20D</c> are defined by the catalog as 63- and
    /// 20-<em>trading-session</em> metrics. Advancing their window per event would warm a "63-day"
    /// signal after sixty-four quotes and compute intraday tick returns under a daily field name.
    /// Sessions are therefore keyed by the event's own date: repeated events within a date restate
    /// that session's close, and a new date opens a new session. That works on the live path, where
    /// the strategy hub carries trades and quotes but never bars, as well as on bar replay.
    /// </remarks>
    internal sealed class SymbolWindow
    {
        private readonly Queue<decimal> _sessionCloses = new(MaxWindow);
        private DateOnly? _currentSessionDate;

        /// <summary>Number of sessions recorded, including the one in progress.</summary>
        public int SessionCount => _sessionCloses.Count;

        /// <summary>
        /// Trading date of the newest <em>completed</em> session, or null when none has closed.
        /// </summary>
        /// <remarks>
        /// The most recent entry is the session currently in progress: its close moves with every
        /// event until the date rolls. Session metrics read only completed sessions, so this is the
        /// date those metrics describe, and it is what cross-sectional plans align on.
        /// </remarks>
        public DateOnly? LastCompletedSessionDate { get; private set; }

        /// <summary>Latest observed price from any event kind.</summary>
        public decimal LastPrice { get; private set; }

        /// <summary>
        /// Records an observation against the trading session <paramref name="sessionDate"/>
        /// identifies. Later prices within the same session restate its close rather than
        /// advancing the window.
        /// </summary>
        public void Observe(decimal price, DateOnly sessionDate)
        {
            if (price <= 0m)
            {
                return;
            }

            LastPrice = price;

            if (_currentSessionDate == sessionDate && _sessionCloses.Count > 0)
            {
                ReplaceLastClose(price);
                return;
            }

            // The session that was in progress has just closed; its final recorded price is its
            // close, and only now is it usable by a session metric.
            if (_currentSessionDate is { } completed && _sessionCloses.Count > 0)
            {
                LastCompletedSessionDate = completed;
            }

            _currentSessionDate = sessionDate;
            _sessionCloses.Enqueue(price);
            while (_sessionCloses.Count > MaxWindow)
            {
                _sessionCloses.Dequeue();
            }
        }

        /// <summary>
        /// A field view that resolves on access rather than up front.
        /// </summary>
        /// <remarks>
        /// Resolving every field the plan mentions before evaluating would defeat the evaluator's
        /// short-circuiting: <c>PORTFOLIO_WEIGHT == 0 || MOMENTUM_63D &gt; 0</c> answers true for a
        /// flat symbol without reading momentum, but eager resolution would reject the symbol for
        /// sixty-four sessions because momentum happens to be cold. A field is only cold if the
        /// expression actually asks for it.
        /// </remarks>
        public IReadOnlyDictionary<string, decimal> CreateFieldView(IBacktestContext ctx, string symbol) =>
            new LazyFieldView(this, ctx, symbol);

        private sealed class LazyFieldView(SymbolWindow window, IBacktestContext ctx, string symbol)
            : IReadOnlyDictionary<string, decimal>
        {
            private readonly Dictionary<string, decimal> _cache = new(StringComparer.OrdinalIgnoreCase);

            public IEnumerable<string> Keys => Supported;

            public IEnumerable<decimal> Values => Supported.Select(field => this[field]);

            public int Count => Supported.Count;

            public decimal this[string key] => TryGetValue(key, out var value)
                ? value
                : throw new KeyNotFoundException(key);

            public bool ContainsKey(string key) => TryGetValue(key, out _);

            public bool TryGetValue(string key, out decimal value)
            {
                if (_cache.TryGetValue(key, out value))
                {
                    return true;
                }

                if (!window.TryResolveField(key, ctx, symbol, out value))
                {
                    return false;
                }

                _cache[key] = value;
                return true;
            }

            public IEnumerator<KeyValuePair<string, decimal>> GetEnumerator()
            {
                foreach (var field in Supported)
                {
                    if (TryGetValue(field, out var value))
                    {
                        yield return new KeyValuePair<string, decimal>(field, value);
                    }
                }
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        /// <summary>
        /// Closes of completed sessions only — the in-progress session is excluded because its
        /// price is still moving, and comparing an opening tick against other symbols' completed
        /// closes would mix timeframes inside one cross-section.
        /// </summary>
        private IEnumerable<decimal> CompletedCloses() => _sessionCloses.Take(_sessionCloses.Count - 1);

        private void ReplaceLastClose(decimal close)
        {
            var closes = _sessionCloses.ToArray();
            closes[^1] = close;
            _sessionCloses.Clear();
            foreach (var value in closes)
            {
                _sessionCloses.Enqueue(value);
            }
        }

        private bool TryResolveField(string field, IBacktestContext ctx, string symbol, out decimal value)
        {
            switch (field.ToUpperInvariant())
            {
                case Price:
                    value = LastPrice;
                    return LastPrice > 0m;
                case Momentum63D:
                    return TryMomentum(out value);
                case Volatility20D:
                    return TryVolatility(out value);
                case PortfolioWeight:
                    return TryPortfolioWeight(ctx, symbol, out value);
                default:
                    // Unreachable for a compiled plan: DesignerStrategyPlan refuses unsupported
                    // fields before a strategy is ever constructed. Guarded anyway so a future
                    // catalog addition fails closed instead of resolving to zero.
                    value = 0m;
                    return false;
            }
        }

        private bool TryMomentum(out decimal value)
        {
            value = 0m;
            // 65 recorded sessions gives 64 completed ones once the in-progress session is dropped.
            if (_sessionCloses.Count < 65)
            {
                return false;
            }

            var window = CompletedCloses().TakeLast(64).ToArray();
            var baseline = window[0];
            if (baseline <= 0m)
            {
                return false;
            }

            value = (window[^1] - baseline) / baseline;
            return true;
        }

        private bool TryVolatility(out decimal value)
        {
            value = 0m;
            if (_sessionCloses.Count < 22)
            {
                return false;
            }

            var window = CompletedCloses().TakeLast(21).ToArray();
            var returns = new double[window.Length - 1];
            for (var i = 1; i < window.Length; i++)
            {
                if (window[i - 1] <= 0m)
                {
                    return false;
                }

                returns[i - 1] = (double)((window[i] - window[i - 1]) / window[i - 1]);
            }

            var mean = returns.Average();
            var variance = returns.Sum(r => (r - mean) * (r - mean)) / (returns.Length - 1);
            // Annualized on 252 sessions to match how the catalog describes the field to
            // operators ("realized volatility"), so a 0.30 threshold in a designer document
            // means the same thing it means on the research screens.
            value = (decimal)(Math.Sqrt(variance) * Math.Sqrt(252d));
            return true;
        }

        private bool TryPortfolioWeight(IBacktestContext ctx, string symbol, out decimal value)
        {
            value = 0m;
            var portfolioValue = ctx.PortfolioValue;
            if (portfolioValue <= 0m)
            {
                return false;
            }

            var quantity = ctx.Positions.TryGetValue(symbol, out var position) ? position.Quantity : 0L;
            if (quantity == 0L)
            {
                return true;
            }

            var price = ctx.GetLastPrice(symbol) ?? LastPrice;
            if (price <= 0m)
            {
                return false;
            }

            value = quantity * price / portfolioValue;
            return true;
        }
    }
}
