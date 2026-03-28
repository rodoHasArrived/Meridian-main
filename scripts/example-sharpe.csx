/// QuantScript — Example: Annualised Sharpe Ratio
/// ------------------------------------------------
/// This script demonstrates the QuantScript API by loading price data for a
/// symbol, computing daily log-returns, and printing the annualised Sharpe ratio
/// together with a quick equity-curve plot.
///
/// How to use:
///   1. Open the QuantScript page (sidebar → QuantScript).
///   2. Make sure you have local bar data for the chosen symbol (run a backfill
///      first if needed: `dotnet run -- --backfill --backfill-symbols SPY ...`).
///   3. Adjust the parameters in the sidebar or edit the Param<T> calls below.
///   4. Click Run (▶) to execute.
///
/// Parameters (overridable from the sidebar):
///   Symbol          — ticker to analyse      (default: "SPY")
///   LookbackMonths  — history window          (default: 12)
///   RiskFreeRate    — annual Rf for Sharpe    (default: 0.04)

// ── Parameters ───────────────────────────────────────────────────────────────

var symbol         = Param<string>("Symbol",         "SPY",  description: "Ticker symbol to analyse");
var lookbackMonths = Param<int>   ("LookbackMonths", 12,     min: 1, max: 120,
                                   description: "Number of months of history to load");
var riskFreeRate   = Param<double>("RiskFreeRate",   0.04,   min: 0.0, max: 0.20,
                                   description: "Annual risk-free rate (e.g. 0.04 = 4%)");

// ── Load price data ───────────────────────────────────────────────────────────

var toDate   = DateTime.Today;
var fromDate = toDate.AddMonths(-lookbackMonths);

Print($"Loading {symbol} from {fromDate:d} to {toDate:d} …");

var prices = Data.GetPrices(symbol, fromDate, toDate);

if (prices.Count < 5)
{
    Print($"[WARNING] Only {prices.Count} bars returned — run a backfill first.");
    return;
}

Print($"Loaded {prices.Count} bars.");

// ── Compute daily log-returns ─────────────────────────────────────────────────

var returns = prices.LogReturns();
Print($"Return observations: {returns.Count}");

// ── Statistics ────────────────────────────────────────────────────────────────

var sharpe     = SharpeRatio(returns, riskFreeRate);
var sortino    = SortinoRatio(returns, riskFreeRate);
var maxDD      = MaxDrawdown(returns);
var annualVol  = AnnualizedVolatility(returns);

PrintMetric("Sharpe (annualised)",  Math.Round(sharpe,    4), "Risk-Adjusted");
PrintMetric("Sortino (annualised)", Math.Round(sortino,   4), "Risk-Adjusted");
PrintMetric("Max Drawdown",         Math.Round(maxDD * 100, 2) + "%", "Risk");
PrintMetric("Annual Volatility",    Math.Round(annualVol * 100, 2) + "%", "Risk");

Print($"Sharpe ratio  : {sharpe:F4}");
Print($"Sortino ratio : {sortino:F4}");
Print($"Max drawdown  : {maxDD:P2}");
Print($"Annual vol    : {annualVol:P2}");

// ── Equity curve plot ─────────────────────────────────────────────────────────

var closePrices = prices.Bars.Select(b => b.Close).ToList();
Plots.Line(
    title: $"{symbol} — Closing Price",
    dates:  prices.Bars.Select(b => b.Date).ToList(),
    values: closePrices,
    label: symbol);

// ── Rolling 20-day Sharpe (simple approximation) ─────────────────────────────

const int window = 20;
if (returns.Count >= window)
{
    var returnValues = returns.ToList().Select(r => r.Value).ToList();
    var rollingDates  = new List<DateTime>();
    var rollingSharpe = new List<double>();

    for (var i = window; i <= returnValues.Count; i++)
    {
        var slice = returnValues.Skip(i - window).Take(window).ToArray();
        var mean  = slice.Average();
        var std   = Math.Sqrt(slice.Select(x => Math.Pow(x - mean, 2)).Average());
        if (std > 0)
        {
            rollingDates.Add(returns.ToList()[i - 1].Date);
            rollingSharpe.Add(mean / std * Math.Sqrt(252));
        }
    }

    if (rollingDates.Count > 0)
        Plots.Line(
            title:  $"{symbol} — Rolling {window}-Day Sharpe",
            dates:  rollingDates,
            values: rollingSharpe,
            label:  $"Sharpe({window}d)");
}

Print("Done.");
