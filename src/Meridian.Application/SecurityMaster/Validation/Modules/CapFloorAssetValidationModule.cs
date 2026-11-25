using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the CapFloor asset module.</summary>
internal static class CapFloorAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("CapFloor", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "CapFloor",
            [
                FieldRule.OptionalAllowedString("capFloorType", ["Cap", "Floor", "Collar"], "SM_CAP_FLOOR_TYPE_INVALID", "Cap/floor type is invalid", "Cap/floor type must be Cap, Floor or Collar."),
                FieldRule.OptionalPositive("notional", "SM_CAP_FLOOR_NOTIONAL_REQUIRED", "Cap/floor notional is missing or invalid", "Caps and floors must include a notional greater than zero."),
                DateOrderRule.Optional("effectiveDate", "maturityDate", "SM_CAP_FLOOR_DATE_RANGE_INVALID", "Cap/floor date range is invalid", "Cap/floor effective date must be on or before the maturity date."),
                new RequiredNumberWhenRule("capFloorType", ["Cap", "Collar"], "capStrike", "SM_CAP_FLOOR_CAP_STRIKE_REQUIRED", "Cap strike is missing", "A Cap or Collar must include its cap strike."),
                new RequiredNumberWhenRule("capFloorType", ["Floor", "Collar"], "floorStrike", "SM_CAP_FLOOR_FLOOR_STRIKE_REQUIRED", "Floor strike is missing", "A Floor or Collar must include its floor strike."),
                NumericOrderRule.Optional("floorStrike", "capStrike", "SM_CAP_FLOOR_STRIKE_ORDER_INVALID", "Collar floor strike is above the cap strike", "A collar's floor strike must be at or below its cap strike."),
                FieldRule.OptionalNonNegative("premiumAmount", "SM_CAP_FLOOR_PREMIUM_INVALID", "Cap/floor premium is invalid", "Cap/floor premium must be zero or greater when present.")
            ]);
}
