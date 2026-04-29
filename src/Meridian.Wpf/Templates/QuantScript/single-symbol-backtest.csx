var symbol = Param<string>("symbol", "SPY");
var quantity = Param<int>("quantity", 100, 1, 1000);
var from = new DateOnly(2024, 1, 2);
var to = new DateOnly(2024, 6, 28);

var backtest = Backtest
    .WithSymbols(symbol)
    .From(from)
    .To(to)
    .WithInitialCash(100_000m)
    .OnBar((bar, ctx) =>
    {
        var hasPosition = ctx.Positions.TryGetValue(symbol, out var position) && position.Quantity != 0;

        if (!hasPosition && bar.Close > bar.Open)
        {
            ctx.PlaceMarketOrder(symbol, quantity);
        }
        else if (hasPosition && position!.Quantity > 0 && bar.Close < bar.Open)
        {
            ctx.PlaceMarketOrder(symbol, -position.Quantity);
        }
    });

var result = await backtest.RunAsync();

Print($"Completed backtest for {symbol}.");
PrintMetric("Net PnL", result.Metrics.NetPnl.ToString("C2"), "Backtest");
PrintMetric("Total Return", result.Metrics.TotalReturn.ToString("P2"), "Backtest");
PrintMetric("Sharpe Ratio", result.Metrics.SharpeRatio.ToString("F2"), "Backtest");
