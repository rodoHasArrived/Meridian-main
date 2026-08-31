namespace Meridian.Execution.PaperMatching;

/// <summary>How paper-fill commission is computed.</summary>
public enum PaperCommissionKind
{
    /// <summary><see cref="PaperTradingCostOptions.CommissionRate"/> is charged per share/contract.</summary>
    PerShare,

    /// <summary><see cref="PaperTradingCostOptions.CommissionRate"/> is charged in basis points of fill notional.</summary>
    BasisPointsOfNotional,

    /// <summary><see cref="PaperTradingCostOptions.CommissionRate"/> is a fixed amount per order.</summary>
    FixedPerOrder
}

/// <summary>
/// Cost model configuration applied to every paper fill (commission, fees, and slippage;
/// spread cost arises from filling at the observed bid/ask side and is reported per fill).
/// Bind from <c>appsettings.json</c> using the <see cref="SectionKey"/> section.
/// Defaults model a retail per-share commission schedule so paper economics are
/// non-zero out of the box; set rates to zero explicitly to simulate free execution.
/// </summary>
public sealed class PaperTradingCostOptions
{
    /// <summary>Configuration section key for <c>IOptions</c> binding.</summary>
    public const string SectionKey = "PaperTrading:Costs";

    /// <summary>Commission computation kind. Default: per share.</summary>
    public PaperCommissionKind CommissionKind { get; set; } = PaperCommissionKind.PerShare;

    /// <summary>
    /// Commission rate. Interpreted per <see cref="CommissionKind"/>: currency per share,
    /// basis points of notional, or currency per order. Default: 0.005 per share.
    /// </summary>
    public decimal CommissionRate { get; set; } = 0.005m;

    /// <summary>Minimum commission per order. Default: 1.00.</summary>
    public decimal CommissionMinimum { get; set; } = 1.00m;

    /// <summary>Optional maximum commission per order.</summary>
    public decimal? CommissionMaximum { get; set; }

    /// <summary>Flat regulatory/exchange fee charged per order. Default: 0.</summary>
    public decimal FeePerOrder { get; set; }

    /// <summary>Fee in basis points of fill notional. Default: 0.</summary>
    public decimal FeeBasisPoints { get; set; }

    /// <summary>
    /// Slippage in basis points of fill notional, charged as an explicit cash cost on every
    /// fill. The fill price itself stays inside the observed market-data envelope; slippage
    /// models execution friction beyond the observed spread. Default: 0.
    /// </summary>
    public decimal SlippageBasisPoints { get; set; }
}
