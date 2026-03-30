namespace Meridian.FSharp.Ledger

open System

/// Severity of a ledger reconciliation break
type BreakSeverity =
    | Critical
    | High
    | Medium
    | Low
    | Info

/// Structural classification of a ledger break
type LedgerBreakClassification =
    | AmountBreak         of expected: decimal * actual: decimal
    | CurrencyBreak       of expected: string  * actual: string
    | TimingBreak         of daysLate: int
    | MissingEntry
    | DuplicateEntry
    | ClassificationBreak of reason: string
    | OtherBreak          of reason: string

/// Immutable record representing one identified break found in a reconciliation run
[<CLIMutable>]
type BreakRecord = {
    BreakId       : Guid
    RunId         : Guid
    SecurityId    : Guid
    FlowId        : Guid
    Classification: string
    Severity      : string
    ExpectedAmount: decimal
    ActualAmount  : decimal
    Currency      : string
    ExpectedDate  : DateTimeOffset
    ActualDate    : DateTimeOffset option
    Notes         : string
    CreatedAt     : DateTimeOffset
    ResolvedAt    : DateTimeOffset option
    IsResolved    : bool
}

/// Lifecycle status of a reconciliation run
type ReconciliationRunStatus =
    | Pending
    | Running
    | Completed
    | Failed       of reason: string
    | PartialMatch of matchRate: decimal

/// Summary of one executed reconciliation run
[<CLIMutable>]
type ReconciliationRun = {
    RunId           : Guid
    RunDate         : DateTimeOffset
    AsOfDate        : DateTimeOffset
    PortfolioId     : string
    StatusLabel     : string
    TotalProjected  : int
    TotalMatched    : int
    TotalBreaks     : int
    BreakAmountTotal: decimal
    Currency        : string
    StartedAt       : DateTimeOffset
    CompletedAt     : DateTimeOffset option
}

[<RequireQualifiedAccess>]
module BreakSeverity =

    let asString = function
        | Critical -> "Critical"
        | High     -> "High"
        | Medium   -> "Medium"
        | Low      -> "Low"
        | Info     -> "Info"

    let fromString = function
        | "Critical" -> Critical
        | "High"     -> High
        | "Medium"   -> Medium
        | "Low"      -> Low
        | _          -> Info

    let isActionable = function
        | Critical | High -> true
        | _               -> false

[<RequireQualifiedAccess>]
module LedgerBreakClassification =

    let asString = function
        | AmountBreak _         -> "AmountBreak"
        | CurrencyBreak _       -> "CurrencyBreak"
        | TimingBreak _         -> "TimingBreak"
        | MissingEntry          -> "MissingEntry"
        | DuplicateEntry        -> "DuplicateEntry"
        | ClassificationBreak _ -> "ClassificationBreak"
        | OtherBreak _          -> "OtherBreak"

    /// Derive break severity from classification and nominal amount.
    let severity (nominalAmount: decimal) = function
        | CurrencyBreak _                                                                  -> Critical
        | AmountBreak(exp, act) when exp <> 0m && abs ((act - exp) / exp) > 0.05m         -> Critical
        | AmountBreak(exp, act) when exp <> 0m && abs ((act - exp) / exp) > 0.01m         -> High
        | AmountBreak _                                                                    -> Medium
        | TimingBreak days      when days > 30                                             -> High
        | TimingBreak days      when days > 5                                              -> Medium
        | TimingBreak _                                                                    -> Low
        | MissingEntry          when abs nominalAmount > 10_000m                           -> High
        | MissingEntry                                                                     -> Medium
        | DuplicateEntry                                                                   -> High
        | ClassificationBreak _                                                            -> High
        | OtherBreak _                                                                     -> Medium

[<RequireQualifiedAccess>]
module ReconciliationRunStatus =

    let asString = function
        | Pending              -> "Pending"
        | Running              -> "Running"
        | Completed            -> "Completed"
        | Failed reason        -> sprintf "Failed(%s)" reason
        | PartialMatch rate    -> sprintf "PartialMatch(%.0f%%)" (float rate * 100.0)
