namespace Meridian.FSharp.Domain

open System

/// A single identified break between a projected and an actual cash event
[<CLIMutable>]
type CashFlowBreak = {
    BreakId            : Guid
    SecurityId         : Guid
    FlowId             : Guid
    EventKind          : string
    ClassificationLabel: string
    SeverityLabel      : string
    BreakAmount        : decimal
    Currency           : string
    AsOf               : DateTimeOffset
    Notes              : string option
}

[<RequireQualifiedAccess>]
module MatchToleranceDefaults =

    let ``default`` : MatchTolerance = {
        AmountTolerancePct   = 0.01m
        TimingToleranceDays  = 2
        RequireCurrencyExact = true
    }

    let strict : MatchTolerance = {
        AmountTolerancePct   = 0m
        TimingToleranceDays  = 0
        RequireCurrencyExact = true
    }

[<RequireQualifiedAccess>]
module BreakSeverityInfo =

    let asString = function
        | Critical -> "Critical"
        | High     -> "High"
        | Medium   -> "Medium"
        | Low      -> "Low"
        | Info     -> "Info"

    /// Derive severity from the break classification and the projected nominal amount.
    let ofClassification (nominalAmount: decimal) = function
        | CurrencyMismatch _                                                    -> Critical
        | AmountMismatch(_, _, pct) when abs pct > 0.05m                        -> Critical
        | AmountMismatch(_, _, pct) when abs pct > 0.01m                        -> High
        | AmountMismatch _                                                       -> Medium
        | TimingMismatch(_, _, days) when days > 30                              -> High
        | TimingMismatch(_, _, days) when days > 5                               -> Medium
        | TimingMismatch _                                                       -> Low
        | MissingActual when abs nominalAmount > 10_000m                         -> High
        | MissingActual                                                          -> Medium
        | ClassificationGap _                                                    -> High
        | OtherBreak _                                                           -> Medium

[<RequireQualifiedAccess>]
module BreakClassificationInfo =

    let asString = function
        | AmountMismatch _    -> "AmountMismatch"
        | CurrencyMismatch _  -> "CurrencyMismatch"
        | TimingMismatch _    -> "TimingMismatch"
        | MissingActual       -> "MissingActual"
        | ClassificationGap _ -> "ClassificationGap"
        | OtherBreak _        -> "OtherBreak"

[<RequireQualifiedAccess>]
module CashFlowRules =

    /// Attempt to match a projected cash event against the actual outcome values.
    /// Returns <c>None</c> when within tolerance (matched), or <c>Some(BreakClassification)</c> on break.
    let applyTolerance
            (tolerance: MatchTolerance)
            (projected: ProjectedCashEvent)
            (actualAmount: decimal)
            (actualCurrency: string)
            (actualDate: DateTimeOffset) : BreakClassification option =

        if tolerance.RequireCurrencyExact
           && not (String.Equals(projected.ExpectedCurrency, actualCurrency, StringComparison.OrdinalIgnoreCase)) then
            Some (CurrencyMismatch(projected.ExpectedCurrency, actualCurrency))
        else
            let daysLate = int (actualDate - projected.DueDate).TotalDays
            let timingOk = abs daysLate <= tolerance.TimingToleranceDays

            let variancePct =
                if projected.ExpectedAmount <> 0m then
                    abs ((actualAmount - projected.ExpectedAmount) / projected.ExpectedAmount)
                else 0m

            let amountOk = variancePct <= tolerance.AmountTolerancePct

            match timingOk, amountOk with
            | true, true  -> None
            | false, _    -> Some (TimingMismatch(projected.DueDate, actualDate, abs daysLate))
            | true, false -> Some (AmountMismatch(projected.ExpectedAmount, actualAmount, variancePct))

    /// Build a <see cref="CashFlowBreak"/> record from a classified break.
    let buildBreak
            (securityIdRaw: Guid)
            (projected: ProjectedCashEvent)
            (classification: BreakClassification) : CashFlowBreak =

        let sev = BreakSeverityInfo.ofClassification projected.ExpectedAmount classification
        {
            BreakId             = Guid.NewGuid()
            SecurityId          = securityIdRaw
            FlowId              = projected.FlowId
            EventKind           = CashFlowEventKind.label projected.EventKind
            ClassificationLabel = BreakClassificationInfo.asString classification
            SeverityLabel       = BreakSeverityInfo.asString sev
            BreakAmount         = projected.ExpectedAmount
            Currency            = projected.ExpectedCurrency
            AsOf                = projected.DueDate
            Notes               = projected.Notes
        }

    /// Evaluate a projected event against a single actual, yielding a strongly-typed state.
    let classifyState
            (tolerance: MatchTolerance)
            (projected: ProjectedCashEvent)
            (actual: ActualCashEvent) : CashFlowState =

        match applyTolerance tolerance projected actual.Amount actual.Currency actual.PostedAt with
        | None -> CashFlowState.Settled(projected, actual)
        | Some classification -> CashFlowState.Broken(projected, classification)

    /// Evaluate a list of projected events against supplied actuals, returning all breaks.
    /// <paramref name="actuals"/> is a map from (securityId, flowId) to <see cref="ActualCashEvent"/>.
    let classifyAll
            (tolerance: MatchTolerance)
            (events: ProjectedCashEvent list)
            (actuals: Map<Guid * Guid, ActualCashEvent>) : CashFlowBreak list =

        events
        |> List.choose (fun projected ->
            let (SecurityId sid) = projected.SecurityId
            match actuals |> Map.tryFind (sid, projected.FlowId) with
            | None ->
                Some (buildBreak sid projected MissingActual)
            | Some actual ->
                applyTolerance tolerance projected actual.Amount actual.Currency actual.PostedAt
                |> Option.map (buildBreak sid projected))

    /// Evaluate a list of projected events and return their state machine representation.
    let classifyStates
            (tolerance: MatchTolerance)
            (events: ProjectedCashEvent list)
            (actuals: Map<Guid * Guid, ActualCashEvent>) : CashFlowState list =

        events
        |> List.map (fun projected ->
            let (SecurityId sid) = projected.SecurityId
            match actuals |> Map.tryFind (sid, projected.FlowId) with
            | Some actual -> classifyState tolerance projected actual
            | None -> CashFlowState.Broken(projected, MissingActual))
