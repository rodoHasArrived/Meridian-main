using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the FxForward asset module.</summary>
internal static class FxForwardAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("FxForward", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "FxForward",
            [
                DistinctStringRule.Required("baseCurrency", "quoteCurrency", "SM_FX_FORWARD_PAIR_INVALID", "FX forward currency pair is invalid", "FX forward base and quote currencies must differ."),
                DateOrderRule.Optional("tradeDate", "valueDate", "SM_FX_FORWARD_TRADE_DATE_INVALID", "FX forward trade date is after value date", "FX forward trade date must be on or before the value date."),
                FieldRule.OptionalPositive("notional", "SM_FX_FORWARD_NOTIONAL_INVALID", "FX forward notional is invalid", "FX forward notional must be greater than zero when present."),
                FieldRule.OptionalPositive("forwardRate", "SM_FX_FORWARD_RATE_INVALID", "FX forward rate is invalid", "FX forward rate must be greater than zero when present.")
            ]);
}
