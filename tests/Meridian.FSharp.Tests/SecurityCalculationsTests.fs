/// Unit tests for the Clearwater-aligned security calculations.
/// Worked examples reference the Clearwater Security Types and Calculation Reference Guide,
/// Sections 2.2, 2.3, 4.x, and Appendix B.
module Meridian.FSharp.Tests.SecurityCalculationsTests

open System
open Xunit
open FsUnit.Xunit
open Meridian.FSharp.Domain
open Meridian.FSharp.Calculations

// ---------------------------------------------------------------------------
// Day-count conventions (Section 2.2)
// ---------------------------------------------------------------------------

[<Fact>]
let ``actual360 divides actual days by 360`` () =
    // 2026-01-01 -> 2026-01-31 is 30 days.
    DayCount.actual360 (DateOnly(2026, 1, 1)) (DateOnly(2026, 1, 31))
    |> should equal (30m / 360m)

[<Fact>]
let ``actual365 divides actual days by 365`` () =
    DayCount.actual365 (DateOnly(2026, 1, 1)) (DateOnly(2026, 1, 31))
    |> should equal (30m / 365m)

[<Fact>]
let ``thirty360 treats a half year as exactly one half`` () =
    // 30/360: 2026-01-01 -> 2026-07-01 is six 30-day months = 0.5.
    DayCount.thirty360 (DateOnly(2026, 1, 1)) (DateOnly(2026, 7, 1))
    |> should equal 0.5m

[<Fact>]
let ``actualActualIsda returns zero when end precedes start`` () =
    DayCount.actualActualIsda (DateOnly(2026, 7, 1)) (DateOnly(2026, 1, 1))
    |> should equal 0m

[<Fact>]
let ``actualActualIsda within a single non-leap year divides by 365`` () =
    // 2026 is not a leap year; 2026-01-01 -> 2026-07-01 is 181 days.
    DayCount.actualActualIsda (DateOnly(2026, 1, 1)) (DateOnly(2026, 7, 1))
    |> should equal (181m / 365m)

[<Fact>]
let ``fractionFor dispatches to the matching convention`` () =
    let start = DateOnly(2026, 1, 1)
    let finish = DateOnly(2026, 4, 1)
    DayCount.fractionFor DayCountConvention.Actual360 start finish
    |> should equal (DayCount.actual360 start finish)
    DayCount.fractionFor DayCountConvention.Thirty360 start finish
    |> should equal (DayCount.thirty360 start finish)

// ---------------------------------------------------------------------------
// Interest and accrual (Section 2.2)
// ---------------------------------------------------------------------------

[<Fact>]
let ``interestCashFlow multiplies principal coupon and day-count fraction`` () =
    // 1,000,000 x 5% x 0.5 = 25,000.
    SecurityCalculations.interestCashFlow 1_000_000m 0.05m 0.5m
    |> should equal 25_000m

[<Fact>]
let ``accruedInterest accrues from last coupon to evaluation date`` () =
    // 30/360 half year: 1,000,000 x 5% x 0.5 = 25,000.
    SecurityCalculations.accruedInterest
        1_000_000m 0.05m (DateOnly(2026, 1, 1)) (DateOnly(2026, 7, 1)) DayCountConvention.Thirty360
    |> should equal 25_000m

[<Fact>]
let ``accruedInterest is zero on or before the last coupon date`` () =
    SecurityCalculations.accruedInterest
        1_000_000m 0.05m (DateOnly(2026, 7, 1)) (DateOnly(2026, 1, 1)) DayCountConvention.Thirty360
    |> should equal 0m

// ---------------------------------------------------------------------------
// Market value (Section 2.2)
// ---------------------------------------------------------------------------

[<Fact>]
let ``bondMarketValue applies Par times Factor times Price over 100`` () =
    // Non-factored bond at 99.5.
    SecurityCalculations.bondMarketValue 1_000_000m 1.0m 99.5m
    |> should equal 995_000m

[<Fact>]
let ``bondMarketValue scales by the principal factor for amortising pools`` () =
    // Half the pool outstanding (factor 0.5) at par.
    SecurityCalculations.bondMarketValue 1_000_000m 0.5m 100m
    |> should equal 500_000m

[<Fact>]
let ``equityMarketValue multiplies shares by price`` () =
    SecurityCalculations.equityMarketValue 100m 25.50m
    |> should equal 2_550m

// ---------------------------------------------------------------------------
// Dirty / clean price (Section 2.2)
// ---------------------------------------------------------------------------

[<Fact>]
let ``dirty and clean price are inverse operations`` () =
    let dirty = SecurityCalculations.dirtyPrice 99.5m 0.75m
    dirty |> should equal 100.25m
    SecurityCalculations.cleanPrice dirty 0.75m |> should equal 99.5m

// ---------------------------------------------------------------------------
// Amortization (Section 2.3)
// ---------------------------------------------------------------------------

[<Fact>]
let ``straightLineDailyAmount is negative for a premium bond`` () =
    // Purchased at 105, amortising to 100 over 100 days => -0.05/day.
    SecurityCalculations.straightLineDailyAmount 100m 105m 100
    |> should equal -0.05m

[<Fact>]
let ``straightLineDailyAmount guards against non-positive day counts`` () =
    SecurityCalculations.straightLineDailyAmount 100m 105m 0 |> should equal 0m

[<Fact>]
let ``constantYieldIncome is amortized cost times periodic yield`` () =
    SecurityCalculations.constantYieldIncome 1_000m 0.025m |> should equal 25m

[<Fact>]
let ``amortizationAccretion is effective minus contractual interest`` () =
    // Effective 25 vs contractual 20 => 5 of discount accretion.
    SecurityCalculations.amortizationAccretion 25m 20m |> should equal 5m

// ---------------------------------------------------------------------------
// Structured / prepaying securities (Section 2.2)
// ---------------------------------------------------------------------------

[<Fact>]
let ``weightedAverageLife weights principal by time to payment`` () =
    // (100 x 1 + 100 x 2) / 200 = 1.5 years.
    SecurityCalculations.weightedAverageLife [ (100m, 1m); (100m, 2m) ]
    |> should equal 1.5m

[<Fact>]
let ``weightedAverageLife is zero when there is no principal`` () =
    SecurityCalculations.weightedAverageLife [] |> should equal 0m

// ---------------------------------------------------------------------------
// Inflation-linked securities (Section 4.8)
// ---------------------------------------------------------------------------

[<Fact>]
let ``inflationAdjustedPrincipal scales base units by the factor`` () =
    // Guide example: 1,000 units x 1.2 factor = 1,200.
    SecurityCalculations.inflationAdjustedPrincipal 1_000m 1.2m |> should equal 1_200m

[<Fact>]
let ``inflationLinkedMarketValue matches the Clearwater TIPS example`` () =
    // Guide example: 1,000 units, 1.2 factor, price 105 => 1,260; at par => 1,200.
    SecurityCalculations.inflationLinkedMarketValue 1_000m 1.2m 105m |> should equal 1_260m
    SecurityCalculations.inflationLinkedMarketValue 1_000m 1.2m 100m |> should equal 1_200m

// ---------------------------------------------------------------------------
// Purchased credit impaired (Section 4.14)
// ---------------------------------------------------------------------------

[<Fact>]
let ``pciDailyAmortization is daily total income minus daily interest`` () =
    // Beginning BV 100,000 x 6% = 6,000 / 30 days = 200/day; minus 15 interest = 185.
    SecurityCalculations.pciDailyAmortization 100_000m 0.06m 30 15m |> should equal 185m

[<Fact>]
let ``pciDailyAmortization guards against non-positive periods`` () =
    SecurityCalculations.pciDailyAmortization 100_000m 0.06m 0 15m |> should equal 0m

// ---------------------------------------------------------------------------
// Foreign currency (Section 6.3)
// ---------------------------------------------------------------------------

[<Fact>]
let ``fxRemeasurement multiplies the local amount by the FX rate`` () =
    SecurityCalculations.fxRemeasurement 1_000m 1.10m |> should equal 1_100m

// ---------------------------------------------------------------------------
// Short-term discount instruments (Section 4.3)
// ---------------------------------------------------------------------------

[<Fact>]
let ``shortTermDailyAccretion matches the 98-to-100 over 90-days example`` () =
    // Guide example: (100 - 98) / 90 per day.
    SecurityCalculations.shortTermDailyAccretion 100m 98m 90 |> should equal (2m / 90m)

[<Fact>]
let ``shortTermDailyAccretion guards against non-positive day counts`` () =
    SecurityCalculations.shortTermDailyAccretion 100m 98m 0 |> should equal 0m

// ---------------------------------------------------------------------------
// Repo interest (Section 4.11)
// ---------------------------------------------------------------------------

[<Fact>]
let ``repoInterest is principal times repo rate times day-count fraction`` () =
    // 1,000,000 x 3% x 0.25 = 7,500.
    SecurityCalculations.repoInterest 1_000_000m 0.03m 0.25m |> should equal 7_500m

// ---------------------------------------------------------------------------
// Callable / putable in-the-money tests (Section 4.5)
// ---------------------------------------------------------------------------

[<Fact>]
let ``isCallInTheMoney is true only above the next call price`` () =
    SecurityCalculations.isCallInTheMoney 102m 101m |> should equal true
    SecurityCalculations.isCallInTheMoney 100m 101m |> should equal false

[<Fact>]
let ``isPutInTheMoney is true at or below the next put price`` () =
    SecurityCalculations.isPutInTheMoney 99m 100m |> should equal true
    SecurityCalculations.isPutInTheMoney 100m 100m |> should equal true
    SecurityCalculations.isPutInTheMoney 101m 100m |> should equal false

// ---------------------------------------------------------------------------
// Yield solvers (Section 2.2 / 5.5) — iterative, so assert within tolerance.
// ---------------------------------------------------------------------------

[<Fact>]
let ``purchaseYield solves a single annual cash flow to its discount rate`` () =
    // 100 = 105 / (1 + y) => y = 5%.
    match SecurityCalculations.purchaseYield 100m [ (1.0m, 105.0m) ] 1 with
    | Some y ->
        y |> should be (greaterThan 0.0499m)
        y |> should be (lessThan 0.0501m)
    | None -> failwith "Expected the solver to converge."

[<Fact>]
let ``bookYield recovers the coupon when a par bond discounts to 100`` () =
    // A par bond: two semi-annual 2.5 coupons plus 100 redemption, priced at 100, yields 5%.
    let cashFlows = [ (0.5m, 2.5m); (1.0m, 102.5m) ]
    match SecurityCalculations.bookYield 100m cashFlows 2 with
    | Some y ->
        y |> should be (greaterThan 0.0499m)
        y |> should be (lessThan 0.0501m)
    | None -> failwith "Expected the solver to converge."

[<Fact>]
let ``yield solvers reject empty cash flows and non-positive prices`` () =
    SecurityCalculations.purchaseYield 100m [] 1 |> should equal None
    SecurityCalculations.purchaseYield 0m [ (1.0m, 105.0m) ] 1 |> should equal None
