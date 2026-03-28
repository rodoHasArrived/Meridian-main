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

var prices = Data.Prices(symbol, fromDate, toDate);

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

var sharpe    = returns.SharpeRatio(riskFreeRate);
var sortino   = returns.SortinoRatio(riskFreeRate);
var maxDD     = returns.MaxDrawdown();
var annualVol = returns.AnnualizedVolatility();

PrintMetric("Sharpe (annualised)",  Math.Round(sharpe,    4), "Risk-Adjusted");
PrintMetric("Sortino (annualised)", Math.Round(sortino,   4), "Risk-Adjusted");
PrintMetric("Max Drawdown",         Math.Round(maxDD * 100, 2) + "%", "Risk");
PrintMetric("Annual Volatility",    Math.Round(annualVol * 100, 2) + "%", "Risk");

Print($"Sharpe ratio  : {sharpe:F4}");
Print($"Sortino ratio : {sortino:F4}");
Print($"Max drawdown  : {maxDD:P2}");
Print($"Annual vol    : {annualVol:P2}");

// ── Equity curve plot ─────────────────────────────────────────────────────────

prices.CumulativeReturns().PlotCumulative(title: $"{symbol} — Cumulative Return");

// ── Daily log-returns plot ────────────────────────────────────────────────────

returns.Plot(title: $"{symbol} — Daily Log Returns");

// ── Rolling 20-day Sharpe (simple approximation) ─────────────────────────────

const int window = 20;
if (returns.Count >= window)
{
    var rolling = returns.RollingMean(window);
    rolling.Plot(title: $"{symbol} — Rolling {window}-Day Mean Return");
}

Print("Done.");
