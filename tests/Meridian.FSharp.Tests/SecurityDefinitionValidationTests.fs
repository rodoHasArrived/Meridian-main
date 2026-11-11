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
        Pool = { Factor = Some 0.9m; FactorDate = None; WeightedAvgCoupon = None; WeightedAvgMaturityMonths = None; WeightedAvgLoanAgeMos = None; CollateralType = Some ("CLO"); PoolIdentifier = None; TrancheClass = Some ("A1"); PrepaymentAssumption = None; AverageLifeYears = None; NotionalBalance = Some (1000000m); Originator = None; CreditEnhancementPct = None; FactorScheduleReference = None; IsInterestOnly = false; IsPrincipalOnly = false; FactorScheduleEntries = entries }
        CouponTerms = { CouponType = Some (CouponKind.OtherCoupon ("SOFR+250")); CouponRate = None; PaymentFrequency = None; DayCount = None }; TrancheContract = { Kind = None; Seniority = None; AttachmentPoint = None; DetachmentPoint = None; ReinvestmentPeriodEnd = None; NonCallPeriodEnd = None }
        Dates = { EffectiveDate = None; IssueDate = None; MaturityDate = Some (DateOnly(2031, 6, 15)) }
        DeclaredSchedule = { DeclaredScheduleTerms.empty with Conventions = ScheduleConventions.empty }
        RateConvention = None
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
        UnderlyingExposure = {
            UnderlyingAssetClass = "Equity"
            UnderlyingDescription = None
            Leverage = leverage
        }
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

[<Fact>]
let ``positive CFD leverage passes both surfaces`` () =
    let kind = cfd (Some 5m)
    Assert.Empty(SecurityMaster.validateKindInvariants kind)
    Assert.Empty(SecurityMaster.validateDefinitionInvariants "Cfd" (SecurityMasterLegacyUpgrade.modulesFromKind kind))

// ── The rules that were codec-only until the stage-3 contraction ─────────────────────────────
// Every per-class field now sits on a shared module, so these rules are expressed on the
// definition surface. Six of them cannot be reached through the flat codec — it refuses the shape
// first, or reads a blank as absent — so they are pinned here on directly built records.

let private bothSurfaces (typeName: string) (kind: SecurityKind) =
    let kindCodes = SecurityMaster.validateKindInvariants kind |> List.map (fun e -> e.Code)
    let definitionCodes =
        SecurityMaster.validateDefinitionInvariants typeName (SecurityMasterLegacyUpgrade.modulesFromKind kind)
        |> List.map (fun e -> e.Code)
    kindCodes, definitionCodes

let private bondWith coupon principal : SecurityKind =
    let bond = BondTerms.fixedRate (DateOnly(2035, 6, 15)) 4.5m None (Some "Meridian Capital Corp")
    SecurityKind.Bond {
        bond with
            CouponStructure = { bond.CouponStructure with Structure = coupon }
            RedemptionFacts = { bond.RedemptionFacts with Par = Some 1000m }
            Principal = { Payments = principal }
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

let private swapLeg : SwapLeg =
    { LegId = Some "fixed"
      LegType = "Fixed"
      Currency = "USD"
      Direction = Some "Pay"
      Index = None
      FixedRate = Some 3.75m
      SpreadBps = None
      CurrentIndexRate = None
      Notional = Some 10000000m
      PaymentFrequency = Some "SemiAnnual"
      DayCount = None
      ExchangesPrincipal = false
      NotionalSchedule = []
      ResetSchedule = []
      ScheduleConventions = ScheduleConventions.empty }

let private swapWith legs : SecurityKind =
    SecurityKind.Swap {
        Dates =
            { EffectiveDate = Some (DateOnly(2026, 1, 15))
              IssueDate = Some (DateOnly(2026, 1, 15))
              MaturityDate = Some (DateOnly(2031, 1, 15)) }
        SwapLegs = { Legs = legs }
        RateConvention = None
    }

[<Fact>]
let ``a swap with a clean leg passes both surfaces`` () =
    let kindCodes, definitionCodes = bothSurfaces "Swap" (swapWith [ swapLeg ])
    Assert.Empty kindCodes
    Assert.Empty definitionCodes

[<Fact>]
let ``a swap with no legs is refused on both surfaces`` () =
    let kindCodes, definitionCodes = bothSurfaces "Swap" (swapWith [])
    Assert.Contains("swap_legs_required", kindCodes)
    Assert.Contains("swap_legs_required", definitionCodes)

[<Theory>]
[<InlineData("swap_leg_type_required")>]
[<InlineData("swap_leg_currency_required")>]
[<InlineData("swap_leg_payment_frequency_invalid")>]
let ``a swap leg shape the flat codec never yields is refused on both surfaces`` (code: string) =
    let leg =
        match code with
        | "swap_leg_type_required" -> { swapLeg with LegType = " " }
        | "swap_leg_currency_required" -> { swapLeg with Currency = " " }
        | _ -> { swapLeg with PaymentFrequency = Some " " }

    let kindCodes, definitionCodes = bothSurfaces "Swap" (swapWith [ leg ])
    Assert.Contains(code, kindCodes)
    Assert.Contains(code, definitionCodes)
    for definitionCode in definitionCodes do
        Assert.Contains(definitionCode, kindCodes)

[<Fact>]
let ``a definition that lacks a module fails its required rules and passes its optional ones`` () =
    // On the definition surface a module can be absent altogether. A rule over a value the class
    // requires then fails; a rule over an optional value has nothing to check.
    let codes typeName =
        SecurityMaster.validateDefinitionInvariants typeName SecurityTermModules.empty |> List.map (fun e -> e.Code)
    Assert.Contains("swap_legs_required", codes "Swap")
    Assert.Contains("other_security_category_required", codes "OtherSecurity")
    Assert.Contains("commodity_type_required", codes "Commodity")
    Assert.Contains("cds_reference_entity_required", codes "CreditDefaultSwap")
    Assert.Contains("cds_protection_side_invalid", codes "CreditDefaultSwap")
    Assert.Contains("real_estate_market_required", codes "RealEstateHolding")
    Assert.DoesNotContain("bond_principal_schedule_invalid", codes "Bond")
    Assert.DoesNotContain("structured_credit_attachment_point_invalid", codes "StructuredCredit")
    Assert.DoesNotContain("covenant_type_required", codes "DirectLoan")
