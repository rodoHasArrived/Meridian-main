using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the Swaption asset module.</summary>
internal static class SwaptionAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("Swaption", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "Swaption",
            [
                FieldRule.OptionalAllowedString("payerReceiver", ["Payer", "Receiver"], "SM_SWAPTION_PAYER_RECEIVER_INVALID", "Swaption payer/receiver is invalid", "Swaptions must be 'Payer' or 'Receiver'."),
                FieldRule.OptionalAllowedString("exerciseStyle", ["European", "American", "Bermudan"], "SM_SWAPTION_EXERCISE_STYLE_INVALID", "Swaption exercise style is invalid", "Swaption exercise style must be European, American or Bermudan when present."),
                DateOrderRule.Optional("expiry", "underlyingMaturityDate", "SM_SWAPTION_EXPIRY_AFTER_MATURITY", "Swaption expiry is after the underlying maturity", "Swaption expiry must be on or before the underlying swap's maturity date."),
                DateOrderRule.OptionalStrict("underlyingEffectiveDate", "underlyingMaturityDate", "SM_SWAPTION_UNDERLYING_DATES_INVALID", "Swaption underlying dates are invalid", "The underlying swap's effective date must fall before its maturity date."),
                DateOrderRule.Optional("expiry", "underlyingEffectiveDate", "SM_SWAPTION_EFFECTIVE_BEFORE_EXPIRY", "Swaption underlying starts before expiry", "The underlying swap's effective date must be on or after the swaption expiry."),
                FieldRule.OptionalPositive("notional", "SM_SWAPTION_NOTIONAL_REQUIRED", "Swaption notional is missing or invalid", "Swaptions must include a notional greater than zero.")
            ]);
}
