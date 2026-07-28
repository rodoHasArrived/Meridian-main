using Meridian.Execution.Sdk;

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
            // An exact match is the order's own book.
            if (AccountNetNotional is not null &&
                AccountNetNotional.TryGetValue(accountId.ToString("D"), out var accountNet))
            {
                return accountNet;
            }

            // No match. FundAccountId is an accounting scope, while exposure is keyed by
            // execution-account ids ("default" for the paper portfolio), so a miss can mean
            // either "different fund" or "same book under a different naming system". When
            // exactly one account contributes and it is not itself fund-keyed, the mapping
            // is unambiguous — that account is the whole book — and a fund-scoped close
            // must be seen as reducing it rather than adding on top.
            if (AccountNetNotional is { Count: 1 })
            {
                var (onlyAccount, onlyNet) = AccountNetNotional.First();
                if (!Guid.TryParse(onlyAccount, out _))
                {
                    return onlyNet;
                }
            }

            // Otherwise the order belongs to a book this snapshot cannot identify: fall
            // back to the additive worst case rather than assuming it reduces someone else's
            // position — a $20k sell from a flat fund against another fund's $100k long
            // projects $120k, not $80k.
            return null;
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

    /// <summary>
    /// Reference price for valuing an order about to route on <paramref name="side"/>. A
    /// midpoint is the right mark for a position already held, but it is not what an
    /// order pays: with a bid of $1 and an ask of $100, a market buy valued at the $50.50
    /// mid measures roughly half of what it will actually route. Implementations that can
    /// see the touch return the executable side of the book; the default falls back to the
    /// symbol mark.
    /// </summary>
    decimal? TryGetExecutablePrice(string symbol, OrderSide side) => TryGetReferencePrice(symbol);
}
