namespace Meridian.FSharp.DirectLending.Aggregates

open System
open Meridian.Contracts.DirectLending
open Meridian.FSharp.DirectLendingInterop

module internal ServicingAggregate =
    let private decimalOrDefault (value: Nullable<decimal>) fallback =
        if value.HasValue then value.Value else fallback

    let private stringOrDefault (value: string) fallback =
        if String.IsNullOrWhiteSpace value then fallback else value

    let private prependRevision revisionNumber sourceType effectiveAsOfDate notes (history: System.Collections.Generic.IReadOnlyList<ServicingRevisionDto>) =
        Array.append
            [| ServicingRevisionDto(revisionNumber, sourceType, effectiveAsOfDate, DateTimeOffset.UtcNow, notes) |]
            (history |> Seq.toArray)

    let private applyPaymentToLots appliedAmount (lots: System.Collections.Generic.IReadOnlyList<DrawdownLotDto>) =
        let updated = lots |> Seq.toArray
        let mutable remaining = appliedAmount

        for i in 0 .. updated.Length - 1 do
            if remaining > 0m then
                let lot = updated[i]
                if lot.RemainingPrincipal > 0m then
                    let appliedToLot = min remaining lot.RemainingPrincipal
                    updated[i] <- DrawdownLotDto(lot.LotId, lot.DrawdownDate, lot.SettleDate, lot.OriginalPrincipal, lot.RemainingPrincipal - appliedToLot, lot.ExternalRef)
                    remaining <- remaining - appliedToLot

        updated

    let private autoAllocate (servicing: LoanServicingStateDto) (terms: DirectLendingTermsDto) amount (effectiveDate: DateOnly) =
        let mutable remaining = amount
        let take available =
            if remaining <= 0m || available <= 0m then
                0m
            else
                let applied = min remaining available
                remaining <- remaining - applied
                applied

        let toInterest = take servicing.Balances.InterestAccruedUnpaid
        let toCommitmentFee = take servicing.Balances.CommitmentFeeAccruedUnpaid
        let toFees = take servicing.Balances.FeesAccruedUnpaid
        let toPenalty = take servicing.Balances.PenaltyAccruedUnpaid
        // During the interest-only period, scheduled principal is 0 in auto-allocation.
        let toPrincipal =
            if DirectLendingInterop.IsInterestOnlyPeriod(terms.OriginationDate, terms.InterestOnlyMonths, effectiveDate) then
                0m
            else
                take servicing.Balances.PrincipalOutstanding
        PaymentBreakdownDto(toInterest, toCommitmentFee, toFees, toPenalty, toPrincipal)

    let private buildResolution (servicing: LoanServicingStateDto) amount (requestedBreakdown: PaymentBreakdownDto) =
        let mutable remaining = amount
        let take requested =
            if remaining <= 0m || requested <= 0m then
                0m
            else
                let applied = min remaining requested
                remaining <- remaining - applied
                applied

        let breakdown =
            PaymentBreakdownDto(
                take requestedBreakdown.ToInterest,
                take requestedBreakdown.ToCommitmentFee,
                take requestedBreakdown.ToFees,
                take requestedBreakdown.ToPenalty,
                take requestedBreakdown.ToPrincipal)

        MixedPaymentResolutionDto(breakdown, "Manual", "manual-v1", remaining)

    let bookDrawdown (servicing: LoanServicingStateDto) amount tradeDate settleDate externalRef =
        let lotId = Guid.NewGuid()
        let lot = DrawdownLotDto(lotId, tradeDate, settleDate, amount, amount, externalRef)
        let updatedTotalDrawn = servicing.TotalDrawn + amount
        let nextRevision = servicing.ServicingRevision + 1L
        let balances =
            OutstandingBalancesDto(
                servicing.Balances.PrincipalOutstanding + amount,
                servicing.Balances.InterestAccruedUnpaid,
                servicing.Balances.CommitmentFeeAccruedUnpaid,
                servicing.Balances.FeesAccruedUnpaid,
                servicing.Balances.PenaltyAccruedUnpaid)

        let updated =
            LoanServicingStateDto(
                servicing.LoanId,
                servicing.Status,
                servicing.CurrentCommitment,
                updatedTotalDrawn,
                DirectLendingInterop.CalculateAvailableToDraw(servicing.CurrentCommitment, updatedTotalDrawn),
                balances,
                (servicing.DrawdownLots |> Seq.append [ lot ] |> Seq.sortBy _.DrawdownDate |> Seq.toArray),
                servicing.CurrentRateReset,
                servicing.LastAccrualDate,
                servicing.LastPaymentDate,
                nextRevision,
                prependRevision nextRevision "InternalEvent" settleDate (sprintf "Drawdown booked for %.2f." amount) servicing.RevisionHistory,
                servicing.AccrualEntries)

        { Servicing = updated; LotId = lotId }

    let applyRateReset (servicing: LoanServicingStateDto) (terms: DirectLendingTermsDto) (effectiveDate: DateOnly) (observedRate: decimal) (spreadBps: Nullable<decimal>) (sourceRef: string) =
        let spread =
            if spreadBps.HasValue then spreadBps.Value else decimalOrDefault terms.SpreadBps 0m
        let floorRate =
            if terms.FloorRate.HasValue then Nullable(terms.FloorRate.Value) else Nullable()
        let capRate =
            if terms.CapRate.HasValue then Nullable(terms.CapRate.Value) else Nullable()
        let allInRate =
            DirectLendingInterop.CalculateAllInRate(
                observedRate,
                spread,
                floorRate,
                capRate)

        let nextRevision = servicing.ServicingRevision + 1L
        let updated =
            LoanServicingStateDto(
                servicing.LoanId,
                servicing.Status,
                servicing.CurrentCommitment,
                servicing.TotalDrawn,
                servicing.AvailableToDraw,
                servicing.Balances,
                servicing.DrawdownLots,
                RateResetDto(effectiveDate, stringOrDefault terms.InterestIndexName "FloatingIndex", observedRate, spread, allInRate, sourceRef),
                servicing.LastAccrualDate,
                servicing.LastPaymentDate,
                nextRevision,
                prependRevision nextRevision "InternalEvent" effectiveDate (sprintf "Rate reset applied at %M." allInRate) servicing.RevisionHistory,
                servicing.AccrualEntries)

        { Servicing = updated; AllInRate = allInRate }

    let applyPrincipalPayment (servicing: LoanServicingStateDto) amount effectiveDate =
        let appliedAmount = min amount servicing.Balances.PrincipalOutstanding
        let principalAfter = DirectLendingInterop.ApplyPrincipalPayment(servicing.Balances.PrincipalOutstanding, appliedAmount)
        let totalDrawnAfter = max 0m (servicing.TotalDrawn - appliedAmount)
        let nextRevision = servicing.ServicingRevision + 1L
        let balances =
            OutstandingBalancesDto(
                principalAfter,
                servicing.Balances.InterestAccruedUnpaid,
                servicing.Balances.CommitmentFeeAccruedUnpaid,
                servicing.Balances.FeesAccruedUnpaid,
                servicing.Balances.PenaltyAccruedUnpaid)

        let updated =
            LoanServicingStateDto(
                servicing.LoanId,
                servicing.Status,
                servicing.CurrentCommitment,
                totalDrawnAfter,
                DirectLendingInterop.CalculateAvailableToDraw(servicing.CurrentCommitment, totalDrawnAfter),
                balances,
                applyPaymentToLots appliedAmount servicing.DrawdownLots,
                servicing.CurrentRateReset,
                servicing.LastAccrualDate,
                Nullable effectiveDate,
                nextRevision,
                prependRevision nextRevision "InternalEvent" effectiveDate (sprintf "Principal payment applied for %.2f." appliedAmount) servicing.RevisionHistory,
                servicing.AccrualEntries)

        { Servicing = updated; AppliedAmount = appliedAmount }

    let applyMixedPayment (servicing: LoanServicingStateDto) (terms: DirectLendingTermsDto) amount breakdown effectiveDate =
        let resolution =
            match box breakdown with
            | null -> MixedPaymentResolutionDto(autoAllocate servicing terms amount effectiveDate, "WaterfallAuto", "waterfall-v1", amount - (autoAllocate servicing terms amount effectiveDate).TotalAllocated)
            | _ -> buildResolution servicing amount breakdown

        let updatedBalances =
            OutstandingBalancesDto(
                max 0m (servicing.Balances.PrincipalOutstanding - resolution.Breakdown.ToPrincipal),
                max 0m (servicing.Balances.InterestAccruedUnpaid - resolution.Breakdown.ToInterest),
                max 0m (servicing.Balances.CommitmentFeeAccruedUnpaid - resolution.Breakdown.ToCommitmentFee),
                max 0m (servicing.Balances.FeesAccruedUnpaid - resolution.Breakdown.ToFees),
                max 0m (servicing.Balances.PenaltyAccruedUnpaid - resolution.Breakdown.ToPenalty))

        let totalDrawnAfter = max 0m (servicing.TotalDrawn - resolution.Breakdown.ToPrincipal)
        let nextRevision = servicing.ServicingRevision + 1L
        let updated =
            LoanServicingStateDto(
                servicing.LoanId,
                servicing.Status,
                servicing.CurrentCommitment,
                totalDrawnAfter,
                DirectLendingInterop.CalculateAvailableToDraw(servicing.CurrentCommitment, totalDrawnAfter),
                updatedBalances,
                applyPaymentToLots resolution.Breakdown.ToPrincipal servicing.DrawdownLots,
                servicing.CurrentRateReset,
                servicing.LastAccrualDate,
                Nullable effectiveDate,
                nextRevision,
                prependRevision nextRevision "InternalEvent" effectiveDate (sprintf "Mixed payment applied for %.2f." amount) servicing.RevisionHistory,
                servicing.AccrualEntries)

        let cashTransactionId = Guid.NewGuid()
        let allocations =
            [|
                ("Interest", resolution.Breakdown.ToInterest)
                ("CommitmentFee", resolution.Breakdown.ToCommitmentFee)
                ("Fees", resolution.Breakdown.ToFees)
                ("Penalty", resolution.Breakdown.ToPenalty)
                ("Principal", resolution.Breakdown.ToPrincipal)
            |]
            |> Array.choose (fun (targetType, allocatedAmount) ->
                if allocatedAmount <= 0m then
                    None
                else
                    Some (targetType, allocatedAmount))
            |> Array.mapi (fun index (targetType, allocatedAmount) ->
                { SequenceNumber = index + 1
                  TargetType = targetType
                  TargetReference = Guid.NewGuid()
                  Amount = allocatedAmount
                  AllocationRule = resolution.ResolutionBasis })

        { Servicing = updated
          Resolution = resolution
          CashTransactionId = cashTransactionId
          Allocations = allocations }

    let assessFee (servicing: LoanServicingStateDto) amount effectiveDate =
        let nextRevision = servicing.ServicingRevision + 1L
        let balances =
            OutstandingBalancesDto(
                servicing.Balances.PrincipalOutstanding,
                servicing.Balances.InterestAccruedUnpaid,
                servicing.Balances.CommitmentFeeAccruedUnpaid,
                servicing.Balances.FeesAccruedUnpaid + amount,
                servicing.Balances.PenaltyAccruedUnpaid)

        let updated =
            LoanServicingStateDto(
                servicing.LoanId,
                servicing.Status,
                servicing.CurrentCommitment,
                servicing.TotalDrawn,
                servicing.AvailableToDraw,
                balances,
                servicing.DrawdownLots,
                servicing.CurrentRateReset,
                servicing.LastAccrualDate,
                servicing.LastPaymentDate,
                nextRevision,
                prependRevision nextRevision "InternalEvent" effectiveDate (sprintf "Fee assessed for %.2f." amount) servicing.RevisionHistory,
                servicing.AccrualEntries)

        { Servicing = updated }

    let applyWriteOff (servicing: LoanServicingStateDto) amount effectiveDate reason =
        let appliedAmount = min amount servicing.Balances.PrincipalOutstanding
        let totalDrawnAfter = max 0m (servicing.TotalDrawn - appliedAmount)
        let nextRevision = servicing.ServicingRevision + 1L
        let balances =
            OutstandingBalancesDto(
                max 0m (servicing.Balances.PrincipalOutstanding - appliedAmount),
                servicing.Balances.InterestAccruedUnpaid,
                servicing.Balances.CommitmentFeeAccruedUnpaid,
                servicing.Balances.FeesAccruedUnpaid,
                servicing.Balances.PenaltyAccruedUnpaid)

        let updated =
            LoanServicingStateDto(
                servicing.LoanId,
                servicing.Status,
                servicing.CurrentCommitment,
                totalDrawnAfter,
                DirectLendingInterop.CalculateAvailableToDraw(servicing.CurrentCommitment, totalDrawnAfter),
                balances,
                applyPaymentToLots appliedAmount servicing.DrawdownLots,
                servicing.CurrentRateReset,
                servicing.LastAccrualDate,
                servicing.LastPaymentDate,
                nextRevision,
                prependRevision nextRevision "InternalEvent" effectiveDate reason servicing.RevisionHistory,
                servicing.AccrualEntries)

        { Servicing = updated; AppliedAmount = appliedAmount }

    let postDailyAccrual (servicing: LoanServicingStateDto) (terms: DirectLendingTermsDto) accrualDate =
        let annualRate =
            if terms.RateTypeKind = RateTypeKind.Fixed then
                decimalOrDefault terms.FixedAnnualRate 0m
            else
                match box servicing.CurrentRateReset with
                | null -> 0m
                | _ -> servicing.CurrentRateReset.AllInRate

        // Clamp the effective rate within the contractual floor/cap bounds.
        let annualRate =
            DirectLendingInterop.ApplyRateBounds(terms.EffectiveRateFloor, terms.EffectiveRateCap, annualRate)

        let interestAmount =
            if servicing.Balances.PrincipalOutstanding > 0m then
                Math.Round(
                    DirectLendingInterop.CalculateDailyAccrualAmount(
                        servicing.Balances.PrincipalOutstanding,
                        annualRate,
                        int terms.DayCountBasis),
                    2,
                    MidpointRounding.AwayFromZero)
            else
                0m

        let commitmentFeeAmount =
            if terms.CommitmentFeeRate.HasValue then
                Math.Round(
                    DirectLendingInterop.CalculateDailyAccrualAmount(
                        max 0m (servicing.CurrentCommitment - servicing.TotalDrawn),
                        terms.CommitmentFeeRate.Value,
                        int terms.DayCountBasis),
                    2,
                    MidpointRounding.AwayFromZero)
            else
                0m

        let entry =
            DailyAccrualEntryDto(
                Guid.NewGuid(),
                accrualDate,
                interestAmount,
                commitmentFeeAmount,
                0m,
                annualRate,
                DateTimeOffset.UtcNow)

        let nextRevision = servicing.ServicingRevision + 1L
        let balances =
            OutstandingBalancesDto(
                servicing.Balances.PrincipalOutstanding,
                servicing.Balances.InterestAccruedUnpaid + interestAmount,
                servicing.Balances.CommitmentFeeAccruedUnpaid + commitmentFeeAmount,
                servicing.Balances.FeesAccruedUnpaid,
                servicing.Balances.PenaltyAccruedUnpaid)

        let accrualEntries =
            servicing.AccrualEntries
            |> Seq.append [ entry ]
            |> Seq.sortByDescending _.AccrualDate
            |> Seq.toArray

        let updated =
            LoanServicingStateDto(
                servicing.LoanId,
                servicing.Status,
                servicing.CurrentCommitment,
                servicing.TotalDrawn,
                servicing.AvailableToDraw,
                balances,
                servicing.DrawdownLots,
                servicing.CurrentRateReset,
                Nullable accrualDate,
                servicing.LastPaymentDate,
                nextRevision,
                prependRevision nextRevision "InternalEvent" accrualDate (sprintf "Daily accrual posted at annual rate %M." annualRate) servicing.RevisionHistory,
                accrualEntries)

        { Servicing = updated; Entry = entry }

    let chargePrepaymentPenalty (servicing: LoanServicingStateDto) (terms: DirectLendingTermsDto) (outstandingPrincipal: decimal) (effectiveDate: DateOnly) =
        if not terms.PrepaymentAllowed then
            failwith "Prepayment is not permitted under the current loan terms."

        let penaltyRate =
            if terms.PrepaymentPenaltyRate.HasValue then terms.PrepaymentPenaltyRate.Value else 0m

        let penaltyAmount =
            if penaltyRate > 0m then
                Math.Round(outstandingPrincipal * penaltyRate, 2, MidpointRounding.AwayFromZero)
            else
                0m

        let nextRevision = servicing.ServicingRevision + 1L
        let balances =
            OutstandingBalancesDto(
                servicing.Balances.PrincipalOutstanding,
                servicing.Balances.InterestAccruedUnpaid,
                servicing.Balances.CommitmentFeeAccruedUnpaid,
                servicing.Balances.FeesAccruedUnpaid,
                servicing.Balances.PenaltyAccruedUnpaid + penaltyAmount)

        let updated =
            LoanServicingStateDto(
                servicing.LoanId,
                servicing.Status,
                servicing.CurrentCommitment,
                servicing.TotalDrawn,
                servicing.AvailableToDraw,
                balances,
                servicing.DrawdownLots,
                servicing.CurrentRateReset,
                servicing.LastAccrualDate,
                servicing.LastPaymentDate,
                nextRevision,
                prependRevision nextRevision "InternalEvent" effectiveDate (sprintf "Prepayment penalty charged for %.2f." penaltyAmount) servicing.RevisionHistory,
                servicing.AccrualEntries)

        { Servicing = updated; PenaltyAmount = penaltyAmount }
