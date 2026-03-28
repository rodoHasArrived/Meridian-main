using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Models;

namespace Meridian.QuantScript.Api;

/// <summary>
/// Adapts lambda callbacks captured by <see cref="BacktestProxy"/> to the
/// <see cref="IBacktestStrategy"/> interface. Unset callbacks default to no-ops.
/// </summary>
internal sealed class LambdaBacktestStrategy : IBacktestStrategy
{
    public string Name => "ScriptStrategy";

    private Action<IBacktestContext>? _onInitialize;
    private Action<HistoricalBar, IBacktestContext>? _onBar;
    private Action<Trade, IBacktestContext>? _onTrade;
    private Action<BboQuotePayload, IBacktestContext>? _onQuote;
    private Action<LOBSnapshot, IBacktestContext>? _onOrderBook;
    private Action<FillEvent, IBacktestContext>? _onFill;
    private Action<DateOnly, IBacktestContext>? _onDayEnd;
    private Action<IBacktestContext, BacktestResult>? _onFinished;
    private BacktestResult? _result;

    internal void SetOnInitialize(Action<IBacktestContext> handler) => _onInitialize = handler;
    internal void SetOnBar(Action<HistoricalBar, IBacktestContext> handler) => _onBar = handler;
    internal void SetOnTrade(Action<Trade, IBacktestContext> handler) => _onTrade = handler;
    internal void SetOnQuote(Action<BboQuotePayload, IBacktestContext> handler) => _onQuote = handler;
    internal void SetOnOrderBook(Action<LOBSnapshot, IBacktestContext> handler) => _onOrderBook = handler;
    internal void SetOnFill(Action<FillEvent, IBacktestContext> handler) => _onFill = handler;
    internal void SetOnDayEnd(Action<DateOnly, IBacktestContext> handler) => _onDayEnd = handler;
    internal void SetOnFinished(Action<IBacktestContext, BacktestResult> handler) => _onFinished = handler;
    internal void SetResult(BacktestResult result) => _result = result;

    public void Initialize(IBacktestContext ctx) => _onInitialize?.Invoke(ctx);
    public void OnBar(HistoricalBar bar, IBacktestContext ctx) => _onBar?.Invoke(bar, ctx);
    public void OnTrade(Trade trade, IBacktestContext ctx) => _onTrade?.Invoke(trade, ctx);
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) => _onQuote?.Invoke(quote, ctx);
    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) => _onOrderBook?.Invoke(snapshot, ctx);
    public void OnOrderFill(FillEvent fill, IBacktestContext ctx) => _onFill?.Invoke(fill, ctx);
    public void OnDayEnd(DateOnly date, IBacktestContext ctx) => _onDayEnd?.Invoke(date, ctx);
    public void OnFinished(IBacktestContext ctx) => _onFinished?.Invoke(ctx, _result!);
}
