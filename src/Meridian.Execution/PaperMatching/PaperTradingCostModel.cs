namespace Meridian.Execution.PaperMatching;

/// <summary>
/// Cost breakdown for one paper fill. <see cref="Commission"/>, <see cref="Fees"/>, and
/// <see cref="SlippageCost"/> are explicit cash costs charged to the paper account;
/// <see cref="SpreadCost"/> is the implicit cost already embedded in the fill price
/// (distance from the quote midpoint), reported for economics visibility.
/// </summary>
public sealed record PaperFillCostBreakdown(
    decimal Commission,
    decimal Fees,
    decimal SlippageCost,
    decimal SpreadCost,
    string CostModelVersion)
{
    /// <summary>Total explicit cash cost charged to the account for this fill.</summary>
    public decimal ExplicitCost => Commission + Fees + SlippageCost;

    /// <summary>A zero-cost breakdown (used when no cost model is configured).</summary>
    public static PaperFillCostBreakdown Zero { get; } =
        new(0m, 0m, 0m, 0m, PaperTradingCostModel.CostModelVersion);
}

/// <summary>
/// Computes commission, fee, slippage, and spread costs for paper fills from
/// <see cref="PaperTradingCostOptions"/>. This is the documented cost policy
/// (<see cref="CostModelVersion"/>) shared by both paper gateways and recorded on paper
/// sessions for promotion evidence.
/// </summary>
public sealed class PaperTradingCostModel
{
    /// <summary>
    /// Version identifier for this cost policy, recorded on paper sessions and cited by
    /// promotion evidence.
    /// </summary>
    public const string CostModelVersion = "paper-cost/1";

    private readonly PaperTradingCostOptions _options;

    /// <summary>Creates a cost model over the given options (defaults when omitted).</summary>
    public PaperTradingCostModel(PaperTradingCostOptions? options = null)
    {
        _options = options ?? new PaperTradingCostOptions();
    }

    /// <summary>
    /// Computes the cost breakdown for one fill.
    /// </summary>
    /// <param name="quantity">Fill quantity (sign ignored).</param>
    /// <param name="fillPrice">Fill price.</param>
    /// <param name="midPrice">
    /// Observed quote midpoint in effect at fill time, when quotes were available; used to
    /// report the implicit spread cost.
    /// </param>
    public PaperFillCostBreakdown Compute(decimal quantity, decimal fillPrice, decimal? midPrice)
    {
        var absQuantity = Math.Abs(quantity);
        if (absQuantity == 0m || fillPrice <= 0m)
        {
            return PaperFillCostBreakdown.Zero;
        }

        var notional = absQuantity * fillPrice;

        var commission = _options.CommissionKind switch
        {
            PaperCommissionKind.PerShare => absQuantity * _options.CommissionRate,
            PaperCommissionKind.BasisPointsOfNotional => notional * _options.CommissionRate / 10_000m,
            PaperCommissionKind.FixedPerOrder => _options.CommissionRate,
            _ => 0m
        };

        // The minimum floors a schedule that actually charges commission; a zero rate
        // means explicitly free execution and stays zero.
        if (commission > 0m)
        {
            commission = Math.Max(commission, _options.CommissionMinimum);
            if (_options.CommissionMaximum is { } maximum)
            {
                commission = Math.Min(commission, maximum);
            }
        }

        commission = Math.Max(commission, 0m);

        var fees = Math.Max(_options.FeePerOrder, 0m)
            + Math.Max(notional * _options.FeeBasisPoints / 10_000m, 0m);

        var slippage = Math.Max(notional * _options.SlippageBasisPoints / 10_000m, 0m);

        var spread = midPrice is { } mid && mid > 0m
            ? Math.Abs(fillPrice - mid) * absQuantity
            : 0m;

        return new PaperFillCostBreakdown(
            Commission: decimal.Round(commission, 2, MidpointRounding.AwayFromZero),
            Fees: decimal.Round(fees, 2, MidpointRounding.AwayFromZero),
            SlippageCost: decimal.Round(slippage, 2, MidpointRounding.AwayFromZero),
            SpreadCost: decimal.Round(spread, 2, MidpointRounding.AwayFromZero),
            CostModelVersion);
    }
}
