using Meridian.Contracts.SecurityMaster;

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

/// <summary>
/// Restates face-value (bond) fill prices from percent of par to dollars per unit of face before
/// the inner model sees them, so notional-based commissions charge on 100,000 face × 101.25 / 100
/// rather than 100× that. Quantity-based schedules still see face units.
/// </summary>
internal sealed class FaceValueQuotedCommissionModel : ICommissionModel
{
    private readonly ICommissionModel _inner;
    private readonly HashSet<string> _faceValueSymbols;

    private FaceValueQuotedCommissionModel(ICommissionModel inner, HashSet<string> faceValueSymbols)
    {
        _inner = inner;
        _faceValueSymbols = faceValueSymbols;
    }

    public static ICommissionModel Wrap(
        ICommissionModel inner,
        IReadOnlyDictionary<string, (SecurityLotUnitKind UnitKind, decimal Multiplier)> lotConventions)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(lotConventions);
        var faceValueSymbols = lotConventions
            .Where(static pair => pair.Value.UnitKind == SecurityLotUnitKind.FaceValue)
            .Select(static pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return faceValueSymbols.Count == 0 ? inner : new FaceValueQuotedCommissionModel(inner, faceValueSymbols);
    }

    public decimal Calculate(string symbol, long quantity, decimal fillPrice)
        => _inner.Calculate(symbol, quantity, _faceValueSymbols.Contains(symbol) ? fillPrice / 100m : fillPrice);
}
