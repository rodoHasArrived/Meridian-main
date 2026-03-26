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

type FeeDecision =
    { Servicing: LoanServicingStateDto }

type WriteOffDecision =
    { Servicing: LoanServicingStateDto
      AppliedAmount: decimal }

type DailyAccrualDecision =
    { Servicing: LoanServicingStateDto
      Entry: DailyAccrualEntryDto }

type PrepaymentPenaltyDecision =
    { Servicing: LoanServicingStateDto
      PenaltyAmount: decimal }
