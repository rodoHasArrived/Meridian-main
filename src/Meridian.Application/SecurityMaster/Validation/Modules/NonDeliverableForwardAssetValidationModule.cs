using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the NonDeliverableForward asset module.</summary>
internal static class NonDeliverableForwardAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("NonDeliverableForward", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "NonDeliverableForward",
            [
                DistinctStringRule.Required("baseCurrency", "quoteCurrency", "SM_NDF_PAIR_INVALID", "NDF currency pair is invalid", "NDF base and quote currencies must differ."),
                DateOrderRule.Optional("fixingDate", "valueDate", "SM_NDF_FIXING_DATE_INVALID", "NDF fixing date is after value date", "NDF fixing date must be on or before the value date."),
                FieldRule.OptionalPositive("notional", "SM_NDF_NOTIONAL_INVALID", "NDF notional is invalid", "NDF notional must be greater than zero when present."),
                FieldRule.OptionalPositive("forwardRate", "SM_NDF_RATE_INVALID", "NDF forward rate is invalid", "NDF forward rate must be greater than zero when present.")
            ]);
}
