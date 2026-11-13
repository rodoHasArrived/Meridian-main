namespace Meridian.FSharp.SecurityMasterInterop

open System.Text.Json
open Meridian.FSharp.Domain

/// Flat v5 convention writer shared by the class codec and economic module projection.
module BondWorkflowTermsJson =
    let write (terms: BondWorkflowTerms) =
        let node = System.Text.Json.Nodes.JsonObject()
        let text (key: string) (value: string option) =
            value |> Option.iter (fun item -> node.[key] <- System.Text.Json.Nodes.JsonValue.Create(item))
        let count (key: string) (value: int option) =
            value |> Option.iter (fun item -> node.[key] <- System.Text.Json.Nodes.JsonValue.Create(item))
        let flag (key: string) (value: bool option) =
            value |> Option.iter (fun item -> node.[key] <- System.Text.Json.Nodes.JsonValue.Create(item))
        let ratio (key: string) (value: decimal option) =
            value |> Option.iter (fun item -> node.[key] <- System.Text.Json.Nodes.JsonValue.Create(item))
        text "floatingMethod" terms.FloatingMethod
        text "fixingCalendarId" terms.FixingCalendarId
        count "lookbackBusinessDays" terms.LookbackBusinessDays
        text "observationMethod" terms.ObservationMethod
        text "observationWeighting" terms.ObservationWeighting
        count "lockoutBusinessDays" terms.LockoutBusinessDays
        count "lagMonths" terms.LagMonths
        flag "interpolateMonthly" terms.InterpolateMonthly
        ratio "couponFloorRatio" terms.CouponFloorRatio
        ratio "redemptionFloorRatio" terms.RedemptionFloorRatio
        count "exCouponBusinessDays" terms.ExCouponBusinessDays
        let payload = JsonSerializer.SerializeToElement(node)
        let mutable path = ""
        let mutable reason: string = null
        if not (Meridian.Contracts.SecurityMaster.SecurityAssetTermsFieldEditValidator.TryValidate(
                    "Bond", "assetSpecificTerms.bondWorkflow", payload.GetRawText(), &path, &reason)) then
            invalidOp reason
        payload
