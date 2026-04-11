namespace Meridian.FSharp.Domain

open System

/// Kind of cash-flow event projected from a security's economic terms
type CashFlowEventKind =
    | CouponPayment
    | PrincipalRepayment
    | PrincipalMaturity
    | DividendPayment
    | FeePayment
    | MarginCall
    | Proceeds
    | PremiumPayment
    | Redemption
    | RollPrincipal
    | OtherCashFlow of label: string

/// A single projected cash event derived from an instrument's terms
[<CLIMutable>]
type ProjectedCashEvent = {
    FlowId          : Guid
    SecurityId      : SecurityId
    EventKind       : CashFlowEventKind
    ExpectedAmount  : decimal
    ExpectedCurrency: string
    DueDate         : DateTimeOffset
    RecordDate      : DateTimeOffset option
    PayableDate     : DateTimeOffset option
    SourceSystem    : string
    Notes           : string option
    IsPrincipalFlow : bool
    IsIncomeFlow    : bool
}

/// An observed cash movement posted to the ledger or bank feed.
[<CLIMutable>]
type ActualCashEvent = {
    FlowId      : Guid
    SecurityId  : SecurityId
    Amount      : decimal
    Currency    : string
    PostedAt    : DateTimeOffset
    SourceSystem: string
    Notes       : string option
}

/// Tolerance parameters for matching projected against actual cash events
type MatchTolerance = {
    AmountTolerancePct  : decimal
    TimingToleranceDays : int
    RequireCurrencyExact: bool
}

/// Severity assigned to a reconciliation break
type BreakSeverity =
    | Critical
    | High
    | Medium
    | Low
    | Info

/// Structural classification of how a projected cash event broke
type BreakClassification =
    | AmountMismatch    of expected: decimal * actual: decimal * variancePct: decimal
    | CurrencyMismatch  of expected: string  * actual: string
    | TimingMismatch    of expectedDate: DateTimeOffset * actualDate: DateTimeOffset * daysLate: int
    | MissingActual
    | ClassificationGap of reason: string
    | OtherBreak        of reason: string

/// State machine for a cash-flow line as it moves from projection to settlement.
type CashFlowState =
    | ProjectedOnly of ProjectedCashEvent
    | Settled of projected: ProjectedCashEvent * actual: ActualCashEvent
    | Broken of projected: ProjectedCashEvent * reason: BreakClassification

/// One time-bucket within a projected cash ladder
[<CLIMutable>]
type CashLadderBucket = {
    BucketStart      : DateTimeOffset
    BucketEnd        : DateTimeOffset
    ProjectedInflows : decimal
    ProjectedOutflows: decimal
    NetFlow          : decimal
    Currency         : string
    EventCount       : int
}

/// A time-bucketed forward view of projected cash flows
[<CLIMutable>]
type CashLadder = {
    AsOf                  : DateTimeOffset
    Currency              : string
    Buckets               : CashLadderBucket list
    TotalProjectedInflows : decimal
    TotalProjectedOutflows: decimal
    NetPosition           : decimal
}

[<RequireQualifiedAccess>]
module CashFlowEventKind =

    let label = function
        | CouponPayment      -> "Coupon"
        | PrincipalRepayment -> "Principal Repayment"
        | PrincipalMaturity  -> "Maturity"
        | DividendPayment    -> "Dividend"
        | FeePayment         -> "Fee"
        | MarginCall         -> "Margin Call"
        | Proceeds           -> "Proceeds"
        | PremiumPayment     -> "Premium"
        | Redemption         -> "Redemption"
        | RollPrincipal      -> "Roll Principal"
        | OtherCashFlow lbl  -> lbl

    let isPrincipalFlow = function
        | PrincipalRepayment | PrincipalMaturity | Redemption | RollPrincipal -> true
        | _ -> false

    let isIncomeFlow = function
        | CouponPayment | DividendPayment -> true
        | _ -> false

[<RequireQualifiedAccess>]
module CashLadder =

    /// Build a time-bucketed cash ladder from a list of projected events in the given currency.
    /// Events before <paramref name="asOf"/> are excluded; <paramref name="bucketDays"/> must be ≥ 1.
    let build (asOf: DateTimeOffset) (currency: string) (bucketDays: int) (events: ProjectedCashEvent list) : CashLadder =
        let days = max 1 bucketDays
        let filtered =
            events
            |> List.filter (fun e ->
                e.ExpectedCurrency = currency
                && e.DueDate >= asOf)

        let buckets =
            filtered
            |> List.groupBy (fun e ->
                let daysFromNow = int (e.DueDate - asOf).TotalDays
                daysFromNow / days)
            |> List.sortBy fst
            |> List.map (fun (bucketIdx, flowList) ->
                let bStart = asOf.AddDays(float (bucketIdx * days))
                let bEnd   = asOf.AddDays(float ((bucketIdx + 1) * days))
                let inflows  = flowList |> List.sumBy (fun f -> if f.ExpectedAmount > 0m then  f.ExpectedAmount else 0m)
                let outflows = flowList |> List.sumBy (fun f -> if f.ExpectedAmount < 0m then -f.ExpectedAmount else 0m)
                {
                    BucketStart       = bStart
                    BucketEnd         = bEnd
                    ProjectedInflows  = inflows
                    ProjectedOutflows = outflows
                    NetFlow           = inflows - outflows
                    Currency          = currency
                    EventCount        = List.length flowList
                })

        let totalIn  = buckets |> List.sumBy (fun b -> b.ProjectedInflows)
        let totalOut = buckets |> List.sumBy (fun b -> b.ProjectedOutflows)
        {
            AsOf                   = asOf
            Currency               = currency
            Buckets                = buckets
            TotalProjectedInflows  = totalIn
            TotalProjectedOutflows = totalOut
            NetPosition            = totalIn - totalOut
        }

[<RequireQualifiedAccess>]
module CashFlowState =

    let label = function
        | ProjectedOnly _ -> "Projected"
        | Settled _ -> "Settled"
        | Broken _ -> "Broken"
