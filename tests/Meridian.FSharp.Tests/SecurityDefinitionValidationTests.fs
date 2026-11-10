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

[<Fact>]
let ``positive CFD leverage passes both surfaces`` () =
    let kind = cfd (Some 5m)
    Assert.Empty(SecurityMaster.validateKindInvariants kind)
    Assert.Empty(SecurityMaster.validateDefinitionInvariants "Cfd" (SecurityMasterLegacyUpgrade.modulesFromKind kind))
