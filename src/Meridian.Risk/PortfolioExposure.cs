namespace Meridian.Risk;

/// <summary>
/// Gross market exposure for a single symbol across the aggregated portfolio.
/// </summary>
/// <param name="Symbol">Ticker symbol.</param>
/// <param name="GrossExposure">Sum of absolute position market values for the symbol.</param>
/// <param name="NetQuantity">Signed net quantity across all contributing runs.</param>
/// <param name="ReferencePrice">Best-available per-share reference price (weighted average cost until live marks flow).</param>
/// <param name="NetNotional">Signed net notional (negative when net short); zero when unknown.</param>
/// <param name="AccountNetNotional">
/// Signed net notional per contributing account/run key. Direction-aware projections need
/// to know which contribution an order actually changes: with a long book in one account
/// and a short book in another, the aggregate net says nothing about whether a given
/// order increases or decreases exposure. Empty when attribution is unavailable.
/// </param>
public sealed record SymbolExposure(
    string Symbol,
    decimal GrossExposure,
    decimal NetQuantity,
    decimal ReferencePrice,
    decimal NetNotional = 0m,
    IReadOnlyDictionary<string, decimal>? AccountNetNotional = null)
{
    /// <summary>
    /// Signed exposure the order's own account carries, for direction-aware projection.
    /// A single contributing account means the aggregate net is that account's net, so the
    /// projection is exact. With several contributing accounts the order's own account must
    /// be identified; when it cannot be, this returns <see langword="null"/> and callers
    /// fall back to the additive worst case rather than assuming a netting that may be wrong.
    /// </summary>
    public decimal? ResolveSignedExposureFor(Guid? fundAccountId)
    {
        if (fundAccountId is { } accountId)
        {
            // The order names its account: use that account's own exposure, or fall back
            // to the additive worst case when the account holds nothing here. Returning
            // the aggregate would treat an order from an empty account as if it reduced
            // another account's position — a $20k sell from a flat account against a
            // $100k long elsewhere would project $80k gross instead of $120k.
            return AccountNetNotional is not null &&
                AccountNetNotional.TryGetValue(accountId.ToString("D"), out var accountNet)
                    ? accountNet
                    : null;
        }

        // Unscoped order: the aggregate is exact only when a single account contributes.
        return AccountNetNotional is not { Count: > 1 } ? NetNotional : null;
    }
}

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

    /// <summary>
    /// Best-available reference price for <paramref name="symbol"/> even when the portfolio
    /// is flat in it — a live quote mid or last trade. Without this, a market order in a
    /// never-held symbol resolves to no measurable notional and every notional-based rule
    /// approves it unmeasured, while the gateway can still price and execute it.
    /// Implementations without a market-data feed return <see langword="null"/>.
    /// </summary>
    decimal? TryGetReferencePrice(string symbol) => null;
}
