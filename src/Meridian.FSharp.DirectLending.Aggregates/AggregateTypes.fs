namespace Meridian.FSharp.DirectLending.Aggregates

open System
open Meridian.Contracts.DirectLending

type CreateLoanDecision =
    { Contract: LoanContractDetailDto
      Servicing: LoanServicingStateDto
      TermsVersion: LoanTermsVersionDto }

type AmendTermsDecision =
    { Contract: LoanContractDetailDto
      Servicing: LoanServicingStateDto
      TermsVersion: LoanTermsVersionDto }

type ActivateLoanDecision =
    { Contract: LoanContractDetailDto
      Servicing: LoanServicingStateDto }

type BookDrawdownDecision =
    { Servicing: LoanServicingStateDto
      LotId: Guid }

type RateResetDecision =
    { Servicing: LoanServicingStateDto
      AllInRate: decimal }

type PrincipalPaymentDecision =
    { Servicing: LoanServicingStateDto
      AppliedAmount: decimal }

type PaymentAllocationInstruction =
    { SequenceNumber: int
      TargetType: string
      TargetReference: Guid
      Amount: decimal
      AllocationRule: string }

type MixedPaymentDecision =
    { Servicing: LoanServicingStateDto
      Resolution: MixedPaymentResolutionDto
      CashTransactionId: Guid
      Allocations: PaymentAllocationInstruction array }

/// PRD-115: unapplied cash allocated to amounts owed; no cash is received.
type UnappliedCashApplicationDecision =
    { Servicing: LoanServicingStateDto
      Breakdown: PaymentBreakdownDto
      ResolutionBasis: string }

/// PRD-115: unapplied cash returned to the borrower.
type UnappliedCashRefundDecision =
    { Servicing: LoanServicingStateDto }

type FeeDecision =
    { Servicing: LoanServicingStateDto }

type WriteOffDecision =
    { Servicing: LoanServicingStateDto
      AppliedAmount: decimal }

type DailyAccrualDecision =
    { Servicing: LoanServicingStateDto
      Entry: DailyAccrualEntryDto
      /// PRD-115: the part of the day's cash interest settled from the prepaid-interest balance.
      PrepaidInterestConsumed: decimal }

type PrepaymentPenaltyDecision =
    { Servicing: LoanServicingStateDto
      PenaltyAmount: decimal }

type PikCapitalizationDecision =
    { Servicing: LoanServicingStateDto
      Capitalization: PikCapitalizationDto }

/// The observed fixing a scheduled reset applies.
type ScheduledFixing =
    { ObservationDate: DateOnly
      RateFraction: decimal
      SourceRef: string
      FixingId: Nullable<Guid>
      FixingRevision: Nullable<int> }
