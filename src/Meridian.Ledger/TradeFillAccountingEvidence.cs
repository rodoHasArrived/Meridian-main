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
    /// <summary>
    /// Bond accrued interest that changed hands on this fill, always nonnegative: paid on a
    /// purchase, received on a sale. Zero for fills without an accrual (and on every historical
    /// record, which omits it).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public decimal AccruedInterest { get; init; }

    /// <summary>
    /// On a sale, the purchased accrued interest receivable relieved for the face sold; the rest
    /// of <see cref="AccruedInterest"/> is coupon income earned while held (negative if the
    /// receivable exceeds what was received). Zero on purchases.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public decimal AccruedInterestRelieved { get; init; }

    [JsonIgnore]
    public decimal RealizedPnl => SettlesVariationMargin
        ? VariationMarginPnl
        : (Closing?.RealizedPnl ?? 0m) + (Opening?.RealizedPnl ?? 0m);
}
