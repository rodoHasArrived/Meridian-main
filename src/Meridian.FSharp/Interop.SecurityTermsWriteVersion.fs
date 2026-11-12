namespace Meridian.FSharp.SecurityMasterInterop

open System
open System.Text.Json
open Meridian.Contracts.SecurityMaster
open Meridian.FSharp.Domain

/// The asset-class terms version a flat payload is written at (ADR-024, readable before writable):
/// the class's S2 document floor, raised to the highest version among the versioned terms the record
/// holds (scheduleConventions; callSchedule and putSchedule). A record read from a node that writes a
/// newer version (a bond stamped 3 with declared calls) therefore re-serializes with its terms and
/// stamp; a record that declares nothing preserves its pre-contract floor and bytes.
[<RequireQualifiedAccess>]
module internal SecurityTermsWriteVersion =
    let private scheduleConventionsKey = "scheduleConventions"
    let private callScheduleKey = "callSchedule"
    let private putScheduleKey = "putSchedule"

    let private fieldSince (assetClass: string) (key: string) =
        match SecurityAssetTermsSchema.Field(assetClass, key) with
        | null -> invalidOp (sprintf "The term schema declares no '%s' on '%s'; the flat writer and the schema have drifted." key assetClass)
        | field -> field.SinceTermsVersion

    let private elementFieldSince (assetClass: string) (container: string) (key: string) =
        match SecurityAssetTermsSchema.ElementFields(assetClass, container) |> Seq.tryFind (fun field -> field.Key = key) with
        | Some field -> field.SinceTermsVersion
        | None -> invalidOp (sprintf "The term schema declares no '%s.%s' on '%s'; the flat writer and the schema have drifted." container key assetClass)

    /// The terms version that introduced the scheduleConventions block on the record's class (on a
    /// swap, on each leg).
    let blockSince (kind: SecurityKind) =
        let assetClass = SecurityKind.assetClass kind
        match kind with
        | SecurityKind.Swap _ -> elementFieldSince assetClass "legs" scheduleConventionsKey
        | _ -> fieldSince assetClass scheduleConventionsKey

    /// The terms version that introduced the callSchedule and putSchedule arrays on the record's
    /// class. The writer emits both together, in the tier above scheduleConventions.
    let schedulesSince (kind: SecurityKind) =
        let assetClass = SecurityKind.assetClass kind
        match kind with
        | SecurityKind.Bond _
        | SecurityKind.CertificateOfDeposit _
        | SecurityKind.StructuredCredit _ ->
            let calls = fieldSince assetClass callScheduleKey
            let puts = fieldSince assetClass putScheduleKey
            if calls <> puts || calls <= blockSince kind then
                invalidOp
                    (sprintf
                        "The flat writer emits callSchedule and putSchedule together, in the tier above scheduleConventions, but '%s' declares them at versions %d and %d; the schema has drifted."
                        assetClass
                        calls
                        puts)
            calls
        | _ -> invalidOp (sprintf "'%s' declares no call or put schedules." assetClass)

    let private declares (conventions: ScheduleConventions) = not (ScheduleConventions.isEmpty conventions)

    let private holdsSchedules (schedules: CallPutSchedules) = not (CallPutSchedules.isEmpty schedules)

    /// The version the record's flat payload is written at: its S2 document floor, raised to the
    /// highest version among the versioned terms the record holds.
    let effective (kind: SecurityKind) =
        let floor = SecurityAssetClassCatalog.GetStampFloor(SecurityKind.assetClass kind)
        // Every versioned term the record can hold, with whether it holds it.
        let held =
            match kind with
            | SecurityKind.Bond terms ->
                [ declares terms.ScheduleConventions, blockSince kind
                  holdsSchedules terms.CallPutSchedules, schedulesSince kind ]
            | SecurityKind.CertificateOfDeposit terms ->
                [ declares terms.ScheduleConventions, blockSince kind
                  holdsSchedules terms.CallPutSchedules, schedulesSince kind ]
            | SecurityKind.StructuredCredit terms ->
                [ declares terms.ScheduleConventions, blockSince kind
                  holdsSchedules terms.CallPutSchedules, schedulesSince kind ]
            | SecurityKind.Swap terms ->
                [ terms.Legs |> List.exists (fun leg -> declares leg.ScheduleConventions), blockSince kind ]
            | _ -> []
        let rateConvention =
            match kind with
            | SecurityKind.Deposit terms -> terms.RateConvention
            | SecurityKind.CertificateOfDeposit terms -> terms.RateConvention
            | SecurityKind.CommercialPaper terms -> terms.RateConvention
            | SecurityKind.TreasuryBill terms -> terms.RateConvention
            | SecurityKind.Repo terms -> terms.RateConvention
            | SecurityKind.CashSweep terms -> terms.RateConvention
            | SecurityKind.DirectLoan terms -> terms.RateConvention
            | SecurityKind.StructuredCredit terms -> terms.RateConvention
            | SecurityKind.Swap terms -> terms.RateConvention
            | SecurityKind.Swaption terms -> terms.RateConvention
            | SecurityKind.CapFloor terms -> terms.RateConvention
            | SecurityKind.StructuredNote terms -> terms.RateConvention
            | _ -> None
        let declaredFloor =
            if rateConvention.IsSome then max floor (fieldSince (SecurityKind.assetClass kind) "rateUnit")
            else floor
        held |> List.filter fst |> List.fold (fun version (_, since) -> max version since) declaredFloor

    /// The canonical written block (frozen by ADR-024): null when nothing is declared, otherwise all
    /// twelve members in alphabetical order, with null or [] for an unset one.
    let conventionsPayload (conventions: ScheduleConventions) =
        if ScheduleConventions.isEmpty conventions then
            None
        else
            Some
                {| accrualAdjusted = conventions.AccrualAdjusted
                   businessDayConvention = conventions.BusinessDayConvention
                   couponCashBasis = conventions.CouponCashBasis
                   couponRateUnit = conventions.CouponRateUnit
                   direction = conventions.Direction
                   endOfMonthRule = conventions.EndOfMonthRule
                   firstRegularDate = conventions.FirstRegularDate
                   lastRegularDate = conventions.LastRegularDate
                   paymentCalendars = conventions.PaymentCalendars
                   paymentLagBusinessDays = conventions.PaymentLagBusinessDays
                   rollConvention = conventions.RollConvention
                   stubPolicy = conventions.StubPolicy |}

    /// The canonical written call schedule (frozen by ADR-024): one object per entry in stored order,
    /// carrying every member in alphabetical order with null for an unset one; [] when none is declared.
    let callSchedulePayload (calls: CallScheduleTerm list) =
        calls
        |> List.map (fun call ->
            {| callDate = call.CallDate
               callEndDate = call.CallEndDate
               callPrice = call.CallPrice
               exerciseFrequency = call.ExerciseFrequency
               exerciseStyle = call.ExerciseStyle
               makeWholeSpreadBps = call.MakeWholeSpreadBps
               maximumNoticeDays = call.MaximumNoticeDays
               minimumNoticeDays = call.MinimumNoticeDays
               noticeDayType = call.NoticeDayType
               partialRedemption = call.PartialRedemption
               priceBasis = call.PriceBasis
               priceType = call.PriceType |})

    /// The canonical written put schedule, under the same rules as the call schedule.
    let putSchedulePayload (puts: PutScheduleTerm list) =
        puts
        |> List.map (fun put ->
            {| exerciseFrequency = put.ExerciseFrequency
               exerciseStyle = put.ExerciseStyle
               maximumNoticeDays = put.MaximumNoticeDays
               minimumNoticeDays = put.MinimumNoticeDays
               noticeDayType = put.NoticeDayType
               priceBasis = put.PriceBasis
               putDate = put.PutDate
               putEndDate = put.PutEndDate
               putPrice = put.PutPrice |})
