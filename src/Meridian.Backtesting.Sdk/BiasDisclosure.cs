namespace Meridian.Backtesting.Sdk;

/// <summary>Severity of a single bias-disclosure line item.</summary>
public enum BiasSeverity
{
    /// <summary>Neutral statement of a simulation assumption.</summary>
    Info,
    /// <summary>Assumption that can flatter results under some strategies or data sets.</summary>
    Caution,
    /// <summary>Known bias that materially overstates confidence in the result.</summary>
    Warning
}

/// <summary>
/// One disclosed simulation assumption or detected data-quality issue.
/// </summary>
/// <param name="Code">Stable machine-readable identifier (e.g. <c>fill-timing</c>, <c>survivorship</c>).</param>
/// <param name="Severity">How much the item can flatter reported results.</param>
/// <param name="Title">Short human-readable heading.</param>
/// <param name="Detail">Full explanation of the assumption and its effect on trustworthiness.</param>
public sealed record BiasDisclosureItem(
    string Code,
    BiasSeverity Severity,
    string Title,
    string Detail);

/// <summary>
/// Record of a position force-liquidated because its symbol stopped producing market data
/// before the end of the backtest range.
/// </summary>
/// <param name="Symbol">Ticker symbol.</param>
/// <param name="Date">Simulation date on which the liquidation was applied.</param>
/// <param name="LastDataDate">Last date on which the symbol produced a market event.</param>
/// <param name="Quantity">Signed position quantity that was closed.</param>
/// <param name="Price">Per-share liquidation price after any haircut.</param>
/// <param name="HaircutPercent">Haircut applied against the position (0 = last observed price).</param>
public sealed record DelistingLiquidation(
    string Symbol,
    DateOnly Date,
    DateOnly LastDataDate,
    long Quantity,
    decimal Price,
    decimal HaircutPercent);

/// <summary>
/// Honest summary of the simulation assumptions and detected biases behind a
/// <see cref="BacktestResult"/>. Rendered as a "bias disclosure" panel wherever results are
/// shown, so a good-looking number is never presented without its caveats.
/// </summary>
/// <param name="FillTiming">Order-eligibility timing the run executed with.</param>
/// <param name="FillConservatism">Limit/stop fill semantics the run executed with.</param>
/// <param name="DelistingPolicy">How positions in symbols whose data ended early were handled.</param>
/// <param name="UniverseSource">
/// Where the tradeable universe came from: <c>explicit-symbol-list</c> (caller-provided) or
/// <c>discovered-from-data</c> (whatever had files on disk — survivorship-prone).
/// </param>
/// <param name="CorporateActionsAdjusted">Whether bar prices were split/dividend adjusted during replay.</param>
/// <param name="SymbolsMissingFromSecurityMaster">Universe symbols with no Security Master record.</param>
/// <param name="InactiveSecurityMasterSymbols">Universe symbols marked Inactive (delisted/renamed) in the Security Master.</param>
/// <param name="DelistingLiquidations">Forced liquidations applied by the delisting policy.</param>
/// <param name="Items">Ordered disclosure line items, most severe first.</param>
public sealed record BiasDisclosureReport(
    FillTiming FillTiming,
    FillConservatism FillConservatism,
    DelistingPolicy DelistingPolicy,
    string UniverseSource,
    bool CorporateActionsAdjusted,
    IReadOnlyList<string> SymbolsMissingFromSecurityMaster,
    IReadOnlyList<string> InactiveSecurityMasterSymbols,
    IReadOnlyList<DelistingLiquidation> DelistingLiquidations,
    IReadOnlyList<BiasDisclosureItem> Items)
{
    /// <summary>Universe source value for caller-provided symbol lists.</summary>
    public const string UniverseSourceExplicit = "explicit-symbol-list";

    /// <summary>Universe source value for universes discovered from on-disk data.</summary>
    public const string UniverseSourceDiscovered = "discovered-from-data";

    /// <summary>Highest severity across all disclosed items (Info when the list is empty).</summary>
    public BiasSeverity MaxSeverity =>
        Items.Count == 0 ? BiasSeverity.Info : Items.Max(static item => item.Severity);
}
