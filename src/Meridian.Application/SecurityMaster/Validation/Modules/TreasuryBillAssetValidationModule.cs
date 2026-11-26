using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the TreasuryBill asset module.</summary>
internal static class TreasuryBillAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("TreasuryBill", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "TreasuryBill",
            [
                DateOrderRule.Optional("auctionDate", "maturity", "SM_TBILL_AUCTION_DATE_INVALID", "Treasury bill auction date is invalid", "Auction date must be on or before maturity."),
                FieldRule.OptionalNonNegative("discountRate", "SM_TBILL_DISCOUNT_RATE_INVALID", "Treasury bill discount rate is invalid", "Treasury bill discount rates must be zero or greater.")
            ]);
}
