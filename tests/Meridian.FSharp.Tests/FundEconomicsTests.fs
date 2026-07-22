module Meridian.FSharp.Tests.FundEconomicsTests

open Xunit
open FsUnit.Xunit
open Meridian.FSharp.Ledger

// Golden worked examples for the fund-economics accrual kernels. Values are hand-computed so the
// live math is pinned against a fund accountant's expectation, not just its own implementation.

[<Fact>]
let ``Management fee accrues a full year at the annual rate`` () =
    // 10,000,000 net assets × 2% × 365/365 = 200,000.00
    FundEconomics.managementFeeAccrual 10_000_000m 0.02m 365 365
    |> should equal 200_000.00m

[<Fact>]
let ``Management fee is day-weighted over the accrual window`` () =
    // 10,000,000 × 2% × 90/360 = 50,000.00
    FundEconomics.managementFeeAccrual 10_000_000m 0.02m 90 360
    |> should equal 50_000.00m

[<Fact>]
let ``Management fee rounds to the currency scale with banker's rounding`` () =
    // 100,000,000 × 2% × 90/365 = 493,150.6849315... -> 493,150.68
    FundEconomics.managementFeeAccrual 100_000_000m 0.02m 90 365
    |> should equal 493_150.68m

[<Fact>]
let ``Expense accrues straight-line over the accrual window`` () =
    // 120,000 annual × 30/360 = 10,000.00
    FundEconomics.expenseAccrual 120_000m 30 360
    |> should equal 10_000.00m

[<Fact>]
let ``Performance fee charges the rate on profit above the high-water mark net of hurdle`` () =
    // gross profit 2,000,000; less 200,000 hurdle -> 1,800,000 base; × 20% = 360,000.00
    let result = FundEconomics.performanceFeeAccrual 12_000_000m 10_000_000m 200_000m 0.20m true
    result.GrossProfit |> should equal 2_000_000m
    result.FeeBase |> should equal 1_800_000m
    result.PerformanceFee |> should equal 360_000.00m
    result.CrystallizedFee |> should equal 360_000.00m
    // Crystallization advances the high-water mark to the post-fee NAV: 12,000,000 - 360,000.
    result.NewHighWaterMark |> should equal 11_640_000.00m

[<Fact>]
let ``Performance fee is zero below the high-water mark and leaves the mark unchanged`` () =
    let result = FundEconomics.performanceFeeAccrual 9_000_000m 10_000_000m 0m 0.20m true
    result.GrossProfit |> should equal 0m
    result.PerformanceFee |> should equal 0m
    result.NewHighWaterMark |> should equal 10_000_000m

[<Fact>]
let ``Performance fee accrues without crystallizing when crystallize is false`` () =
    // 2,000,000 profit × 20% = 400,000 accrued, but no crystallization -> mark unchanged.
    let result = FundEconomics.performanceFeeAccrual 12_000_000m 10_000_000m 0m 0.20m false
    result.PerformanceFee |> should equal 400_000.00m
    result.CrystallizedFee |> should equal 0m
    result.NewHighWaterMark |> should equal 10_000_000m

[<Fact>]
let ``Interop surfaces the management fee accrual to C# callers`` () =
    LedgerInterop.ManagementFeeAccrual(10_000_000m, 0.02m, 365, 365)
    |> should equal 200_000.00m

[<Fact>]
let ``Interop surfaces the performance fee accrual DTO to C# callers`` () =
    let dto = LedgerInterop.PerformanceFeeAccrual(12_000_000m, 10_000_000m, 200_000m, 0.20m, true)
    dto.PerformanceFee |> should equal 360_000.00m
    dto.NewHighWaterMark |> should equal 11_640_000.00m
