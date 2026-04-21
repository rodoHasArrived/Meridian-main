module Meridian.FSharp.Tests.LedgerKernelTests

open System
open Xunit
open FsUnit.Xunit
open Meridian.FSharp.Ledger

let private line debit credit =
    {
        EntryId = Guid.NewGuid()
        JournalEntryId = Guid.Parse("11111111-1111-1111-1111-111111111111")
        Timestamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
        AccountName = "Cash"
        AccountType = 0
        Symbol = ""
        FinancialAccountId = ""
        Debit = debit
        Credit = credit
        Description = "test"
    }

[<Fact>]
let ``Ledger validation rejects unbalanced journal`` () =
    let result =
        LedgerInterop.ValidateJournalEntry(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            "test",
            [| line 100m 0m; line 0m 50m |],
            [||],
            [||])

    result.IsValid |> should equal false
    result.Errors[0].Contains("not balanced") |> should equal true

[<Fact>]
let ``Ledger net balance uses debit normal for assets`` () =
    LedgerInterop.CalculateNetBalance(0, 100m, 25m) |> should equal 75m

[<Fact>]
let ``Ledger trial balance groups by account identity`` () =
    let balances =
        LedgerInterop.BuildTrialBalance(
            [|
                { AccountName = "Cash"; AccountType = 0; Symbol = ""; FinancialAccountId = ""; Debit = 100m; Credit = 0m }
                { AccountName = "Cash"; AccountType = 0; Symbol = ""; FinancialAccountId = ""; Debit = 50m; Credit = 25m }
            |])

    balances.Length |> should equal 1
    balances[0].Balance |> should equal 125m

[<Fact>]
let ``Ledger reconciliation marks exact cash flow as matched`` () =
    let projected =
        {
            SecurityId = "bond-1"
            FlowId = "coupon-2026-06-30"
            ExpectedAmount = 1250m
            ExpectedCurrency = "USD"
            DueDate = DateTimeOffset.Parse("2026-06-30T00:00:00Z")
        }

    let actual =
        {
            SecurityId = "bond-1"
            FlowId = "coupon-2026-06-30"
            EventId = "payment-1"
            ActualAmount = 1250m
            ActualCurrency = "USD"
            PostedAt = DateTimeOffset.Parse("2026-06-30T13:00:00Z")
        }

    let result = Reconciliation.reconcilePayment 0 projected actual

    result.Outcome |> should equal ReconciliationOutcome.Matched
    result.Variance |> should equal 0m

[<Fact>]
let ``Ledger reconciliation flags underpayment`` () =
    let projected =
        {
            SecurityId = "loan-1"
            FlowId = "interest-2026-03"
            ExpectedAmount = 500m
            ExpectedCurrency = "USD"
            DueDate = DateTimeOffset.Parse("2026-03-31T00:00:00Z")
        }

    let actual =
        {
            SecurityId = "loan-1"
            FlowId = "interest-2026-03"
            EventId = "receipt-1"
            ActualAmount = 450m
            ActualCurrency = "USD"
            PostedAt = DateTimeOffset.Parse("2026-03-31T14:00:00Z")
        }

    let result = Reconciliation.reconcilePayment 0 projected actual

    result.Outcome |> should equal (ReconciliationOutcome.UnderPaid -50m)
    result.Variance |> should equal -50m

[<Fact>]
let ``Ledger reconciliation flags currency mismatch before amount comparison`` () =
    let projected =
        {
            SecurityId = "swap-1"
            FlowId = "leg-a-1"
            ExpectedAmount = 1000m
            ExpectedCurrency = "USD"
            DueDate = DateTimeOffset.Parse("2026-04-15T00:00:00Z")
        }

    let actual =
        {
            SecurityId = "swap-1"
            FlowId = "leg-a-1"
            EventId = "settlement-1"
            ActualAmount = 1000m
            ActualCurrency = "EUR"
            PostedAt = DateTimeOffset.Parse("2026-04-15T00:00:00Z")
        }

    let result = Reconciliation.reconcilePayment 0 projected actual

    result.Outcome |> should equal (ReconciliationOutcome.CurrencyMismatch ("USD", "EUR"))

[<Fact>]
let ``Ledger reconciliation flags timing mismatch outside tolerance`` () =
    let projected =
        {
            SecurityId = "bond-2"
            FlowId = "principal-2030-01-01"
            ExpectedAmount = 10000m
            ExpectedCurrency = "USD"
            DueDate = DateTimeOffset.Parse("2030-01-01T00:00:00Z")
        }

    let actual =
        {
            SecurityId = "bond-2"
            FlowId = "principal-2030-01-01"
            EventId = "redemption-1"
            ActualAmount = 10000m
            ActualCurrency = "USD"
            PostedAt = DateTimeOffset.Parse("2030-01-04T00:00:00Z")
        }

    let result = Reconciliation.reconcilePayment 1 projected actual

    result.Outcome |> should equal (ReconciliationOutcome.TimingMismatch 3)

[<Fact>]
let ``Ledger reconciliation matches event stream by security and flow id`` () =
    let projectedFlows =
        [|
            {
                SecurityId = "bond-3"
                FlowId = "coupon-1"
                ExpectedAmount = 100m
                ExpectedCurrency = "USD"
                DueDate = DateTimeOffset.Parse("2026-07-01T00:00:00Z")
            }
            {
                SecurityId = "bond-3"
                FlowId = "coupon-2"
                ExpectedAmount = 150m
                ExpectedCurrency = "USD"
                DueDate = DateTimeOffset.Parse("2026-10-01T00:00:00Z")
            }
        |]

    let events =
        [|
            PaymentReceived {
                SecurityId = "bond-3"
                FlowId = "coupon-2"
                EventId = "evt-2"
                Amount = 150m
                Currency = "USD"
                PostedAt = DateTimeOffset.Parse("2026-10-01T10:00:00Z")
            }
            PaymentReceived {
                SecurityId = "bond-3"
                FlowId = "coupon-1"
                EventId = "evt-1"
                Amount = 100m
                Currency = "USD"
                PostedAt = DateTimeOffset.Parse("2026-07-01T10:00:00Z")
            }
        |]

    let results = Reconciliation.reconcileEventStream 0 projectedFlows events

    results.Length |> should equal 2
    results |> Array.find (fun result -> result.FlowId = "coupon-1") |> fun result -> result.Outcome |> should equal ReconciliationOutcome.Matched
    results |> Array.find (fun result -> result.FlowId = "coupon-2") |> fun result -> result.Outcome |> should equal ReconciliationOutcome.Matched

[<Fact>]
let ``Ledger reconciliation aggregates multiple events for one projected flow`` () =
    let projectedFlows =
        [|
            {
                SecurityId = "loan-2"
                FlowId = "principal-1"
                ExpectedAmount = 1000m
                ExpectedCurrency = "USD"
                DueDate = DateTimeOffset.Parse("2026-08-15T00:00:00Z")
            }
        |]

    let events =
        [|
            PaymentReceived {
                SecurityId = "loan-2"
                FlowId = "principal-1"
                EventId = "evt-a"
                Amount = 400m
                Currency = "USD"
                PostedAt = DateTimeOffset.Parse("2026-08-15T09:00:00Z")
            }
            PaymentReceived {
                SecurityId = "loan-2"
                FlowId = "principal-1"
                EventId = "evt-b"
                Amount = 600m
                Currency = "USD"
                PostedAt = DateTimeOffset.Parse("2026-08-15T16:00:00Z")
            }
        |]

    let result = Reconciliation.reconcileEventStream 0 projectedFlows events |> Array.exactlyOne

    result.Outcome |> should equal ReconciliationOutcome.Matched
    result.ActualAmount |> should equal 1000m
    result.EventId |> should equal "evt-a,evt-b"

[<Fact>]
let ``Ledger reconciliation treats disbursement as negative cash movement`` () =
    let projectedFlows =
        [|
            {
                SecurityId = "facility-1"
                FlowId = "drawdown-1"
                ExpectedAmount = -250m
                ExpectedCurrency = "USD"
                DueDate = DateTimeOffset.Parse("2026-05-01T00:00:00Z")
            }
        |]

    let events =
        [|
            DisbursementMade {
                SecurityId = "facility-1"
                FlowId = "drawdown-1"
                EventId = "cash-out-1"
                Amount = 250m
                Currency = "USD"
                PostedAt = DateTimeOffset.Parse("2026-05-01T12:00:00Z")
            }
        |]

    let result = Reconciliation.reconcileEventStream 0 projectedFlows events |> Array.exactlyOne

    result.Outcome |> should equal ReconciliationOutcome.Matched
    result.ActualAmount |> should equal -250m

[<Fact>]
let ``Portfolio ledger reconciliation marks exact match as matched`` () =
    let checks : PortfolioLedgerCheckDto array =
        [|
            {
                CheckId = "cash-balance"
                Label = "Portfolio cash vs ledger cash"
                ExpectedSource = "portfolio"
                ActualSource = "ledger"
                ExpectedAmount = 750m
                ActualAmount = 750m
                HasExpectedAmount = true
                HasActualAmount = true
                ExpectedPresent = true
                ActualPresent = true
                ExpectedAsOf = DateTimeOffset.Parse("2026-03-21T16:30:00Z")
                ActualAsOf = DateTimeOffset.Parse("2026-03-21T16:30:00Z")
                HasExpectedAsOf = true
                HasActualAsOf = true
                CategoryHint = "amount"
                MissingSourceHint = ""
                ActualKind = "amount"
            }
        |]

    let result = LedgerInterop.ReconcilePortfolioLedgerChecks(0.01m, 5, checks) |> Array.exactlyOne

    result.IsMatch |> should equal true
    result.Category |> should equal "matched"
    result.Status |> should equal "matched"
    result.Severity |> should equal "Info"

[<Fact>]
let ``Portfolio ledger reconciliation flags amount mismatch`` () =
    let checks : PortfolioLedgerCheckDto array =
        [|
            {
                CheckId = "net-equity"
                Label = "Portfolio total equity vs ledger net assets"
                ExpectedSource = "portfolio"
                ActualSource = "ledger"
                ExpectedAmount = 1000m
                ActualAmount = 975m
                HasExpectedAmount = true
                HasActualAmount = true
                ExpectedPresent = true
                ActualPresent = true
                ExpectedAsOf = DateTimeOffset.Parse("2026-03-21T16:30:00Z")
                ActualAsOf = DateTimeOffset.Parse("2026-03-21T16:30:00Z")
                HasExpectedAsOf = true
                HasActualAsOf = true
                CategoryHint = "amount"
                MissingSourceHint = ""
                ActualKind = "amount"
            }
        |]

    let result = LedgerInterop.ReconcilePortfolioLedgerChecks(0.01m, 5, checks) |> Array.exactlyOne

    result.IsMatch |> should equal false
    result.Category |> should equal "amount_mismatch"
    result.Status |> should equal "open"
    result.Severity |> should equal "High"

[<Fact>]
let ``Portfolio ledger reconciliation surfaces partial match status explicitly`` () =
    let checks : PortfolioLedgerCheckDto array =
        [|
            {
                CheckId = "timing-partial"
                Label = "Portfolio cash vs ledger cash timing drift"
                ExpectedSource = "portfolio"
                ActualSource = "ledger"
                ExpectedAmount = 750m
                ActualAmount = 750m
                HasExpectedAmount = true
                HasActualAmount = true
                ExpectedPresent = true
                ActualPresent = true
                ExpectedAsOf = DateTimeOffset.Parse("2026-03-01T00:00:00Z")
                ActualAsOf = DateTimeOffset.Parse("2026-03-05T00:00:00Z")
                HasExpectedAsOf = true
                HasActualAsOf = true
                CategoryHint = "amount"
                MissingSourceHint = ""
                ActualKind = "amount"
            }
        |]

    let result = LedgerInterop.ReconcilePortfolioLedgerChecks(0.01m, 4320, checks) |> Array.exactlyOne

    result.IsMatch |> should equal false
    result.Category |> should equal "partial_match"
    result.Status |> should equal "partial_match"
    result.Severity |> should equal "Low"

[<Fact>]
let ``Portfolio ledger reconciliation flags missing ledger coverage`` () =
    let checks : PortfolioLedgerCheckDto array =
        [|
            {
                CheckId = "long-AAPL"
                Label = "Long position coverage for AAPL"
                ExpectedSource = "portfolio"
                ActualSource = "ledger"
                ExpectedAmount = 0m
                ActualAmount = 0m
                HasExpectedAmount = false
                HasActualAmount = false
                ExpectedPresent = true
                ActualPresent = false
                ExpectedAsOf = DateTimeOffset.Parse("2026-03-21T16:30:00Z")
                ActualAsOf = DateTimeOffset.Parse("2026-03-21T16:30:00Z")
                HasExpectedAsOf = true
                HasActualAsOf = true
                CategoryHint = "long"
                MissingSourceHint = "ledger"
                ActualKind = ""
            }
        |]

    let result = LedgerInterop.ReconcilePortfolioLedgerChecks(0.01m, 5, checks) |> Array.exactlyOne

    result.IsMatch |> should equal false
    result.Category |> should equal "missing_ledger_coverage"
    result.MissingSource |> should equal "ledger"

[<Fact>]
let ``Portfolio ledger reconciliation flags classification gap`` () =
    let checks : PortfolioLedgerCheckDto array =
        [|
            {
                CheckId = "short-TSLA"
                Label = "Short position coverage for TSLA"
                ExpectedSource = "portfolio"
                ActualSource = "ledger"
                ExpectedAmount = 0m
                ActualAmount = 0m
                HasExpectedAmount = false
                HasActualAmount = false
                ExpectedPresent = true
                ActualPresent = true
                ExpectedAsOf = DateTimeOffset.Parse("2026-03-21T16:30:00Z")
                ActualAsOf = DateTimeOffset.Parse("2026-03-21T16:30:00Z")
                HasExpectedAsOf = true
                HasActualAsOf = true
                CategoryHint = "short"
                MissingSourceHint = ""
                ActualKind = "long"
            }
        |]

    let result = LedgerInterop.ReconcilePortfolioLedgerChecks(0.01m, 5, checks) |> Array.exactlyOne

    result.IsMatch |> should equal false
    result.Category |> should equal "classification_gap"

[<Fact>]
let ``Ledger reconciliation reports missing actual when no event exists for flow`` () =
    let projectedFlows =
        [|
            {
                SecurityId = "bond-4"
                FlowId = "coupon-missing"
                ExpectedAmount = 75m
                ExpectedCurrency = "USD"
                DueDate = DateTimeOffset.Parse("2026-11-01T00:00:00Z")
            }
        |]

    let result = Reconciliation.reconcileEventStream 0 projectedFlows Array.empty |> Array.exactlyOne

    result.Outcome |> should equal ReconciliationOutcome.MissingActual
    result.Variance |> should equal -75m

// ---------------------------------------------------------------------------
// ReconciliationRules — matching rule engine
// ---------------------------------------------------------------------------

let private candidate
        secId
        expectedAmount actualAmount
        expectedCurrency actualCurrency
        (expectedDate: DateTimeOffset) (actualDate: DateTimeOffset) =
    {
        CandidateId      = Guid.NewGuid()
        SecurityId       = secId
        ExpectedAmount   = expectedAmount
        ActualAmount     = actualAmount
        ExpectedCurrency = expectedCurrency
        ActualCurrency   = actualCurrency
        ExpectedDate     = expectedDate
        ActualDate       = actualDate
        Notes            = ""
    }

[<Fact>]
let ``ReconciliationRules apply returns FullMatch for exact candidate with default rule`` () =
    let c = candidate
                (Guid.NewGuid())
                500m 500m
                "USD" "USD"
                (DateTimeOffset.Parse("2026-01-01T00:00:00Z"))
                (DateTimeOffset.Parse("2026-01-01T00:00:00Z"))

    match ReconciliationRules.apply MatchingRule.``default`` c with
    | FullMatch conf -> conf |> should (be greaterThanOrEqualTo) 0.9m
    | other -> failwithf "Expected FullMatch but got %A" other

[<Fact>]
let ``ReconciliationRules apply returns NoMatch CurrencyBreak when currencies differ`` () =
    let c = candidate
                (Guid.NewGuid())
                500m 500m
                "USD" "EUR"
                (DateTimeOffset.Parse("2026-01-01T00:00:00Z"))
                (DateTimeOffset.Parse("2026-01-01T00:00:00Z"))

    match ReconciliationRules.apply MatchingRule.strict c with
    | NoMatch (CurrencyBreak(exp, act)) ->
        exp |> should equal "USD"
        act |> should equal "EUR"
    | other -> failwithf "Expected NoMatch CurrencyBreak but got %A" other

[<Fact>]
let ``ReconciliationRules apply returns NoMatch TimingBreak when date exceeds tolerance`` () =
    let c = candidate
                (Guid.NewGuid())
                1000m 1000m
                "USD" "USD"
                (DateTimeOffset.Parse("2026-01-01T00:00:00Z"))
                (DateTimeOffset.Parse("2026-01-10T00:00:00Z")) // 9 days late

    match ReconciliationRules.apply MatchingRule.strict c with
    | NoMatch (TimingBreak days) -> days |> should equal 9
    | other -> failwithf "Expected NoMatch TimingBreak but got %A" other

[<Fact>]
let ``ReconciliationRules apply returns NoMatch AmountBreak when amount exceeds tolerance`` () =
    let c = candidate
                (Guid.NewGuid())
                1000m 900m  // 10% variance — above strict zero-tolerance
                "USD" "USD"
                (DateTimeOffset.Parse("2026-06-01T00:00:00Z"))
                (DateTimeOffset.Parse("2026-06-01T00:00:00Z"))

    match ReconciliationRules.apply MatchingRule.strict c with
    | NoMatch (AmountBreak(exp, act)) ->
        exp |> should equal 1000m
        act |> should equal 900m
    | other -> failwithf "Expected NoMatch AmountBreak but got %A" other

[<Fact>]
let ``ReconciliationRules applyBest selects first matching rule from priority list`` () =
    let secId = Guid.NewGuid()
    let c = candidate
                secId
                1000m 999m   // 0.1% variance — within default 1%, outside strict 0%
                "USD" "USD"
                (DateTimeOffset.Parse("2026-03-15T00:00:00Z"))
                (DateTimeOffset.Parse("2026-03-15T00:00:00Z"))

    // strict first, then default: should fall through to default and match
    let outcome = ReconciliationRules.applyBest [ MatchingRule.strict; MatchingRule.``default`` ] c

    match outcome with
    | FullMatch _ -> ()
    | other -> failwithf "Expected FullMatch from default rule but got %A" other

[<Fact>]
let ``ReconciliationRules applyBest returns NoMatch MissingEntry when no rule matches`` () =
    let c = candidate
                (Guid.NewGuid())
                1000m 500m   // 50% variance — above even default tolerance
                "USD" "USD"
                (DateTimeOffset.Parse("2026-03-15T00:00:00Z"))
                (DateTimeOffset.Parse("2026-03-25T00:00:00Z")) // also 10 days late

    let outcome = ReconciliationRules.applyBest [ MatchingRule.strict; MatchingRule.``default`` ] c

    match outcome with
    | NoMatch MissingEntry -> ()
    | other -> failwithf "Expected NoMatch MissingEntry but got %A" other

[<Fact>]
let ``ReconciliationRules classifyBreaks emits BreakRecord for non-matching candidates`` () =
    let runId = Guid.NewGuid()
    let secId = Guid.NewGuid()

    let c = candidate
                secId
                200m 100m   // 50% variance — fails strict rule
                "USD" "USD"
                (DateTimeOffset.Parse("2026-05-01T00:00:00Z"))
                (DateTimeOffset.Parse("2026-05-01T00:00:00Z"))

    let breaks = ReconciliationRules.classifyBreaks runId MatchingRule.strict [ c ]

    breaks.Length |> should equal 1
    breaks[0].RunId |> should equal runId
    breaks[0].ExpectedAmount |> should equal 200m
    breaks[0].ActualAmount |> should equal 100m
    breaks[0].TaxonomyVersion |> should equal "reconciliation-break-taxonomy/v1"
    breaks[0].CanonicalClass |> should equal "CashFlow"
    breaks[0].IsResolved |> should equal false

[<Fact>]
let ``ReconciliationRules classifyBreaks returns empty list when all candidates match`` () =
    let runId = Guid.NewGuid()
    let c = candidate
                (Guid.NewGuid())
                750m 750m
                "USD" "USD"
                (DateTimeOffset.Parse("2026-08-01T00:00:00Z"))
                (DateTimeOffset.Parse("2026-08-01T00:00:00Z"))

    let breaks = ReconciliationRules.classifyBreaks runId MatchingRule.strict [ c ]

    breaks |> should be Empty

[<Fact>]
let ``LedgerBreakClassification severity marks currency break as Critical`` () =
    LedgerBreakClassification.severity 1000m (CurrencyBreak("USD", "EUR"))
    |> should equal Critical

[<Fact>]
let ``LedgerBreakClassification severity marks large amount break as Critical`` () =
    // > 5% variance on a non-zero expected amount
    LedgerBreakClassification.severity 1000m (AmountBreak(1000m, 1060m))
    |> should equal Critical

[<Fact>]
let ``LedgerBreakClassification severity marks small amount break as Medium`` () =
    // Within 1% variance
    LedgerBreakClassification.severity 1000m (AmountBreak(1000m, 1005m))
    |> should equal Medium

[<Fact>]
let ``BreakSeverity round-trips through string conversion`` () =
    let severities = [ Critical; High; Medium; Low; Info ]
    for sev in severities do
        BreakSeverity.fromString (BreakSeverity.asString sev) |> should equal sev

let private rawFacts breakType =
    {
        BreakType = breakType
        ExpectedQuantity = None
        ActualQuantity = None
        ExpectedPrice = None
        ActualPrice = None
        ExpectedInstrumentId = None
        ActualInstrumentId = None
        ExpectedCashAmount = None
        ActualCashAmount = None
        ExpectedCurrency = None
        ActualCurrency = None
        ExpectedSettlementDate = None
        ActualSettlementDate = None
        TimingToleranceDays = 0
        ExpectedCorporateActionType = None
        ActualCorporateActionType = None
        ExpectedCorporateActionFactor = None
        ActualCorporateActionFactor = None
        MappingKey = None
        MappingResolved = None
    }

[<Theory>]
[<InlineData("timing", "Timing", "TimingOutsideTolerance")>]
[<InlineData("quantity", "Quantity", "QuantityMismatch")>]
[<InlineData("price", "Price", "PriceMismatch")>]
[<InlineData("instrument", "Instrument", "InstrumentIdentifierMismatch")>]
[<InlineData("cash-flow", "CashFlow", "CashAmountMismatch")>]
[<InlineData("corporate-action", "CorporateAction", "CorporateActionFactorMismatch")>]
[<InlineData("mapping-error", "MappingError", "MappingKeyNotFound")>]
let ``ReconciliationClassification maps each break class into canonical taxonomy`` breakType expectedClass expectedReason =
    let facts =
        match breakType with
        | "timing" ->
            { rawFacts (Some breakType) with
                ExpectedSettlementDate = Some DateTimeOffset.UnixEpoch
                ActualSettlementDate = Some (DateTimeOffset.UnixEpoch.AddDays(3))
                TimingToleranceDays = 0 }
        | "quantity" ->
            { rawFacts (Some breakType) with
                ExpectedQuantity = Some 100m
                ActualQuantity = Some 95m
                ExpectedInstrumentId = Some "AAPL"
                ActualInstrumentId = Some "AAPL" }
        | "price" ->
            { rawFacts (Some breakType) with
                ExpectedPrice = Some 100m
                ActualPrice = Some 99m
                ExpectedInstrumentId = Some "AAPL"
                ActualInstrumentId = Some "AAPL" }
        | "instrument" ->
            { rawFacts (Some breakType) with
                ExpectedInstrumentId = Some "AAPL"
                ActualInstrumentId = Some "MSFT" }
        | "cash-flow" ->
            { rawFacts (Some breakType) with
                ExpectedCashAmount = Some 1000m
                ActualCashAmount = Some 900m
                ExpectedInstrumentId = Some "AAPL"
                ActualInstrumentId = Some "AAPL" }
        | "corporate-action" ->
            { rawFacts (Some breakType) with
                ExpectedCorporateActionFactor = Some 2m
                ActualCorporateActionFactor = Some 1m
                ExpectedInstrumentId = Some "AAPL"
                ActualInstrumentId = Some "AAPL" }
        | _ ->
            { rawFacts (Some breakType) with
                MappingKey = Some "CUSIP:123"
                MappingResolved = Some false }

    let classification = ReconciliationClassification.classify facts

    classification.TaxonomyVersion |> should equal BreakTaxonomyVersion.V1
    CanonicalBreakClass.asString classification.BreakClass |> should equal expectedClass
    BreakReasonCode.asString classification.PrimaryReasonCode |> should equal expectedReason
    classification.IsFallback |> should equal false

[<Fact>]
let ``ReconciliationClassification preserves ambiguous multi-cause breaks with reason set`` () =
    let facts =
        { rawFacts (Some "cash-flow") with
            ExpectedCashAmount = Some 1000m
            ActualCashAmount = Some 900m
            ExpectedCurrency = Some "USD"
            ActualCurrency = Some "EUR"
            ExpectedInstrumentId = Some "AAPL"
            ActualInstrumentId = Some "MSFT"
            ExpectedSettlementDate = Some DateTimeOffset.UnixEpoch
            ActualSettlementDate = Some (DateTimeOffset.UnixEpoch.AddDays(5))
            TimingToleranceDays = 0 }

    let classification = ReconciliationClassification.classify facts
    let reasons = classification.ReasonCodes |> List.map BreakReasonCode.asString

    classification.IsFallback |> should equal false
    reasons |> should contain "CashAmountMismatch"
    reasons |> should contain "CashCurrencyMismatch"
    reasons |> should contain "InstrumentIdentifierMismatch"
    reasons |> should contain "TimingOutsideTolerance"

[<Fact>]
let ``ReconciliationClassification unknown break type uses safe fallback migration path`` () =
    let classification = ReconciliationClassification.classify { rawFacts (Some "vendor-new-break") with MappingResolved = None }
    let reasons = classification.ReasonCodes |> List.map BreakReasonCode.asString

    CanonicalBreakClass.asString classification.BreakClass |> should equal "MappingError"
    classification.IsFallback |> should equal true
    reasons |> should contain "UnsupportedBreakTypeFallback"
    reasons |> should not' (contain "InstrumentMissing")

[<Fact>]
let ``LedgerInterop ClassifyBreakFacts returns stable DTO values for governance consumers`` () =
    let input : BreakFactsDto array = [|
        {
            BreakType = "price"
            ExpectedQuantity = None
            ActualQuantity = None
            ExpectedPrice = Some 100m
            ActualPrice = Some 97m
            ExpectedInstrumentId = "AAPL"
            ActualInstrumentId = "AAPL"
            ExpectedCashAmount = None
            ActualCashAmount = None
            ExpectedCurrency = "USD"
            ActualCurrency = "USD"
            ExpectedSettlementDate = None
            ActualSettlementDate = None
            TimingToleranceDays = 0
            ExpectedCorporateActionType = ""
            ActualCorporateActionType = ""
            ExpectedCorporateActionFactor = None
            ActualCorporateActionFactor = None
            MappingKey = ""
            MappingResolved = None
        }
    |]

    let classifications = LedgerInterop.ClassifyBreakFacts input

    classifications.Length |> should equal 1
    classifications[0].TaxonomyVersion |> should equal "reconciliation-break-taxonomy/v1"
    classifications[0].BreakClass |> should equal "Price"
    classifications[0].PrimaryReasonCode |> should equal "PriceMismatch"
    classifications[0].IsFallback |> should equal false

[<Fact>]
let ``LedgerInterop ToBreakRecordClassificationDtos preserves canonical classification metadata`` () =
    let record = {
        BreakId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
        RunId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
        SecurityId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")
        FlowId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd")
        Classification = "AmountBreak"
        TaxonomyVersion = "reconciliation-break-taxonomy/v1"
        CanonicalClass = "CashFlow"
        PrimaryReasonCode = "CashAmountMismatch"
        ReasonCodes = [| "CashAmountMismatch" |]
        IsFallbackClassification = false
        Severity = "High"
        ExpectedAmount = 1000m
        ActualAmount = 900m
        Currency = "USD"
        ExpectedDate = DateTimeOffset.UnixEpoch
        ActualDate = Some DateTimeOffset.UnixEpoch
        Notes = "test"
        CreatedAt = DateTimeOffset.UnixEpoch
        ResolvedAt = None
        IsResolved = false
    }

    let dtos = LedgerInterop.ToBreakRecordClassificationDtos [| record |]

    dtos.Length |> should equal 1
    dtos[0].BreakId |> should equal record.BreakId
    dtos[0].CanonicalClass |> should equal "CashFlow"
    dtos[0].PrimaryReasonCode |> should equal "CashAmountMismatch"
    dtos[0].IsFallbackClassification |> should equal false
