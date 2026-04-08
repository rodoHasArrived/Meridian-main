using Meridian.Backtesting.Portfolio;
using Meridian.Contracts.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Backtesting.Engine;

/// <summary>
/// Mutable context object passed into every strategy callback during replay.
/// Wraps <see cref="SimulatedPortfolio"/> and collects submitted orders.
/// </summary>
internal sealed class BacktestContext(
    SimulatedPortfolio portfolio,
    IReadOnlySet<string> universe,
    BacktestLedger ledger,
    string defaultBrokerageAccountId,
    IOptionsChainProvider? optionsProvider = null) : IBacktestContext
{
    private readonly List<Order> _pendingOrders = [];

    public IReadOnlySet<string> Universe => universe;
    public DateTimeOffset CurrentTime { get; internal set; }
    public DateOnly CurrentDate { get; internal set; }
    public decimal Cash => portfolio.Cash;
    public decimal PortfolioValue => portfolio.ComputeCurrentEquity();
    public IReadOnlyDictionary<string, Position> Positions => portfolio.GetCurrentPositions();
    public IReadOnlyDictionary<string, FinancialAccountSnapshot> Accounts => portfolio.GetAccountSnapshots();
    public IReadOnlyLedger Ledger => ledger;

    public decimal? GetLastPrice(string symbol) =>
        portfolio.LastPrices.TryGetValue(symbol, out var p) ? p : null;

    public Guid PlaceOrder(OrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Symbol);
        if (request.Quantity == 0)
            throw new ArgumentOutOfRangeException(nameof(request.Quantity), "Quantity cannot be zero.");

        if ((request.Type is OrderType.Limit or OrderType.StopLimit) && (!request.LimitPrice.HasValue || request.LimitPrice <= 0))
            throw new ArgumentOutOfRangeException(nameof(request.LimitPrice), "Limit price must be greater than zero.");

        if ((request.Type is OrderType.StopMarket or OrderType.StopLimit) && (!request.StopPrice.HasValue || request.StopPrice <= 0))
            throw new ArgumentOutOfRangeException(nameof(request.StopPrice), "Stop price must be greater than zero.");

        if (request.TakeProfitPrice.HasValue && request.TakeProfitPrice <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.TakeProfitPrice), "Take-profit price must be greater than zero.");

        if (request.StopLossPrice.HasValue && request.StopLossPrice <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.StopLossPrice), "Stop-loss price must be greater than zero.");

        var accountId = string.IsNullOrWhiteSpace(request.AccountId)
            ? defaultBrokerageAccountId
            : request.AccountId.Trim();

        var order = new Order(
            Guid.NewGuid(),
            request.Symbol,
            request.Type,
            request.Quantity,
            request.LimitPrice,
            request.StopPrice,
            CurrentTime,
            request.TimeInForce,
            request.ExecutionModel,
            request.AllowPartialFills,
            request.ProviderParameters,
            accountId,
            TakeProfitPrice: request.TakeProfitPrice,
            StopLossPrice: request.StopLossPrice);

        _pendingOrders.Add(order);
        return order.OrderId;
    }

    public Guid PlaceBracketOrder(BracketOrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return PlaceOrder(new OrderRequest(
            request.Symbol,
            request.Quantity,
            request.EntryType,
            request.LimitPrice,
            request.StopPrice,
            request.TakeProfitPrice,
            request.StopLossPrice,
            request.TimeInForce,
            request.ExecutionModel,
            request.AllowPartialFills,
            request.ProviderParameters,
            request.AccountId));
    }

    public Guid PlaceMarketOrder(string symbol, long quantity)
        => PlaceOrder(new OrderRequest(symbol, quantity, OrderType.Market));

    public Guid PlaceMarketOrder(string symbol, long quantity, string accountId)
        => PlaceOrder(new OrderRequest(symbol, quantity, OrderType.Market, AccountId: accountId));

    public Guid PlaceLimitOrder(string symbol, long quantity, decimal limitPrice)
        => PlaceOrder(new OrderRequest(symbol, quantity, OrderType.Limit, LimitPrice: limitPrice));

    public Guid PlaceLimitOrder(string symbol, long quantity, decimal limitPrice, string accountId)
        => PlaceOrder(new OrderRequest(symbol, quantity, OrderType.Limit, LimitPrice: limitPrice, AccountId: accountId));

    public Guid PlaceStopMarketOrder(string symbol, long quantity, decimal stopPrice)
        => PlaceOrder(new OrderRequest(symbol, quantity, OrderType.StopMarket, StopPrice: stopPrice));

    public Guid PlaceStopMarketOrder(string symbol, long quantity, decimal stopPrice, string accountId)
        => PlaceOrder(new OrderRequest(symbol, quantity, OrderType.StopMarket, StopPrice: stopPrice, AccountId: accountId));

    public Guid PlaceStopLimitOrder(string symbol, long quantity, decimal stopPrice, decimal limitPrice)
    {
        return PlaceOrder(new OrderRequest(
            symbol,
            quantity,
            OrderType.StopLimit,
            LimitPrice: limitPrice,
            StopPrice: stopPrice));
    }

    public Guid PlaceStopLimitOrder(string symbol, long quantity, decimal stopPrice, decimal limitPrice, string accountId)
    {
        return PlaceOrder(new OrderRequest(
            symbol,
            quantity,
            OrderType.StopLimit,
            LimitPrice: limitPrice,
            StopPrice: stopPrice,
            AccountId: accountId));
    }

    public void CancelOrder(Guid orderId) =>
        _pendingOrders.RemoveAll(o => o.OrderId == orderId);

    public void CancelContingentOrders(Guid parentOrderId) =>
        _pendingOrders.RemoveAll(o => o.ParentOrderId == parentOrderId);

    // --------------------------------------------------------------------- //
    //  Options chain access                                                   //
    // --------------------------------------------------------------------- //

    /// <inheritdoc/>
    public Task<OptionChainSnapshot?> GetOptionChainAsync(
        string underlyingSymbol,
        DateOnly expiration,
        int? strikeRange = null,
        CancellationToken ct = default)
    {
        if (optionsProvider is null)
            return Task.FromResult<OptionChainSnapshot?>(null);

        return optionsProvider.GetChainSnapshotAsync(underlyingSymbol, expiration, strikeRange, ct);
    }

    /// <inheritdoc/>
    public async Task<DateOnly?> GetNearestExpirationAsync(
        string underlyingSymbol,
        int minDte = 0,
        CancellationToken ct = default)
    {
        if (optionsProvider is null)
            return null;

        var expirations = await optionsProvider.GetExpirationsAsync(underlyingSymbol, ct).ConfigureAwait(false);
        var minDate = CurrentDate.AddDays(minDte);
        return expirations
            .Where(e => e >= minDate)
            .Select(static e => (DateOnly?)e)
            .FirstOrDefault();
    }

    internal IReadOnlyList<Order> DrainPendingOrders()
    {
        var orders = _pendingOrders.ToList();
        _pendingOrders.Clear();
        return orders;
    }
}
