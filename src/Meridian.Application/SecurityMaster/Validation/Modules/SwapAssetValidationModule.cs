using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the Swap asset module.</summary>
internal static class SwapAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("Swap", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "Swap",
            [
                DateOrderRule.Optional("effectiveDate", "maturityDate", "SM_SWAP_DATE_RANGE_INVALID", "Swap date range is invalid", "Swap effective date must be on or before maturity date."),
                FieldRule.OptionalArrayNotEmpty("legs", "SM_SWAP_LEGS_REQUIRED", "Swap legs are missing", "Swaps must include at least one leg."),
                // Per-leg economic consistency (Warning while existing records are measured). Leg
                // currency and leg type are already required by the schema.
                TermArrayElementRule.Create(
                    "legs",
                    SecurityValidationSeverityDto.Warning,
                    "SM_SWAP_FLOATING_LEG_INDEX_MISSING",
                    "Floating swap leg has no index",
                    "A leg with legType Floating has no index, so its resets cannot be fixed.",
                    "Set the leg's reference index (for example SOFR or EURIBOR 3M).",
                    static leg => leg.HasString("legType", "Floating") && !leg.HasNonBlankString("index") ? "index" : null),
                TermArrayElementRule.Create(
                    "legs",
                    SecurityValidationSeverityDto.Warning,
                    "SM_SWAP_FIXED_LEG_RATE_MISSING",
                    "Fixed swap leg has no fixed rate",
                    "A leg with legType Fixed has no fixedRate.",
                    "Set the leg's fixed rate and its rate unit.",
                    static leg => leg.HasString("legType", "Fixed") && !leg.HasNumber("fixedRate") ? "fixedRate" : null),
                TermArrayElementRule.Create(
                    "legs",
                    SecurityValidationSeverityDto.Warning,
                    "SM_SWAP_LEG_DAY_COUNT_MISSING",
                    "Swap leg has no day count",
                    "A swap leg has no dayCount and the record declares none to fall back to, so its accrual cannot be computed from declared terms.",
                    "Set the leg's day count convention (for example ACT/360).",
                    static leg => leg.HasNonBlankString("dayCount") || leg.RecordDeclaresString(SecurityCashFlowTermRole.DayCount)
                        ? null
                        : "dayCount"),
                TermArrayElementRule.Create(
                    "legs",
                    SecurityValidationSeverityDto.Warning,
                    "SM_SWAP_LEG_PAYMENT_FREQUENCY_MISSING",
                    "Swap leg has no payment frequency",
                    "A swap leg has no paymentFrequency and the record declares none to fall back to, so its payment schedule cannot be generated from declared terms.",
                    "Set the leg's payment frequency (for example Quarterly).",
                    static leg => leg.HasNonBlankString("paymentFrequency") || leg.RecordDeclaresString(SecurityCashFlowTermRole.PaymentFrequency)
                        ? null
                        : "paymentFrequency"),
                TermArrayElementRule.Create(
                    "legs",
                    SecurityValidationSeverityDto.Warning,
                    "SM_SWAP_LEG_NOTIONAL_SCHEDULE_ORDER",
                    "Swap leg notional schedule is out of order",
                    "A leg's notionalSchedule effective dates must be strictly increasing.",
                    "Sort the leg's notional schedule by effective date and combine steps that share a date.",
                    static leg => leg.ScheduleDatesIncrease("notionalSchedule", "effectiveDate") ? null : "notionalSchedule")
            ]);
}
