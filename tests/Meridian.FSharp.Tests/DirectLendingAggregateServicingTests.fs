module Meridian.FSharp.Tests.DirectLendingAggregateServicingTests

// Servicing-state transitions of the direct-lending aggregates through the public
// DirectLendingAggregateInterop surface: terms versioning, lot-level principal reduction,
// payment waterfalls, write-offs, rate resets, and prepayment penalties.

open System
open Xunit
open FsUnit.Xunit
open Meridian.Contracts.DirectLending
open Meridian.FSharp.DirectLending.Aggregates

let private origination = DateOnly(2026, 1, 15)

let private termsWithCommitment
    (commitment: decimal)
    (rateType: RateTypeKind)
    (interestOnlyMonths: int)
    (prepaymentAllowed: bool)
    (prepaymentPenaltyRate: Nullable<decimal>) =
    DirectLendingTermsDto(
        origination,
        DateOnly(2029, 1, 15),
        commitment,
        CurrencyCode.USD,
        rateType,
        (if rateType = RateTypeKind.Fixed then Nullable 0.08m else Nullable()),
        (if rateType = RateTypeKind.Fixed then null else "SOFR"),
        Nullable 250m,
        Nullable 0.04m,
        Nullable 0.12m,
        DayCountBasis.Act360,
        PaymentFrequency.Quarterly,
        AmortizationType.InterestOnly,
        Nullable 0.005m,
        Nullable 200m,
        prepaymentAllowed,
        null,
        interestOnlyMonths,
        Nullable(),
        Nullable(),
        Nullable(),
        prepaymentPenaltyRate)

let private terms rateType interestOnlyMonths prepaymentAllowed prepaymentPenaltyRate =
    termsWithCommitment 1_000_000m rateType interestOnlyMonths prepaymentAllowed prepaymentPenaltyRate

let private fixedTerms () = terms RateTypeKind.Fixed 0 true (Nullable 0.02m)

let private fixedTermsWithCommitment commitment =
    termsWithCommitment commitment RateTypeKind.Fixed 0 true (Nullable 0.02m)

let private createLoan (loanTerms: DirectLendingTermsDto) =
    DirectLendingAggregateInterop.CreateLoan
        (Guid.NewGuid())
        (CreateLoanRequest(
            Nullable(),
            "  Servicing Aggregate Loan  ",
            BorrowerInfoDto(Guid.NewGuid(), "Borrower", Nullable(Guid.NewGuid())),
            origination,
            loanTerms))
        (DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero))

/// Active loan with two drawdown lots booked out of chronological order:
/// 300k on 2026-03-01 is booked first, then 200k on 2026-02-01.
let private loanWithTwoLots (loanTerms: DirectLendingTermsDto) =
    let created = createLoan loanTerms
    let activated =
        DirectLendingAggregateInterop.ActivateLoan created.Contract created.Servicing (ActivateLoanRequest(origination))
    let later =
        DirectLendingAggregateInterop.BookDrawdown
            activated.Servicing
            (BookDrawdownRequest(300_000m, DateOnly(2026, 3, 1), DateOnly(2026, 3, 3), "wire-later"))
    let earlier =
        DirectLendingAggregateInterop.BookDrawdown
            later.Servicing
            (BookDrawdownRequest(200_000m, DateOnly(2026, 2, 1), DateOnly(2026, 2, 3), "wire-earlier"))
    created.Contract, earlier.Servicing

let private withBalances interest commitmentFee fees penalty (servicing: LoanServicingStateDto) =
    LoanServicingStateDto(
        servicing.LoanId,
        servicing.Status,
        servicing.CurrentCommitment,
        servicing.TotalDrawn,
        servicing.AvailableToDraw,
        OutstandingBalancesDto(
            servicing.Balances.PrincipalOutstanding, interest, commitmentFee, fees, penalty,
            UnappliedCash = servicing.Balances.UnappliedCash,
            PrepaidInterest = servicing.Balances.PrepaidInterest,
            PrepaidInterestLots = servicing.Balances.PrepaidInterestLots),
        servicing.DrawdownLots,
        servicing.CurrentRateReset,
        servicing.LastAccrualDate,
        servicing.LastPaymentDate,
        servicing.ServicingRevision,
        servicing.RevisionHistory,
        servicing.AccrualEntries)

// ── Contract lifecycle ──────────────────────────────────────────────────────

[<Fact>]
let ``CreateLoan trims the facility name and opens a single hashed terms version`` () =
    let decision = createLoan (fixedTerms ())

    decision.Contract.FacilityName |> should equal "Servicing Aggregate Loan"
    decision.Contract.Status |> should equal LoanStatus.Draft
    decision.Contract.TermsVersions.Count |> should equal 1
    decision.TermsVersion.VersionNumber |> should equal 1
    decision.TermsVersion.SourceAction |> should equal "LoanCreated"
    decision.TermsVersion.TermsHash.Length |> should equal 64
    decision.Servicing.TotalDrawn |> should equal 0m
    decision.Servicing.ServicingRevision |> should equal 0L

[<Fact>]
let ``Identical terms hash identically and any term change alters the hash`` () =
    let a = createLoan (fixedTerms ())
    let b = createLoan (fixedTerms ())
    let changed = createLoan (fixedTermsWithCommitment 1_000_001m)

    a.TermsVersion.TermsHash |> should equal b.TermsVersion.TermsHash
    changed.TermsVersion.TermsHash |> should not' (equal a.TermsVersion.TermsHash)

[<Fact>]
let ``AmendTerms appends a newest-first version and re-derives availability from the new commitment`` () =
    let contract, servicing = loanWithTwoLots (fixedTerms ())
    let reduced = fixedTermsWithCommitment 600_000m

    let amended =
        DirectLendingAggregateInterop.AmendTerms
            contract
            servicing
            (AmendLoanTermsRequest(reduced, "Commitment step-down"))
            DateTimeOffset.UtcNow

    amended.Contract.CurrentTermsVersion |> should equal 2
    amended.Contract.TermsVersions |> Seq.map _.VersionNumber |> Seq.toList |> should equal [ 2; 1 ]
    amended.TermsVersion.SourceAction |> should equal "TermsAmended"
    amended.TermsVersion.AmendmentReason |> should equal "Commitment step-down"
    amended.Servicing.CurrentCommitment |> should equal 600_000m
    amended.Servicing.TotalDrawn |> should equal 500_000m
    amended.Servicing.AvailableToDraw |> should equal 100_000m
    amended.Contract.Status |> should equal contract.Status

[<Fact>]
let ``AmendTerms below the drawn amount floors availability at zero`` () =
    let contract, servicing = loanWithTwoLots (fixedTerms ())

    let amended =
        DirectLendingAggregateInterop.AmendTerms
            contract
            servicing
            (AmendLoanTermsRequest(fixedTermsWithCommitment 400_000m, "Cut"))
            DateTimeOffset.UtcNow

    amended.Servicing.AvailableToDraw |> should equal 0m

[<Fact>]
let ``ActivateLoan moves contract and servicing to Active together`` () =
    let created = createLoan (fixedTerms ())

    let activated =
        DirectLendingAggregateInterop.ActivateLoan
            created.Contract
            created.Servicing
            (ActivateLoanRequest(DateOnly(2026, 1, 20)))

    activated.Contract.Status |> should equal LoanStatus.Active
    activated.Servicing.Status |> should equal LoanStatus.Active
    activated.Contract.ActivationDate |> should equal (Nullable(DateOnly(2026, 1, 20)))

// ── Drawdowns and principal reduction ───────────────────────────────────────

[<Fact>]
let ``BookDrawdown orders lots by drawdown date and records a revision per event`` () =
    let _, servicing = loanWithTwoLots (fixedTerms ())

    servicing.DrawdownLots |> Seq.map _.ExternalRef |> Seq.toList |> should equal [ "wire-earlier"; "wire-later" ]
    servicing.TotalDrawn |> should equal 500_000m
    servicing.AvailableToDraw |> should equal 500_000m
    servicing.Balances.PrincipalOutstanding |> should equal 500_000m
    servicing.ServicingRevision |> should equal 2L
    servicing.RevisionHistory |> Seq.map _.RevisionNumber |> Seq.toList |> should equal [ 2L; 1L ]

[<Fact>]
let ``Principal payment reduces the earliest lot first then spills into the next`` () =
    let _, servicing = loanWithTwoLots (fixedTerms ())

    let payment =
        DirectLendingAggregateInterop.ApplyPrincipalPayment
            servicing
            (ApplyPrincipalPaymentRequest(250_000m, DateOnly(2026, 4, 1), "pay-1"))

    payment.AppliedAmount |> should equal 250_000m
    payment.Servicing.DrawdownLots |> Seq.map _.RemainingPrincipal |> Seq.toList |> should equal [ 0m; 250_000m ]
    payment.Servicing.Balances.PrincipalOutstanding |> should equal 250_000m
    payment.Servicing.TotalDrawn |> should equal 250_000m
    payment.Servicing.AvailableToDraw |> should equal 750_000m
    payment.Servicing.LastPaymentDate |> should equal (Nullable(DateOnly(2026, 4, 1)))
    payment.Servicing.ServicingRevision |> should equal (servicing.ServicingRevision + 1L)

[<Fact>]
let ``Principal payment above the outstanding balance is capped and never goes negative`` () =
    let _, servicing = loanWithTwoLots (fixedTerms ())

    let payment =
        DirectLendingAggregateInterop.ApplyPrincipalPayment
            servicing
            (ApplyPrincipalPaymentRequest(900_000m, DateOnly(2026, 4, 1), "pay-all"))

    payment.AppliedAmount |> should equal 500_000m
    payment.Servicing.Balances.PrincipalOutstanding |> should equal 0m
    payment.Servicing.DrawdownLots |> Seq.forall (fun lot -> lot.RemainingPrincipal = 0m) |> should equal true
    payment.Servicing.AvailableToDraw |> should equal 1_000_000m

[<Fact>]
let ``Write-off is capped at principal, leaves receivables and payment date untouched, and records the reason`` () =
    let _, servicing = loanWithTwoLots (fixedTerms ())
    let withInterest = servicing |> withBalances 1_234m 0m 0m 0m

    let writeOff =
        DirectLendingAggregateInterop.ApplyWriteOff
            withInterest
            (ApplyWriteOffRequest(750_000m, DateOnly(2026, 6, 30), "Borrower insolvency"))

    writeOff.AppliedAmount |> should equal 500_000m
    writeOff.Servicing.Balances.PrincipalOutstanding |> should equal 0m
    writeOff.Servicing.Balances.InterestAccruedUnpaid |> should equal 1_234m
    writeOff.Servicing.LastPaymentDate |> should equal withInterest.LastPaymentDate
    writeOff.Servicing.RevisionHistory[0].Notes |> should equal "Borrower insolvency"

// ── Mixed-payment waterfalls ────────────────────────────────────────────────

[<Fact>]
let ``Automatic waterfall pays interest, commitment fee, fees, penalty, then principal`` () =
    let contract, servicing = loanWithTwoLots (fixedTerms ())
    let owing = servicing |> withBalances 1_000m 200m 300m 400m

    let payment =
        DirectLendingAggregateInterop.ApplyMixedPayment
            owing
            contract.CurrentTerms
            (ApplyMixedPaymentRequest(2_400m, DateOnly(2026, 4, 1), null, "pay-auto"))

    let breakdown = payment.Resolution.Breakdown
    (breakdown.ToInterest, breakdown.ToCommitmentFee, breakdown.ToFees, breakdown.ToPenalty, breakdown.ToPrincipal)
    |> should equal (1_000m, 200m, 300m, 400m, 500m)
    payment.Resolution.ResolutionBasis |> should equal "WaterfallAuto"
    payment.Resolution.UnappliedAmount |> should equal 0m
    payment.Allocations |> Array.map _.TargetType |> should equal [| "Interest"; "CommitmentFee"; "Fees"; "Penalty"; "Principal" |]
    payment.Allocations |> Array.map _.SequenceNumber |> should equal [| 1; 2; 3; 4; 5 |]
    payment.Servicing.Balances.PrincipalOutstanding |> should equal 499_500m
    payment.Servicing.Balances.InterestAccruedUnpaid |> should equal 0m

[<Fact>]
let ``Automatic waterfall stops when cash runs out and omits zero allocations`` () =
    let contract, servicing = loanWithTwoLots (fixedTerms ())
    let owing = servicing |> withBalances 1_000m 200m 300m 400m

    let payment =
        DirectLendingAggregateInterop.ApplyMixedPayment
            owing
            contract.CurrentTerms
            (ApplyMixedPaymentRequest(1_100m, DateOnly(2026, 4, 1), null, "pay-short"))

    let breakdown = payment.Resolution.Breakdown
    (breakdown.ToInterest, breakdown.ToCommitmentFee, breakdown.ToFees, breakdown.ToPenalty, breakdown.ToPrincipal)
    |> should equal (1_000m, 100m, 0m, 0m, 0m)
    payment.Allocations |> Array.map _.TargetType |> should equal [| "Interest"; "CommitmentFee" |]
    payment.Servicing.Balances.CommitmentFeeAccruedUnpaid |> should equal 100m
    payment.Servicing.Balances.PrincipalOutstanding |> should equal 500_000m

[<Fact>]
let ``Automatic waterfall during the interest-only period leaves principal untouched and reports unapplied cash`` () =
    let ioTerms = terms RateTypeKind.Fixed 12 true (Nullable())
    let contract, servicing = loanWithTwoLots ioTerms
    let owing = servicing |> withBalances 1_000m 0m 0m 0m

    let payment =
        DirectLendingAggregateInterop.ApplyMixedPayment
            owing
            contract.CurrentTerms
            (ApplyMixedPaymentRequest(5_000m, DateOnly(2026, 6, 1), null, "pay-io"))

    payment.Resolution.Breakdown.ToInterest |> should equal 1_000m
    payment.Resolution.Breakdown.ToPrincipal |> should equal 0m
    payment.Resolution.UnappliedAmount |> should equal 4_000m
    payment.Servicing.Balances.UnappliedCash |> should equal 4_000m
    payment.Allocations |> Array.sumBy _.Amount |> should equal 5_000m
    payment.Servicing.Balances.PrincipalOutstanding |> should equal 500_000m
    payment.Servicing.DrawdownLots |> Seq.sumBy _.RemainingPrincipal |> should equal 500_000m

[<Fact>]
let ``Manual breakdown is honoured in waterfall order and caps at the cash received`` () =
    let contract, servicing = loanWithTwoLots (fixedTerms ())
    let owing = servicing |> withBalances 1_000m 0m 500m 0m

    let payment =
        DirectLendingAggregateInterop.ApplyMixedPayment
            owing
            contract.CurrentTerms
            (ApplyMixedPaymentRequest(1_200m, DateOnly(2026, 4, 1), PaymentBreakdownDto(1_000m, 0m, 500m, 0m, 0m), "pay-manual"))

    payment.Resolution.ResolutionBasis |> should equal "Manual"
    payment.Resolution.Breakdown.ToInterest |> should equal 1_000m
    payment.Resolution.Breakdown.ToFees |> should equal 200m
    payment.Resolution.UnappliedAmount |> should equal 0m
    payment.Servicing.Balances.FeesAccruedUnpaid |> should equal 300m
    payment.Allocations |> Array.forall (fun a -> a.AllocationRule = "direct-lending-cash-v1") |> should equal true

[<Fact>]
let ``Manual breakdown smaller than the cash received leaves the excess unapplied`` () =
    let contract, servicing = loanWithTwoLots (fixedTerms ())

    let payment =
        DirectLendingAggregateInterop.ApplyMixedPayment
            servicing
            contract.CurrentTerms
            (ApplyMixedPaymentRequest(10_000m, DateOnly(2026, 4, 1), PaymentBreakdownDto(0m, 0m, 0m, 0m, 6_000m), "pay-partial"))

    payment.Resolution.Breakdown.ToPrincipal |> should equal 6_000m
    payment.Resolution.UnappliedAmount |> should equal 4_000m
    payment.Servicing.Balances.UnappliedCash |> should equal 4_000m
    payment.Allocations |> Array.sumBy _.Amount |> should equal 10_000m
    payment.Servicing.Balances.PrincipalOutstanding |> should equal 494_000m
    payment.Servicing.DrawdownLots |> Seq.map _.RemainingPrincipal |> Seq.toList |> should equal [ 194_000m; 300_000m ]

// PRD-115: receipt conservation and retained contractual excess cash.

let private cashPolicyTermsWithCurrency currency policy prepaymentAllowed interestOnlyMonths penaltyRate =
    DirectLendingTermsDto(
        origination,
        DateOnly(2029, 1, 15),
        1_000_000m,
        currency,
        RateTypeKind.Fixed,
        Nullable 0.08m,
        null,
        Nullable(),
        Nullable(),
        Nullable(),
        DayCountBasis.Act360,
        PaymentFrequency.Quarterly,
        AmortizationType.InterestOnly,
        Nullable 0.005m,
        Nullable(),
        prepaymentAllowed,
        null,
        InterestOnlyMonths = interestOnlyMonths,
        PrepaymentPenaltyRate = penaltyRate,
        ExcessPaymentPolicy = policy)

let private cashPolicyTerms policy prepaymentAllowed interestOnlyMonths penaltyRate =
    cashPolicyTermsWithCurrency CurrencyCode.USD policy prepaymentAllowed interestOnlyMonths penaltyRate

let private cashPolicy treatment =
    DirectLendingExcessPaymentPolicyDto(1, treatment, "synthetic-contract:excess-payment-clause")

let private prepaidPolicy application =
    DirectLendingExcessPaymentPolicyDto(
        1, ExcessPaymentTreatment.PrepaidInterest, "synthetic-contract:future-interest-clause",
        Application = application)

let private assertConserved amount (payment: MixedPaymentDecision) =
    let resolution = payment.Resolution
    (resolution.Breakdown.TotalAllocated + resolution.UnappliedAmount + resolution.PrepaidInterestAmount)
    |> should equal amount
    payment.Allocations |> Array.sumBy _.Amount |> should equal amount
    payment.Allocations |> Array.forall (fun allocation -> allocation.Amount > 0m) |> should equal true
    resolution.CashAccountingVersion |> should equal "direct-lending-cash-v1"

[<Fact>]
let ``Manual receipt caps every owed bucket and retains the unallocated cash`` () =
    let contract, servicing = loanWithTwoLots (fixedTerms ())
    let owing = servicing |> withBalances 10m 20m 30m 40m
    let payment =
        DirectLendingAggregateInterop.ApplyMixedPayment
            owing
            contract.CurrentTerms
            (ApplyMixedPaymentRequest(1_000m, DateOnly(2026, 4, 1), PaymentBreakdownDto(1_000m, 1_000m, 1_000m, 1_000m, 0m), "cap-all"))

    let breakdown = payment.Resolution.Breakdown
    (breakdown.ToInterest, breakdown.ToCommitmentFee, breakdown.ToFees, breakdown.ToPenalty)
    |> should equal (10m, 20m, 30m, 40m)
    payment.Resolution.UnappliedAmount |> should equal 900m
    payment.Servicing.Balances.UnappliedCash |> should equal 900m
    payment.Servicing.Balances.PrepaidInterest |> should equal 0m
    payment.Servicing.Balances.PrincipalOutstanding |> should equal 500_000m
    payment.Allocations |> Array.map _.TargetType
    |> should equal [| "Interest"; "CommitmentFee"; "Fees"; "Penalty"; "UnappliedCash" |]
    assertConserved 1_000m payment

[<Fact>]
let ``Explicit permitted prepayment applies residual during an interest-only period and caps principal`` () =
    let loanTerms = cashPolicyTerms (cashPolicy ExcessPaymentTreatment.PrincipalPrepayment) true 12 (Nullable())
    let _, servicing = loanWithTwoLots loanTerms
    let owing = servicing |> withBalances 10m 0m 0m 0m
    let payment =
        DirectLendingAggregateInterop.ApplyMixedPayment
            owing
            loanTerms
            (ApplyMixedPaymentRequest(500_050m, DateOnly(2026, 4, 1), null, "prepay-all"))

    payment.Resolution.Breakdown.ToInterest |> should equal 10m
    payment.Resolution.Breakdown.ToPrincipal |> should equal 500_000m
    payment.Resolution.UnappliedAmount |> should equal 40m
    payment.Servicing.Balances.UnappliedCash |> should equal 40m
    payment.Servicing.Balances.PrincipalOutstanding |> should equal 0m
    payment.Servicing.DrawdownLots |> Seq.sumBy _.RemainingPrincipal |> should equal 0m
    assertConserved 500_050m payment

[<Theory>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``Prepayment-prohibited loans retain premature principal requests as unapplied cash`` (manual: bool) =
    let loanTerms = cashPolicyTerms (cashPolicy ExcessPaymentTreatment.HoldUnappliedCash) false 0 (Nullable())
    let _, servicing = loanWithTwoLots loanTerms
    let breakdown = if manual then PaymentBreakdownDto(0m, 0m, 0m, 0m, 100m) else null
    let payment =
        DirectLendingAggregateInterop.ApplyMixedPayment
            servicing
            loanTerms
            (ApplyMixedPaymentRequest(100m, DateOnly(2026, 4, 1), breakdown, "prohibited"))

    payment.Resolution.Breakdown.ToPrincipal |> should equal 0m
    payment.Servicing.Balances.PrincipalOutstanding |> should equal 500_000m
    payment.Servicing.Balances.UnappliedCash |> should equal 100m
    assertConserved 100m payment

[<Fact>]
let ``Prohibited prepayment does not prohibit repayment at contractual maturity`` () =
    let loanTerms = cashPolicyTerms (cashPolicy ExcessPaymentTreatment.HoldUnappliedCash) false 0 (Nullable())
    let _, servicing = loanWithTwoLots loanTerms
    let payment =
        DirectLendingAggregateInterop.ApplyMixedPayment
            servicing
            loanTerms
            (ApplyMixedPaymentRequest(500_100m, loanTerms.MaturityDate, null, "maturity"))

    payment.Resolution.Breakdown.ToPrincipal |> should equal 500_000m
    payment.Servicing.Balances.PrincipalOutstanding |> should equal 0m
    payment.Servicing.Balances.UnappliedCash |> should equal 100m
    assertConserved 500_100m payment

[<Fact>]
let ``Prepaid-interest policy retains future interest separately from interest owed`` () =
    let loanTerms = cashPolicyTerms (cashPolicy ExcessPaymentTreatment.PrepaidInterest) true 12 (Nullable())
    let _, servicing = loanWithTwoLots loanTerms
    let owing = servicing |> withBalances 10m 0m 0m 0m
    let payment =
        DirectLendingAggregateInterop.ApplyMixedPayment
            owing
            loanTerms
            (ApplyMixedPaymentRequest(100m, DateOnly(2026, 4, 1), null, "prepaid"))

    payment.Resolution.Breakdown.ToInterest |> should equal 10m
    payment.Resolution.Breakdown.ToPrincipal |> should equal 0m
    payment.Resolution.PrepaidInterestAmount |> should equal 90m
    payment.Resolution.UnappliedAmount |> should equal 0m
    payment.Servicing.Balances.PrepaidInterest |> should equal 90m
    payment.Servicing.Balances.InterestAccruedUnpaid |> should equal 0m
    payment.Servicing.Balances.PrincipalOutstanding |> should equal 500_000m
    payment.Allocations |> Array.map _.TargetType |> should equal [| "Interest"; "PrepaidInterest" |]
    assertConserved 100m payment

[<Fact>]
let ``Invalid or unsupported excess-payment elections are rejected on both create and amend`` () =
    let valid = cashPolicyTerms (cashPolicy ExcessPaymentTreatment.HoldUnappliedCash) true 0 (Nullable())
    let contract, servicing = loanWithTwoLots valid
    let invalidTerms =
        [ cashPolicyTerms (DirectLendingExcessPaymentPolicyDto(2, ExcessPaymentTreatment.HoldUnappliedCash, "contract")) true 0 (Nullable())
          cashPolicyTerms (DirectLendingExcessPaymentPolicyDto(1, ExcessPaymentTreatment.PrepaidInterest, " ")) true 0 (Nullable())
          cashPolicyTerms (cashPolicy ExcessPaymentTreatment.PrincipalPrepayment) false 0 (Nullable())
          cashPolicyTerms (cashPolicy ExcessPaymentTreatment.PrincipalPrepayment) true 0 (Nullable 0.02m) ]

    for invalidTerms in invalidTerms do
        (fun () -> createLoan invalidTerms |> ignore) |> should throw typeof<ArgumentException>
        (fun () ->
            DirectLendingAggregateInterop.AmendTerms
                contract servicing (AmendLoanTermsRequest(invalidTerms, "Invalid election")) DateTimeOffset.UtcNow
            |> ignore)
        |> should throw typeof<ArgumentException>

let private withCashBalances unapplied prepaid isPik (servicing: LoanServicingStateDto) =
    LoanServicingStateDto(
        servicing.LoanId, servicing.Status, servicing.CurrentCommitment,
        servicing.TotalDrawn, servicing.AvailableToDraw,
        OutstandingBalancesDto(
            servicing.Balances.PrincipalOutstanding,
            servicing.Balances.InterestAccruedUnpaid,
            servicing.Balances.CommitmentFeeAccruedUnpaid,
            servicing.Balances.FeesAccruedUnpaid,
            servicing.Balances.PenaltyAccruedUnpaid,
            UnappliedCash = unapplied,
            PrepaidInterest = prepaid,
            PrepaidInterestLots = servicing.Balances.PrepaidInterestLots),
        servicing.DrawdownLots, servicing.CurrentRateReset,
        servicing.LastAccrualDate, servicing.LastPaymentDate,
        servicing.ServicingRevision, servicing.RevisionHistory, servicing.AccrualEntries,
        Collateral = servicing.Collateral,
        UnamortizedDiscount = servicing.UnamortizedDiscount,
        UnamortizedPremium = servicing.UnamortizedPremium,
        IsPikToggled = isPik)

let private withRetainedCash isPik servicing = withCashBalances 123.45m 678.90m isPik servicing

[<Theory>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``Currency amendment cannot relabel either retained cash liability`` (prepaid: bool) =
    let loanTerms = cashPolicyTerms null true 0 (Nullable())
    let contract, servicing = loanWithTwoLots loanTerms
    let retained =
        servicing |> withCashBalances (if prepaid then 0m else 100m) (if prepaid then 100m else 0m) false
    let newCurrency = cashPolicyTermsWithCurrency CurrencyCode.EUR null true 0 (Nullable())

    (fun () ->
        DirectLendingAggregateInterop.AmendTerms
            contract retained (AmendLoanTermsRequest(newCurrency, "Currency change")) DateTimeOffset.UtcNow
        |> ignore)
    |> should throw typeof<ArgumentException>

    let unchangedCurrency =
        DirectLendingAggregateInterop.AmendTerms
            contract retained (AmendLoanTermsRequest(loanTerms, "Same-currency amendment")) DateTimeOffset.UtcNow
    unchangedCurrency.Contract.CurrentTerms.BaseCurrency |> should equal CurrencyCode.USD
    unchangedCurrency.Servicing.Balances.UnappliedCash |> should equal retained.Balances.UnappliedCash
    unchangedCurrency.Servicing.Balances.PrepaidInterest |> should equal retained.Balances.PrepaidInterest

[<Theory>]
[<InlineData("drawdown")>]
[<InlineData("principal")>]
[<InlineData("mixed")>]
[<InlineData("fee")>]
[<InlineData("writeoff")>]
[<InlineData("accrual")>]
[<InlineData("pik-accrual")>]
[<InlineData("rate")>]
[<InlineData("penalty")>]
[<InlineData("activate")>]
[<InlineData("amend")>]
let ``Servicing transitions preserve held cash liabilities and their receipt evidence`` (operation: string) =
    let loanTerms = cashPolicyTerms (prepaidPolicy PrepaidInterestApplication.HoldForReview) true 0 (Nullable())
    let contract, servicing = loanWithTwoLots loanTerms
    let receipt =
        DirectLendingAggregateInterop.ApplyMixedPayment servicing loanTerms
            (ApplyMixedPaymentRequest(678.90m, DateOnly(2026, 3, 31), null, "held-cash"))
    let retained = receipt.Servicing |> withRetainedCash (operation = "pik-accrual")
    let date = DateOnly(2026, 4, 1)
    let updated =
        match operation with
        | "drawdown" ->
            (DirectLendingAggregateInterop.BookDrawdown retained (BookDrawdownRequest(100m, date, date, "preserve"))).Servicing
        | "principal" ->
            (DirectLendingAggregateInterop.ApplyPrincipalPayment retained (ApplyPrincipalPaymentRequest(100m, date, "preserve"))).Servicing
        | "mixed" ->
            (DirectLendingAggregateInterop.ApplyMixedPayment retained loanTerms (ApplyMixedPaymentRequest(100m, date, PaymentBreakdownDto(0m, 0m, 0m, 0m, 100m), "preserve"))).Servicing
        | "fee" ->
            (DirectLendingAggregateInterop.AssessFee retained (AssessFeeRequest("Fee", 100m, date, "preserve"))).Servicing
        | "writeoff" ->
            (DirectLendingAggregateInterop.ApplyWriteOff retained (ApplyWriteOffRequest(100m, date, "preserve"))).Servicing
        | "accrual" | "pik-accrual" ->
            (DirectLendingAggregateInterop.PostDailyAccrual retained loanTerms (PostDailyAccrualRequest(date))).Servicing
        | "rate" ->
            (DirectLendingAggregateInterop.ApplyRateReset retained loanTerms (ApplyRateResetRequest(date, 0.05m, Nullable(), "preserve"))).Servicing
        | "penalty" ->
            (DirectLendingAggregateInterop.ChargePrepaymentPenalty retained loanTerms (ChargePrepaymentPenaltyRequest(500_000m, date, "preserve"))).Servicing
        | "activate" ->
            (DirectLendingAggregateInterop.ActivateLoan contract retained (ActivateLoanRequest(date))).Servicing
        | "amend" ->
            (DirectLendingAggregateInterop.AmendTerms contract retained (AmendLoanTermsRequest(loanTerms, "preserve")) DateTimeOffset.UtcNow).Servicing
        | _ -> failwith "Unknown operation"

    updated.Balances.UnappliedCash |> should equal 123.45m
    updated.Balances.PrepaidInterest |> should equal 678.90m
    updated.Balances.PrepaidInterestLots |> Seq.toArray
    |> should equal (retained.Balances.PrepaidInterestLots |> Seq.toArray)

let private withPik isPik (servicing: LoanServicingStateDto) =
    LoanServicingStateDto(
        servicing.LoanId, servicing.Status, servicing.CurrentCommitment,
        servicing.TotalDrawn, servicing.AvailableToDraw, servicing.Balances,
        servicing.DrawdownLots, servicing.CurrentRateReset,
        servicing.LastAccrualDate, servicing.LastPaymentDate,
        servicing.ServicingRevision, servicing.RevisionHistory, servicing.AccrualEntries,
        Collateral = servicing.Collateral,
        UnamortizedDiscount = servicing.UnamortizedDiscount,
        UnamortizedPremium = servicing.UnamortizedPremium,
        IsPikToggled = isPik)

[<Fact>]
let ``Explicit future cash interest election releases prepaid cash only against newly earned interest`` () =
    let policy = prepaidPolicy PrepaidInterestApplication.FutureCashInterest
    let loanTerms = cashPolicyTerms policy true 12 (Nullable())
    let _, servicing = loanWithTwoLots loanTerms
    let receipt =
        DirectLendingAggregateInterop.ApplyMixedPayment servicing loanTerms
            (ApplyMixedPaymentRequest(200m, DateOnly(2026, 4, 1), null, "future-interest"))
    let accrued =
        DirectLendingAggregateInterop.PostDailyAccrual receipt.Servicing loanTerms
            (PostDailyAccrualRequest(DateOnly(2026, 4, 2)))

    accrued.Entry.InterestAmount |> should equal 111.11m
    accrued.Entry.PrepaidInterestApplied |> should equal 111.11m
    accrued.Servicing.Balances.InterestAccruedUnpaid |> should equal 0m
    accrued.Servicing.Balances.PrepaidInterest |> should equal 88.89m
    accrued.Servicing.Balances.CommitmentFeeAccruedUnpaid |> should equal accrued.Entry.CommitmentFeeAmount
    accrued.Servicing.Balances.UnappliedCash |> should equal 0m
    accrued.Entry.PrepaidInterestReleases |> Seq.toList
    |> should equal [ PrepaidInterestReleaseDto(receipt.CashTransactionId, 111.11m) ]
    let lot = accrued.Servicing.Balances.PrepaidInterestLots[0]
    lot.CashTransactionId |> should equal receipt.CashTransactionId
    lot.ReceiptDate |> should equal (DateOnly(2026, 4, 1))
    lot.OriginalAmount |> should equal 200m
    lot.RemainingAmount |> should equal 88.89m
    lot.Application |> should equal PrepaidInterestApplication.FutureCashInterest
    lot.ContractualReference |> should equal policy.ContractualReference

[<Fact>]
let ``Prepaid release is capped to the retained balance and cannot repeat an exhausted receipt`` () =
    let loanTerms = cashPolicyTerms (prepaidPolicy PrepaidInterestApplication.FutureCashInterest) true 12 (Nullable())
    let _, servicing = loanWithTwoLots loanTerms
    let receipt =
        DirectLendingAggregateInterop.ApplyMixedPayment servicing loanTerms
            (ApplyMixedPaymentRequest(50m, DateOnly(2026, 4, 1), null, "small-prepaid"))
    let first =
        DirectLendingAggregateInterop.PostDailyAccrual receipt.Servicing loanTerms
            (PostDailyAccrualRequest(DateOnly(2026, 4, 2)))
    let next =
        DirectLendingAggregateInterop.PostDailyAccrual first.Servicing loanTerms
            (PostDailyAccrualRequest(DateOnly(2026, 4, 3)))

    first.Entry.PrepaidInterestApplied |> should equal 50m
    first.Servicing.Balances.InterestAccruedUnpaid |> should equal 61.11m
    first.Servicing.Balances.PrepaidInterest |> should equal 0m
    next.Entry.PrepaidInterestApplied |> should equal 0m
    next.Servicing.Balances.InterestAccruedUnpaid |> should equal 172.22m
    next.Servicing.Balances.PrepaidInterest |> should equal 0m
    next.Servicing.Balances.PrepaidInterestLots[0].RemainingAmount |> should equal 0m

[<Fact>]
let ``Same-day accrual remains unpaid and a later release does not retroactively pay it`` () =
    let loanTerms = cashPolicyTerms (prepaidPolicy PrepaidInterestApplication.FutureCashInterest) true 12 (Nullable())
    let _, servicing = loanWithTwoLots loanTerms
    let receipt =
        DirectLendingAggregateInterop.ApplyMixedPayment servicing loanTerms
            (ApplyMixedPaymentRequest(200m, DateOnly(2026, 4, 1), null, "same-day"))
    let sameDay =
        DirectLendingAggregateInterop.PostDailyAccrual receipt.Servicing loanTerms
            (PostDailyAccrualRequest(DateOnly(2026, 4, 1)))
    let later =
        DirectLendingAggregateInterop.PostDailyAccrual sameDay.Servicing loanTerms
            (PostDailyAccrualRequest(DateOnly(2026, 4, 2)))

    sameDay.Entry.PrepaidInterestApplied |> should equal 0m
    sameDay.Servicing.Balances.PrepaidInterest |> should equal 200m
    sameDay.Servicing.Balances.InterestAccruedUnpaid |> should equal 111.11m
    later.Entry.PrepaidInterestApplied |> should equal 111.11m
    later.Servicing.Balances.InterestAccruedUnpaid |> should equal 111.11m
    later.Servicing.Balances.PrepaidInterest |> should equal 88.89m

[<Fact>]
let ``PIK accrual never consumes a receipt elected for future cash interest`` () =
    let loanTerms = cashPolicyTerms (prepaidPolicy PrepaidInterestApplication.FutureCashInterest) true 12 (Nullable())
    let _, servicing = loanWithTwoLots loanTerms
    let receipt =
        DirectLendingAggregateInterop.ApplyMixedPayment servicing loanTerms
            (ApplyMixedPaymentRequest(200m, DateOnly(2026, 4, 1), null, "pik-prepaid"))
    let pikState = receipt.Servicing |> withPik true
    let accrued =
        DirectLendingAggregateInterop.PostDailyAccrual pikState loanTerms
            (PostDailyAccrualRequest(DateOnly(2026, 4, 2)))

    accrued.Entry.InterestAmount |> should equal 0m
    accrued.Entry.PikInterestAmount |> should equal 111.11m
    accrued.Entry.PrepaidInterestApplied |> should equal 0m
    accrued.Servicing.Balances.PrepaidInterest |> should equal 200m
    accrued.Servicing.Balances.PrepaidInterestLots[0].RemainingAmount |> should equal 200m
    accrued.Servicing.Balances.PrincipalOutstanding |> should equal 500_111.11m

[<Fact>]
let ``Legacy prepaid balance without receipt instructions never inherits a later release election`` () =
    let loanTerms = cashPolicyTerms (prepaidPolicy PrepaidInterestApplication.FutureCashInterest) true 12 (Nullable())
    let _, servicing = loanWithTwoLots loanTerms
    let untracked = servicing |> withCashBalances 0m 200m false
    let accrued =
        DirectLendingAggregateInterop.PostDailyAccrual untracked loanTerms
            (PostDailyAccrualRequest(DateOnly(2026, 4, 2)))

    accrued.Entry.PrepaidInterestApplied |> should equal 0m
    accrued.Servicing.Balances.PrepaidInterest |> should equal 200m
    accrued.Servicing.Balances.InterestAccruedUnpaid |> should equal 111.11m
    accrued.Servicing.Balances.PrepaidInterestLots.Count |> should equal 0

[<Theory>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``Terms amendments cannot change the application election frozen on an existing cash receipt`` (originallyReleased: bool) =
    let originalApplication =
        if originallyReleased then PrepaidInterestApplication.FutureCashInterest else PrepaidInterestApplication.HoldForReview
    let changedApplication =
        if originallyReleased then PrepaidInterestApplication.HoldForReview else PrepaidInterestApplication.FutureCashInterest
    let originalTerms = cashPolicyTerms (prepaidPolicy originalApplication) true 12 (Nullable())
    let contract, servicing = loanWithTwoLots originalTerms
    let receipt =
        DirectLendingAggregateInterop.ApplyMixedPayment servicing originalTerms
            (ApplyMixedPaymentRequest(200m, DateOnly(2026, 4, 1), null, "frozen-election"))
    let changedTerms = cashPolicyTerms (prepaidPolicy changedApplication) true 12 (Nullable())
    let amended =
        DirectLendingAggregateInterop.AmendTerms contract receipt.Servicing
            (AmendLoanTermsRequest(changedTerms, "Prospective election only")) DateTimeOffset.UtcNow
    let accrued =
        DirectLendingAggregateInterop.PostDailyAccrual amended.Servicing amended.Contract.CurrentTerms
            (PostDailyAccrualRequest(DateOnly(2026, 4, 2)))

    accrued.Entry.PrepaidInterestApplied |> should equal (if originallyReleased then 111.11m else 0m)
    accrued.Servicing.Balances.PrepaidInterestLots[0].Application |> should equal originalApplication
    accrued.Servicing.Balances.PrepaidInterest |> should equal (if originallyReleased then 88.89m else 200m)

[<Theory>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``Eligible prepaid receipts release in date and cash-identity order with retained operands`` (sameDate: bool) =
    let loanTerms = cashPolicyTerms (prepaidPolicy PrepaidInterestApplication.FutureCashInterest) true 12 (Nullable())
    let _, servicing = loanWithTwoLots loanTerms
    let first =
        DirectLendingAggregateInterop.ApplyMixedPayment servicing loanTerms
            (ApplyMixedPaymentRequest(70m, DateOnly(2026, 4, 1), null, "first-lot"))
    let secondDate = if sameDate then DateOnly(2026, 4, 1) else DateOnly(2026, 4, 2)
    let second =
        DirectLendingAggregateInterop.ApplyMixedPayment first.Servicing loanTerms
            (ApplyMixedPaymentRequest(70m, secondDate, null, "second-lot"))
    let accrued =
        DirectLendingAggregateInterop.PostDailyAccrual second.Servicing loanTerms
            (PostDailyAccrualRequest(DateOnly(2026, 4, 3)))
    let ordered =
        second.Servicing.Balances.PrepaidInterestLots
        |> Seq.sortBy (fun lot -> lot.ReceiptDate, lot.CashTransactionId)
        |> Seq.toArray

    accrued.Entry.PrepaidInterestApplied |> should equal 111.11m
    accrued.Entry.PrepaidInterestReleases |> Seq.toList
    |> should equal [ PrepaidInterestReleaseDto(ordered[0].CashTransactionId, 70m)
                      PrepaidInterestReleaseDto(ordered[1].CashTransactionId, 41.11m) ]
    accrued.Servicing.Balances.PrepaidInterest |> should equal 28.89m
    accrued.Servicing.Balances.PrepaidInterestLots |> Seq.sumBy _.RemainingAmount |> should equal 28.89m

// ── Rate resets and penalties ───────────────────────────────────────────────

[<Fact>]
let ``Rate reset uses the contractual spread unless the request overrides it, then applies floor and cap`` () =
    let floating = terms RateTypeKind.Floating 0 true (Nullable())
    let _, servicing = loanWithTwoLots floating

    let contractual =
        DirectLendingAggregateInterop.ApplyRateReset servicing floating (ApplyRateResetRequest(DateOnly(2026, 4, 1), 0.05m, Nullable(), "fix-1"))
    let overridden =
        DirectLendingAggregateInterop.ApplyRateReset servicing floating (ApplyRateResetRequest(DateOnly(2026, 4, 1), 0.05m, Nullable 100m, "fix-2"))
    let floored =
        DirectLendingAggregateInterop.ApplyRateReset servicing floating (ApplyRateResetRequest(DateOnly(2026, 4, 1), 0.001m, Nullable 0m, "fix-3"))
    let capped =
        DirectLendingAggregateInterop.ApplyRateReset servicing floating (ApplyRateResetRequest(DateOnly(2026, 4, 1), 0.20m, Nullable(), "fix-4"))

    contractual.AllInRate |> should equal 0.075m
    contractual.Servicing.CurrentRateReset.SpreadBps |> should equal 250m
    contractual.Servicing.CurrentRateReset.IndexName |> should equal "SOFR"
    overridden.AllInRate |> should equal 0.06m
    floored.AllInRate |> should equal 0.04m
    capped.AllInRate |> should equal 0.12m

[<Fact>]
let ``Prepayment penalty is refused when the terms forbid prepayment`` () =
    let noPrepay = terms RateTypeKind.Fixed 0 false (Nullable 0.02m)
    let _, servicing = loanWithTwoLots noPrepay

    (fun () ->
        DirectLendingAggregateInterop.ChargePrepaymentPenalty
            servicing
            noPrepay
            (ChargePrepaymentPenaltyRequest(500_000m, DateOnly(2026, 4, 1), "pp-1"))
        |> ignore)
    |> should throw typeof<Exception>

[<Fact>]
let ``Prepayment penalty rounds half away from zero and accrues to the penalty balance only`` () =
    let loanTerms = terms RateTypeKind.Fixed 0 true (Nullable 0.02m)
    let _, servicing = loanWithTwoLots loanTerms

    let charged =
        DirectLendingAggregateInterop.ChargePrepaymentPenalty
            servicing
            loanTerms
            (ChargePrepaymentPenaltyRequest(100_006.25m, DateOnly(2026, 4, 1), "pp-2"))

    // 100,006.25 * 2% = 2,000.125 exactly: away-from-zero gives .13 where banker's rounding gives .12.
    charged.PenaltyAmount |> should equal 2_000.13m
    charged.Servicing.Balances.PenaltyAccruedUnpaid |> should equal 2_000.13m
    charged.Servicing.Balances.PrincipalOutstanding |> should equal servicing.Balances.PrincipalOutstanding

[<Fact>]
let ``Prepayment penalty without a configured rate charges nothing but still records a revision`` () =
    let loanTerms = terms RateTypeKind.Fixed 0 true (Nullable())
    let _, servicing = loanWithTwoLots loanTerms

    let charged =
        DirectLendingAggregateInterop.ChargePrepaymentPenalty
            servicing
            loanTerms
            (ChargePrepaymentPenaltyRequest(500_000m, DateOnly(2026, 4, 1), "pp-3"))

    charged.PenaltyAmount |> should equal 0m
    charged.Servicing.ServicingRevision |> should equal (servicing.ServicingRevision + 1L)
