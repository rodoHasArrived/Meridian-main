var symbol = Param<string>("symbol", "SPY");
var fastWindow = Param<int>("fastWindow", 20, 5, 100);
var slowWindow = Param<int>("slowWindow", 50, 10, 200);
var from = new DateOnly(2024, 1, 2);
var to = new DateOnly(2024, 6, 28);

var prices = await Data.PricesAsync(symbol, from, to);
if (prices.Count < slowWindow)
{
    Print($"Need at least {slowWindow} bars to calculate the slow window for {symbol}.");
    return;
}

double AverageClose(IEnumerable<PriceBar> bars) => bars.Average(bar => (double)bar.Close);

var fastAverage = AverageClose(prices.Bars.TakeLast(fastWindow));
var slowAverage = AverageClose(prices.Bars.TakeLast(slowWindow));
var spread = fastAverage - slowAverage;

Print($"Computed SMA spread for {symbol} over {prices.Count} bars.");
PrintMetric("Fast SMA", fastAverage.ToString("F2"), "Indicator");
PrintMetric("Slow SMA", slowAverage.ToString("F2"), "Indicator");
PrintMetric("Spread", spread.ToString("F2"), "Indicator");
