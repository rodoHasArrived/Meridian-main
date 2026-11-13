namespace Meridian.FSharp.SecurityMasterInterop

open System.Text.Json
open Meridian.FSharp.Domain

/// Typed structural modules use the same flat member spellings as the codec. No raw JSON is
/// retained in the domain, and no sort, numeric conversion or schedule normalization occurs here.
module StructuralTermsJson =
    let workflow (terms: BondWorkflowTerms) = BondWorkflowTermsJson.write terms

    let coupon (terms: BondCouponTerms) =
        let kind, rate, index, spread, cap, floor, steps, inflation, basis, ratio, dc =
            match terms.Structure with
            | BondCouponStructure.Fixed(rate, dc) -> "Fixed", Some rate, None, None, None, None, [], None, None, None, dc
            | BondCouponStructure.Floating(index, spread, cap, floor, dc) -> "Floating", None, Some index, spread, cap, floor, [], None, None, None, dc
            | BondCouponStructure.ZeroCoupon -> "ZeroCoupon", None, None, None, None, None, [], None, None, None, None
            | BondCouponStructure.Step(steps, dc) -> "Step", None, None, None, None, None, steps, None, None, None, dc
            | BondCouponStructure.InflationLinked(rate, index, basis, ratio, dc) -> "InflationLinked", Some rate, None, None, None, None, [], Some index, basis, ratio, dc
        JsonSerializer.SerializeToElement(
            {| couponType = kind; couponRate = rate; floatingIndex = index; spreadBps = spread
               capRate = cap; floorRate = floor
               stepSchedule = steps |> List.map (fun step -> {| effectiveDate = step.EffectiveDate; rate = step.Rate |})
               inflationIndex = inflation; inflationBaseIndexValue = basis; inflationIndexRatio = ratio
               dayCount = dc |> Option.map Meridian.Contracts.SecurityMaster.DayCountConventions.TermLabel |})

    let principal (terms: PrincipalScheduleTerms) =
        JsonSerializer.SerializeToElement(
            {| principalSchedule = terms.Payments |> List.map (fun entry -> {| paymentDate = entry.PaymentDate; amount = entry.Amount |}) |})

    let covenants (terms: LoanCovenantTerms) =
        JsonSerializer.SerializeToElement(
            {| covenants = terms.Entries |> List.map (fun entry -> {| covenantType = entry.CovenantType; threshold = entry.Threshold; notes = entry.Notes |}) |})

    let swapLegs (terms: SwapLegTerms) =
        let payload = JsonSerializer.SerializeToElement(
            {| legs = terms.Legs |> List.map (fun leg ->
                   {| legId = leg.LegId; legType = leg.LegType; currency = leg.Currency
                      direction = leg.Direction; index = leg.Index; fixedRate = leg.FixedRate
                      spreadBps = leg.SpreadBps; currentIndexRate = leg.CurrentIndexRate
                      notional = leg.Notional; paymentFrequency = leg.PaymentFrequency
                      dayCount = leg.DayCount |> Option.map Meridian.Contracts.SecurityMaster.DayCountConventions.TermLabel
                      exchangesPrincipal = leg.ExchangesPrincipal
                      notionalSchedule = leg.NotionalSchedule |> List.map (fun step -> {| effectiveDate = step.EffectiveDate; notional = step.Notional |})
                      resetSchedule = leg.ResetSchedule |> List.map (fun entry -> {| resetDate = entry.ResetDate; fixingRate = entry.FixingRate |})
                      scheduleConventions = SecurityTermsWriteVersion.conventionsPayload leg.ScheduleConventions |}) |})

        if terms.Legs |> List.exists (fun leg -> not (ScheduleConventions.isEmpty leg.ScheduleConventions)) then
            payload
        else
            // The v1 codec omits the block on every leg, rather than emitting stray null keys.
            let node = System.Text.Json.Nodes.JsonNode.Parse(payload.GetRawText())
            for leg in node.["legs"].AsArray() do
                leg.AsObject().Remove("scheduleConventions") |> ignore
            JsonSerializer.SerializeToElement(node)

    let declaredSchedule (terms: DeclaredScheduleTerms) includeBlock includeExercises =
        let node = System.Text.Json.Nodes.JsonObject()
        if includeBlock then
            node.["scheduleConventions"] <- JsonSerializer.SerializeToNode(SecurityTermsWriteVersion.conventionsPayload terms.Conventions)
        if includeExercises then
            node.["callSchedule"] <- JsonSerializer.SerializeToNode(SecurityTermsWriteVersion.callSchedulePayload terms.Exercises.Calls)
            node.["putSchedule"] <- JsonSerializer.SerializeToNode(SecurityTermsWriteVersion.putSchedulePayload terms.Exercises.Puts)
        JsonSerializer.SerializeToElement(node)
