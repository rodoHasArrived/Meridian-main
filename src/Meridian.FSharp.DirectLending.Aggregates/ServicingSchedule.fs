namespace Meridian.FSharp.DirectLending.Aggregates

open System
open Meridian.Contracts.DirectLending
open Meridian.Contracts.SecurityMaster.Schedules

/// Pure date rules for the persisted direct-lending servicing schedules: floating-rate reset dates,
/// fixing observation dates (lookback) and PIK capitalization dates. Every function is total over
/// valid terms and deterministic, so the daily worker, the command service, the rebuilder and the
/// schedule preview agree on the same dates.
/// See docs/engineering/blueprints/direct-lending-pik-capitalization-and-rate-resets.md.
module DirectLendingServicingSchedule =
    let private resetMonths (frequency: RateResetFrequency) =
        match frequency with
        | RateResetFrequency.Monthly -> 1
        | RateResetFrequency.Quarterly -> 3
        | RateResetFrequency.SemiAnnual -> 6
        | RateResetFrequency.Annual -> 12
        | other -> invalidArg "frequency" (sprintf "Unsupported rate reset frequency '%A'." other)

    let private paymentMonths (frequency: PaymentFrequency) =
        match frequency with
        | PaymentFrequency.Monthly -> Some 1
        | PaymentFrequency.Quarterly -> Some 3
        | PaymentFrequency.SemiAnnual -> Some 6
        | PaymentFrequency.Annual -> Some 12
        | _ -> None // Bullet: the only payment date is maturity.

    let private capitalizationMonths (frequency: PikCapitalizationFrequency) (terms: DirectLendingTermsDto) =
        match frequency with
        | PikCapitalizationFrequency.Monthly -> Some 1
        | PikCapitalizationFrequency.Quarterly -> Some 3
        | PikCapitalizationFrequency.SemiAnnual -> Some 6
        | PikCapitalizationFrequency.Annual -> Some 12
        | PikCapitalizationFrequency.OnInterestPaymentDate -> paymentMonths terms.PaymentFrequency
        | other -> invalidArg "frequency" (sprintf "Unsupported PIK capitalization frequency '%A'." other)

    /// Dates anchor + k*months for k = 0, 1, ... up to and including maturity, on the shared schedule
    /// kernel's anchored grid: each date is computed from the anchor (not from the previous date), so a
    /// 31st anchor gives Feb 28, Mar 31, Apr 30 instead of drifting to the 28th.
    let private stepDates (anchor: DateOnly) (months: int) (maturity: DateOnly) =
        ScheduleGenerator.RollDates(anchor, ScheduleTenor.Months months, ScheduleAnchorMode.Anchored)
        |> Seq.takeWhile (fun date -> date <= maturity)
        |> Seq.toArray

    /// All reset dates of a schedule, ascending and distinct. Explicit ResetDates replace the cadence.
    let resetDates (schedule: FloatingRateResetSchedule) (maturity: DateOnly) : DateOnly array =
        match schedule.ResetDates with
        | null -> stepDates schedule.FirstResetDate (resetMonths schedule.Frequency) maturity
        | explicitDates when explicitDates.Count = 0 ->
            stepDates schedule.FirstResetDate (resetMonths schedule.Frequency) maturity
        | explicitDates ->
            explicitDates |> Seq.filter (fun date -> date <= maturity) |> Seq.distinct |> Seq.sort |> Seq.toArray

    let isResetDate (schedule: FloatingRateResetSchedule) (maturity: DateOnly) (date: DateOnly) =
        resetDates schedule maturity |> Array.contains date

    /// The latest reset date on or before the given date: the reset that governs that day's rate.
    let latestResetOnOrBefore (schedule: FloatingRateResetSchedule) (maturity: DateOnly) (date: DateOnly) : DateOnly option =
        resetDates schedule maturity |> Array.filter (fun reset -> reset <= date) |> Array.tryLast

    let private isWeekend (date: DateOnly) =
        date.DayOfWeek = DayOfWeek.Saturday || date.DayOfWeek = DayOfWeek.Sunday

    let rec private rollBackToWeekday (date: DateOnly) =
        if isWeekend date then rollBackToWeekday (date.AddDays -1) else date

    /// The date whose fixing sets the rate for a reset date. Calendar lookback subtracts calendar
    /// days. Business lookback first rolls a weekend reset date back to Friday, then steps back the
    /// given number of weekdays. No holiday calendar is applied (documented follow-up).
    let fixingObservationDate (schedule: FloatingRateResetSchedule) (resetDate: DateOnly) =
        if schedule.LookbackDays < 0 then
            invalidArg "schedule" "LookbackDays must not be negative."

        match schedule.LookbackDayKind with
        | LookbackDayKind.Calendar -> resetDate.AddDays(-schedule.LookbackDays)
        | _ ->
            let mutable date = rollBackToWeekday resetDate
            let mutable remaining = schedule.LookbackDays
            while remaining > 0 do
                date <- rollBackToWeekday (date.AddDays -1)
                remaining <- remaining - 1
            date

    /// All PIK capitalization dates, ascending and distinct. Maturity is always a capitalization
    /// date so no accrued PIK is left uncapitalized at the end of the loan.
    let capitalizationDates (policy: PikCapitalizationPolicy) (terms: DirectLendingTermsDto) : DateOnly array =
        let maturity = terms.MaturityDate
        let cadence =
            match capitalizationMonths policy.Frequency terms with
            | Some months -> stepDates policy.AnchorDate months maturity
            | None -> [||]
        Array.append cadence [| maturity |] |> Array.distinct |> Array.sort

    let isCapitalizationDate (policy: PikCapitalizationPolicy) (terms: DirectLendingTermsDto) (date: DateOnly) =
        capitalizationDates policy terms |> Array.contains date

    // ---- C#-friendly wrappers -------------------------------------------------------------

    let ResetDates (schedule: FloatingRateResetSchedule, maturity: DateOnly) : DateOnly array =
        resetDates schedule maturity

    let IsResetDate (schedule: FloatingRateResetSchedule, maturity: DateOnly, date: DateOnly) =
        isResetDate schedule maturity date

    let LatestResetOnOrBefore (schedule: FloatingRateResetSchedule, maturity: DateOnly, date: DateOnly) : Nullable<DateOnly> =
        match latestResetOnOrBefore schedule maturity date with
        | Some reset -> Nullable reset
        | None -> Nullable()

    let FixingObservationDate (schedule: FloatingRateResetSchedule, resetDate: DateOnly) =
        fixingObservationDate schedule resetDate

    let CapitalizationDates (policy: PikCapitalizationPolicy, terms: DirectLendingTermsDto) : DateOnly array =
        capitalizationDates policy terms

    let IsCapitalizationDate (policy: PikCapitalizationPolicy, terms: DirectLendingTermsDto, date: DateOnly) =
        isCapitalizationDate policy terms date
