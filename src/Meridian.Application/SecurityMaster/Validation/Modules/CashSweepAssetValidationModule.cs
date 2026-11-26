using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the CashSweep asset module.</summary>
internal static class CashSweepAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("CashSweep", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "CashSweep",
            [
                FieldRule.OptionalNonNegative("yieldRate", "SM_CASH_SWEEP_YIELD_RATE_INVALID", "Cash sweep yield is invalid", "Cash sweep yield rates must be zero or greater.")
            ]);
}
