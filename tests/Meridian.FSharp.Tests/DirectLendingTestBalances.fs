module Meridian.FSharp.Tests.DirectLendingTestBalances

// Shared helpers for direct-lending aggregate tests that need balances the public commands do
// not set yet (PRD-115 prepaid interest and unapplied cash).

open Meridian.Contracts.DirectLending

/// Copies the servicing state with the given prepaid-interest and unapplied-cash balances,
/// keeping every other member, including the optional PIK, collateral and rate-reset history.
let withExcessBalances prepaid unapplied (servicing: LoanServicingStateDto) =
    let b = servicing.Balances
    LoanServicingStateDto(
        servicing.LoanId,
        servicing.Status,
        servicing.CurrentCommitment,
        servicing.TotalDrawn,
        servicing.AvailableToDraw,
        OutstandingBalancesDto(
            b.PrincipalOutstanding,
            b.InterestAccruedUnpaid,
            b.CommitmentFeeAccruedUnpaid,
            b.FeesAccruedUnpaid,
            b.PenaltyAccruedUnpaid,
            PrepaidInterest = prepaid,
            UnappliedCash = unapplied),
        servicing.DrawdownLots,
        servicing.CurrentRateReset,
        servicing.LastAccrualDate,
        servicing.LastPaymentDate,
        servicing.ServicingRevision,
        servicing.RevisionHistory,
        servicing.AccrualEntries,
        Collateral = servicing.Collateral,
        UnamortizedDiscount = servicing.UnamortizedDiscount,
        UnamortizedPremium = servicing.UnamortizedPremium,
        IsPikToggled = servicing.IsPikToggled,
        PikInterestAccruedUnpaid = servicing.PikInterestAccruedUnpaid,
        LastPikCapitalizationDate = servicing.LastPikCapitalizationDate,
        RateResetHistory = servicing.RateResetHistory,
        PikCapitalizations = servicing.PikCapitalizations)
