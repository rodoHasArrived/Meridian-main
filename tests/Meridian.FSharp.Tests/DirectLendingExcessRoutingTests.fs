module Meridian.FSharp.Tests.DirectLendingExcessRoutingTests

// PRD-115 increment 3: the cash left after a mixed payment's allocation is routed by the loan's
// ExcessPaymentApplication rule. The C# resolver (DirectLendingServiceSupport.ResolveMixedPayment)
// must agree exactly; DirectLendingExcessRoutingParityTests in Meridian.Tests checks that.

open System
open Xunit
open FsUnit.Xunit
open Meridian.Contracts.DirectLending
open Meridian.FSharp.DirectLending.Aggregates

let private origination = DateOnly(2026, 1, 15)
let private paymentDate = DateOnly(2026, 4, 1)

let private termsWith (rule: Nullable<ExcessPaymentApplication>) prepaymentAllowed (penaltyRate: Nullable<decimal>) interestOnlyMonths =
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
        prepaymentAllowed,
        null,
        interestOnlyMonths,
        Nullable(),
        Nullable(),
        Nullable(),
        penaltyRate,
        ExcessPaymentApplication = rule)

/// Active loan with 200k drawn on 2026-02-01 and 300k on 2026-03-01, owing 1,000 of interest.
let private loanOwingInterest (terms: DirectLendingTermsDto) =
    let created =
        DirectLendingAggregateInterop.CreateLoan
            (Guid.NewGuid())
            (CreateLoanRequest(Nullable(), "Excess Routing Loan", BorrowerInfoDto(Guid.NewGuid(), "Borrower", Nullable()), origination, terms))
            (DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero))
    let activated = DirectLendingAggregateInterop.ActivateLoan created.Contract created.Servicing (ActivateLoanRequest(origination))
    let later = DirectLendingAggregateInterop.BookDrawdown activated.Servicing (BookDrawdownRequest(300_000m, DateOnly(2026, 3, 1), DateOnly(2026, 3, 1), "wire-later"))
    let earlier = DirectLendingAggregateInterop.BookDrawdown later.Servicing (BookDrawdownRequest(200_000m, DateOnly(2026, 2, 1), DateOnly(2026, 2, 1), "wire-earlier"))
    let s = earlier.Servicing
    let b = s.Balances
    LoanServicingStateDto(
        s.LoanId, s.Status, s.CurrentCommitment, s.TotalDrawn, s.AvailableToDraw,
        OutstandingBalancesDto(b.PrincipalOutstanding, 1_000m, b.CommitmentFeeAccruedUnpaid, b.FeesAccruedUnpaid, b.PenaltyAccruedUnpaid),
        s.DrawdownLots, s.CurrentRateReset, s.LastAccrualDate, s.LastPaymentDate, s.ServicingRevision, s.RevisionHistory, s.AccrualEntries)

let private pay terms amount (breakdown: PaymentBreakdownDto) =
    DirectLendingAggregateInterop.ApplyMixedPayment
        (loanOwingInterest terms)
        terms
        (ApplyMixedPaymentRequest(amount, paymentDate, breakdown, "pay"))

let private allocationsOf (payment: MixedPaymentDecision) =
    payment.Allocations |> Array.map (fun a -> a.TargetType, a.Amount)

[<Fact>]
let ``Without a rule the excess is held as tracked unapplied cash`` () =
    // Interest-only period: the waterfall leaves principal alone, so 500 is excess.
    let payment = pay (termsWith (Nullable()) true (Nullable 0.02m) 12) 1_500m null

    payment.Resolution.UnappliedAmount |> should equal 500m
    payment.Resolution.ExcessRuleApplied |> should equal (Nullable ExcessPaymentApplication.HoldUnapplied)
    payment.Resolution.ExcessRuleNote |> should equal null
    payment.Servicing.Balances.UnappliedCash |> should equal 500m
    payment.Servicing.Balances.PrincipalOutstanding |> should equal 500_000m
    allocationsOf payment |> should equal [| "Interest", 1_000m; "UnappliedCash", 500m |]

[<Fact>]
let ``Principal prepayment applies the excess to principal earliest lot first and charges the penalty`` () =
    let terms = termsWith (Nullable ExcessPaymentApplication.PrincipalPrepayment) true (Nullable 0.02m) 12
    let payment = pay terms 251_000m null

    payment.Resolution.Breakdown.ToPrincipal |> should equal 0m // interest-only period
    payment.Resolution.ExcessToPrincipal |> should equal 250_000m
    payment.Resolution.UnappliedAmount |> should equal 0m
    payment.Resolution.PrepaymentPenaltyCharged |> should equal 5_000m // 2% of 250,000
    payment.Servicing.Balances.PrincipalOutstanding |> should equal 250_000m
    payment.Servicing.Balances.PenaltyAccruedUnpaid |> should equal 5_000m
    payment.Servicing.TotalDrawn |> should equal 250_000m
    payment.Servicing.DrawdownLots |> Seq.map _.RemainingPrincipal |> Seq.toList |> should equal [ 0m; 250_000m ]
    allocationsOf payment |> should equal [| "Interest", 1_000m; "PrincipalPrepayment", 250_000m |]

[<Fact>]
let ``Principal prepayment above the outstanding principal holds the remainder with a note`` () =
    let terms = termsWith (Nullable ExcessPaymentApplication.PrincipalPrepayment) true (Nullable()) 0
    // Manual: 1,000 interest + 400,000 principal; the other 199,000 is excess, 100,000 of which
    // fits in the remaining principal.
    let payment = pay terms 600_000m (PaymentBreakdownDto(1_000m, 0m, 0m, 0m, 400_000m))

    payment.Resolution.Breakdown.ToPrincipal |> should equal 400_000m
    payment.Resolution.ExcessToPrincipal |> should equal 100_000m
    payment.Resolution.UnappliedAmount |> should equal 99_000m
    payment.Resolution.ExcessRuleNote |> should equal ExcessPaymentRuleNotes.ExceedsOutstandingPrincipal
    payment.Resolution.PrepaymentPenaltyCharged |> should equal 0m // no rate configured
    payment.Servicing.Balances.PrincipalOutstanding |> should equal 0m
    payment.Servicing.Balances.UnappliedCash |> should equal 99_000m
    payment.Allocations |> Array.sumBy _.Amount |> should equal 600_000m

[<Fact>]
let ``Principal prepayment when the terms forbid prepayment holds the excess with a note and no penalty`` () =
    let terms = termsWith (Nullable ExcessPaymentApplication.PrincipalPrepayment) false (Nullable 0.02m) 12
    let payment = pay terms 1_500m null

    payment.Resolution.ExcessToPrincipal |> should equal 0m
    payment.Resolution.UnappliedAmount |> should equal 500m
    payment.Resolution.ExcessRuleNote |> should equal ExcessPaymentRuleNotes.PrepaymentNotAllowed
    payment.Resolution.PrepaymentPenaltyCharged |> should equal 0m
    payment.Servicing.Balances.PrincipalOutstanding |> should equal 500_000m
    payment.Servicing.Balances.UnappliedCash |> should equal 500m

[<Fact>]
let ``Prepaid interest rule credits the whole excess to the prepaid balance`` () =
    let terms = termsWith (Nullable ExcessPaymentApplication.PrepaidInterest) true (Nullable 0.02m) 12
    let payment = pay terms 1_600m (PaymentBreakdownDto(1_000m, 0m, 0m, 0m, 0m))

    payment.Resolution.ExcessToPrepaidInterest |> should equal 600m
    payment.Resolution.UnappliedAmount |> should equal 0m
    payment.Servicing.Balances.PrepaidInterest |> should equal 600m
    payment.Servicing.Balances.UnappliedCash |> should equal 0m
    allocationsOf payment |> should equal [| "Interest", 1_000m; "PrepaidInterest", 600m |]

[<Fact>]
let ``A payment with no excess records no rule and moves neither balance`` () =
    let terms = termsWith (Nullable ExcessPaymentApplication.PrepaidInterest) true (Nullable 0.02m) 0
    let payment = pay terms 1_000m null

    payment.Resolution.ExcessRuleApplied |> should equal (Nullable<ExcessPaymentApplication>())
    (payment.Resolution.ExcessToPrincipal, payment.Resolution.ExcessToPrepaidInterest, payment.Resolution.UnappliedAmount)
    |> should equal (0m, 0m, 0m)
    (payment.Servicing.Balances.PrepaidInterest, payment.Servicing.Balances.UnappliedCash) |> should equal (0m, 0m)
