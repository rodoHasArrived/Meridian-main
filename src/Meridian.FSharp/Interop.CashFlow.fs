namespace Meridian.FSharp.CashFlowInterop

open System
open Meridian.FSharp.Domain

/// Input record for a single cash-flow event, using C#-friendly (non-DU) fields.
[<CLIMutable>]
type CashFlowProjectionInput = {
    FlowId          : Guid
    SecurityGuid    : Guid
    EventKindLabel  : string
    ExpectedAmount  : decimal
    ExpectedCurrency: string
    DueDate         : DateTimeOffset
    IsPrincipalFlow : bool
    IsIncomeFlow    : bool
    Notes           : string
}

/// C#-friendly representation of a single time bucket in the cash ladder.
[<CLIMutable>]
type CashLadderBucketInterop = {
    BucketStart      : DateTimeOffset
    BucketEnd        : DateTimeOffset
    ProjectedInflows : decimal
    ProjectedOutflows: decimal
    NetFlow          : decimal
    Currency         : string
    EventCount       : int
}

/// C#-friendly representation of the full time-bucketed cash ladder.
[<CLIMutable>]
type CashLadderInterop = {
    AsOf                   : DateTimeOffset
    Currency               : string
    Buckets                : CashLadderBucketInterop array
    TotalProjectedInflows  : decimal
    TotalProjectedOutflows : decimal
    NetPosition            : decimal
}

[<RequireQualifiedAccess>]
module private EventKindMapping =
    let ofLabel (label: string) =
        match label with
        | "Coupon" | "CouponPayment"           -> CashFlowEventKind.CouponPayment
        | "Dividend" | "DividendPayment"       -> CashFlowEventKind.DividendPayment
        | "Principal" | "PrincipalRepayment"   -> CashFlowEventKind.PrincipalRepayment
        | "Maturity" | "PrincipalMaturity"     -> CashFlowEventKind.PrincipalMaturity
        | "Fee" | "FeePayment" | "Commission"  -> CashFlowEventKind.FeePayment
        | "MarginCall"                         -> CashFlowEventKind.MarginCall
        | "Proceeds"                           -> CashFlowEventKind.Proceeds
        | "Premium" | "PremiumPayment"         -> CashFlowEventKind.PremiumPayment
        | "Redemption"                         -> CashFlowEventKind.Redemption
        | "RollPrincipal"                      -> CashFlowEventKind.RollPrincipal
        | other                                -> CashFlowEventKind.OtherCashFlow other

/// C#-facing sealed class that builds a time-bucketed cash ladder from C#-provided inputs.
[<Sealed>]
type CashFlowProjector private () =

    /// <summary>
    /// Builds a time-bucketed <see cref="CashLadderInterop"/> from the supplied cash-flow inputs.
    /// Only events with <see cref="CashFlowProjectionInput.DueDate"/> &gt;= <paramref name="asOf"/>
    /// and matching <paramref name="currency"/> are included.
    /// </summary>
    static member BuildLadder(
            asOf      : DateTimeOffset,
            currency  : string,
            bucketDays: int,
            inputs    : CashFlowProjectionInput seq) : CashLadderInterop =

        let events : ProjectedCashEvent list =
            inputs
            |> Seq.map (fun input ->
                let flowId   = if input.FlowId = Guid.Empty then Guid.NewGuid() else input.FlowId
                let notesOpt = if String.IsNullOrEmpty(input.Notes) then None else Some input.Notes
                {
                    FlowId           = flowId
                    SecurityId       = SecurityId input.SecurityGuid
                    EventKind        = EventKindMapping.ofLabel input.EventKindLabel
                    ExpectedAmount   = input.ExpectedAmount
                    ExpectedCurrency = input.ExpectedCurrency
                    DueDate          = input.DueDate
                    RecordDate       = None
                    PayableDate      = None
                    SourceSystem     = "Meridian"
                    Notes            = notesOpt
                    IsPrincipalFlow  = input.IsPrincipalFlow
                    IsIncomeFlow     = input.IsIncomeFlow
                })
            |> List.ofSeq

        let ladder = CashLadder.build asOf currency bucketDays events

        let buckets =
            ladder.Buckets
            |> List.map (fun b ->
                {
                    BucketStart       = b.BucketStart
                    BucketEnd         = b.BucketEnd
                    ProjectedInflows  = b.ProjectedInflows
                    ProjectedOutflows = b.ProjectedOutflows
                    NetFlow           = b.NetFlow
                    Currency          = b.Currency
                    EventCount        = b.EventCount
                })
            |> List.toArray

        {
            AsOf                   = ladder.AsOf
            Currency               = ladder.Currency
            Buckets                = buckets
            TotalProjectedInflows  = ladder.TotalProjectedInflows
            TotalProjectedOutflows = ladder.TotalProjectedOutflows
            NetPosition            = ladder.NetPosition
        }
