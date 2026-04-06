namespace Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite;

/// <summary>
/// Scoring mode used to rank candidate call options.
/// </summary>
public enum OverwriteScoringMode
{
    /// <summary>
    /// Score by bid price × multiplier (conservative executable premium dollar amount).
    /// </summary>
    Basic,

    /// <summary>
    /// Score by vega-dollar IV residual penalised by spread and boosted by market depth.
    /// Selects relatively expensive volatility versus an estimated IV surface.
    /// </summary>
    Relative
}

/// <summary>
/// All tunable parameters for the <see cref="CoveredCallOverwriteStrategy"/>.
/// Conservative defaults are provided for every parameter per the strategy specification.
/// </summary>
public sealed record OptionsOverwriteParams
{
    // ------------------------------------------------------------------ //
    //  Strike constraint                                                   //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Hard minimum strike the owner is willing to accept as a cap on the underlying position.
    /// Only call options with <c>strike &gt;= MinStrike</c> are eligible.
    /// Must be set by the user; there is no meaningful default.
    /// </summary>
    [StrategyParameter("Minimum strike", "Hard constraint: do not sell calls with strike below this price.")]
    public decimal MinStrike { get; init; } = 0m;

    // ------------------------------------------------------------------ //
    //  Position sizing                                                     //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Fraction of the long underlying exposure to overwrite. 1.0 = fully covered; 0.5 = 50 % overwrite.
    /// Conservative default: 0.75.
    /// </summary>
    [StrategyParameter("Overwrite ratio", "Fraction of underlying shares to overwrite. 1.0 = fully covered.")]
    public double OverwriteRatio { get; init; } = 0.75;

    // ------------------------------------------------------------------ //
    //  Risk filters                                                        //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Maximum absolute call delta allowed. Options with |delta| &gt; MaxDelta are excluded.
    /// Conservative default: 0.35 (30-delta buy-write benchmark standard).
    /// </summary>
    [StrategyParameter("Max call delta", "Do not sell calls with absolute delta above this threshold.")]
    public double MaxDelta { get; init; } = 0.35;

    /// <summary>
    /// Optional maximum days-to-expiration cap. <c>null</c> means no DTE cap is applied.
    /// Consider setting 60 if margin / liquidity concerns dominate.
    /// </summary>
    [StrategyParameter("Max DTE", "Optional maximum days to expiration. Leave null for no cap.")]
    public int? MaxDte { get; init; } = null;

    /// <summary>
    /// Optional minimum days-to-expiration. Options with fewer DTE than this are excluded.
    /// Conservative default: 7 (avoids gamma-risk pinning near expiration).
    /// </summary>
    [StrategyParameter("Min DTE", "Minimum days to expiration. Options expiring sooner are excluded.")]
    public int MinDte { get; init; } = 7;

    /// <summary>
    /// Minimum IV percentile (0–100). Avoids selling cheap volatility.
    /// Conservative default: 50 (sell only when IV is above its historical median).
    /// </summary>
    [StrategyParameter("Min IV percentile", "Minimum implied-volatility percentile (0–100). Avoids selling cheap vol.")]
    public double MinIvPercentile { get; init; } = 50.0;

    // ------------------------------------------------------------------ //
    //  Liquidity filters                                                   //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Minimum open interest for a contract to be considered liquid.
    /// Conservative default: 1,000.
    /// </summary>
    [StrategyParameter("Min open interest", "Minimum open interest required. Excludes illiquid contracts.")]
    public long MinOpenInterest { get; init; } = 1_000;

    /// <summary>
    /// Minimum daily volume for a contract.
    /// Conservative default: 100.
    /// </summary>
    [StrategyParameter("Min volume", "Minimum daily trading volume required.")]
    public long MinVolume { get; init; } = 100;

    /// <summary>
    /// Maximum (ask - bid) / mid allowed as a fraction.
    /// Conservative defaults: 5 % for liquid ETFs; 10 % for single-name stocks.
    /// Strategy default: 0.05.
    /// </summary>
    [StrategyParameter("Max spread %", "Maximum bid-ask spread as a fraction of mid price (e.g. 0.05 = 5%).")]
    public double MaxSpreadPct { get; init; } = 0.05;

    // ------------------------------------------------------------------ //
    //  Position management                                                 //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Close / roll when this fraction of the original premium has been captured.
    /// Conservative default: 0.80 (80 % capture reduces late-gamma and assignment risk).
    /// </summary>
    [StrategyParameter("Take-profit capture", "Close or roll when this fraction of initial premium has been earned.")]
    public double TakeProfitCapture { get; init; } = 0.80;

    /// <summary>
    /// Roll the call when its absolute delta rises to or above this value.
    /// Conservative default: 0.55 (prevents the short from becoming too deep ITM).
    /// </summary>
    [StrategyParameter("Roll delta trigger", "Roll when the short call's absolute delta reaches or exceeds this value.")]
    public double RollDelta { get; init; } = 0.55;

    /// <summary>
    /// Number of calendar days before ex-dividend to apply the dividend-assignment-risk filter.
    /// Conservative default: 7.
    /// </summary>
    [StrategyParameter("Ex-div window days", "Days before ex-dividend date to screen for early-assignment risk.")]
    public int ExDivWindowDays { get; init; } = 7;

    // ------------------------------------------------------------------ //
    //  Scoring                                                             //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Scoring mode: <c>Basic</c> maximises raw bid premium; <c>Relative</c> maximises
    /// vega-dollar IV residual penalised by spread and boosted by depth.
    /// Default: <see cref="OverwriteScoringMode.Relative"/>.
    /// </summary>
    [StrategyParameter("Scoring mode", "Basic: highest bid. Relative: richest vol adjusted for spread/depth.")]
    public OverwriteScoringMode ScoringMode { get; init; } = OverwriteScoringMode.Relative;

    /// <summary>
    /// Weight applied to the log-depth bonus term in the <see cref="OverwriteScoringMode.Relative"/> score.
    /// Default: 0.05.
    /// </summary>
    [StrategyParameter("Depth bonus weight", "Coefficient applied to the log-OI+volume term in the relative score.")]
    public double DepthBonusWeight { get; init; } = 0.05;

    // ------------------------------------------------------------------ //
    //  Risk-free rate (for BSM mark-to-market)                            //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Continuously-compounded risk-free rate used in Black-Scholes mark-to-market.
    /// Default: 0.04 (4 %).
    /// </summary>
    [StrategyParameter("Risk-free rate", "Annual risk-free rate for Black-Scholes option pricing.")]
    public double RiskFreeRate { get; init; } = 0.04;

    // ------------------------------------------------------------------ //
    //  Helpers                                                             //
    // ------------------------------------------------------------------ //

    /// <summary>Returns a copy with <see cref="MinStrike"/> set.</summary>
    public OptionsOverwriteParams WithMinStrike(decimal minStrike) => this with { MinStrike = minStrike };

    /// <summary>Returns a copy with <see cref="OverwriteRatio"/> set.</summary>
    public OptionsOverwriteParams WithOverwriteRatio(double ratio) => this with { OverwriteRatio = ratio };

    /// <summary>Returns a copy with <see cref="MaxDelta"/> set.</summary>
    public OptionsOverwriteParams WithMaxDelta(double maxDelta) => this with { MaxDelta = maxDelta };

    /// <summary>Returns a copy with <see cref="MinIvPercentile"/> set.</summary>
    public OptionsOverwriteParams WithMinIvPercentile(double percentile) => this with { MinIvPercentile = percentile };

    /// <summary>Returns a copy with <see cref="MaxDte"/> set.</summary>
    public OptionsOverwriteParams WithMaxDte(int? maxDte) => this with { MaxDte = maxDte };
}
