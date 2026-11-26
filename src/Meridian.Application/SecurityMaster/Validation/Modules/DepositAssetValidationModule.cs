using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the Deposit asset module.</summary>
internal static class DepositAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("Deposit", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "Deposit",
            [
                FieldRule.OptionalNonNegative("interestRate", "SM_DEPOSIT_INTEREST_RATE_INVALID", "Deposit interest rate is invalid", "Deposit interest rates must be zero or greater.")
            ]);
}
