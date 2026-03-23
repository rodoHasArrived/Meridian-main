module Meridian.FSharp.Tests.DirectLendingInteropTests

open System
open Xunit
open FsUnit.Xunit
open Meridian.Contracts.DirectLending
open Meridian.FSharp.DirectLending.Aggregates
open Meridian.FSharp.DirectLendingInterop

[<Fact>]
let ``Direct lending available to draw never goes below zero`` () =
    DirectLendingInterop.CalculateAvailableToDraw(100m, 130m) |> should equal 0m

[<Fact>]
let ``Direct lending all-in rate applies spread and floor`` () =
    let result = DirectLendingInterop.CalculateAllInRate(0.03m, 150m, Nullable 0.05m, Nullable())

    result |> should equal 0.05m

[<Fact>]
let ``Direct lending daily accrual uses day-count basis`` () =
    let result = DirectLendingInterop.CalculateDailyAccrualAmount(360000m, 0.10m, 0)

    result |> should equal 100m

[<Fact>]
let ``Direct lending principal payment floors at zero`` () =
    let result = DirectLendingInterop.ApplyPrincipalPayment(500m, 700m)

    result |> should equal 0m

let private buildTerms () =
    DirectLendingTermsDto(
        DateOnly(2026, 3, 22),
        DateOnly(2029, 3, 22),
        1_000_000m,
        CurrencyCode.USD,
        RateTypeKind.Fixed,
        Nullable 0.08m,
        null,
        Nullable(),
        Nullable(),
        Nullable(),
        DayCountBasis.Act360,
        PaymentFrequency.Quarterly,
        AmortizationType.InterestOnly,
        Nullable 0.03m,
        Nullable 200m,
        true,
        """{"leverage":"<=4.0x"}""")

let private buildCreateRequest () =
    CreateLoanRequest(
        Nullable(),
        "Aggregate Test Loan",
        BorrowerInfoDto(Guid.NewGuid(), "Borrower", Nullable(Guid.NewGuid())),
        DateOnly(2026, 3, 22),
        buildTerms())

[<Fact>]
let ``Direct lending aggregate create loan builds draft state`` () =
    let loanId = Guid.NewGuid()
    let decision = DirectLendingAggregateInterop.CreateLoan loanId (buildCreateRequest()) DateTimeOffset.UtcNow

    decision.Contract.LoanId |> should equal loanId
    decision.Contract.CurrentTermsVersion |> should equal 1
    decision.Servicing.AvailableToDraw |> should equal 1_000_000m
    decision.Servicing.Status |> should equal LoanStatus.Draft

[<Fact>]
let ``Direct lending aggregate mixed payment allocates fees and principal`` () =
    let createDecision = DirectLendingAggregateInterop.CreateLoan (Guid.NewGuid()) (buildCreateRequest()) DateTimeOffset.UtcNow
    let activated = DirectLendingAggregateInterop.ActivateLoan createDecision.Contract createDecision.Servicing (ActivateLoanRequest(DateOnly(2026, 3, 23)))
    let drawdown = DirectLendingAggregateInterop.BookDrawdown activated.Servicing (BookDrawdownRequest(250_000m, DateOnly(2026, 3, 23), DateOnly(2026, 3, 23), "wire-1"))
    let fee = DirectLendingAggregateInterop.AssessFee drawdown.Servicing (AssessFeeRequest("Origination", 500m, DateOnly(2026, 3, 24), "upfront"))
    let payment =
        DirectLendingAggregateInterop.ApplyMixedPayment
            fee.Servicing
            (ApplyMixedPaymentRequest(
                2_000m,
                DateOnly(2026, 3, 25),
                PaymentBreakdownDto(0m, 0m, 500m, 0m, 1_500m),
                "pay-1"))

    payment.Servicing.Balances.FeesAccruedUnpaid |> should equal 0m
    payment.Servicing.Balances.PrincipalOutstanding |> should equal 248_500m
    payment.Allocations.Length |> should equal 2
