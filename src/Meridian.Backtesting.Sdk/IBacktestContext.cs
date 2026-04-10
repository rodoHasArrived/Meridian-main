using Meridian.Ledger;

namespace Meridian.Backtesting.Sdk;

/// <summary>
/// Context object passed to every strategy callback. Provides read-only portfolio state
/// and order submission methods. All order methods are fire-and-forget within the same
/// simulated tick; fills are delivered via <see cref="IBacktestStrategy.OnOrderFill"/>.
/// </summary>
public interface IBacktestContext
{
    /// <summary>All symbols for which data is available in the requested date range.</summary>
    IReadOnlySet<string> Universe { get; }

    /// <summary>Wall-clock timestamp of the current market event being processed.</summary>
    DateTimeOffset CurrentTime { get; }

    /// <summary>Current simulated date.</summary>
    DateOnly CurrentDate { get; }

    /// <summary>Available cash across all configured accounts.</summary>
    decimal Cash { get; }

    /// <summary>Gross portfolio value: cash + long market value + short market value.</summary>
    decimal PortfolioValue { get; }

    /// <summary>Current open positions aggregated across all brokerage accounts.</summary>
    IReadOnlyDictionary<string, Position> Positions { get; }

    /// <summary>Point-in-time per-account balances and positions.</summary>
    IReadOnlyDictionary<string, FinancialAccountSnapshot> Accounts { get; }

    /// <summary>Returns the last known price for <paramref name="symbol"/>, or <c>null</c> if unseen.</summary>
    decimal? GetLastPrice(string symbol);

    /// <summary>
    /// Submit an order using the provider-independent order request surface.
    /// Provider-specific extensions can be attached via <see cref="OrderRequest.ProviderParameters"/>.
    /// If <see cref="OrderRequest.AccountId"/> is omitted, the request is routed to the default brokerage account.
    /// </summary>
    Guid PlaceOrder(OrderRequest request);

    /// <summary>
    /// Submit an entry order that automatically creates contingent exit orders after the
    /// entry fills. Exits are linked OCO-style when both a take-profit and stop-loss are set.
    /// </summary>
    Guid PlaceBracketOrder(BracketOrderRequest request);

    /// <summary>Submit a market order. Returns the assigned order ID.</summary>
    Guid PlaceMarketOrder(string symbol, long quantity);

    /// <summary>Submit a market order routed to a specific brokerage account.</summary>
    Guid PlaceMarketOrder(string symbol, long quantity, string accountId);

    /// <summary>Submit a limit order. Returns the assigned order ID.</summary>
    Guid PlaceLimitOrder(string symbol, long quantity, decimal limitPrice);

    /// <summary>Submit a limit order routed to a specific brokerage account. Returns the assigned order ID.</summary>
    Guid PlaceLimitOrder(string symbol, long quantity, decimal limitPrice, string accountId);

    /// <summary>Submit a stop-market order. Returns the assigned order ID.</summary>
    Guid PlaceStopMarketOrder(string symbol, long quantity, decimal stopPrice);

    /// <summary>Submit a stop-market order routed to a specific brokerage account.</summary>
    Guid PlaceStopMarketOrder(string symbol, long quantity, decimal stopPrice, string accountId);

    /// <summary>Submit a stop-limit order. Returns the assigned order ID.</summary>
    Guid PlaceStopLimitOrder(string symbol, long quantity, decimal stopPrice, decimal limitPrice);

    /// <summary>Submit a stop-limit order routed to a specific brokerage account.</summary>
    Guid PlaceStopLimitOrder(string symbol, long quantity, decimal stopPrice, decimal limitPrice, string accountId);

    /// <summary>Cancel a pending order.</summary>
    void CancelOrder(Guid orderId);

    /// <summary>
    /// Cancel any working contingent exit orders generated from the specified parent order.
    /// Useful when a strategy wants to replace or remove an attached bracket after entry.
    /// </summary>
    void CancelContingentOrders(Guid parentOrderId);

    /// <summary>
    /// The double-entry accounting ledger for this backtest run.
    /// Strategies can query account balances and journal entries to audit or report on costs.
    /// The ledger is read-only to prevent strategy code from corrupting the audit trail.
    /// </summary>
    IReadOnlyLedger Ledger { get; }
}
