/// Unit tests for the F# CashFlowProjector interop bridge.
module Meridian.FSharp.Tests.CashFlowProjectorTests

open System
open Xunit
open FsUnit.Xunit
open Meridian.FSharp.CashFlowInterop

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let private buildInput (dueDate: DateTimeOffset) (amount: decimal) (currency: string) : CashFlowProjectionInput =
    {
        FlowId           = Guid.NewGuid()
        SecurityGuid     = Guid.NewGuid()
        EventKindLabel   = if amount > 0m then "Proceeds" else "Fee"
        ExpectedAmount   = amount
        ExpectedCurrency = currency
        DueDate          = dueDate
        IsPrincipalFlow  = false
        IsIncomeFlow     = false
        Notes            = ""
    }

// ---------------------------------------------------------------------------
// Empty inputs
// ---------------------------------------------------------------------------

[<Fact>]
let ``BuildLadder returns empty buckets for empty input`` () =
    let asOf   = DateTimeOffset.UtcNow
    let result = CashFlowProjector.BuildLadder(asOf, "USD", 7, Array.empty)

    result.Buckets        |> should haveLength 0
    result.TotalProjectedInflows  |> should equal 0m
    result.TotalProjectedOutflows |> should equal 0m
    result.NetPosition            |> should equal 0m

// ---------------------------------------------------------------------------
// Bucket grouping
// ---------------------------------------------------------------------------

[<Fact>]
let ``BuildLadder groups events into correct number of buckets`` () =
    let asOf = DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)

    let inputs =
        [|
            buildInput (asOf.AddDays  1.0) 100m "USD"   // bucket 0
            buildInput (asOf.AddDays  3.0) 200m "USD"   // bucket 0 (same 7-day window)
            buildInput (asOf.AddDays 10.0) -50m "USD"   // bucket 1
        |]

    let result = CashFlowProjector.BuildLadder(asOf, "USD", 7, inputs)

    result.Buckets              |> should haveLength 2
    result.TotalProjectedInflows  |> should equal 300m
    result.TotalProjectedOutflows |> should equal  50m
    result.NetPosition            |> should equal 250m

[<Fact>]
let ``BuildLadder net position equals sum of bucket net flows`` () =
    let asOf = DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)

    let inputs =
        [|
            buildInput (asOf.AddDays 1.0)  500m "USD"
            buildInput (asOf.AddDays 5.0) -100m "USD"
            buildInput (asOf.AddDays 8.0)  200m "USD"
        |]

    let result = CashFlowProjector.BuildLadder(asOf, "USD", 7, inputs)

    let bucketNetSum = result.Buckets |> Array.sumBy (fun b -> b.NetFlow)
    result.NetPosition |> should equal bucketNetSum

// ---------------------------------------------------------------------------
// asOf filtering
// ---------------------------------------------------------------------------

[<Fact>]
let ``BuildLadder excludes events before asOf`` () =
    let asOf = DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)

    let inputs =
        [|
            buildInput (asOf.AddDays -1.0) 500m "USD"  // before asOf → excluded
            buildInput (asOf.AddDays  1.0) 100m "USD"  // included
        |]

    let result = CashFlowProjector.BuildLadder(asOf, "USD", 7, inputs)

    result.TotalProjectedInflows |> should equal 100m

[<Fact>]
let ``BuildLadder with asOf far in future excludes all events`` () =
    let asOf = DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)

    let inputs =
        [|
            buildInput (asOf.AddDays 1.0) 100m "USD"
            buildInput (asOf.AddDays 3.0) 200m "USD"
        |]

    let result = CashFlowProjector.BuildLadder(DateTimeOffset.MaxValue, "USD", 7, inputs)

    result.Buckets |> should haveLength 0

// ---------------------------------------------------------------------------
// Currency filtering
// ---------------------------------------------------------------------------

[<Fact>]
let ``BuildLadder excludes events with different currency`` () =
    let asOf = DateTimeOffset.UtcNow

    let inputs =
        [|
            buildInput (asOf.AddDays 1.0) 100m "EUR"   // wrong currency → excluded
            buildInput (asOf.AddDays 1.0) 200m "USD"   // included
        |]

    let result = CashFlowProjector.BuildLadder(asOf, "USD", 7, inputs)

    result.TotalProjectedInflows |> should equal 200m

// ---------------------------------------------------------------------------
// Event kind mapping
// ---------------------------------------------------------------------------

[<Fact>]
let ``BuildLadder handles known event kind labels without throwing`` () =
    let asOf = DateTimeOffset.UtcNow

    let knownLabels =
        [| "Coupon"; "CouponPayment"; "Dividend"; "DividendPayment"
           "Principal"; "PrincipalRepayment"; "Maturity"; "PrincipalMaturity"
           "Fee"; "FeePayment"; "Commission"; "MarginCall"; "Proceeds"
           "Premium"; "PremiumPayment"; "Redemption"; "RollPrincipal"
           "SomeUnknownLabel" |]

    for label in knownLabels do
        let input =
            { buildInput (asOf.AddDays 1.0) 10m "USD" with EventKindLabel = label }

        // Should not throw
        let result = CashFlowProjector.BuildLadder(asOf, "USD", 7, [| input |])
        result.TotalProjectedInflows |> should equal 10m

// ---------------------------------------------------------------------------
// Ladder totals consistency
// ---------------------------------------------------------------------------

[<Fact>]
let ``BuildLadder total inflows equals sum of bucket inflows`` () =
    let asOf = DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)

    let inputs =
        [|
            buildInput (asOf.AddDays  2.0)  300m "USD"
            buildInput (asOf.AddDays  9.0)  150m "USD"
            buildInput (asOf.AddDays 16.0) -100m "USD"
        |]

    let result = CashFlowProjector.BuildLadder(asOf, "USD", 7, inputs)

    let sumBucketInflows  = result.Buckets |> Array.sumBy (fun b -> b.ProjectedInflows)
    let sumBucketOutflows = result.Buckets |> Array.sumBy (fun b -> b.ProjectedOutflows)

    result.TotalProjectedInflows  |> should equal sumBucketInflows
    result.TotalProjectedOutflows |> should equal sumBucketOutflows
