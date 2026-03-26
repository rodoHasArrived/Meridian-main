namespace Meridian.FSharp.DirectLending.Aggregates

open System
open Meridian.Contracts.DirectLending

module DirectLendingAggregateInterop =
    let CreateLoan (loanId: Guid) (request: CreateLoanRequest) (recordedAt: DateTimeOffset) =
        ContractAggregate.createLoan loanId request.FacilityName request.Borrower request.EffectiveDate request.Terms recordedAt

    let AmendTerms (contract: LoanContractDetailDto) (servicing: LoanServicingStateDto) (request: AmendLoanTermsRequest) (recordedAt: DateTimeOffset) =
        ContractAggregate.amendTerms contract servicing request.Terms request.AmendmentReason recordedAt

    let ActivateLoan (contract: LoanContractDetailDto) (servicing: LoanServicingStateDto) (request: ActivateLoanRequest) =
        ContractAggregate.activateLoan contract servicing request.ActivationDate

    let BookDrawdown (servicing: LoanServicingStateDto) (request: BookDrawdownRequest) =
        ServicingAggregate.bookDrawdown servicing request.Amount request.TradeDate request.SettleDate request.ExternalRef

    let ApplyRateReset (servicing: LoanServicingStateDto) (terms: DirectLendingTermsDto) (request: ApplyRateResetRequest) =
        ServicingAggregate.applyRateReset servicing terms request.EffectiveDate request.ObservedRate request.SpreadBps request.SourceRef

    let ApplyPrincipalPayment (servicing: LoanServicingStateDto) (request: ApplyPrincipalPaymentRequest) =
        ServicingAggregate.applyPrincipalPayment servicing request.Amount request.EffectiveDate

    let ApplyMixedPayment (servicing: LoanServicingStateDto) (request: ApplyMixedPaymentRequest) =
        ServicingAggregate.applyMixedPayment servicing request.Amount request.Breakdown request.EffectiveDate

    let AssessFee (servicing: LoanServicingStateDto) (request: AssessFeeRequest) =
        ServicingAggregate.assessFee servicing request.Amount request.EffectiveDate

    let ApplyWriteOff (servicing: LoanServicingStateDto) (request: ApplyWriteOffRequest) =
        ServicingAggregate.applyWriteOff servicing request.Amount request.EffectiveDate request.Reason

    let PostDailyAccrual (servicing: LoanServicingStateDto) (terms: DirectLendingTermsDto) (request: PostDailyAccrualRequest) =
        ServicingAggregate.postDailyAccrual servicing terms request.AccrualDate

    let ChargePrepaymentPenalty (servicing: LoanServicingStateDto) (terms: DirectLendingTermsDto) (request: ChargePrepaymentPenaltyRequest) =
        ServicingAggregate.chargePrepaymentPenalty servicing terms request.OutstandingPrincipal request.EffectiveDate
