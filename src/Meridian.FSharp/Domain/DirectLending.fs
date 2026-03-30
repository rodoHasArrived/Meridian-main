namespace Meridian.FSharp.Domain

open System

[<RequireQualifiedAccess>]
type DirectLendingDayCountBasis =
    | Act360
    | Act365F
    | Thirty360
    | ActualActualISDA

[<RequireQualifiedAccess>]
type DirectLendingRateType =
    | Fixed of annualRate: decimal
    | Floating of indexName: string * spreadBps: decimal * floorRate: decimal option * capRate: decimal option

[<RequireQualifiedAccess>]
module DirectLending =

    let private clampFloorCap (floorRate: decimal option) (capRate: decimal option) rate =
        let floored =
            match floorRate with
            | Some floor when rate < floor -> floor
            | _ -> rate

        match capRate with
        | Some cap when floored > cap -> cap
        | _ -> floored

    let dayCountDenominator basis =
        match basis with
        | DirectLendingDayCountBasis.Act360
        | DirectLendingDayCountBasis.Thirty360 -> 360m
        | DirectLendingDayCountBasis.Act365F -> 365m
        | DirectLendingDayCountBasis.ActualActualISDA -> 365m // denominator per period; numerator is actual days

    /// Returns the accrual fraction for a single day under each convention.
    let accrualFractionForDay (basis: DirectLendingDayCountBasis) (date: DateOnly) =
        match basis with
        | DirectLendingDayCountBasis.Act360 -> 1m / 360m
        | DirectLendingDayCountBasis.Act365F -> 1m / 365m
        | DirectLendingDayCountBasis.Thirty360 -> 1m / 360m
        | DirectLendingDayCountBasis.ActualActualISDA ->
            let year = date.Year
            let daysInYear = if DateTime.IsLeapYear year then 366m else 365m
            1m / daysInYear

    /// Net present value of collateral minus principal outstanding.
    let calculateCollateralCoverage (totalCollateralValue: decimal) (principalOutstanding: decimal) =
        totalCollateralValue - principalOutstanding

    /// Amortization amount for one period using straight-line method.
    let calculateStraightLineAmortization (originalAmount: decimal) (remainingPeriods: int) =
        if remainingPeriods <= 0 then 0m
        else originalAmount / decimal remainingPeriods

    /// PIK accrual: add interest to principal balance instead of cash payment.
    let calculatePikAccrual (principalBasis: decimal) (annualRate: decimal) (basis: DirectLendingDayCountBasis) (date: DateOnly) =
        if principalBasis <= 0m || annualRate <= 0m then 0m
        else principalBasis * annualRate * (accrualFractionForDay basis date)

    let calculateAvailableToDraw currentCommitment totalDrawn =
        max 0m (currentCommitment - totalDrawn)

    let calculateAllInRate observedRate spreadBps floorRate capRate =
        let spreadRate = spreadBps / 10000m
        observedRate + spreadRate
        |> clampFloorCap floorRate capRate

    let calculateDailyAccrualAmount basis principalBasis annualRate =
        if principalBasis <= 0m || annualRate <= 0m then
            0m
        else
            principalBasis * annualRate / dayCountDenominator basis

    /// Date-aware variant used for ActualActualISDA where leap-year matters.
    let calculateDailyAccrualAmountForDate basis principalBasis annualRate (date: DateOnly) =
        if principalBasis <= 0m || annualRate <= 0m then 0m
        else principalBasis * annualRate * (accrualFractionForDay basis date)

    let applyPrincipalPayment principalOutstanding paymentAmount =
        max 0m (principalOutstanding - max 0m paymentAmount)

    let isInterestOnlyPeriod (originationDate: DateOnly) (interestOnlyMonths: int) (asOfDate: DateOnly) =
        if interestOnlyMonths <= 0 then
            false
        else
            let ioEndDate = originationDate.AddMonths interestOnlyMonths
            asOfDate < ioEndDate

    let isWithinGracePeriod (dueDate: DateOnly) (gracePeriodDays: int option) (asOfDate: DateOnly) =
        match gracePeriodDays with
        | None | Some 0 -> false
        | Some days ->
            let graceEnd = dueDate.AddDays days
            asOfDate <= graceEnd

    let estimatePrepaymentPenalty (prepaymentAllowed: bool) (prepaymentPenaltyRate: decimal option) (outstandingPrincipal: decimal) =
        if not prepaymentAllowed then
            None
        else
            match prepaymentPenaltyRate with
            | None -> Some 0m
            | Some rate when rate <= 0m -> Some 0m
            | Some rate -> Some (outstandingPrincipal * rate)

    let applyRateBounds (effectiveRateFloor: decimal option) (effectiveRateCap: decimal option) rate =
        match effectiveRateFloor, effectiveRateCap with
        | Some floor, Some cap when cap < floor ->
            invalidArg "effectiveRateCap" (sprintf "EffectiveRateCap (%M) must not be less than EffectiveRateFloor (%M)." cap floor)
        | _ ->
            rate
            |> clampFloorCap effectiveRateFloor effectiveRateCap
