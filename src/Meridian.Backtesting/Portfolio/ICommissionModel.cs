namespace Meridian.Backtesting.Portfolio;

/// <summary>
/// Immutable quote for the incremental commission attributable to one candidate fill.
/// A quote does not consume per-order fee state until <see cref="ICommissionModel.Commit"/>
/// is called after the portfolio accepts the fill.
/// </summary>
public sealed record CommissionQuote
{
    internal CommissionQuote(
        object owner,
        Guid orderId,
        string symbol,
        long quantity,
        decimal fillPrice,
        decimal amount,
        long expectedVersion,
        decimal cumulativeAbsoluteQuantity,
        decimal cumulativeNotional,
        decimal cumulativeCommission)
    {
        Owner = owner;
        OrderId = orderId;
        Symbol = symbol;
        Quantity = quantity;
        FillPrice = fillPrice;
        Amount = amount;
        ExpectedVersion = expectedVersion;
        CumulativeAbsoluteQuantity = cumulativeAbsoluteQuantity;
        CumulativeNotional = cumulativeNotional;
        CumulativeCommission = cumulativeCommission;
    }

    public Guid OrderId { get; }
    public string Symbol { get; }
    public long Quantity { get; }
    public decimal FillPrice { get; }
    public decimal Amount { get; }

    internal object Owner { get; }
    internal long ExpectedVersion { get; }
    internal decimal CumulativeAbsoluteQuantity { get; }
    internal decimal CumulativeNotional { get; }
    internal decimal CumulativeCommission { get; }
}

/// <summary>One candidate execution used to quote an ordered batch without consuming fee state.</summary>
public readonly record struct CommissionFill(string Symbol, long Quantity, decimal FillPrice);

/// <summary>
/// Computes brokerage commission for fills and tracks the amount already charged to each order.
/// </summary>
public interface ICommissionModel
{
    /// <summary>
    /// Quotes the incremental commission for a candidate fill without mutating order fee state.
    /// The default implementation preserves compatibility with stateless models that only
    /// implement <see cref="Calculate"/>; built-in models override it with per-order accumulation.
    /// </summary>
    CommissionQuote Quote(Guid orderId, string symbol, long quantity, decimal fillPrice)
    {
        var amount = Calculate(symbol, quantity, fillPrice);
        var absoluteQuantity = Math.Abs((decimal)quantity);
        return new CommissionQuote(
            this,
            orderId,
            symbol,
            quantity,
            fillPrice,
            amount,
            expectedVersion: 0,
            cumulativeAbsoluteQuantity: absoluteQuantity,
            cumulativeNotional: absoluteQuantity * fillPrice,
            cumulativeCommission: amount);
    }

    /// <summary>
    /// Quotes an ordered batch against one order without mutating fee state. Stateful models
    /// override this method so each returned quote is chained to the preceding provisional slice.
    /// </summary>
    IReadOnlyList<CommissionQuote> QuoteBatch(
        Guid orderId,
        IReadOnlyList<CommissionFill> fills)
    {
        ArgumentNullException.ThrowIfNull(fills);
        var quotes = new List<CommissionQuote>(fills.Count);
        foreach (var fill in fills)
            quotes.Add(Quote(orderId, fill.Symbol, fill.Quantity, fill.FillPrice));
        return quotes;
    }

    /// <summary>
    /// Commits a previously quoted candidate after the portfolio accepts that fill.
    /// Stateless legacy models inherit the default no-op implementation.
    /// </summary>
    void Commit(CommissionQuote quote) => ArgumentNullException.ThrowIfNull(quote);

    /// <summary>
    /// Calculates a standalone single-fill commission for compatibility and diagnostics.
    /// This method does not read or mutate per-order accumulation state.
    /// </summary>
    decimal Calculate(string symbol, long quantity, decimal fillPrice);
}

/// <summary>Fixed commission once per accepted order, regardless of fill count or size.</summary>
public sealed class FixedCommissionModel : ICommissionModel
{
    private readonly CommissionAccumulator _accumulator;

    public FixedCommissionModel(decimal commissionPerOrder = 0m)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(commissionPerOrder);
        _accumulator = new CommissionAccumulator((_, _) => commissionPerOrder);
    }

    public CommissionQuote Quote(Guid orderId, string symbol, long quantity, decimal fillPrice)
        => _accumulator.Quote(orderId, symbol, quantity, fillPrice);

    public IReadOnlyList<CommissionQuote> QuoteBatch(Guid orderId, IReadOnlyList<CommissionFill> fills)
        => _accumulator.QuoteBatch(orderId, fills);

    public void Commit(CommissionQuote quote) => _accumulator.Commit(quote);

    public decimal Calculate(string symbol, long quantity, decimal fillPrice)
        => _accumulator.Calculate(quantity, fillPrice);
}

/// <summary>Per-share commission with a minimum and maximum applied once per order.</summary>
public sealed class PerShareCommissionModel : ICommissionModel
{
    private readonly CommissionAccumulator _accumulator;

    public PerShareCommissionModel(
        decimal perShare = 0.005m,
        decimal minimumPerOrder = 1.00m,
        decimal maximumPerOrder = decimal.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(perShare);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumPerOrder);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumPerOrder);
        if (maximumPerOrder < minimumPerOrder)
            throw new ArgumentException("Maximum commission must be greater than or equal to the minimum.", nameof(maximumPerOrder));

        _accumulator = new CommissionAccumulator(
            (absoluteQuantity, _) => Math.Min(Math.Max(absoluteQuantity * perShare, minimumPerOrder), maximumPerOrder));
    }

    public CommissionQuote Quote(Guid orderId, string symbol, long quantity, decimal fillPrice)
        => _accumulator.Quote(orderId, symbol, quantity, fillPrice);

    public IReadOnlyList<CommissionQuote> QuoteBatch(Guid orderId, IReadOnlyList<CommissionFill> fills)
        => _accumulator.QuoteBatch(orderId, fills);

    public void Commit(CommissionQuote quote) => _accumulator.Commit(quote);

    public decimal Calculate(string symbol, long quantity, decimal fillPrice)
        => _accumulator.Calculate(quantity, fillPrice);
}

/// <summary>Percentage-of-notional commission model with a minimum applied once per order.</summary>
public sealed class PercentageCommissionModel : ICommissionModel
{
    private readonly CommissionAccumulator _accumulator;

    public PercentageCommissionModel(
        decimal basisPoints = 5m,
        decimal minimumPerOrder = 1.00m)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(basisPoints);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumPerOrder);
        _accumulator = new CommissionAccumulator(
            (_, notional) => Math.Max(notional * (basisPoints / 10_000m), minimumPerOrder));
    }

    public CommissionQuote Quote(Guid orderId, string symbol, long quantity, decimal fillPrice)
        => _accumulator.Quote(orderId, symbol, quantity, fillPrice);

    public IReadOnlyList<CommissionQuote> QuoteBatch(Guid orderId, IReadOnlyList<CommissionFill> fills)
        => _accumulator.QuoteBatch(orderId, fills);

    public void Commit(CommissionQuote quote) => _accumulator.Commit(quote);

    public decimal Calculate(string symbol, long quantity, decimal fillPrice)
        => _accumulator.Calculate(quantity, fillPrice);
}

/// <summary>
/// Single-threaded per-order accumulator shared by the concrete pricing models. Backtest engines
/// own one model instance per run, so no cross-run state or synchronization is required.
/// </summary>
internal sealed class CommissionAccumulator(Func<decimal, decimal, decimal> calculateTotalCommission)
{
    private readonly Dictionary<Guid, OrderCommissionState> _orders = [];

    public CommissionQuote Quote(Guid orderId, string symbol, long quantity, decimal fillPrice)
    {
        var prior = _orders.GetValueOrDefault(orderId);
        return BuildQuote(orderId, symbol, quantity, fillPrice, prior);
    }

    public IReadOnlyList<CommissionQuote> QuoteBatch(
        Guid orderId,
        IReadOnlyList<CommissionFill> fills)
    {
        ArgumentNullException.ThrowIfNull(fills);
        var quotes = new List<CommissionQuote>(fills.Count);
        var provisional = _orders.GetValueOrDefault(orderId);
        foreach (var fill in fills)
        {
            var quote = BuildQuote(orderId, fill.Symbol, fill.Quantity, fill.FillPrice, provisional);
            quotes.Add(quote);
            provisional = new OrderCommissionState(
                provisional.Version + 1,
                quote.CumulativeAbsoluteQuantity,
                quote.CumulativeNotional,
                quote.CumulativeCommission);
        }

        return quotes;
    }

    private CommissionQuote BuildQuote(
        Guid orderId,
        string symbol,
        long quantity,
        decimal fillPrice,
        OrderCommissionState prior)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentOutOfRangeException.ThrowIfNegative(fillPrice);

        var absoluteQuantity = Math.Abs((decimal)quantity);
        var cumulativeAbsoluteQuantity = prior.AbsoluteQuantity + absoluteQuantity;
        var cumulativeNotional = prior.Notional + (absoluteQuantity * fillPrice);
        var cumulativeCommission = calculateTotalCommission(cumulativeAbsoluteQuantity, cumulativeNotional);
        var incremental = Math.Max(0m, cumulativeCommission - prior.Commission);

        return new CommissionQuote(
            this,
            orderId,
            symbol,
            quantity,
            fillPrice,
            incremental,
            prior.Version,
            cumulativeAbsoluteQuantity,
            cumulativeNotional,
            cumulativeCommission);
    }

    public void Commit(CommissionQuote quote)
    {
        ArgumentNullException.ThrowIfNull(quote);
        if (!ReferenceEquals(quote.Owner, this))
            throw new InvalidOperationException("Commission quote belongs to a different commission model.");

        var prior = _orders.GetValueOrDefault(quote.OrderId);
        if (prior.Version != quote.ExpectedVersion)
        {
            throw new InvalidOperationException(
                $"Commission quote for order '{quote.OrderId}' is stale and cannot be committed.");
        }

        _orders[quote.OrderId] = new OrderCommissionState(
            prior.Version + 1,
            quote.CumulativeAbsoluteQuantity,
            quote.CumulativeNotional,
            quote.CumulativeCommission);
    }

    public decimal Calculate(long quantity, decimal fillPrice)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fillPrice);
        var absoluteQuantity = Math.Abs((decimal)quantity);
        return calculateTotalCommission(absoluteQuantity, absoluteQuantity * fillPrice);
    }

    private readonly record struct OrderCommissionState(
        long Version,
        decimal AbsoluteQuantity,
        decimal Notional,
        decimal Commission);
}
