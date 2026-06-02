module Meridian.FSharp.Tests.DeterministicInvariantTests

open System
open global.FsCheck
open global.Xunit
open FsUnit.Xunit
open Meridian.FSharp.Ledger
open Meridian.FSharp.Domain
open Meridian.FSharp.Operations

let private config = Config.QuickThrowOnFailure.WithMaxTest(128)

let private date = DateOnly(2026, 1, 1)
let private asOf = DateTimeOffset.Parse("2026-01-01T00:00:00Z")

let private mkCandidate expected actual =
    {
        CandidateId = Guid.NewGuid()
        SecurityId = Guid.NewGuid()
        ExpectedAmount = expected
        ActualAmount = actual
        ExpectedCurrency = "USD"
        ActualCurrency = "USD"
        ExpectedDate = asOf
        ActualDate = asOf
        Notes = ""
    }

let private mkProjectedFlow amount ccy dueDate =
    {
        FlowId = Guid.NewGuid()
        SecurityId = SecurityId(Guid.NewGuid())
        EventKind = CouponPayment
        ExpectedAmount = amount
        ExpectedCurrency = ccy
        DueDate = dueDate
        RecordDate = None
        PayableDate = None
        SourceSystem = "test"
        Notes = None
        IsPrincipalFlow = false
        IsIncomeFlow = true
    }

[<Fact>]
let ``Deterministic accounting calendar validates contiguous monthly periods`` () =
    let periods = PeriodManagement.generateCalendarMonthPeriods 2026 asOf
    PeriodCalendarValidation.validate periods |> should be Empty

[<Fact>]
let ``Deterministic accounting posting fails when date matches multiple periods`` () =
    let first =
        {
            PeriodId = Guid.NewGuid()
            FiscalYear = 2026
            PeriodNo = 1
            Label = "2026-P01"
            StartDate = DateOnly(2026, 1, 1)
            EndDate = DateOnly(2026, 1, 31)
            Status = Open
            OpenedAt = asOf
            ClosedAt = None
        }
    let second =
        {
            PeriodId = Guid.NewGuid()
            FiscalYear = 2026
            PeriodNo = 2
            Label = "2026-P02"
            StartDate = DateOnly(2026, 1, 15)
            EndDate = DateOnly(2026, 2, 14)
            Status = Open
            OpenedAt = asOf
            ClosedAt = None
        }
    let result = PeriodManagement.checkPostingDate (DateOnly(2026, 1, 20)) false [ first; second ]
    result |> should equal (Denied "Posting date is ambiguous; multiple ledger periods match this date.")

[<Fact>]
let ``Deterministic accounting close guard blocks hard close before prior period hard-close`` () =
    let first =
        {
            PeriodId = Guid.NewGuid()
            FiscalYear = 2026
            PeriodNo = 1
            Label = "2026-P01"
            StartDate = DateOnly(2026, 1, 1)
            EndDate = DateOnly(2026, 1, 31)
            Status = Open
            OpenedAt = asOf
            ClosedAt = None
        }
    let second =
        {
            PeriodId = Guid.NewGuid()
            FiscalYear = 2026
            PeriodNo = 2
            Label = "2026-P02"
            StartDate = DateOnly(2026, 2, 1)
            EndDate = DateOnly(2026, 2, 28)
            Status = Open
            OpenedAt = asOf
            ClosedAt = None
        }
    PeriodManagement.validateCloseTransition second HardClosed [ first; second ]
    |> should equal (Error "Cannot hard-close 2026-P02 before prior period 2026-P01 is hard-closed.")

[<Fact>]
let ``Deterministic reconciliation materiality thresholds are consistent for candidate profiles`` () =
    let p = MatchingThresholdProfile.conservative
    let baseCase = mkCandidate 1000m 1020m
    let highVariance = mkCandidate 1000m 1500m
    ReconciliationRules.isMaterialAmountMatch baseCase.ExpectedAmount baseCase.ActualAmount p |> should equal true
    ReconciliationRules.isMaterialAmountMatch highVariance.ExpectedAmount highVariance.ActualAmount p |> should equal true

[<Fact>]
let ``ReconciliationRules classifyBreaks keeps non-matches deterministic`` () =
    let rule = MatchingRule.strict
    let candidate = mkCandidate 1000m 1005m
    let breaks = ReconciliationRules.classifyBreaks (Guid.NewGuid()) rule [ candidate ]
    breaks.Length |> should equal 1
    breaks[0].Classification |> should equal "AmountBreak"

[<Fact>]
let ``ReconciliationRules materiality-adjusted classification emits materiality marker`` () =
    let profile = MatchingThresholdProfile.conservative
    let candidate = mkCandidate 10000m 12000m
    let breaks = ReconciliationRules.materialityAdjustedClassifyBreaks profile (Guid.NewGuid()) MatchingRule.strict [ candidate ]
    breaks.Length |> should equal 1
    breaks[0] |> snd |> should equal true

[<Fact>]
let ``Provider degradation scoring is deterministic under unhealthy and healthy health snapshots`` () =
    let config = ProviderScoringConfig.defaultConfig
    let degraded =
        ProviderDegradationScoring.score
            "provider-a"
            { IsConnected = false; MissedHeartbeats = 12; ReconnectCount = 12; UptimeHours = 1.0 }
            { P95LatencyMs = 2500.0 }
            { ErrorRate = 0.10 }
            config

    let healthy =
        ProviderDegradationScoring.score
            "provider-a"
            { IsConnected = true; MissedHeartbeats = 0; ReconnectCount = 0; UptimeHours = 24.0 }
            { P95LatencyMs = 120.0 }
            { ErrorRate = 0.0 }
            config

    degraded.IsDegraded |> should equal true
    healthy.IsDegraded |> should equal false

[<Fact>]
let ``Security term validation blocks incomplete private-asset modules`` () =
    let definition =
        {
            SecurityId = SecurityId(Guid.NewGuid())
            Classification = {
                AssetClass = AssetClass.PrivateCredit
                Family = Some AssetFamily.PrivateLoan
                SubType = SecuritySubType.DirectLoan
                TypeName = "Direct private credit note"
                IssuerType = None
                RiskCountry = None
                Taxonomy = None
            }
            Common = {
                DisplayName = "Private Book"
                Currency = "USD"
                CountryOfRisk = None
                IssuerName = None
                Exchange = None
                LotSize = None
                TickSize = None
                PrimaryListingMic = None
                CountryOfIncorporation = None
                SettlementCycleDays = None
                HolidayCalendarId = None
            }
            Terms = SecurityTermModules.empty
            Identifiers = []
            Status = SecurityStatus.Active
            Version = 1L
            EffectiveFrom = asOf
            EffectiveTo = None
            Provenance = { SourceSystem = "unit-test"; SourceRecordId = Some "src"; AsOf = asOf; UpdatedBy = "builder"; Reason = Some "unit" }
        }

    let result = SecurityTermValidation.validate definition
    result.IsValid |> should equal false
    result.Errors.Length |> should equal 4

[<global.FsCheck.Xunit.Property>]
let ``Cash-flow settled invariant under exact matches`` (amount: int, days: int) =
    let expected = decimal amount + 1m
    let due = asOf.AddDays(float (abs days % 30))
    let projected = mkProjectedFlow expected "USD" due
    let actual =
        {
            FlowId = projected.FlowId
            SecurityId = projected.SecurityId
            Amount = expected
            Currency = "USD"
            PostedAt = due
            SourceSystem = "test"
            Notes = None
        }

    let securityId = let (SecurityId value) = projected.SecurityId in value
    let states = CashFlowRules.classifyStates MatchToleranceDefaults.strict [ projected ] (Map.ofList [ (securityId, projected.FlowId), actual ])
    states |> List.exactlyOne |> function
        | Settled (_, _) -> true
        | _ -> false

[<global.FsCheck.Xunit.Property>]
let ``Approval workflow rejects illegal transitions deterministically`` (seed: int) =
    let states = [| "Open"; "Submitted"; "UnderReview"; "Approved"; "Rejected"; "Executed" |]
    let actions = [| "Submit"; "Review"; "Approve"; "Reject"; "Execute"; "Reopen"; "Archive"; "Cancel" |]
    let state = states[(abs seed) % states.Length]
    let action = actions[(abs (seed / states.Length)) % actions.Length]

    let result =
        ApprovalWorkflowRules.evaluate
            {
                LifecycleState = state
                Action = action
                Actor = "alice"
                EvidenceCount = 2
                ApprovedBy = "bob"
                Reason = "test approval"
            }

    let isLegal =
        (state = "Open" && action = "Submit")
        || (state = "Open" && action = "Cancel")
        || (state = "Submitted" && action = "Review")
        || (state = "UnderReview" && (action = "Reject" || action = "Approve"))
        || (state = "Approved" && action = "Execute")
        || (state = "Rejected" && action = "Reopen")
        || (state = "Executed" && action = "Archive")

    if isLegal then
        result.IsValid |> should equal true
    else
        result.ErrorCode |> should equal "IllegalTransition"
