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

    result.Status |> should equal Matched
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

    result.Status |> should equal UnderPaid
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

    result.Status |> should equal CurrencyMismatch

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

    result.Status |> should equal TimingMismatch

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
    results |> Array.find (fun result -> result.FlowId = "coupon-1") |> fun result -> result.Status |> should equal Matched
    results |> Array.find (fun result -> result.FlowId = "coupon-2") |> fun result -> result.Status |> should equal Matched

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

    result.Status |> should equal Matched
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

    result.Status |> should equal Matched
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

    result.Status |> should equal MissingActual
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
