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
    public const string AverageVolume20D = "VOLUME_AVG_20D";
    public const string Momentum63D = "MOMENTUM_63D";
    public const string Volatility20D = "VOLATILITY_20D";
    public const string PortfolioWeight = "PORTFOLIO_WEIGHT";
    public const string LedgerCash = "LEDGER_CASH";

    /// <summary>Catalog fields the live evaluator can resolve.</summary>
    public static readonly IReadOnlySet<string> Supported =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Price,
            AverageVolume20D,
            Momentum63D,
            Volatility20D,
            PortfolioWeight,
            LedgerCash
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
            ["OPTION_EXPIRATION"] = "Options-chain expiration date; not resolvable from the live market feed."
        };

    /// <summary>Number of observations the longest supported window needs before it is warm.</summary>
    public const int MaxWindow = 64;

    /// <summary>
    /// Rolling per-symbol price and volume observations. Sized to the longest window any supported
    /// field needs (63-day momentum, so 64 observations including the base).
    /// </summary>
    internal sealed class SymbolWindow
    {
        private readonly Queue<decimal> _prices = new(MaxWindow);
        private readonly Queue<long> _volumes = new(MaxWindow);

        public int Count => _prices.Count;

        public decimal LastPrice { get; private set; }

        /// <summary>
        /// Records one observation. <paramref name="volume"/> is null for trade and quote events,
        /// which carry no session volume: the volume series simply does not advance, because
        /// pushing a zero would drag VOLUME_AVG_20D down and let a liquidity filter admit a name
        /// it was written to exclude.
        /// </summary>
        public void Observe(decimal price, long? volume)
        {
            if (price <= 0m)
            {
                return;
            }

            LastPrice = price;
            _prices.Enqueue(price);
            while (_prices.Count > MaxWindow)
            {
                _prices.Dequeue();
            }

            if (volume is not { } observed)
            {
                return;
            }

            _volumes.Enqueue(observed);
            while (_volumes.Count > MaxWindow)
            {
                _volumes.Dequeue();
            }
        }

        /// <summary>
        /// Resolves every field the plan reads for one symbol, or returns <c>false</c> with the
        /// field that is still cold. A cold window means "do not trade yet", never "assume zero".
        /// </summary>
        public bool TryResolve(
            IReadOnlySet<string> requiredFields,
            IBacktestContext ctx,
            string symbol,
            out IReadOnlyDictionary<string, decimal> values,
            out string? coldField)
        {
            var resolved = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            coldField = null;

            foreach (var field in requiredFields)
            {
                if (!TryResolveField(field, ctx, symbol, out var value))
                {
                    coldField = field;
                    values = resolved;
                    return false;
                }

                resolved[field] = value;
            }

            values = resolved;
            return true;
        }

        private bool TryResolveField(string field, IBacktestContext ctx, string symbol, out decimal value)
        {
            switch (field.ToUpperInvariant())
            {
                case Price:
                    value = LastPrice;
                    return LastPrice > 0m;
                case AverageVolume20D:
                    return TryAverageVolume(out value);
                case Momentum63D:
                    return TryMomentum(out value);
                case Volatility20D:
                    return TryVolatility(out value);
                case PortfolioWeight:
                    return TryPortfolioWeight(ctx, symbol, out value);
                case LedgerCash:
                    value = ctx.Cash;
                    return true;
                default:
                    // Unreachable for a compiled plan: DesignerStrategyPlan refuses unsupported
                    // fields before a strategy is ever constructed. Guarded anyway so a future
                    // catalog addition fails closed instead of resolving to zero.
                    value = 0m;
                    return false;
            }
        }

        private bool TryAverageVolume(out decimal value)
        {
            value = 0m;
            if (_volumes.Count < 20)
            {
                return false;
            }

            value = (decimal)_volumes.TakeLast(20).Average();
            return true;
        }

        private bool TryMomentum(out decimal value)
        {
            value = 0m;
            if (_prices.Count < 64)
            {
                return false;
            }

            var window = _prices.TakeLast(64).ToArray();
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
            if (_prices.Count < 21)
            {
                return false;
            }

            var window = _prices.TakeLast(21).ToArray();
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
