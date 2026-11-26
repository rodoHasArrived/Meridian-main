namespace Meridian.FSharp.SecurityMasterInterop

open System.Text.Json
open Meridian.FSharp.Domain

/// One lossless typed equity serializer shared by the economic document and flat codec. The
/// classification union is written as four keys; the generated Equity writer names each one.
module EquityCapitalCodec =
    let private preferredTermsToLegacy (preferred: PreferredTerms) =
        let liquidationKind, liquidationMultiple =
            match preferred.LiquidationPreference with
            | LiquidationPreference.Pari -> "Pari", None
            | LiquidationPreference.Senior multiple -> "Senior", Some multiple
            | LiquidationPreference.Subordinated -> "Subordinated", None

        {| dividendRate = preferred.DividendRate
           dividendType = preferred.DividendType |> DividendType.asString
           redemptionPrice = preferred.RedemptionPrice
           redemptionDate = preferred.RedemptionDate
           callableDate = preferred.CallableDate
           participationTerms =
                preferred.ParticipationTerms
                |> Option.map (fun terms ->
                    {| participatesInCommonDividends = terms.ParticipatesInCommonDividends
                       additionalDividendThreshold = terms.AdditionalDividendThreshold |})
           liquidationPreference =
                {| kind = liquidationKind
                   multiple = liquidationMultiple |} |}

    let private convertibleTermsToLegacy (convertible: ConvertibleTerms) =
        let (SecurityId underlyingSecurityId) = convertible.UnderlyingSecurityId
        {| underlyingSecurityId = underlyingSecurityId
           conversionRatio = convertible.ConversionRatio
           conversionPrice = convertible.ConversionPrice
           conversionStartDate = convertible.ConversionStartDate
           conversionEndDate = convertible.ConversionEndDate |}

    /// "Other" classifications serialize as the discriminant "Other" plus the raw label in
    /// otherClassification — writing the raw label into the classification slot produced a value
    /// the deserializer did not recognize, failing every read of the row.
    let classification (terms: EquityCapitalTerms) =
        match terms.Classification with
        | Some (EquityClassification.Other _) -> Some "Other"
        | Some value -> Some (EquityClassification.asString value)
        | None -> None

    let otherClassification (terms: EquityCapitalTerms) =
        match terms.Classification with
        | Some (EquityClassification.Other label) -> Some label
        | _ -> None

    let preferredTerms (terms: EquityCapitalTerms) =
        match terms.Classification with
        | Some (EquityClassification.Preferred preferred)
        | Some (EquityClassification.ConvertiblePreferred (preferred, _)) -> Some (preferredTermsToLegacy preferred)
        | _ -> None

    let convertibleTerms (terms: EquityCapitalTerms) =
        match terms.Classification with
        | Some (EquityClassification.Convertible convertible)
        | Some (EquityClassification.ConvertiblePreferred (_, convertible)) -> Some (convertibleTermsToLegacy convertible)
        | _ -> None

    let toPayload (terms: EquityCapitalTerms) =
        {| shareClass = terms.ShareClass
           votingRightsCat = terms.VotingRightsCat |> Option.map VotingRightsCat.asString
           classification = classification terms
           otherClassification = otherClassification terms
           preferredTerms = preferredTerms terms
           convertibleTerms = convertibleTerms terms |}

    let toJson (terms: EquityCapitalTerms) = JsonSerializer.Serialize(toPayload terms)

/// The written forms the generated flat writer cannot state as a record property: the bond coupon
/// union, written as one key per structure-specific field, and a swap leg, whose schedule-conventions
/// block is written from the version that declares it. The binding table names these functions;
/// changing what they write changes the stored format.
[<RequireQualifiedAccess>]
module internal FlatWriterParts =

    let couponType (coupon: BondCouponStructure) =
        match coupon with
        | BondCouponStructure.Fixed _ -> "Fixed"
        | BondCouponStructure.Floating _ -> "Floating"
        | BondCouponStructure.ZeroCoupon -> "ZeroCoupon"
        | BondCouponStructure.Step _ -> "Step"
        | BondCouponStructure.InflationLinked _ -> "InflationLinked"

    let floatingIndex (coupon: BondCouponStructure) =
        match coupon with
        | BondCouponStructure.Floating(index, _, _, _, _) -> Some index
        | _ -> None

    let floatingSpreadBps (coupon: BondCouponStructure) =
        match coupon with
        | BondCouponStructure.Floating(_, spread, _, _, _) -> spread
        | _ -> None

    let floatingCapRate (coupon: BondCouponStructure) =
        match coupon with
        | BondCouponStructure.Floating(_, _, cap, _, _) -> cap
        | _ -> None

    let floatingFloorRate (coupon: BondCouponStructure) =
        match coupon with
        | BondCouponStructure.Floating(_, _, _, floor, _) -> floor
        | _ -> None

    /// The dated step schedule. An empty array asserts "not a step coupon", so the field set stays
    /// constant across coupon variants (the round-trip guard's exact-match contract).
    let stepSchedule (coupon: BondCouponStructure) =
        match coupon with
        | BondCouponStructure.Step(schedule, _) ->
            schedule
            |> List.map (fun entry ->
                {| effectiveDate = entry.EffectiveDate
                   rate = entry.Rate |})
        | _ -> []

    let inflationIndex (coupon: BondCouponStructure) =
        match coupon with
        | BondCouponStructure.InflationLinked(_, indexName, _, _, _) -> Some indexName
        | _ -> None

    let inflationBaseIndexValue (coupon: BondCouponStructure) =
        match coupon with
        | BondCouponStructure.InflationLinked(_, _, baseIndexValue, _, _) -> baseIndexValue
        | _ -> None

    let inflationIndexRatio (coupon: BondCouponStructure) =
        match coupon with
        | BondCouponStructure.InflationLinked(_, _, _, indexRatio, _) -> indexRatio
        | _ -> None

    let private swapLegPayload (leg: SwapLeg) =
        {| legId = leg.LegId
           legType = leg.LegType
           currency = leg.Currency
           direction = leg.Direction
           index = leg.Index
           fixedRate = leg.FixedRate
           spreadBps = leg.SpreadBps
           currentIndexRate = leg.CurrentIndexRate
           notional = leg.Notional
           paymentFrequency = leg.PaymentFrequency
           dayCount = leg.DayCount |> Option.map Meridian.Contracts.SecurityMaster.DayCountConventions.TermLabel
           exchangesPrincipal = leg.ExchangesPrincipal
           notionalSchedule =
               leg.NotionalSchedule
               |> List.map (fun step -> {| effectiveDate = step.EffectiveDate; notional = step.Notional |})
           resetSchedule =
               leg.ResetSchedule
               |> List.map (fun entry -> {| resetDate = entry.ResetDate; fixingRate = entry.FixingRate |}) |}

    /// A swap leg as written. Boxed so the two shapes (with and without the block) share a list;
    /// the serializer writes each entry by its runtime shape.
    let swapLeg (writesScheduleConventions: bool) (leg: SwapLeg) : obj =
        if writesScheduleConventions then
            box
                {| swapLegPayload leg with
                     scheduleConventions = SecurityTermsWriteVersion.conventionsPayload leg.ScheduleConventions |}
        else
            box (swapLegPayload leg)
