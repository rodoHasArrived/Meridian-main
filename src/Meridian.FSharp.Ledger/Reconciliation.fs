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
    Status: ReconciliationStatus
}

[<RequireQualifiedAccess>]
module Reconciliation =

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
        let status =
            if not (System.String.Equals(projected.ExpectedCurrency, actual.ActualCurrency, System.StringComparison.OrdinalIgnoreCase)) then
                CurrencyMismatch
            else
                let dayDelta = abs ((actual.PostedAt.Date - projected.DueDate.Date).Days)
                if dayDelta > toleranceDays then TimingMismatch
                elif variance = 0m then Matched
                elif variance > 0m then OverPaid
                else UnderPaid

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
            Status = status
        }

    let private missingActualForProjection (projected: ProjectedFlow) =
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
            Status = MissingActual
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
