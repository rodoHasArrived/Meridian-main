module Meridian.FSharp.Tests.DirectLendingPrepaidInterestConsumptionTests

// PRD-115 increment 4: each daily accrual settles the day's cash interest from the prepaid-interest
// balance first (decision 4: uncapped, by daily accruals). Income is unchanged; only the split
// between prepaid interest and the interest receivable moves.

open System
open Xunit
open FsUnit.Xunit
open Meridian.Contracts.DirectLending
open Meridian.FSharp.DirectLending.Aggregates

let private origination = DateOnly(2026, 1, 15)

// 900,000 at 8% Act/360 accrues exactly 200.00 a day.
let private terms () =
    DirectLendingTermsDto(
        origination,
        DateOnly(2029, 1, 15),
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
        Nullable(),
        Nullable(),
        true,
        null)

let private drawnLoan (loanTerms: DirectLendingTermsDto) prepaid =
    let created =
        DirectLendingAggregateInterop.CreateLoan
            (Guid.NewGuid())
            (CreateLoanRequest(Nullable(), "Prepaid Interest Loan", BorrowerInfoDto(Guid.NewGuid(), "Borrower", Nullable()), origination, loanTerms))
            (DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero))
    let activated = DirectLendingAggregateInterop.ActivateLoan created.Contract created.Servicing (ActivateLoanRequest(origination))
    let drawn = DirectLendingAggregateInterop.BookDrawdown activated.Servicing (BookDrawdownRequest(900_000m, origination, origination, "wire"))
    drawn.Servicing |> DirectLendingTestBalances.withExcessBalances prepaid 0m

let private accrue loanTerms (servicing: LoanServicingStateDto) (date: DateOnly) =
    DirectLendingAggregateInterop.PostDailyAccrual servicing loanTerms (PostDailyAccrualRequest(date))

[<Fact>]
let ``Prepaid interest is consumed day by day until exhausted, then interest accrues as a receivable`` () =
    let loanTerms = terms ()
    let mutable servicing = drawnLoan loanTerms 500m
    let schedule = ResizeArray()

    for day in 16 .. 19 do
        let decision = accrue loanTerms servicing (DateOnly(2026, 1, day))
        servicing <- decision.Servicing
        schedule.Add(
            decision.Entry.InterestAmount,
            decision.PrepaidInterestConsumed,
            servicing.Balances.PrepaidInterest,
            servicing.Balances.InterestAccruedUnpaid)

    // (income, consumed, prepaid after, receivable after), computed by hand.
    schedule
    |> Seq.toList
    |> should equal
        [ (200m, 200m, 300m, 0m)
          (200m, 200m, 100m, 0m)
          (200m, 100m, 0m, 100m)
          (200m, 0m, 0m, 300m) ]

[<Fact>]
let ``Without prepaid interest the accrual is unchanged and consumes nothing`` () =
    let loanTerms = terms ()
    let decision = accrue loanTerms (drawnLoan loanTerms 0m) (DateOnly(2026, 1, 16))

    decision.PrepaidInterestConsumed |> should equal 0m
    decision.Servicing.Balances.InterestAccruedUnpaid |> should equal 200m
    decision.Servicing.Balances.PrepaidInterest |> should equal 0m
