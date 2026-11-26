using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the Future asset module.</summary>
internal static class FutureAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("Future", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "Future",
            [
                FieldRule.OptionalPositive("multiplier", "SM_FUTURE_MULTIPLIER_INVALID", "Future multiplier is invalid", "Future multiplier must be greater than zero."),
                DateOrderRule.Optional("lastTradingDt", "expiry", "SM_FUTURE_LAST_TRADING_DATE_INVALID", "Future last-trading date is invalid", "Future last-trading date must be on or before expiry."),
                DateOrderRule.Optional("firstNoticeDt", "expiry", "SM_FUTURE_FIRST_NOTICE_DATE_INVALID", "Future first-notice date is invalid", "Future first-notice date must be on or before expiry.")
            ]);
}
