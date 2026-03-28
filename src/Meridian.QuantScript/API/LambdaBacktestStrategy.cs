using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Models;

namespace Meridian.QuantScript.API;

/// <summary>
/// Wraps a user-supplied lambda as an <see cref="IBacktestStrategy"/>.
/// </summary>
internal sealed class LambdaBacktestStrategy : IBacktestStrategy
{
    private readonly Action<HistoricalBar, IBacktestContext> _onBar;

    internal LambdaBacktestStrategy(Action<HistoricalBar, IBacktestContext> onBar)
    {
        _onBar = onBar ?? throw new ArgumentNullException(nameof(onBar));
    }

    public string Name => "LambdaStrategy";

    public void Initialize(IBacktestContext ctx) { }

    public void OnBar(HistoricalBar bar, IBacktestContext ctx) => _onBar(bar, ctx);

    public void OnTrade(Trade trade, IBacktestContext ctx) { }

    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }

    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }

    public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }

    public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }

    public void OnFinished(IBacktestContext ctx) { }
}
