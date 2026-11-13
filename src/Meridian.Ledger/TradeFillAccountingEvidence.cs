using System.Text.Json.Serialization;

namespace Meridian.Ledger;

/// <summary>
/// Immutable per-fill accounting facts captured under the portfolio's lot-book lock.
/// A fill crossing zero retains both its closing and opening leg. No monetary basis is
/// reconstructed from an average quote, order side, or realized result.
/// </summary>
public sealed record TradeFillAccountingEvidence(
    SecurityTradePosting? Closing = null,
    SecurityTradePosting? Opening = null,
    decimal Fees = 0m,
    decimal SlippageCost = 0m,
    decimal ShortCollateralHeld = 0m,
    decimal ShortCollateralReleased = 0m,
    bool SettlesVariationMargin = false,
    decimal VariationMarginPnl = 0m,
    string? FinancialAccountId = null)
{
    [JsonIgnore]
    public decimal RealizedPnl => SettlesVariationMargin
        ? VariationMarginPnl
        : (Closing?.RealizedPnl ?? 0m) + (Opening?.RealizedPnl ?? 0m);
}
