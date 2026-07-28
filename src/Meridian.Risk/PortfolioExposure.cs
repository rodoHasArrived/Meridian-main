namespace Meridian.Risk;

/// <summary>
/// Gross market exposure for a single symbol across the aggregated portfolio.
/// </summary>
/// <param name="Symbol">Ticker symbol.</param>
/// <param name="GrossExposure">Sum of absolute position market values for the symbol.</param>
/// <param name="NetQuantity">Signed net quantity across all contributing runs.</param>
/// <param name="ReferencePrice">Best-available per-share reference price (weighted average cost until live marks flow).</param>
public sealed record SymbolExposure(
    string Symbol,
    decimal GrossExposure,
    decimal NetQuantity,
    decimal ReferencePrice);

/// <summary>
/// Point-in-time exposure snapshot of the aggregated portfolio, consumed by the
/// portfolio-aware pre-trade rules (gross exposure, concentration, order notional).
/// </summary>
/// <param name="GrossExposure">Portfolio-wide gross exposure (long plus absolute short market value).</param>
/// <param name="NetExposure">Long market value minus short market value.</param>
/// <param name="PortfolioValue">Total portfolio value (cash plus positions) when known; zero otherwise.</param>
/// <param name="SymbolExposures">Per-symbol exposure keyed case-insensitively by symbol.</param>
/// <param name="AsOf">UTC timestamp of the snapshot.</param>
public sealed record PortfolioExposureSnapshot(
    decimal GrossExposure,
    decimal NetExposure,
    decimal PortfolioValue,
    IReadOnlyDictionary<string, SymbolExposure> SymbolExposures,
    DateTimeOffset AsOf)
{
    /// <summary>Snapshot representing an empty (or unavailable) portfolio.</summary>
    public static PortfolioExposureSnapshot Empty { get; } = new(
        GrossExposure: 0m,
        NetExposure: 0m,
        PortfolioValue: 0m,
        SymbolExposures: new Dictionary<string, SymbolExposure>(StringComparer.OrdinalIgnoreCase),
        AsOf: DateTimeOffset.MinValue);

    /// <summary>Returns the exposure entry for <paramref name="symbol"/>, or a zero entry when the symbol is flat.</summary>
    public SymbolExposure GetSymbolExposure(string symbol) =>
        SymbolExposures.TryGetValue(symbol, out var exposure)
            ? exposure
            : new SymbolExposure(symbol, 0m, 0m, 0m);
}

/// <summary>
/// Supplies the live aggregated-portfolio exposure snapshot to portfolio-aware risk rules.
/// Implementations must be cheap enough to call on every pre-trade evaluation and must
/// never throw for an empty portfolio — return <see cref="PortfolioExposureSnapshot.Empty"/> instead.
/// </summary>
public interface IPortfolioExposureProvider
{
    /// <summary>Builds a point-in-time exposure snapshot of the aggregated portfolio.</summary>
    PortfolioExposureSnapshot GetSnapshot();
}
