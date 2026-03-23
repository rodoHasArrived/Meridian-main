namespace Meridian.FSharp.DirectLendingInterop

open System
open System.Runtime.CompilerServices
open Meridian.FSharp.Domain

[<Sealed; Extension>]
type DirectLendingInterop private () =

    static member private FromBasisCode(dayCountBasisCode: int) =
        match dayCountBasisCode with
        | 0 -> DirectLendingDayCountBasis.Act360
        | 1 -> DirectLendingDayCountBasis.Act365F
        | 2 -> DirectLendingDayCountBasis.Thirty360
        | _ -> invalidArg (nameof dayCountBasisCode) "Unsupported day-count basis code."

    static member CalculateAvailableToDraw(currentCommitment: decimal, totalDrawn: decimal) =
        DirectLending.calculateAvailableToDraw currentCommitment totalDrawn

    static member CalculateAllInRate(
        observedRate: decimal,
        spreadBps: decimal,
        floorRate: Nullable<decimal>,
        capRate: Nullable<decimal>) =
        let floorOption = if floorRate.HasValue then Some floorRate.Value else None
        let capOption = if capRate.HasValue then Some capRate.Value else None
        DirectLending.calculateAllInRate observedRate spreadBps floorOption capOption

    static member CalculateDailyAccrualAmount(
        principalBasis: decimal,
        annualRate: decimal,
        dayCountBasisCode: int) =
        DirectLending.calculateDailyAccrualAmount
            (DirectLendingInterop.FromBasisCode dayCountBasisCode)
            principalBasis
            annualRate

    static member ApplyPrincipalPayment(principalOutstanding: decimal, paymentAmount: decimal) =
        DirectLending.applyPrincipalPayment principalOutstanding paymentAmount
