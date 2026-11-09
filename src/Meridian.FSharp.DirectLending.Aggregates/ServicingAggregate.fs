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

    let private appendResetHistory (servicing: LoanServicingStateDto) (reset: RateResetDto) : System.Collections.Generic.IReadOnlyList<RateResetDto> =
        let existing =
            match servicing.RateResetHistory with
            | null -> [||]
            | history -> history |> Seq.toArray
        Array.append (existing |> Array.filter (fun item -> item.EffectiveDate <> reset.EffectiveDate)) [| reset |]
        |> Array.sortBy _.EffectiveDate
        :> System.Collections.Generic.IReadOnlyList<RateResetDto>

    let private hasPolicy (value: obj) = not (isNull value)

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
        // A manual line never allocates more than the bucket owes: 1,000 "to interest" against
        // 10 accrued would book 990 of income that does not exist. Excess is reported as
        // unapplied, exactly as the automatic waterfall reports it.
        let mutable remaining = amount
        let take requested outstanding =
            let cap = min (max 0m requested) (max 0m outstanding)
            if remaining <= 0m || cap <= 0m then
                0m
            else
                let applied = min remaining cap
                remaining <- remaining - applied
                applied

        let owed = servicing.Balances
        let breakdown =
            PaymentBreakdownDto(
                take requestedBreakdown.ToInterest owed.InterestAccruedUnpaid,
                take requestedBreakdown.ToCommitmentFee owed.CommitmentFeeAccruedUnpaid,
                take requestedBreakdown.ToFees owed.FeesAccruedUnpaid,
                take requestedBreakdown.ToPenalty owed.PenaltyAccruedUnpaid,
                take requestedBreakdown.ToPrincipal owed.PrincipalOutstanding)

        MixedPaymentResolutionDto(breakdown, "Manual", "manual-v1", remaining)

    /// PRD-115: routes the cash left after allocation (UnappliedAmount on entry) by the loan's
    /// excess rule. Mirrored exactly by DirectLendingServiceSupport.RouteExcess in C#.
    let private routeExcess (servicing: LoanServicingStateDto) (terms: DirectLendingTermsDto) (allocated: MixedPaymentResolutionDto) =
        let excess = allocated.UnappliedAmount
        if excess <= 0m then
            allocated
        else
            let rule = terms.EffectiveExcessPaymentApplication
            let routed held toPrincipal toPrepaid penalty (note: string) =
                MixedPaymentResolutionDto(
                    allocated.Breakdown,
                    allocated.ResolutionBasis,
                    allocated.RuleVersion,
                    held,
                    ExcessToPrincipal = toPrincipal,
                    ExcessToPrepaidInterest = toPrepaid,
                    ExcessRuleApplied = Nullable rule,
                    ExcessRuleNote = note,
                    PrepaymentPenaltyCharged = penalty)

            match rule with
            | ExcessPaymentApplication.PrincipalPrepayment when not terms.PrepaymentAllowed ->
                routed excess 0m 0m 0m ExcessPaymentRuleNotes.PrepaymentNotAllowed
            | ExcessPaymentApplication.PrincipalPrepayment ->
                let principalAfterAllocation = max 0m (servicing.Balances.PrincipalOutstanding - allocated.Breakdown.ToPrincipal)
                let toPrincipal = min excess principalAfterAllocation
                let held = excess - toPrincipal
                let rate = decimalOrDefault terms.PrepaymentPenaltyRate 0m
                let penalty =
                    if rate > 0m && toPrincipal > 0m then
                        Math.Round(toPrincipal * rate, 2, MidpointRounding.AwayFromZero)
                    else
                        0m
                routed held toPrincipal 0m penalty (if held > 0m then ExcessPaymentRuleNotes.ExceedsOutstandingPrincipal else null)
            | ExcessPaymentApplication.PrepaidInterest ->
                routed 0m 0m excess 0m null
            | _ ->
                routed excess 0m 0m 0m null

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
                servicing.Balances.PenaltyAccruedUnpaid,
                PrepaidInterest = servicing.Balances.PrepaidInterest,
                UnappliedCash = servicing.Balances.UnappliedCash)

        let updated =
            LoanServicingStateDto(
                servicing.LoanId,
                servicing.Status,
                servicing.CurrentCommitment,
                updatedTotalDrawn,
                DirectLendingInterop.CalculateAvailableToDraw(servicing.CurrentCommitment, updatedTotalDrawn),
                balances,
                // Append, then a stable sort: same-day lots stay in booking order, as in the
                // in-memory service and the event rebuilder, so payments reduce the same lots.
                (Seq.append servicing.DrawdownLots [ lot ] |> Seq.sortBy _.DrawdownDate |> Seq.toArray),
                servicing.CurrentRateReset,
                servicing.LastAccrualDate,
                servicing.LastPaymentDate,
                nextRevision,
                prependRevision nextRevision "InternalEvent" settleDate (sprintf "Drawdown booked for %.2f." amount) servicing.RevisionHistory,
                servicing.AccrualEntries,
                Collateral = servicing.Collateral,
                UnamortizedDiscount = servicing.UnamortizedDiscount,
                UnamortizedPremium = servicing.UnamortizedPremium,
                IsPikToggled = servicing.IsPikToggled,
                PikInterestAccruedUnpaid = servicing.PikInterestAccruedUnpaid,
                LastPikCapitalizationDate = servicing.LastPikCapitalizationDate,
                RateResetHistory = servicing.RateResetHistory,
                PikCapitalizations = servicing.PikCapitalizations)

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

        // Loans with a reset schedule record every reset, manual ones included, so the projection
        // and the worker see the full reset history. Legacy loans keep a single current reset.
        let hasSchedule = not (isNull (box terms.RateResetSchedule))
        let reset =
            if hasSchedule then
                RateResetDto(effectiveDate, stringOrDefault terms.InterestIndexName "FloatingIndex", observedRate, spread, allInRate, sourceRef, ResetKind = Nullable RateResetKind.Manual)
            else
                RateResetDto(effectiveDate, stringOrDefault terms.InterestIndexName "FloatingIndex", observedRate, spread, allInRate, sourceRef)
        let history = if hasSchedule then appendResetHistory servicing reset else servicing.RateResetHistory

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
                reset,
                servicing.LastAccrualDate,
                servicing.LastPaymentDate,
                nextRevision,
                prependRevision nextRevision "InternalEvent" effectiveDate (sprintf "Rate reset applied at %M." allInRate) servicing.RevisionHistory,
                servicing.AccrualEntries,
                Collateral = servicing.Collateral,
                UnamortizedDiscount = servicing.UnamortizedDiscount,
                UnamortizedPremium = servicing.UnamortizedPremium,
                IsPikToggled = servicing.IsPikToggled,
                PikInterestAccruedUnpaid = servicing.PikInterestAccruedUnpaid,
                LastPikCapitalizationDate = servicing.LastPikCapitalizationDate,
                RateResetHistory = history,
                PikCapitalizations = servicing.PikCapitalizations)

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
                servicing.Balances.PenaltyAccruedUnpaid,
                PrepaidInterest = servicing.Balances.PrepaidInterest,
                UnappliedCash = servicing.Balances.UnappliedCash)

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
                servicing.AccrualEntries,
                Collateral = servicing.Collateral,
                UnamortizedDiscount = servicing.UnamortizedDiscount,
                UnamortizedPremium = servicing.UnamortizedPremium,
                IsPikToggled = servicing.IsPikToggled,
                PikInterestAccruedUnpaid = servicing.PikInterestAccruedUnpaid,
                LastPikCapitalizationDate = servicing.LastPikCapitalizationDate,
                RateResetHistory = servicing.RateResetHistory,
                PikCapitalizations = servicing.PikCapitalizations)

        { Servicing = updated; AppliedAmount = appliedAmount }

    let applyMixedPayment (servicing: LoanServicingStateDto) (terms: DirectLendingTermsDto) amount breakdown effectiveDate =
        let allocated =
            match box breakdown with
            | null ->
                let auto = autoAllocate servicing terms amount effectiveDate
                MixedPaymentResolutionDto(auto, "WaterfallAuto", "waterfall-v1", amount - auto.TotalAllocated)
            | _ -> buildResolution servicing amount breakdown

        let resolution = routeExcess servicing terms allocated
        let principalReduction = resolution.Breakdown.ToPrincipal + resolution.ExcessToPrincipal

        let updatedBalances =
            OutstandingBalancesDto(
                max 0m (servicing.Balances.PrincipalOutstanding - principalReduction),
                max 0m (servicing.Balances.InterestAccruedUnpaid - resolution.Breakdown.ToInterest),
                max 0m (servicing.Balances.CommitmentFeeAccruedUnpaid - resolution.Breakdown.ToCommitmentFee),
                max 0m (servicing.Balances.FeesAccruedUnpaid - resolution.Breakdown.ToFees),
                max 0m (servicing.Balances.PenaltyAccruedUnpaid - resolution.Breakdown.ToPenalty) + resolution.PrepaymentPenaltyCharged,
                PrepaidInterest = servicing.Balances.PrepaidInterest + resolution.ExcessToPrepaidInterest,
                UnappliedCash = servicing.Balances.UnappliedCash + resolution.UnappliedAmount)

        let totalDrawnAfter = max 0m (servicing.TotalDrawn - principalReduction)
        let nextRevision = servicing.ServicingRevision + 1L
        let updated =
            LoanServicingStateDto(
                servicing.LoanId,
                servicing.Status,
                servicing.CurrentCommitment,
                totalDrawnAfter,
                DirectLendingInterop.CalculateAvailableToDraw(servicing.CurrentCommitment, totalDrawnAfter),
                updatedBalances,
                applyPaymentToLots principalReduction servicing.DrawdownLots,
                servicing.CurrentRateReset,
                servicing.LastAccrualDate,
                Nullable effectiveDate,
                nextRevision,
                prependRevision nextRevision "InternalEvent" effectiveDate (sprintf "Mixed payment applied for %.2f." amount) servicing.RevisionHistory,
                servicing.AccrualEntries,
                Collateral = servicing.Collateral,
                UnamortizedDiscount = servicing.UnamortizedDiscount,
                UnamortizedPremium = servicing.UnamortizedPremium,
                IsPikToggled = servicing.IsPikToggled,
                PikInterestAccruedUnpaid = servicing.PikInterestAccruedUnpaid,
                LastPikCapitalizationDate = servicing.LastPikCapitalizationDate,
                RateResetHistory = servicing.RateResetHistory,
                PikCapitalizations = servicing.PikCapitalizations)

        let cashTransactionId = Guid.NewGuid()
        // PRD-115: routed excess gets its own rows, so allocations always sum to the cash received.
        let excessRule =
            if resolution.ExcessRuleApplied.HasValue then
                $"Excess{resolution.ExcessRuleApplied.Value}"
            else
                "Excess"

        let allocations =
            [|
                ("Interest", resolution.Breakdown.ToInterest, resolution.ResolutionBasis)
                ("CommitmentFee", resolution.Breakdown.ToCommitmentFee, resolution.ResolutionBasis)
                ("Fees", resolution.Breakdown.ToFees, resolution.ResolutionBasis)
                ("Penalty", resolution.Breakdown.ToPenalty, resolution.ResolutionBasis)
                ("Principal", resolution.Breakdown.ToPrincipal, resolution.ResolutionBasis)
                ("PrincipalPrepayment", resolution.ExcessToPrincipal, excessRule)
                ("PrepaidInterest", resolution.ExcessToPrepaidInterest, excessRule)
                ("UnappliedCash", resolution.UnappliedAmount, excessRule)
            |]
            |> Array.filter (fun (_, allocatedAmount, _) -> allocatedAmount > 0m)
            |> Array.mapi (fun index (targetType, allocatedAmount, rule) ->
                { SequenceNumber = index + 1
                  TargetType = targetType
                  TargetReference = Guid.NewGuid()
                  Amount = allocatedAmount
                  AllocationRule = rule })

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
                servicing.Balances.PenaltyAccruedUnpaid,
                PrepaidInterest = servicing.Balances.PrepaidInterest,
                UnappliedCash = servicing.Balances.UnappliedCash)

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
                servicing.AccrualEntries,
                Collateral = servicing.Collateral,
                UnamortizedDiscount = servicing.UnamortizedDiscount,
                UnamortizedPremium = servicing.UnamortizedPremium,
                IsPikToggled = servicing.IsPikToggled,
                PikInterestAccruedUnpaid = servicing.PikInterestAccruedUnpaid,
                LastPikCapitalizationDate = servicing.LastPikCapitalizationDate,
                RateResetHistory = servicing.RateResetHistory,
                PikCapitalizations = servicing.PikCapitalizations)

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
                servicing.Balances.PenaltyAccruedUnpaid,
                PrepaidInterest = servicing.Balances.PrepaidInterest,
                UnappliedCash = servicing.Balances.UnappliedCash)

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
                servicing.AccrualEntries,
                Collateral = servicing.Collateral,
                UnamortizedDiscount = servicing.UnamortizedDiscount,
                UnamortizedPremium = servicing.UnamortizedPremium,
                IsPikToggled = servicing.IsPikToggled,
                PikInterestAccruedUnpaid = servicing.PikInterestAccruedUnpaid,
                LastPikCapitalizationDate = servicing.LastPikCapitalizationDate,
                RateResetHistory = servicing.RateResetHistory,
                PikCapitalizations = servicing.PikCapitalizations)

        { Servicing = updated; AppliedAmount = appliedAmount }

    let postDailyAccrual (servicing: LoanServicingStateDto) (terms: DirectLendingTermsDto) (accrualDate: DateOnly) =
        let annualRate =
            if terms.RateTypeKind = RateTypeKind.Fixed then
                decimalOrDefault terms.FixedAnnualRate 0m
            else
                match box servicing.CurrentRateReset with
                | null when hasPolicy (box terms.RateResetSchedule) ->
                    failwith "Floating-rate loans with a reset schedule require an applied rate reset before accrual."
                | null -> 0m
                | _ -> servicing.CurrentRateReset.AllInRate

        // Clamp the effective rate within the contractual floor/cap bounds.
        let annualRate =
            DirectLendingInterop.ApplyRateBounds(terms.EffectiveRateFloor, terms.EffectiveRateCap, annualRate)

        // Capitalization policy (opt-in): the PIK share of the day's interest accrues as a PIK
        // receivable and is capitalized on the policy's dates. Without a policy the legacy toggle
        // applies: the whole day's interest capitalizes into principal immediately.
        let policy = terms.PikCapitalization
        let hasCapitalizationPolicy = hasPolicy (box policy)
        let pikShare =
            if not hasCapitalizationPolicy then 0m
            elif policy.Mode = PikElection.Always || servicing.IsPikToggled then
                RateUnitNormalizer.ToFraction(policy.PikShare, DirectLendingRateField.PikShare)
            else 0m

        // Legacy PIK posture: interest capitalizes into the principal balance instead of
        // accruing as a cash receivable.
        let isPikAccrual = not hasCapitalizationPolicy && servicing.IsPikToggled

        // Use the date-aware day-count helpers so Act/Act accrues against the actual
        // year length of the accrual date (366 in leap years); other conventions
        // keep their fixed denominators.
        let accruedInterest =
            if servicing.Balances.PrincipalOutstanding > 0m then
                Math.Round(
                    (if isPikAccrual then
                        DirectLendingInterop.CalculatePikAccrual(
                            servicing.Balances.PrincipalOutstanding,
                            annualRate,
                            int terms.DayCountBasis,
                            accrualDate)
                     else
                        DirectLendingInterop.CalculateDailyAccrualAmountForDate(
                            servicing.Balances.PrincipalOutstanding,
                            annualRate,
                            int terms.DayCountBasis,
                            accrualDate)),
                    2,
                    MidpointRounding.AwayFromZero)
            else
                0m

        let deferredPik =
            if hasCapitalizationPolicy && pikShare > 0m then
                Math.Round(accruedInterest * pikShare, 2, MidpointRounding.AwayFromZero)
            else
                0m

        let interestAmount = if isPikAccrual then 0m else accruedInterest - deferredPik
        let pikInterestAmount = if isPikAccrual then accruedInterest else deferredPik
        let capitalizedToday = if isPikAccrual then pikInterestAmount else 0m

        // PRD-115: the day's cash interest is settled from prepaid interest first; only the rest
        // becomes a receivable. PIK interest never consumes prepaid interest.
        let prepaidInterestConsumed = min (max 0m servicing.Balances.PrepaidInterest) (max 0m interestAmount)

        let commitmentFeeAmount =
            if terms.CommitmentFeeRate.HasValue then
                Math.Round(
                    DirectLendingInterop.CalculateDailyAccrualAmountForDate(
                        max 0m (servicing.CurrentCommitment - servicing.TotalDrawn),
                        terms.CommitmentFeeRate.Value,
                        int terms.DayCountBasis,
                        accrualDate),
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
                DateTimeOffset.UtcNow,
                pikInterestAmount,
                hasCapitalizationPolicy && deferredPik > 0m)

        let nextRevision = servicing.ServicingRevision + 1L
        let balances =
            OutstandingBalancesDto(
                servicing.Balances.PrincipalOutstanding + capitalizedToday,
                servicing.Balances.InterestAccruedUnpaid + interestAmount - prepaidInterestConsumed,
                servicing.Balances.CommitmentFeeAccruedUnpaid + commitmentFeeAmount,
                servicing.Balances.FeesAccruedUnpaid,
                servicing.Balances.PenaltyAccruedUnpaid,
                PrepaidInterest = servicing.Balances.PrepaidInterest - prepaidInterestConsumed,
                UnappliedCash = servicing.Balances.UnappliedCash)

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
                prependRevision
                    nextRevision
                    "InternalEvent"
                    accrualDate
                    (if isPikAccrual then
                        sprintf "Daily PIK accrual capitalized at annual rate %M." annualRate
                     elif deferredPik > 0m then
                        sprintf "Daily accrual posted at annual rate %M; PIK %.2f deferred to capitalization." annualRate deferredPik
                     else
                        sprintf "Daily accrual posted at annual rate %M." annualRate)
                    servicing.RevisionHistory,
                accrualEntries,
                Collateral = servicing.Collateral,
                UnamortizedDiscount = servicing.UnamortizedDiscount,
                UnamortizedPremium = servicing.UnamortizedPremium,
                IsPikToggled = servicing.IsPikToggled,
                PikInterestAccruedUnpaid = servicing.PikInterestAccruedUnpaid + deferredPik,
                LastPikCapitalizationDate = servicing.LastPikCapitalizationDate,
                RateResetHistory = servicing.RateResetHistory,
                PikCapitalizations = servicing.PikCapitalizations)

        { Servicing = updated
          Entry = entry
          PrepaidInterestConsumed = prepaidInterestConsumed }

    /// Moves accrued PIK interest into principal on a capitalization date of the loan's policy.
    /// The day's accrual must already be posted, so capitalized PIK compounds from the next day.
    let capitalizePik (servicing: LoanServicingStateDto) (terms: DirectLendingTermsDto) (capitalizationDate: DateOnly) =
        let policy = terms.PikCapitalization
        if not (hasPolicy (box policy)) then
            failwith "PIK capitalization requires a PIK capitalization policy on the loan terms."

        if not (DirectLendingServicingSchedule.isCapitalizationDate policy terms capitalizationDate) then
            failwithf "%O is not a PIK capitalization date under the loan's policy." capitalizationDate

        if not servicing.LastAccrualDate.HasValue || servicing.LastAccrualDate.Value < capitalizationDate then
            failwithf "Accrual must be posted through %O before PIK is capitalized." capitalizationDate

        if servicing.LastPikCapitalizationDate.HasValue && servicing.LastPikCapitalizationDate.Value >= capitalizationDate then
            failwithf "PIK has already been capitalized through %O." servicing.LastPikCapitalizationDate.Value

        let amount = servicing.PikInterestAccruedUnpaid
        let periodStart =
            if servicing.LastPikCapitalizationDate.HasValue then servicing.LastPikCapitalizationDate.Value.AddDays 1
            else terms.OriginationDate
        let capitalization = PikCapitalizationDto(capitalizationDate, periodStart, capitalizationDate, amount, DateTimeOffset.UtcNow)
        let capitalizations =
            match servicing.PikCapitalizations with
            | null -> [| capitalization |]
            | existing -> Array.append (existing |> Seq.toArray) [| capitalization |]

        let nextRevision = servicing.ServicingRevision + 1L
        let balances =
            OutstandingBalancesDto(
                servicing.Balances.PrincipalOutstanding + amount,
                servicing.Balances.InterestAccruedUnpaid,
                servicing.Balances.CommitmentFeeAccruedUnpaid,
                servicing.Balances.FeesAccruedUnpaid,
                servicing.Balances.PenaltyAccruedUnpaid,
                PrepaidInterest = servicing.Balances.PrepaidInterest,
                UnappliedCash = servicing.Balances.UnappliedCash)

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
                prependRevision nextRevision "PikCapitalization" capitalizationDate (sprintf "PIK interest of %.2f capitalized into principal." amount) servicing.RevisionHistory,
                servicing.AccrualEntries,
                Collateral = servicing.Collateral,
                UnamortizedDiscount = servicing.UnamortizedDiscount,
                UnamortizedPremium = servicing.UnamortizedPremium,
                IsPikToggled = servicing.IsPikToggled,
                PikInterestAccruedUnpaid = 0m,
                LastPikCapitalizationDate = Nullable capitalizationDate,
                RateResetHistory = servicing.RateResetHistory,
                PikCapitalizations = capitalizations)

        { Servicing = updated; Capitalization = capitalization }

    /// Applies the reset scheduled for resetDate from an observed fixing. Units come from the
    /// schedule's declared rates (RateUnitNormalizer); the floor and cap bound either the index or
    /// the all-in rate as the schedule declares.
    let applyScheduledRateReset (servicing: LoanServicingStateDto) (terms: DirectLendingTermsDto) (resetDate: DateOnly) (fixing: ScheduledFixing) =
        let schedule = terms.RateResetSchedule
        if not (hasPolicy (box schedule)) then
            failwith "Scheduled rate resets require a rate reset schedule on the loan terms."

        if not (DirectLendingServicingSchedule.isResetDate schedule terms.MaturityDate resetDate) then
            failwithf "%O is not a scheduled reset date." resetDate

        let expectedObservation = DirectLendingServicingSchedule.fixingObservationDate schedule resetDate
        if fixing.ObservationDate <> expectedObservation then
            failwithf "Reset %O needs the fixing observed on %O, not %O." resetDate expectedObservation fixing.ObservationDate

        let spread = RateUnitNormalizer.ToFraction(schedule.Spread, DirectLendingRateField.Spread)
        let floor =
            if isNull (box schedule.Floor) then Nullable() else Nullable(RateUnitNormalizer.ToFraction(schedule.Floor, DirectLendingRateField.Floor))
        let cap =
            if isNull (box schedule.Cap) then Nullable() else Nullable(RateUnitNormalizer.ToFraction(schedule.Cap, DirectLendingRateField.Cap))

        let allInRate =
            match schedule.FloorCapAppliesTo with
            | FloorCapApplication.AllIn -> DirectLendingInterop.ApplyRateBounds(floor, cap, fixing.RateFraction + spread)
            | _ -> DirectLendingInterop.ApplyRateBounds(floor, cap, fixing.RateFraction) + spread

        let reset =
            RateResetDto(
                resetDate,
                schedule.IndexName,
                fixing.RateFraction,
                RateUnitNormalizer.ToBasisPoints spread,
                allInRate,
                fixing.SourceRef,
                ResetKind = Nullable RateResetKind.Scheduled,
                FixingObservationDate = Nullable fixing.ObservationDate,
                FixingId = fixing.FixingId,
                FixingRevision = fixing.FixingRevision)

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
                reset,
                servicing.LastAccrualDate,
                servicing.LastPaymentDate,
                nextRevision,
                prependRevision nextRevision "ScheduledRateReset" resetDate (sprintf "Scheduled rate reset applied at %M (fixing observed %O)." allInRate fixing.ObservationDate) servicing.RevisionHistory,
                servicing.AccrualEntries,
                Collateral = servicing.Collateral,
                UnamortizedDiscount = servicing.UnamortizedDiscount,
                UnamortizedPremium = servicing.UnamortizedPremium,
                IsPikToggled = servicing.IsPikToggled,
                PikInterestAccruedUnpaid = servicing.PikInterestAccruedUnpaid,
                LastPikCapitalizationDate = servicing.LastPikCapitalizationDate,
                RateResetHistory = appendResetHistory servicing reset,
                PikCapitalizations = servicing.PikCapitalizations)

        { Servicing = updated; AllInRate = allInRate }

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
                servicing.Balances.PenaltyAccruedUnpaid + penaltyAmount,
                PrepaidInterest = servicing.Balances.PrepaidInterest,
                UnappliedCash = servicing.Balances.UnappliedCash)

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
                servicing.AccrualEntries,
                Collateral = servicing.Collateral,
                UnamortizedDiscount = servicing.UnamortizedDiscount,
                UnamortizedPremium = servicing.UnamortizedPremium,
                IsPikToggled = servicing.IsPikToggled,
                PikInterestAccruedUnpaid = servicing.PikInterestAccruedUnpaid,
                LastPikCapitalizationDate = servicing.LastPikCapitalizationDate,
                RateResetHistory = servicing.RateResetHistory,
                PikCapitalizations = servicing.PikCapitalizations)

        { Servicing = updated; PenaltyAmount = penaltyAmount }
