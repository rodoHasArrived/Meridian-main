module Meridian.FSharp.Tests.ReturnMeasurementTests

open System
open Xunit
open FsUnit.Xunit
open Meridian.FSharp.Ledger

// Golden worked examples for the W10-PERF-001 return kernels. Expected values were computed
// independently (exact decimal arithmetic for time-weighted and Modified Dietz, bisection on the
// NPV for money-weighted), not by running this implementation.

let private d (y: int) (m: int) (day: int) = DateOnly(y, m, day)

let private point date value =
    { AsOf = date; Value = value; Currency = "USD"; Basis = ReturnInputBasis.Posted }

let private flow date amount =
    { Date = date; Amount = amount; Currency = "USD"; Basis = ReturnInputBasis.Posted }

let private valueOf (figure: ReturnFigure) =
    figure.Value.HasValue |> should equal true
    figure.Value.Value

// Q3 scenario: 1,000,000 on 1 Jul; a 200,000 subscription on 15 Aug lifts the value to
// 1,230,000; 1,242,300 at 30 Sep. Performance is +3% then +1%; the subscription is not return.
let private q3Points =
    [ point (d 2026 7 1) 1_000_000m
      point (d 2026 8 15) 1_230_000m
      point (d 2026 9 30) 1_242_300m ]

let private q3Flows = [ flow (d 2026 8 15) 200_000m ]

[<Fact>]
let ``Time-weighted return breaks at the external flow and excludes it from performance`` () =
    // 1,030,000 / 1,000,000 × 1,242,300 / 1,230,000 − 1 = 1.03 × 1.01 − 1 = 0.0403
    let result = ReturnMeasurement.timeWeighted "USD" q3Points q3Flows
    result.Convention |> should equal ReturnConvention.TimeWeighted
    result.Status |> should equal ReturnFigureStatus.Reported
    valueOf result |> should equal 0.0403000000m
    result.SubPeriodCount |> should equal 2
    result.Reasons |> should be Empty
    result.PeriodStart |> should equal (d 2026 7 1)
    result.PeriodEnd |> should equal (d 2026 9 30)
    result.Currency |> should equal "USD"

[<Fact>]
let ``Time-weighted return handles a withdrawal at a valuation boundary`` () =
    // (420,000 + 100,000) / 500,000 × 441,000 / 420,000 − 1 = 1.04 × 1.05 − 1 = 0.092
    let points = [ point (d 2026 1 1) 500_000m; point (d 2026 3 31) 420_000m; point (d 2026 6 30) 441_000m ]
    let result = ReturnMeasurement.timeWeighted "USD" points [ flow (d 2026 3 31) -100_000m ]
    valueOf result |> should equal 0.0920000000m
    result.Status |> should equal ReturnFigureStatus.Reported

[<Fact>]
let ``Missing valuation at a flow date is labeled as a Modified Dietz approximation`` () =
    // 91-day period, flow weight 46/91: 42,300 / (1,000,000 + 200,000 × 46/91) = 0.0384161677
    let points = [ point (d 2026 7 1) 1_000_000m; point (d 2026 9 30) 1_242_300m ]
    let result = ReturnMeasurement.timeWeighted "USD" points q3Flows
    result.Convention |> should equal ReturnConvention.LinkedModifiedDietz
    result.Status |> should equal ReturnFigureStatus.Reported
    valueOf result |> should equal 0.0384161677m
    result.Reasons |> Array.exists (fun r -> r.Contains "Approximation" && r.Contains "2026-08-15") |> should equal true

[<Fact>]
let ``Single-period Modified Dietz matches the hand-computed figure`` () =
    let result = ReturnMeasurement.modifiedDietzReturn "USD" q3Points q3Flows
    result.Convention |> should equal ReturnConvention.ModifiedDietz
    valueOf result |> should equal 0.0384161677m

[<Fact>]
let ``A flow dated on the opening valuation is already in the opening value and is ignored`` () =
    let points = [ point (d 2026 7 1) 1_000_000m; point (d 2026 9 30) 1_010_000m ]
    let result = ReturnMeasurement.timeWeighted "USD" points [ flow (d 2026 7 1) 1_000_000m ]
    valueOf result |> should equal 0.0100000000m
    result.Convention |> should equal ReturnConvention.TimeWeighted

[<Fact>]
let ``Unposted activity makes the figure pro-forma rather than reported`` () =
    let flows = [ { flow (d 2026 8 15) 200_000m with Basis = ReturnInputBasis.ProForma } ]
    let result = ReturnMeasurement.timeWeighted "USD" q3Points flows
    result.Status |> should equal ReturnFigureStatus.ProForma
    valueOf result |> should equal 0.0403000000m
    result.Reasons |> Array.exists (fun r -> r.Contains "Pro-forma") |> should equal true

[<Fact>]
let ``Fewer than two valuation points is review-required with no number`` () =
    let result = ReturnMeasurement.timeWeighted "USD" [ point (d 2026 7 1) 1_000_000m ] []
    result.Status |> should equal ReturnFigureStatus.ReviewRequired
    result.Value.HasValue |> should equal false

[<Fact>]
let ``A non-positive opening value is review-required rather than infinite`` () =
    let points = [ point (d 2026 7 1) 0m; point (d 2026 9 30) 100m ]
    let result = ReturnMeasurement.timeWeighted "USD" points []
    result.Status |> should equal ReturnFigureStatus.ReviewRequired
    result.Value.HasValue |> should equal false

[<Fact>]
let ``Inputs in another currency are never summed and block the figure`` () =
    let flows = [ { flow (d 2026 8 15) 200_000m with Currency = "EUR" } ]
    let result = ReturnMeasurement.timeWeighted "USD" q3Points flows
    result.Status |> should equal ReturnFigureStatus.ReviewRequired
    result.Value.HasValue |> should equal false
    result.Reasons |> Array.exists (fun r -> r.Contains "EUR" && r.Contains "2026-08-15") |> should equal true

[<Fact>]
let ``Translation at the flow's as-of rate admits the figure; a missing rate still blocks`` () =
    let eurFlow = [ { flow (d 2026 8 15) 160_000m with Currency = "EUR" } ]
    let rate = Func<string, DateOnly, Nullable<decimal>>(fun ccy date ->
        if ccy = "EUR" && date = d 2026 8 15 then Nullable 1.25m else Nullable())

    let translated = ReturnMeasurement.translateFlows "USD" rate eurFlow
    translated[0].Amount |> should equal 200_000m
    translated[0].Currency |> should equal "USD"
    valueOf (ReturnMeasurement.timeWeighted "USD" q3Points translated) |> should equal 0.0403000000m

    let noRate = Func<string, DateOnly, Nullable<decimal>>(fun _ _ -> Nullable())
    let untranslated = ReturnMeasurement.translateFlows "USD" noRate eurFlow
    (ReturnMeasurement.timeWeighted "USD" q3Points untranslated).Status
    |> should equal ReturnFigureStatus.ReviewRequired

[<Fact>]
let ``Money-weighted return of a one-year 10 percent gain`` () =
    let points = [ point (d 2025 1 1) 1_000m; point (d 2026 1 1) 1_100m ]
    let result = ReturnMeasurement.moneyWeighted "USD" points []
    result.Convention |> should equal ReturnConvention.MoneyWeighted
    result.Status |> should equal ReturnFigureStatus.Reported
    float result.AnnualizedValue.Value |> should (equalWithin 1e-9) 0.10
    float result.Value.Value |> should (equalWithin 1e-9) 0.10

[<Fact>]
let ``Money-weighted return honors the timing of a mid-year contribution`` () =
    // -1,000,000 on 1 Jan, -500,000 on 1 Jul, +1,600,000 on 31 Dec (Actual/365):
    // annualized 0.0804503618; over the 364-day period 0.0802213366.
    let points = [ point (d 2026 1 1) 1_000_000m; point (d 2026 12 31) 1_600_000m ]
    let result = ReturnMeasurement.moneyWeighted "USD" points [ flow (d 2026 7 1) 500_000m ]
    result.Status |> should equal ReturnFigureStatus.Reported
    float result.AnnualizedValue.Value |> should (equalWithin 1e-9) 0.0804503618
    float result.Value.Value |> should (equalWithin 1e-9) 0.0802213366
    result.Reasons |> should be Empty

[<Fact>]
let ``A series admitting two rates is review-required and both candidates are returned`` () =
    // Investor side: -100, +230, -132 at yearly intervals — NPV roots at exactly 10% and 20%.
    let points = [ point (d 2025 1 1) 100m; point (d 2027 1 1) 0m ]
    let flows = [ flow (d 2026 1 1) -230m; flow (d 2027 1 1) 132m ]
    let result = ReturnMeasurement.moneyWeighted "USD" points flows
    result.Status |> should equal ReturnFigureStatus.ReviewRequired
    result.Value.HasValue |> should equal false
    result.CandidateRates |> should haveLength 2
    float result.CandidateRates[0] |> should (equalWithin 1e-8) 0.10
    float result.CandidateRates[1] |> should (equalWithin 1e-8) 0.20

[<Fact>]
let ``A series with no rate is review-required as non-convergent`` () =
    // Paid in 100, nothing ever returned: no rate above -99% zeroes the NPV.
    let points = [ point (d 2025 1 1) 100m; point (d 2026 1 1) 0m ]
    let result = ReturnMeasurement.moneyWeighted "USD" points []
    result.Status |> should equal ReturnFigureStatus.ReviewRequired
    result.Value.HasValue |> should equal false
    result.Reasons |> Array.exists (fun r -> r.Contains "did not converge") |> should equal true
