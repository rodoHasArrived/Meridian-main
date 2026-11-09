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
        OutstandingBalancesDto(servicing.Balances.PrincipalOutstanding, interest, commitmentFee, fees, penalty),
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
    payment.Allocations |> Array.forall (fun a -> a.AllocationRule = "Manual") |> should equal true

[<Fact>]
let ``Manual breakdown above the amount owed is capped and never books phantom income`` () =
    let contract, servicing = loanWithTwoLots (fixedTerms ())
    let owing = servicing |> withBalances 10m 0m 0m 0m

    let payment =
        DirectLendingAggregateInterop.ApplyMixedPayment
            owing
            contract.CurrentTerms
            (ApplyMixedPaymentRequest(1_000m, DateOnly(2026, 4, 1), PaymentBreakdownDto(1_000m, 0m, 0m, 0m, 0m), "pay-over"))

    payment.Resolution.Breakdown.ToInterest |> should equal 10m
    payment.Resolution.UnappliedAmount |> should equal 990m
    payment.Servicing.Balances.InterestAccruedUnpaid |> should equal 0m
    // Only 10 is income; since PRD-115 the 990 excess is held as tracked unapplied cash (the
    // default rule), so allocations sum to the cash received.
    payment.Allocations |> Array.map (fun a -> a.TargetType, a.Amount) |> should equal [| "Interest", 10m; "UnappliedCash", 990m |]
    payment.Servicing.Balances.UnappliedCash |> should equal 990m

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
    payment.Servicing.Balances.PrincipalOutstanding |> should equal 494_000m
    payment.Servicing.DrawdownLots |> Seq.map _.RemainingPrincipal |> Seq.toList |> should equal [ 194_000m; 300_000m ]

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

// ── PRD-115 balances owed to the borrower ───────────────────────────────────

[<Fact>]
let ``Every servicing command carries prepaid interest and unapplied cash through unchanged`` () =
    // Each command rebuilds OutstandingBalancesDto; a positional rebuild would silently reset the
    // two PRD-115 balances to their 0 default. No command changes them until excess routing lands.
    let loanTerms = fixedTerms ()
    let _, drawn = loanWithTwoLots loanTerms
    let seeded = drawn |> DirectLendingTestBalances.withExcessBalances 123.45m 678.90m
    let effective = DateOnly(2026, 4, 1)

    let commands: (string * (LoanServicingStateDto -> LoanServicingStateDto)) list =
        [ "BookDrawdown",
          fun s -> (DirectLendingAggregateInterop.BookDrawdown s (BookDrawdownRequest(10_000m, effective, effective, "wire-x"))).Servicing
          "ApplyPrincipalPayment",
          fun s -> (DirectLendingAggregateInterop.ApplyPrincipalPayment s (ApplyPrincipalPaymentRequest(1_000m, effective, "pp-x"))).Servicing
          "ApplyMixedPayment",
          fun s -> (DirectLendingAggregateInterop.ApplyMixedPayment s loanTerms (ApplyMixedPaymentRequest(1_000m, effective, null, "mix-x"))).Servicing
          "AssessFee",
          fun s -> (DirectLendingAggregateInterop.AssessFee s (AssessFeeRequest("Amendment", 500m, effective, null))).Servicing
          "ApplyWriteOff",
          fun s -> (DirectLendingAggregateInterop.ApplyWriteOff s (ApplyWriteOffRequest(1_000m, effective, "test write-off"))).Servicing
          "PostDailyAccrual",
          fun s -> (DirectLendingAggregateInterop.PostDailyAccrual s loanTerms (PostDailyAccrualRequest(effective))).Servicing
          "ChargePrepaymentPenalty",
          fun s -> (DirectLendingAggregateInterop.ChargePrepaymentPenalty s loanTerms (ChargePrepaymentPenaltyRequest(100_000m, effective, "pen-x"))).Servicing ]

    for name, command in commands do
        let after = command seeded
        (name, after.Balances.PrepaidInterest, after.Balances.UnappliedCash)
        |> should equal (name, 123.45m, 678.90m)

[<Fact>]
let ``Loans created before PRD-115 start with zero prepaid interest and unapplied cash`` () =
    let created = createLoan (fixedTerms ())

    created.Servicing.Balances.PrepaidInterest |> should equal 0m
    created.Servicing.Balances.UnappliedCash |> should equal 0m
