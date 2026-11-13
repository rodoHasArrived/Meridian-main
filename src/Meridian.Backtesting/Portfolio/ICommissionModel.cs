namespace Meridian.Backtesting.Portfolio;

/// <summary>Computes total brokerage commission for the executed portion of one order.</summary>
public interface ICommissionModel
{
    decimal Calculate(string symbol, long quantity, decimal fillPrice);
}

/// <summary>Fixed commission per order regardless of size.</summary>
public sealed class FixedCommissionModel(decimal commissionPerOrder = 0m) : ICommissionModel
{
    public decimal Calculate(string symbol, long quantity, decimal fillPrice) => commissionPerOrder;
}

/// <summary>Per-share commission with optional minimum and maximum.</summary>
public sealed class PerShareCommissionModel(
    decimal perShare = 0.005m,
    decimal minimumPerOrder = 1.00m,
    decimal maximumPerOrder = decimal.MaxValue) : ICommissionModel
{
    public decimal Calculate(string symbol, long quantity, decimal fillPrice)
    {
        var raw = Math.Abs(quantity) * perShare;
        return Math.Min(Math.Max(raw, minimumPerOrder), maximumPerOrder);
    }
}

/// <summary>Percentage-of-notional commission model.</summary>
public sealed class PercentageCommissionModel(
    decimal basisPoints = 5m,          // 5 bps = 0.05%
    decimal minimumPerOrder = 1.00m) : ICommissionModel
{
    public decimal Calculate(string symbol, long quantity, decimal fillPrice)
    {
        var notional = Math.Abs(quantity) * fillPrice;
        var raw = notional * (basisPoints / 10_000m);
        return Math.Max(raw, minimumPerOrder);
    }
}

/// <summary>Allocates per-order fees incrementally, including fills at different prices.</summary>
internal static class OrderCommission
{
    public static decimal CalculateIncrement(ICommissionModel model, Order order, long quantity, decimal price)
    {
        var executed = Math.Abs(order.FilledQuantity) + Math.Abs(quantity);
        var notional = order.FilledNotional + Math.Abs(quantity) * price;
        var cumulative = model.Calculate(order.Symbol, executed, notional / executed);
        return Math.Max(0m, cumulative - order.CommissionCharged);
    }
}
