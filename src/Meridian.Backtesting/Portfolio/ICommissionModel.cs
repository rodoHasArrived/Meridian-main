namespace Meridian.Backtesting.Portfolio;

/// <summary>Computes brokerage commission for a single fill.</summary>
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
