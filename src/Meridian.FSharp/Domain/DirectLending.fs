namespace Meridian.FSharp.Domain

open System

[<RequireQualifiedAccess>]
type DirectLendingDayCountBasis =
    | Act360
    | Act365F
    | Thirty360

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

    let applyPrincipalPayment principalOutstanding paymentAmount =
        max 0m (principalOutstanding - max 0m paymentAmount)
