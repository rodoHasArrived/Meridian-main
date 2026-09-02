namespace Meridian.Backtesting.Sdk;

/// <summary>
/// Preserves the source tax-lot facts that contributed basis to a synthesized whole successor
/// share. Quantities may be fractional even though the enclosing <see cref="OpenLot"/> remains a
/// whole-unit position lot.
/// </summary>
public sealed record OpenLotBasisComponent(
    Guid SourceLotId,
    Guid SourceOpenFillId,
    DateTimeOffset OpenedAt,
    decimal SuccessorQuantity,
    decimal AllocatedBasis);

/// <summary>
/// Represents an open (unrealised) tax lot — a block of shares acquired in a single fill.
/// Lots are always positive-quantity records. <see cref="IsShort"/> carries direction explicitly
/// when a short lot leaves the portfolio's internal short-lot collection in a snapshot.
/// </summary>
public sealed record OpenLot(
    Guid LotId,
    string Symbol,
    long Quantity,           // always positive; lots are never negative
    decimal EntryPrice,
    DateTimeOffset OpenedAt,
    Guid OpenFillId,
    string? AccountId = null,
    string? Notes = null)
{
    private IReadOnlyList<OpenLotBasisComponent> _basisComponents =
        Array.Empty<OpenLotBasisComponent>();

    /// <summary>
    /// True when the positive lot quantity represents a short-sale obligation. Kept outside the
    /// positional constructor so existing constructor and deconstruction shapes remain valid.
    /// </summary>
    public bool IsShort { get; init; }

    /// <summary>
    /// Exact source-lot contributions when a corporate action combines fractional entitlements
    /// into a whole successor share. Empty for ordinary single-source lots. Kept outside the
    /// positional constructor so existing constructor and deconstruction shapes remain valid.
    /// </summary>
    public IReadOnlyList<OpenLotBasisComponent> BasisComponents
    {
        get => _basisComponents;
        init => _basisComponents = Array.AsReadOnly(value?.ToArray() ?? []);
    }

    /// <summary>Exact basis represented by this lot, including synthesized components.</summary>
    public decimal CostBasis() =>
        BasisComponents is { Count: > 0 }
            ? BasisComponents.Sum(static component => component.AllocatedBasis)
            : Quantity * EntryPrice;

    /// <summary>Mark-to-market unrealised P&amp;L for this lot at the given price.</summary>
    public decimal UnrealizedPnl(decimal currentPrice) =>
        IsShort
            ? CostBasis() - (currentPrice * Quantity)
            : (currentPrice * Quantity) - CostBasis();

    /// <summary>Current notional value of this lot at the given price.</summary>
    public decimal NotionalValue(decimal currentPrice) =>
        Quantity * currentPrice;

    /// <summary>
    /// How long the newest basis component has been open as of the given point in time. Using the
    /// newest component prevents a mixed-age composite lot from overstating its holding period.
    /// </summary>
    public TimeSpan Age(DateTimeOffset asOf) => asOf - HoldingPeriodStart;

    /// <summary>
    /// Returns <c>true</c> when the lot has been held for at least 365 days — the IRS
    /// long-term capital gains threshold.
    /// </summary>
    public bool IsLongTerm(DateTimeOffset asOf) =>
        BasisComponents is { Count: > 0 }
            ? BasisComponents.All(component =>
                asOf - component.OpenedAt >= TimeSpan.FromDays(365))
            : asOf - OpenedAt >= TimeSpan.FromDays(365);

    private DateTimeOffset HoldingPeriodStart =>
        BasisComponents is { Count: > 0 }
            ? BasisComponents.Max(static component => component.OpenedAt)
            : OpenedAt;
}
