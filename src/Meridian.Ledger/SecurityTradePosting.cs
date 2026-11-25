using System.Text.Json.Serialization;

namespace Meridian.Ledger;

/// <summary>
/// Exact monetary facts retained by the lot book for one opening or closing leg.
/// Carrying value is relieved long basis or the original proceeds of a covered short.
/// Opening legs carry zero; financing remains separate from securities basis.
/// </summary>
public sealed record SecurityTradePosting(
    SecurityTradePostingKind Kind,
    decimal Quantity,
    decimal GrossValue,
    decimal? CarryingValue = null,
    decimal BorrowedAmount = 0m,
    decimal LoanRepaid = 0m)
{
    [JsonIgnore]
    public decimal RealizedPnl => Kind switch
    {
        SecurityTradePostingKind.CloseLong => GrossValue - RequiredCarryingValue,
        SecurityTradePostingKind.CoverShort => RequiredCarryingValue - GrossValue,
        SecurityTradePostingKind.OpenLong or SecurityTradePostingKind.OpenShort => 0m,
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unknown security trade posting kind.")
    };

    private decimal RequiredCarryingValue => CarryingValue
        ?? throw new InvalidOperationException("A closing trade requires explicit retained carrying value.");
}
