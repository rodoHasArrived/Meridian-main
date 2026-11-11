/// ADR-027 stage 2 (S2-B1): the two-surface proof for the factor-schedule codes whose invalid
/// shapes the C# codec gate (SecurityEconomicScheduleValidation) refuses before either validator
/// runs on the JSON route. Here the records mutate directly, so both the kind surface
/// (validateKindInvariants) and the definition surface (validateDefinitionInvariants over
/// modulesFromKind) are exercised on the same invalid data and must report the same code —
/// the defense-in-depth half of the equivalence that
/// SecurityDefinitionValidationEquivalenceTests (Meridian.Tests) pins for every other ledgered
/// code through the codec.
module Meridian.FSharp.Tests.SecurityDefinitionValidationTests

open System
open Xunit
open Meridian.FSharp.Domain

let private structuredCredit entries : SecurityKind =
    SecurityKind.StructuredCredit {
        Tranche = "A1"
        PoolId = None
        CollateralType = "CLO"
        OriginalFace = 1000000m
        CurrentFactor = Some 0.9m
        CouponOrIndex = "SOFR+250"
        FactorSchedule = None
        FactorScheduleEntries = entries
        Maturity = Some (DateOnly(2031, 6, 15))
        Kind = None
        Seniority = None
        AttachmentPoint = None
        DetachmentPoint = None
        WeightedAverageCoupon = None
        WeightedAverageMaturityMonths = None
        WeightedAverageLoanAgeMonths = None
        ReinvestmentPeriodEnd = None
        NonCallPeriodEnd = None
        ScheduleConventions = ScheduleConventions.empty
        CallPutSchedules = CallPutSchedules.empty
    }

let private codesOnBothSurfaces entries =
    let kind = structuredCredit entries
    let kindCodes = SecurityMaster.validateKindInvariants kind |> List.map (fun e -> e.Code)
    let definitionCodes =
        SecurityMaster.validateDefinitionInvariants "StructuredCredit" (SecurityMasterLegacyUpgrade.modulesFromKind kind)
        |> List.map (fun e -> e.Code)
    kindCodes, definitionCodes

let private entry (date: DateOnly) factor : FactorScheduleEntry = { AsOfDate = date; Factor = factor }

let private cfd leverage : SecurityKind =
    SecurityKind.Cfd {
        UnderlyingAssetClass = "Equity"
        UnderlyingDescription = None
        Leverage = leverage
    }

[<Fact>]
let ``a clean factor schedule passes both surfaces`` () =
    let kindCodes, definitionCodes =
        codesOnBothSurfaces [ entry (DateOnly(2026, 1, 1)) 0.95m; entry (DateOnly(2026, 2, 1)) 0.9m ]
    Assert.Empty kindCodes
    Assert.Empty definitionCodes

[<Theory>]
[<InlineData("structured_credit_factor_schedule_invalid")>]
[<InlineData("structured_credit_factor_schedule_duplicate_date")>]
[<InlineData("structured_credit_factor_schedule_not_monotonic")>]
let ``an invalid factor schedule trips both surfaces with the same code`` (code: string) =
    let entries =
        match code with
        | "structured_credit_factor_schedule_invalid" -> [ entry (DateOnly(2026, 1, 1)) 1.5m ]
        | "structured_credit_factor_schedule_duplicate_date" ->
            [ entry (DateOnly(2026, 1, 1)) 0.9m; entry (DateOnly(2026, 1, 1)) 0.9m ]
        | _ -> [ entry (DateOnly(2026, 1, 1)) 0.8m; entry (DateOnly(2026, 2, 1)) 0.9m ]

    let kindCodes, definitionCodes = codesOnBothSurfaces entries
    Assert.Contains(code, kindCodes)
    Assert.Contains(code, definitionCodes)
    // The definition surface may never invent an error the kind surface does not raise.
    for definitionCode in definitionCodes do
        Assert.Contains(definitionCode, kindCodes)

/// Zero-leverage regression (Codex review on #3439): validateKindInvariants projects modules
/// before validating, and the CFD margin projection divides by leverage. Zero must come back as
/// the cfd_leverage_invalid refusal on both surfaces, never a DivideByZeroException.
[<Fact>]
let ``zero CFD leverage is refused on both surfaces instead of throwing`` () =
    let kind = cfd (Some 0m)
    let kindCodes = SecurityMaster.validateKindInvariants kind |> List.map (fun e -> e.Code)
    let definitionCodes =
        SecurityMaster.validateDefinitionInvariants "Cfd" (SecurityMasterLegacyUpgrade.modulesFromKind kind)
        |> List.map (fun e -> e.Code)
    Assert.Contains("cfd_leverage_invalid", kindCodes)
    Assert.Contains("cfd_leverage_invalid", definitionCodes)

let private customAsset profileId version termsJson : SecurityKind =
    SecurityKind.CustomAsset {
        CustomProfileId = profileId
        ProfileVersion = version
        TermsJson = termsJson
    }

let private validEnvelope =
    """{"schemaVersion":3,"customProfileId":"co-invest-spv","profileVersion":2,"profileFields":{"vehicle":"SPV I"}}"""

let private customAssetCodes kind =
    let kindCodes = SecurityMaster.validateKindInvariants kind |> List.map (fun e -> e.Code)
    let definitionCodes =
        SecurityMaster.validateDefinitionInvariants "CustomAsset" (SecurityMasterLegacyUpgrade.modulesFromKind kind)
        |> List.map (fun e -> e.Code)
    kindCodes, definitionCodes

/// ADR-027 stage 3 (S3-E4): the envelope codes the codec gate keeps off the JSON route — a blank
/// profile id reads as the OtherSecurity salvage, and a non-object or unparseable document never
/// decodes to a CustomAsset kind — proven on both surfaces by mutating the record directly.
[<Fact>]
let ``a valid custom-asset envelope passes both surfaces`` () =
    let kindCodes, definitionCodes = customAssetCodes (customAsset "co-invest-spv" 2 validEnvelope)
    Assert.Empty kindCodes
    Assert.Empty definitionCodes

[<Theory>]
[<InlineData("custom_asset_profile_required")>]
[<InlineData("custom_asset_terms_required")>]
[<InlineData("custom_asset_terms_invalid")>]
let ``an invalid custom-asset envelope trips both surfaces with the same code`` (code: string) =
    let kind =
        match code with
        | "custom_asset_profile_required" -> customAsset " " 2 validEnvelope
        | "custom_asset_terms_required" -> customAsset "co-invest-spv" 2 " "
        | _ -> customAsset "co-invest-spv" 2 "[]"

    let kindCodes, definitionCodes = customAssetCodes kind
    Assert.Contains(code, kindCodes)
    Assert.Contains(code, definitionCodes)
    for definitionCode in definitionCodes do
        Assert.Contains(definitionCode, kindCodes)

[<Fact>]
let ``unparseable custom-asset terms are refused, not thrown`` () =
    let kindCodes, definitionCodes = customAssetCodes (customAsset "co-invest-spv" 2 "{not json")
    Assert.Contains("custom_asset_terms_invalid", kindCodes)
    Assert.Contains("custom_asset_terms_invalid", definitionCodes)

[<Fact>]
let ``a custom-asset definition without the envelope module is refused as an empty envelope`` () =
    let codes =
        SecurityMaster.validateDefinitionInvariants "CustomAsset" SecurityTermModules.empty
        |> List.map (fun e -> e.Code)
    Assert.Contains("custom_asset_profile_required", codes)
    Assert.Contains("custom_asset_profile_version_invalid", codes)
    Assert.Contains("custom_asset_terms_required", codes)

// ── ADR-027 stage 3 (S3-E contraction): required values the records no longer guarantee ─────────
// The per-class records used to hold these as non-optional fields; they now reference the term
// module that defines them, whose members are all optional. The flat codec still refuses a
// payload that omits them, so only a record built directly can lack one — and the definition
// validator must refuse it on both surfaces.

let private noExercise : ExerciseTerms =
    { OptionRight = None; StrikePrice = None; StrikeRate = None; ExerciseStyle = None
      UnderlyingSecurityId = None; PremiumAmount = None }

let private noAmounts : NotionalTerms =
    { Notional = None; Denomination = None; CommittedAmount = None; FundedAmount = None; UnfundedAmount = None }

let private someUnderlying = Some (SecurityId (Guid.Parse "9e9e9e9e-0000-0000-0000-000000000005"))

let private optionWith (exercise: ExerciseTerms) : SecurityKind =
    SecurityKind.Option {
        Exercise = exercise
        Expiry = DateOnly(2030, 1, 18)
        Multiplier = 100m
        OptChainId = None
        SettlementType = None
        IsAdjusted = false
        LastTradingDt = None
    }

let private warrantWith (exercise: ExerciseTerms) : SecurityKind =
    SecurityKind.Warrant { Exercise = exercise; Expiry = None; Multiplier = None }

let private swaptionWith (exercise: ExerciseTerms) (notional: NotionalTerms) : SecurityKind =
    SecurityKind.Swaption {
        Exercise = exercise
        Expiry = DateOnly(2027, 3, 15)
        UnderlyingEffectiveDate = None
        UnderlyingMaturityDate = DateOnly(2037, 3, 17)
        Notional = notional
        FloatingIndex = None
        FixedLegPaymentFrequency = None
        FixedLegDayCount = None
        SettlementType = None
    }

let private validOption = { noExercise with OptionRight = Some "Call"; StrikePrice = Some 100m; UnderlyingSecurityId = someUnderlying }
let private validWarrant = { noExercise with OptionRight = Some "Call"; UnderlyingSecurityId = someUnderlying }
let private validSwaption = { noExercise with OptionRight = Some "Payer"; StrikeRate = Some 0.0375m }
let private swaptionNotional = { noAmounts with Notional = Some 25_000_000m }

let private bothSurfaces (typeName: string) (kind: SecurityKind) =
    let kindCodes = SecurityMaster.validateKindInvariants kind |> List.map (fun e -> e.Code)
    let definitionCodes =
        SecurityMaster.validateDefinitionInvariants typeName (SecurityMasterLegacyUpgrade.modulesFromKind kind)
        |> List.map (fun e -> e.Code)
    kindCodes, definitionCodes

[<Fact>]
let ``complete option, warrant and swaption records pass both surfaces`` () =
    for typeName, kind in
        [ "Option", optionWith validOption
          "Warrant", warrantWith validWarrant
          "Swaption", swaptionWith validSwaption swaptionNotional ] do
        let kindCodes, definitionCodes = bothSurfaces typeName kind
        Assert.Empty kindCodes
        Assert.Empty definitionCodes

[<Theory>]
[<InlineData("option_underlying_required")>]
[<InlineData("option_strike_invalid")>]
[<InlineData("option_put_call_required")>]
[<InlineData("warrant_underlying_required")>]
[<InlineData("warrant_type_required")>]
[<InlineData("swaption_strike_rate_required")>]
[<InlineData("swaption_notional_invalid")>]
[<InlineData("swaption_payer_receiver_invalid")>]
let ``a record missing a value the flat codec requires is refused on both surfaces`` (code: string) =
    let typeName, kind =
        match code with
        | "option_underlying_required" -> "Option", optionWith { validOption with UnderlyingSecurityId = None }
        | "option_strike_invalid" -> "Option", optionWith { validOption with StrikePrice = None }
        | "option_put_call_required" -> "Option", optionWith { validOption with OptionRight = None }
        | "warrant_underlying_required" -> "Warrant", warrantWith { validWarrant with UnderlyingSecurityId = None }
        | "warrant_type_required" -> "Warrant", warrantWith { validWarrant with OptionRight = None }
        | "swaption_strike_rate_required" -> "Swaption", swaptionWith { validSwaption with StrikeRate = None } swaptionNotional
        | "swaption_notional_invalid" -> "Swaption", swaptionWith validSwaption noAmounts
        | _ -> "Swaption", swaptionWith { validSwaption with OptionRight = None } swaptionNotional

    let kindCodes, definitionCodes = bothSurfaces typeName kind
    Assert.Contains(code, kindCodes)
    Assert.Contains(code, definitionCodes)
    for definitionCode in definitionCodes do
        Assert.Contains(definitionCode, kindCodes)

[<Fact>]
let ``positive CFD leverage passes both surfaces`` () =
    let kind = cfd (Some 5m)
    Assert.Empty(SecurityMaster.validateKindInvariants kind)
    Assert.Empty(SecurityMaster.validateDefinitionInvariants "Cfd" (SecurityMasterLegacyUpgrade.modulesFromKind kind))

// ADR-027 stage 3 (S3-E7): the bond's principal and step schedules now have module homes
// (sinkingFund, couponSchedule). The flat codec's schedule validation refuses these three shapes
// before a kind exists, so the definition-surface copies are pinned on directly built records.
let private bondWith coupon principalSchedule : SecurityKind =
    SecurityKind.Bond {
        BondTerms.fixedRate (DateOnly(2035, 6, 15)) 4.5m None (Some "Meridian Capital Corp") with
            Coupon = coupon
            Par = Some 1000m
            PrincipalSchedule = principalSchedule
    }

let private fixedCoupon = BondCouponStructure.Fixed(4.5m, None)

[<Fact>]
let ``a bond with clean principal and step schedules passes both surfaces`` () =
    let amortizing = bondWith fixedCoupon [ { PaymentDate = DateOnly(2030, 6, 15); Amount = 400m } ]
    let stepped =
        bondWith
            (BondCouponStructure.Step(
                [ { EffectiveDate = DateOnly(2027, 6, 15); Rate = 4m }
                  { EffectiveDate = DateOnly(2029, 6, 15); Rate = 5m } ],
                None))
            []

    for kind in [ amortizing; stepped ] do
        let kindCodes, definitionCodes = bothSurfaces "Bond" kind
        Assert.Empty kindCodes
        Assert.Empty definitionCodes

[<Theory>]
[<InlineData("bond_principal_schedule_invalid")>]
[<InlineData("bond_step_rate_invalid")>]
[<InlineData("bond_step_dates_duplicate")>]
let ``a bond schedule shape the flat codec refuses is refused on both surfaces`` (code: string) =
    let kind =
        match code with
        | "bond_principal_schedule_invalid" ->
            bondWith fixedCoupon [ { PaymentDate = DateOnly(2030, 6, 15); Amount = 0m } ]
        | "bond_step_rate_invalid" ->
            bondWith (BondCouponStructure.Step([ { EffectiveDate = DateOnly(2027, 6, 15); Rate = -1m } ], None)) []
        | _ ->
            bondWith
                (BondCouponStructure.Step(
                    [ { EffectiveDate = DateOnly(2027, 6, 15); Rate = 4m }
                      { EffectiveDate = DateOnly(2027, 6, 15); Rate = 5m } ],
                    None))
                []

    let kindCodes, definitionCodes = bothSurfaces "Bond" kind
    Assert.Contains(code, kindCodes)
    Assert.Contains(code, definitionCodes)
    for definitionCode in definitionCodes do
        Assert.Contains(definitionCode, kindCodes)
