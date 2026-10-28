using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Meridian.Execution.Sdk;

/// <summary>A structural problem in a new order request, before gateway or risk evaluation.</summary>
public sealed record OrderValidationError(string Field, string Code, string Message);

/// <summary>
/// An immutable request snapshot with the fields required by its pricing variant.
/// Instrument-specific price, quantity, and leg policies remain gateway and risk concerns.
/// </summary>
public sealed class ValidatedOrder
{
    private ValidatedOrder(OrderRequest request, OrderPricing pricing)
    {
        Request = request;
        Pricing = pricing;
    }

    public OrderRequest Request { get; }
    public OrderPricing Pricing { get; }

    /// <summary>Snapshots caller collections before validating the fields used downstream.</summary>
    public static bool TryCreate(
        OrderRequest request,
        [NotNullWhen(true)] out ValidatedOrder? order,
        out IReadOnlyList<OrderValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(request);

        var snapshot = request with
        {
            // Match the OMS dictionary copy semantics: retain key spellings, enumeration order,
            // and case-distinct aliases rather than collapsing them with a new comparer.
            Metadata = request.Metadata is { } metadata
                ? new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(metadata))
                : null,
            Legs = request.Legs is { } legs ? Array.AsReadOnly(legs.ToArray()) : null
        };

        var failures = new List<OrderValidationError>();
        if (string.IsNullOrWhiteSpace(snapshot.Symbol))
            failures.Add(new(nameof(OrderRequest.Symbol), "REQUIRED", "Symbol must not be blank."));
        if (!Enum.IsDefined(snapshot.Side))
            failures.Add(new(nameof(OrderRequest.Side), "UNDEFINED_ENUM", "Order side is not defined."));
        if (!Enum.IsDefined(snapshot.Type))
            failures.Add(new(nameof(OrderRequest.Type), "UNDEFINED_ENUM", "Order type is not defined."));
        if (!Enum.IsDefined(snapshot.TimeInForce))
            failures.Add(new(nameof(OrderRequest.TimeInForce), "UNDEFINED_ENUM", "Time in force is not defined."));

        switch (snapshot.Type)
        {
            case OrderType.Limit:
            case OrderType.LimitOnOpen:
            case OrderType.LimitOnClose:
                RequirePrice(snapshot.LimitPrice, nameof(OrderRequest.LimitPrice), failures);
                break;
            case OrderType.StopMarket:
                RequirePrice(snapshot.StopPrice, nameof(OrderRequest.StopPrice), failures);
                break;
            case OrderType.StopLimit:
                RequirePrice(snapshot.LimitPrice, nameof(OrderRequest.LimitPrice), failures);
                RequirePrice(snapshot.StopPrice, nameof(OrderRequest.StopPrice), failures);
                break;
            case OrderType.TrailingStop:
                if (snapshot.TrailPrice.HasValue && snapshot.TrailPercent.HasValue)
                {
                    failures.Add(new(nameof(OrderRequest.TrailPrice), "CONFLICTING_TRAIL",
                        "Specify a trail amount or a trail percentage, not both."));
                }
                else if (snapshot.TrailPrice is not > 0m && snapshot.TrailPercent is not > 0m)
                {
                    failures.Add(new(snapshot.TrailPercent.HasValue
                            ? nameof(OrderRequest.TrailPercent) : nameof(OrderRequest.TrailPrice),
                        "POSITIVE_TRAIL_REQUIRED", "A trailing order requires one positive trail amount or percentage."));
                }
                break;
        }

        errors = failures.AsReadOnly();
        if (failures.Count > 0)
        {
            order = null;
            return false;
        }

        order = new(snapshot, OrderPricing.FromValidatedRequest(snapshot));
        return true;
    }

    private static void RequirePrice(decimal? price, string field, List<OrderValidationError> failures)
    {
        if (!price.HasValue)
            failures.Add(new(field, "REQUIRED", $"{field} is required for this order type."));
    }
}

/// <summary>Closed pricing shapes: required price limbs cannot be absent in a validated order.</summary>
public abstract class OrderPricing
{
    private OrderPricing() { }

    internal static OrderPricing FromValidatedRequest(OrderRequest request) => request.Type switch
    {
        OrderType.Market or OrderType.MarketOnOpen or OrderType.MarketOnClose => new MarketStyle(),
        OrderType.Limit or OrderType.LimitOnOpen or OrderType.LimitOnClose => new LimitStyle(request.LimitPrice!.Value),
        OrderType.StopMarket => new StopMarket(request.StopPrice!.Value),
        OrderType.StopLimit => new StopLimit(request.StopPrice!.Value, request.LimitPrice!.Value),
        OrderType.TrailingStop when request.TrailPrice is { } amount => new TrailingAmount(amount),
        OrderType.TrailingStop => new TrailingPercentage(request.TrailPercent!.Value),
        _ => throw new ArgumentOutOfRangeException(nameof(request), "Only validated order types have a pricing variant.")
    };

    public sealed class MarketStyle : OrderPricing
    {
        internal MarketStyle() { }
    }

    public sealed class LimitStyle : OrderPricing
    {
        internal LimitStyle(decimal limitPrice) => LimitPrice = limitPrice;
        public decimal LimitPrice { get; }
    }

    public sealed class StopMarket : OrderPricing
    {
        internal StopMarket(decimal stopPrice) => StopPrice = stopPrice;
        public decimal StopPrice { get; }
    }

    public sealed class StopLimit : OrderPricing
    {
        internal StopLimit(decimal stopPrice, decimal limitPrice)
        {
            StopPrice = stopPrice;
            LimitPrice = limitPrice;
        }

        public decimal StopPrice { get; }
        public decimal LimitPrice { get; }
    }

    public sealed class TrailingAmount : OrderPricing
    {
        internal TrailingAmount(decimal amount) => Amount = amount;
        public decimal Amount { get; }
    }

    public sealed class TrailingPercentage : OrderPricing
    {
        internal TrailingPercentage(decimal percent) => Percent = percent;
        public decimal Percent { get; }
    }
}
