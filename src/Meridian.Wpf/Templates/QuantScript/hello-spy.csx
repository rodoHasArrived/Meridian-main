var symbol = Param<string>("symbol", "SPY");
var from = new DateOnly(2024, 1, 2);
var to = new DateOnly(2024, 3, 29);

var prices = await Data.PricesAsync(symbol, from, to);
if (prices.Count == 0)
{
    Print($"No bars found for {symbol} between {from:yyyy-MM-dd} and {to:yyyy-MM-dd}.");
    return;
}

var firstBar = prices.Bars[0];
var lastBar = prices.Bars[^1];
var totalReturn = lastBar.Close / firstBar.Close - 1m;

Print($"Loaded {prices.Count} bars for {symbol}.");
Print($"From {firstBar.Date:yyyy-MM-dd} close {firstBar.Close:F2} to {lastBar.Date:yyyy-MM-dd} close {lastBar.Close:F2}.");
PrintMetric("Total Return", totalReturn.ToString("P2"));
PrintMetric("Average Daily Volume", prices.Bars.Average(bar => (double)bar.Volume).ToString("N0"));
