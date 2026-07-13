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
let ``Direct lending date-aware accrual uses 366 for Act/Act on a leap-year date`` () =
    let result =
        DirectLendingInterop.CalculateDailyAccrualAmountForDate(
            1_000_000m, 0.073m, int DayCountBasis.ActualActualISDA, DateOnly(2024, 2, 29))

    // 1,000,000 * 0.073 / 366 = 199.4535...
    Math.Round(result, 2) |> should equal 199.45m

[<Fact>]
let ``Direct lending date-aware accrual uses 365 for Act/Act on a non-leap-year date`` () =
    let result =
        DirectLendingInterop.CalculateDailyAccrualAmountForDate(
            1_000_000m, 0.073m, int DayCountBasis.ActualActualISDA, DateOnly(2023, 7, 15))

    // 1,000,000 * 0.073 / 365 = 200.00
    Math.Round(result, 2) |> should equal 200.00m

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
            createDecision.Contract.CurrentTerms
            (ApplyMixedPaymentRequest(
                2_000m,
                DateOnly(2026, 3, 25),
                PaymentBreakdownDto(0m, 0m, 500m, 0m, 1_500m),
                "pay-1"))

    payment.Servicing.Balances.FeesAccruedUnpaid |> should equal 0m
    payment.Servicing.Balances.PrincipalOutstanding |> should equal 248_500m
    payment.Allocations.Length |> should equal 2

// ----- PostDailyAccrual day-count behavior -----

let private buildAccrualTerms (dayCountBasis: DayCountBasis) =
    DirectLendingTermsDto(
        DateOnly(2023, 6, 1),
        DateOnly(2028, 6, 1),
        1_000_000m,
        CurrencyCode.USD,
        RateTypeKind.Fixed,
        Nullable 0.073m,
        null,
        Nullable(),
        Nullable(),
        Nullable(),
        dayCountBasis,
        PaymentFrequency.Quarterly,
        AmortizationType.InterestOnly,
        Nullable(),
        Nullable 200m,
        true,
        """{"leverage":"<=4.0x"}""")

let private postDailyAccrualOn (dayCountBasis: DayCountBasis) (accrualDate: DateOnly) =
    let request =
        CreateLoanRequest(
            Nullable(),
            "Accrual Day-Count Test Loan",
            BorrowerInfoDto(Guid.NewGuid(), "Borrower", Nullable(Guid.NewGuid())),
            DateOnly(2023, 6, 1),
            buildAccrualTerms dayCountBasis)
    let created = DirectLendingAggregateInterop.CreateLoan (Guid.NewGuid()) request DateTimeOffset.UtcNow
    let activated = DirectLendingAggregateInterop.ActivateLoan created.Contract created.Servicing (ActivateLoanRequest(DateOnly(2023, 6, 1)))
    let drawn = DirectLendingAggregateInterop.BookDrawdown activated.Servicing (BookDrawdownRequest(1_000_000m, DateOnly(2023, 6, 1), DateOnly(2023, 6, 1), "wire-accrual"))
    DirectLendingAggregateInterop.PostDailyAccrual drawn.Servicing created.Contract.CurrentTerms (PostDailyAccrualRequest(accrualDate))

[<Fact>]
let ``Servicing daily accrual on a leap-year date uses 366 for Act/Act`` () =
    let decision = postDailyAccrualOn DayCountBasis.ActualActualISDA (DateOnly(2024, 2, 29))

    // 1,000,000 * 0.073 / 366 = 199.4535... -> 199.45 (not 200.00 from a hardcoded 365 denominator)
    decision.Entry.InterestAmount |> should equal 199.45m
    decision.Servicing.Balances.InterestAccruedUnpaid |> should equal 199.45m

[<Fact>]
let ``Servicing daily accrual on a non-leap-year date uses 365 for Act/Act`` () =
    let decision = postDailyAccrualOn DayCountBasis.ActualActualISDA (DateOnly(2023, 7, 15))

    // 1,000,000 * 0.073 / 365 = 200.00
    decision.Entry.InterestAmount |> should equal 200.00m
    decision.Servicing.Balances.InterestAccruedUnpaid |> should equal 200.00m

[<Fact>]
let ``Servicing daily accrual keeps 360 denominator for Act/360 on a leap-year date`` () =
    let decision = postDailyAccrualOn DayCountBasis.Act360 (DateOnly(2024, 2, 29))

    // 1,000,000 * 0.073 / 360 = 202.7777... -> 202.78 regardless of leap year
    decision.Entry.InterestAmount |> should equal 202.78m

[<Fact>]
let ``Servicing daily accrual keeps 365 denominator for Act/365F on a leap-year date`` () =
    let decision = postDailyAccrualOn DayCountBasis.Act365F (DateOnly(2024, 2, 29))

    // 1,000,000 * 0.073 / 365 = 200.00 regardless of leap year
    decision.Entry.InterestAmount |> should equal 200.00m

// ----- IsInterestOnlyPeriod -----

[<Fact>]
let ``IsInterestOnlyPeriod returns false when months is zero`` () =
    DirectLendingInterop.IsInterestOnlyPeriod(DateOnly(2026, 1, 1), 0, DateOnly(2026, 3, 1))
    |> should equal false

[<Fact>]
let ``IsInterestOnlyPeriod returns true when inside IO window`` () =
    // 12-month IO window; date is 5 months after origination
    DirectLendingInterop.IsInterestOnlyPeriod(DateOnly(2026, 1, 1), 12, DateOnly(2026, 6, 1))
    |> should equal true

[<Fact>]
let ``IsInterestOnlyPeriod returns false when past IO window`` () =
    // 6-month IO window; date is 8 months after origination
    DirectLendingInterop.IsInterestOnlyPeriod(DateOnly(2026, 1, 1), 6, DateOnly(2026, 9, 1))
    |> should equal false

[<Fact>]
let ``IsInterestOnlyPeriod returns false on IO end boundary`` () =
    // Exact end date is NOT in the window (asOfDate < ioEndDate)
    DirectLendingInterop.IsInterestOnlyPeriod(DateOnly(2026, 1, 1), 6, DateOnly(2026, 7, 1))
    |> should equal false

// ----- IsWithinGracePeriod -----

[<Fact>]
let ``IsWithinGracePeriod returns false when grace period is null`` () =
    DirectLendingInterop.IsWithinGracePeriod(DateOnly(2026, 3, 1), Nullable(), DateOnly(2026, 3, 5))
    |> should equal false

[<Fact>]
let ``IsWithinGracePeriod returns false when grace period is zero`` () =
    DirectLendingInterop.IsWithinGracePeriod(DateOnly(2026, 3, 1), Nullable 0, DateOnly(2026, 3, 2))
    |> should equal false

[<Fact>]
let ``IsWithinGracePeriod returns true when inside grace window`` () =
    // Due 2026-03-01, 10-day grace; check on day 7 (2026-03-08)
    DirectLendingInterop.IsWithinGracePeriod(DateOnly(2026, 3, 1), Nullable 10, DateOnly(2026, 3, 8))
    |> should equal true

[<Fact>]
let ``IsWithinGracePeriod returns true on grace end boundary`` () =
    // asOfDate <= graceEnd (inclusive)
    DirectLendingInterop.IsWithinGracePeriod(DateOnly(2026, 3, 1), Nullable 5, DateOnly(2026, 3, 6))
    |> should equal true

[<Fact>]
let ``IsWithinGracePeriod returns false when past grace window`` () =
    DirectLendingInterop.IsWithinGracePeriod(DateOnly(2026, 3, 1), Nullable 5, DateOnly(2026, 3, 7))
    |> should equal false

// ----- EstimatePrepaymentPenalty -----

[<Fact>]
let ``EstimatePrepaymentPenalty returns null when prepayment not allowed`` () =
    let result = DirectLendingInterop.EstimatePrepaymentPenalty(false, Nullable 0.02m, 500_000m)
    result.HasValue |> should equal false

[<Fact>]
let ``EstimatePrepaymentPenalty returns zero when penalty rate is null`` () =
    let result = DirectLendingInterop.EstimatePrepaymentPenalty(true, Nullable(), 500_000m)
    result |> should equal (Nullable 0m)

[<Fact>]
let ``EstimatePrepaymentPenalty returns zero when penalty rate is zero`` () =
    let result = DirectLendingInterop.EstimatePrepaymentPenalty(true, Nullable 0m, 500_000m)
    result |> should equal (Nullable 0m)

[<Fact>]
let ``EstimatePrepaymentPenalty computes amount correctly`` () =
    // 2 % of 500 000 = 10 000
    let result = DirectLendingInterop.EstimatePrepaymentPenalty(true, Nullable 0.02m, 500_000m)
    result |> should equal (Nullable 10_000m)

// ----- ApplyRateBounds -----

[<Fact>]
let ``ApplyRateBounds passes rate through when no bounds are set`` () =
    DirectLendingInterop.ApplyRateBounds(Nullable(), Nullable(), 0.07m)
    |> should equal 0.07m

[<Fact>]
let ``ApplyRateBounds applies floor when rate is below floor`` () =
    DirectLendingInterop.ApplyRateBounds(Nullable 0.05m, Nullable(), 0.03m)
    |> should equal 0.05m

[<Fact>]
let ``ApplyRateBounds applies cap when rate is above cap`` () =
    DirectLendingInterop.ApplyRateBounds(Nullable(), Nullable 0.08m, 0.12m)
    |> should equal 0.08m

[<Fact>]
let ``ApplyRateBounds passes rate through when within bounds`` () =
    DirectLendingInterop.ApplyRateBounds(Nullable 0.03m, Nullable 0.10m, 0.07m)
    |> should equal 0.07m

[<Fact>]
let ``ApplyRateBounds raises ArgumentException when cap is less than floor`` () =
    (fun () -> DirectLendingInterop.ApplyRateBounds(Nullable 0.10m, Nullable 0.05m, 0.07m) |> ignore)
    |> should throw typeof<System.ArgumentException>
