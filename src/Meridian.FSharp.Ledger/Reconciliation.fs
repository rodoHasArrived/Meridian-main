namespace Meridian.FSharp.Ledger

[<CLIMutable>]
type ProjectedFlow = {
    SecurityId: string
    FlowId: string
    ExpectedAmount: decimal
    ExpectedCurrency: string
    DueDate: System.DateTimeOffset
}

[<CLIMutable>]
type ActualCashEvent = {
    SecurityId: string
    FlowId: string
    EventId: string
    ActualAmount: decimal
    ActualCurrency: string
    PostedAt: System.DateTimeOffset
}

[<CLIMutable>]
type CashLedgerEventData = {
    SecurityId: string
    FlowId: string
    EventId: string
    Amount: decimal
    Currency: string
    PostedAt: System.DateTimeOffset
}

type CashLedgerEvent =
    | PaymentReceived of CashLedgerEventData
    | DisbursementMade of CashLedgerEventData
    | WriteOffRecorded of CashLedgerEventData

type ReconciliationStatus =
    | Matched
    | UnderPaid
    | OverPaid
    | CurrencyMismatch
    | TimingMismatch
    | MissingActual

[<CLIMutable>]
type ReconciliationResult = {
    SecurityId: string
    FlowId: string
    EventId: string
    ExpectedAmount: decimal
    ActualAmount: decimal
    Variance: decimal
    ExpectedCurrency: string
    ActualCurrency: string
    DueDate: System.DateTimeOffset
    PostedAt: System.DateTimeOffset
    Outcome: ReconciliationOutcome
    OutcomeLabel: string
    Status: ReconciliationStatus
}

[<CLIMutable>]
type PortfolioLedgerCheck = {
    CheckId: string
    Label: string
    ExpectedSource: string
    ActualSource: string
    ExpectedAmount: decimal
    ActualAmount: decimal
    HasExpectedAmount: bool
    HasActualAmount: bool
    ExpectedPresent: bool
    ActualPresent: bool
    ExpectedAsOf: System.DateTimeOffset
    ActualAsOf: System.DateTimeOffset
    HasExpectedAsOf: bool
    HasActualAsOf: bool
    CategoryHint: string
    MissingSourceHint: string
    ActualKind: string
}

[<CLIMutable>]
type PortfolioLedgerCheckResult = {
    CheckId: string
    Label: string
    IsMatch: bool
    Category: string
    Status: string
    MissingSource: string
    ExpectedSource: string
    ActualSource: string
    ExpectedAmount: decimal
    ActualAmount: decimal
    HasExpectedAmount: bool
    HasActualAmount: bool
    Variance: decimal
    Reason: string
    Severity: string
    ExpectedAsOf: System.DateTimeOffset
    ActualAsOf: System.DateTimeOffset
    HasExpectedAsOf: bool
    HasActualAsOf: bool
}

[<RequireQualifiedAccess>]
module Reconciliation =

    let private statusOfOutcome = function
        | ReconciliationOutcome.Matched -> ReconciliationStatus.Matched
        | ReconciliationOutcome.UnderPaid _ -> ReconciliationStatus.UnderPaid
        | ReconciliationOutcome.OverPaid _ -> ReconciliationStatus.OverPaid
        | ReconciliationOutcome.CurrencyMismatch _ -> ReconciliationStatus.CurrencyMismatch
        | ReconciliationOutcome.TimingMismatch _ -> ReconciliationStatus.TimingMismatch
        | ReconciliationOutcome.MissingActual -> ReconciliationStatus.MissingActual

    let classifyDifference expected actual =
        let difference = actual - expected
        if difference = 0m then "matched"
        elif difference > 0m then "over"
        else "under"

    let private toActualCashEvent event =
        let build amount (data: CashLedgerEventData) =
            {
                SecurityId = data.SecurityId
                FlowId = data.FlowId
                EventId = data.EventId
                ActualAmount = amount
                ActualCurrency = data.Currency
                PostedAt = data.PostedAt
            }

        match event with
        | PaymentReceived data -> build data.Amount data
        | DisbursementMade data -> build (-data.Amount) data
        | WriteOffRecorded data -> build (-data.Amount) data

    let reconcilePayment toleranceDays (projected: ProjectedFlow) (actual: ActualCashEvent) =
        let variance = actual.ActualAmount - projected.ExpectedAmount
        let outcome =
            if not (System.String.Equals(projected.ExpectedCurrency, actual.ActualCurrency, System.StringComparison.OrdinalIgnoreCase)) then
                ReconciliationOutcome.CurrencyMismatch(projected.ExpectedCurrency, actual.ActualCurrency)
            else
                let dayDelta = abs ((actual.PostedAt.Date - projected.DueDate.Date).Days)
                if dayDelta > toleranceDays then ReconciliationOutcome.TimingMismatch dayDelta
                elif variance = 0m then ReconciliationOutcome.Matched
                elif variance > 0m then ReconciliationOutcome.OverPaid variance
                else ReconciliationOutcome.UnderPaid variance

        {
            SecurityId = projected.SecurityId
            FlowId = projected.FlowId
            EventId = actual.EventId
            ExpectedAmount = projected.ExpectedAmount
            ActualAmount = actual.ActualAmount
            Variance = variance
            ExpectedCurrency = projected.ExpectedCurrency
            ActualCurrency = actual.ActualCurrency
            DueDate = projected.DueDate
            PostedAt = actual.PostedAt
            Outcome = outcome
            OutcomeLabel = ReconciliationOutcome.label outcome
            Status = statusOfOutcome outcome
        }

    let private missingActualForProjection (projected: ProjectedFlow) =
        let outcome = ReconciliationOutcome.MissingActual
        {
            SecurityId = projected.SecurityId
            FlowId = projected.FlowId
            EventId = System.String.Empty
            ExpectedAmount = projected.ExpectedAmount
            ActualAmount = 0m
            Variance = -projected.ExpectedAmount
            ExpectedCurrency = projected.ExpectedCurrency
            ActualCurrency = projected.ExpectedCurrency
            DueDate = projected.DueDate
            PostedAt = projected.DueDate
            Outcome = outcome
            OutcomeLabel = ReconciliationOutcome.label outcome
            Status = statusOfOutcome outcome
        }

    let foldActualCashEvents (events: CashLedgerEvent seq) =
        events
        |> Seq.map toActualCashEvent
        |> Seq.toArray

    let reconcilePayments toleranceDays (projectedFlows: ProjectedFlow seq) (actualEvents: ActualCashEvent seq) =
        let actualByProjection =
            actualEvents
            |> Seq.groupBy (fun event -> event.SecurityId, event.FlowId)
            |> Seq.map (fun (projectionKey, events) ->
                projectionKey, (events |> Seq.sortBy (fun event -> event.PostedAt) |> Seq.toArray))
            |> Map.ofSeq

        projectedFlows
        |> Seq.map (fun projected ->
            match actualByProjection |> Map.tryFind (projected.SecurityId, projected.FlowId) with
            | Some events when events.Length > 0 ->
                events
                |> Array.reduce (fun accumulated next ->
                    {
                        accumulated with
                            EventId = if System.String.IsNullOrWhiteSpace accumulated.EventId then next.EventId else accumulated.EventId + "," + next.EventId
                            ActualAmount = accumulated.ActualAmount + next.ActualAmount
                            PostedAt = max accumulated.PostedAt next.PostedAt
                    })
                |> reconcilePayment toleranceDays projected
            | _ -> missingActualForProjection projected)
        |> Seq.toArray

    let reconcileEventStream toleranceDays (projectedFlows: ProjectedFlow seq) (events: CashLedgerEvent seq) =
        events
        |> foldActualCashEvents
        |> reconcilePayments toleranceDays projectedFlows

    let reconcilePortfolioLedgerChecks amountTolerance maxAsOfDriftMinutes (checks: PortfolioLedgerCheck seq) =
        let classifyCheck (check: PortfolioLedgerCheck) =
            let variance =
                if check.HasExpectedAmount && check.HasActualAmount then
                    check.ActualAmount - check.ExpectedAmount
                else
                    0m

            let category, status, missingSource, reason, isMatch, severity =
                if check.ExpectedPresent && not check.ActualPresent then
                    "missing_ledger_coverage", "open", (if System.String.IsNullOrWhiteSpace check.MissingSourceHint then "ledger" else check.MissingSourceHint), "Expected portfolio coverage is missing a ledger counterpart.", false, BreakSeverity.asString BreakSeverity.Medium
                elif not check.ExpectedPresent && check.ActualPresent then
                    "missing_portfolio_coverage", "open", (if System.String.IsNullOrWhiteSpace check.MissingSourceHint then "portfolio" else check.MissingSourceHint), "Ledger coverage exists without a matching portfolio reference.", false, BreakSeverity.asString BreakSeverity.Medium
                elif not (System.String.IsNullOrWhiteSpace check.ActualKind)
                     && not (System.String.Equals(check.ActualKind, check.CategoryHint, System.StringComparison.OrdinalIgnoreCase)) then
                    "classification_gap", "open", "unknown", "Coverage was found, but it is classified in the wrong ledger bucket.", false, BreakSeverity.asString BreakSeverity.High
                elif check.HasExpectedAmount && check.HasActualAmount && check.HasExpectedAsOf && check.HasActualAsOf then
                    let expectedAmountForSeverity =
                        if check.ExpectedAmount = 0m then 1m else check.ExpectedAmount

                    let timingToleranceDays =
                        max 0 (int (System.Math.Ceiling(float maxAsOfDriftMinutes / 1440.0)))

                    let candidate: MatchCandidate = {
                        CandidateId = System.Guid.NewGuid()
                        SecurityId = System.Guid.Empty
                        ExpectedAmount = check.ExpectedAmount
                        ActualAmount = check.ActualAmount
                        ExpectedCurrency = "USD"
                        ActualCurrency = "USD"
                        ExpectedDate = check.ExpectedAsOf
                        ActualDate = check.ActualAsOf
                        Notes = check.Label
                    }

                    let matchingRule =
                        { MatchingRule.``default`` with
                            AmountTolerancePct = amountTolerance
                            TimingToleranceDays = timingToleranceDays
                            AllowPartialMatch = true
                            MinMatchConfidence = 0.60m
                        }

                    match ReconciliationRules.applyBest [ matchingRule ] candidate with
                    | FullMatch _ ->
                        "matched", "matched", "unknown", "Comparison satisfied all configured checks.", true, BreakSeverity.asString BreakSeverity.Info
                    | PartialMatch (_, partialReason) ->
                        "partial_match", "partial_match", "unknown", partialReason, false, BreakSeverity.asString BreakSeverity.Low
                    | NoMatch classification ->
                        let inferredSeverity = LedgerBreakClassification.severity expectedAmountForSeverity classification |> BreakSeverity.asString
                        match classification with
                        | AmountBreak _ ->
                            "amount_mismatch", "open", "unknown", "Amounts differ beyond the configured tolerance.", false, inferredSeverity
                        | TimingBreak _ ->
                            "timing_mismatch", "open", "unknown", "Comparison timestamps drift beyond tolerance.", false, inferredSeverity
                        | CurrencyBreak _ ->
                            "classification_gap", "open", "unknown", "Coverage was found, but currency classification diverged.", false, inferredSeverity
                        | MissingEntry ->
                            "missing_ledger_coverage", "open", "ledger", "Expected portfolio coverage is missing a ledger counterpart.", false, inferredSeverity
                        | DuplicateEntry ->
                            "classification_gap", "open", "unknown", "Coverage contains duplicate entries requiring investigation.", false, inferredSeverity
                        | ClassificationBreak reason ->
                            "classification_gap", "open", "unknown", reason, false, inferredSeverity
                        | OtherBreak reason ->
                            "classification_gap", "open", "unknown", reason, false, inferredSeverity
                elif check.HasExpectedAmount && check.HasActualAmount then
                    if check.ActualAmount <> check.ExpectedAmount then
                        "amount_mismatch", "open", "unknown", "Amounts differ beyond the configured tolerance.", false, BreakSeverity.asString BreakSeverity.Medium
                    else
                        "matched", "matched", "unknown", "Comparison satisfied all configured checks.", true, BreakSeverity.asString BreakSeverity.Info
                elif check.HasExpectedAsOf && check.HasActualAsOf then
                    if check.ActualAsOf <> check.ExpectedAsOf then
                        "timing_mismatch", "open", "unknown", "Comparison timestamps drift beyond tolerance.", false, BreakSeverity.asString BreakSeverity.Medium
                    else
                        "matched", "matched", "unknown", "Comparison satisfied all configured checks.", true, BreakSeverity.asString BreakSeverity.Info
                else
                    "matched", "matched", "unknown", "Comparison satisfied all configured checks.", true, BreakSeverity.asString BreakSeverity.Info

            {
                CheckId = check.CheckId
                Label = check.Label
                IsMatch = isMatch
                Category = category
                Status = status
                MissingSource = missingSource
                ExpectedSource = check.ExpectedSource
                ActualSource = check.ActualSource
                ExpectedAmount = check.ExpectedAmount
                ActualAmount = check.ActualAmount
                HasExpectedAmount = check.HasExpectedAmount
                HasActualAmount = check.HasActualAmount
                Variance = variance
                Reason = reason
                Severity = severity
                ExpectedAsOf = check.ExpectedAsOf
                ActualAsOf = check.ActualAsOf
                HasExpectedAsOf = check.HasExpectedAsOf
                HasActualAsOf = check.HasActualAsOf
            }

        checks
        |> Seq.map classifyCheck
        |> Seq.toArray
