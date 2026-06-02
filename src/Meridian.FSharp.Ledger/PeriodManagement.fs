namespace Meridian.FSharp.Ledger

open System

/// Lifecycle status of a ledger accounting period.
type PeriodStatus =
    /// Period is open; postings are accepted and adjustments are free.
    | Open
    /// Period is soft-closed; no new originating entries, but approved adjustments are accepted.
    | SoftClosed
    /// Period is hard-closed; no further postings or adjustments are permitted.
    | HardClosed

/// An accounting period identified by a fiscal year and period number.
[<CLIMutable>]
type AccountingPeriod = {
    PeriodId   : Guid
    FiscalYear : int
    PeriodNo   : int
    Label      : string
    StartDate  : DateOnly
    EndDate    : DateOnly
    Status     : PeriodStatus
    OpenedAt   : DateTimeOffset
    ClosedAt   : DateTimeOffset option
}

/// A recorded close event for one accounting period.
[<CLIMutable>]
type PeriodCloseEvent = {
    EventId      : Guid
    PeriodId     : Guid
    PriorStatus  : PeriodStatus
    NewStatus    : PeriodStatus
    ClosedBy     : string
    Notes        : string
    RecordedAt   : DateTimeOffset
}

/// Result of attempting a period-close operation.
type PeriodCloseResult =
    | CloseAccepted  of event: PeriodCloseEvent
    | AlreadyClosed
    | InvalidTransition of reason: string

/// Result of checking whether a posting date is acceptable in a period.
type PostingCheck =
    | Allowed
    | PeriodNotFound
    | Denied of reason: string

[<RequireQualifiedAccess>]
module PeriodCalendarValidation =

    let private overlaps (left: AccountingPeriod) (right: AccountingPeriod) =
        not (left.EndDate < right.StartDate || right.EndDate < left.StartDate)

    let private isGap (left: AccountingPeriod) (right: AccountingPeriod) =
        left.EndDate.AddDays(1) < right.StartDate

    let validate (periods: AccountingPeriod list) =
        let ordered =
            periods
            |> List.sortBy (fun period ->
                period.StartDate.DayNumber, period.FiscalYear, period.PeriodNo)

        let invalidDates =
            ordered
            |> List.choose (fun period ->
                if period.StartDate > period.EndDate then
                    Some (sprintf "Period %s has StartDate after EndDate." period.Label)
                else
                    None)

        let duplicateIds =
            ordered
            |> List.map (fun period -> period.PeriodId)
            |> List.countBy id
            |> List.choose (fun (periodId, count) ->
                if count > 1 then
                    Some (sprintf "Duplicate period Id detected: %O" periodId)
                else
                    None)

        let outOfOrderIds =
            ordered
            |> List.choose (fun period ->
                if period.PeriodNo <= 0 then
                    Some (sprintf "Period '%s' has non-positive PeriodNo." period.Label)
                else
                    None)

        let overlapErrors =
            ordered
            |> List.pairwise
            |> List.choose (fun (left, right) ->
                if overlaps left right then
                    Some (sprintf "Period '%s' overlaps '%s'." left.Label right.Label)
                else
                    None)

        let gapErrors =
            ordered
            |> List.pairwise
            |> List.choose (fun (left, right) ->
                if isGap left right then
                    Some (sprintf "Gap between period '%s' and '%s'." left.Label right.Label)
                else
                    None)

        let fiscalConsistencyErrors =
            ordered
            |> List.choose (fun period ->
                if period.EndDate < period.StartDate.AddDays(1) then
                    None
                elif ordered |> List.exists (fun other -> other.PeriodId <> period.PeriodId && other.FiscalYear <> period.FiscalYear && overlaps period other) then
                    Some (sprintf "Period '%s' overlaps a different fiscal year period." period.Label)
                else
                    None)

        invalidDates
        |> List.append duplicateIds
        |> List.append outOfOrderIds
        |> List.append overlapErrors
        |> List.append gapErrors
        |> List.append fiscalConsistencyErrors

[<RequireQualifiedAccess>]
module PeriodStatus =

    let asString = function
        | Open       -> "Open"
        | SoftClosed -> "SoftClosed"
        | HardClosed -> "HardClosed"

    let fromString = function
        | "Open"       -> Some Open
        | "SoftClosed" -> Some SoftClosed
        | "HardClosed" -> Some HardClosed
        | _            -> None

    /// Returns true when the period accepts ordinary new originating postings.
    /// Soft-closed and hard-closed periods reject ordinary postings; only approved
    /// adjustment entries are allowed through the soft-close guard (see <c>isAdjustable</c>).
    let isPostable = function
        | Open       -> true
        | SoftClosed -> false
        | HardClosed -> false

    /// Returns true when the period accepts approved adjustment entries.
    /// Hard-closed periods reject all postings, including adjustments.
    let isAdjustable = function
        | Open | SoftClosed -> true
        | HardClosed        -> false

    /// Returns true when the requested close transition is valid.
    let isValidTransition (from: PeriodStatus) (``to``: PeriodStatus) =
        match from, ``to`` with
        | Open, SoftClosed   -> true
        | Open, HardClosed   -> true
        | SoftClosed, HardClosed -> true
        | _ -> false

[<RequireQualifiedAccess>]
module AccountingPeriod =

    /// Build a calendar-month period label such as "2025-Q1" or "2025-P03".
    let monthlyLabel (fiscalYear: int) (periodNo: int) =
        sprintf "%d-P%02d" fiscalYear periodNo

    /// Build a quarterly period label such as "2025-Q1".
    let quarterlyLabel (fiscalYear: int) (quarter: int) =
        sprintf "%d-Q%d" fiscalYear quarter

    /// Create a new open period starting today.
    let create (fiscalYear: int) (periodNo: int) (startDate: DateOnly) (endDate: DateOnly) (now: DateTimeOffset) : AccountingPeriod =
        { PeriodId   = Guid.NewGuid()
          FiscalYear = fiscalYear
          PeriodNo   = periodNo
          Label      = monthlyLabel fiscalYear periodNo
          StartDate  = startDate
          EndDate    = endDate
          Status     = Open
          OpenedAt   = now
          ClosedAt   = None }

    /// Returns true when the given date falls inside the period's date range (inclusive).
    let contains (date: DateOnly) (period: AccountingPeriod) =
        date >= period.StartDate && date <= period.EndDate

    /// Returns true when the period can accept ordinary new postings.
    let acceptsPostings (period: AccountingPeriod) =
        PeriodStatus.isPostable period.Status

    /// Returns true when the period can accept approved adjustment entries.
    let acceptsAdjustments (period: AccountingPeriod) =
        PeriodStatus.isAdjustable period.Status

[<RequireQualifiedAccess>]
module PeriodManagement =

    /// Attempt to close a period to the requested next status.
    let close
            (period: AccountingPeriod)
            (newStatus: PeriodStatus)
            (closedBy: string)
            (notes: string)
            (now: DateTimeOffset)
            : PeriodCloseResult =

        match period.Status with
        | HardClosed -> AlreadyClosed
        | from when not (PeriodStatus.isValidTransition from newStatus) ->
            InvalidTransition (sprintf "Cannot transition from %s to %s." (PeriodStatus.asString from) (PeriodStatus.asString newStatus))
        | _ ->
            // Normalise null/whitespace notes to empty string so downstream storage is consistent.
            let normalisedNotes = if String.IsNullOrWhiteSpace notes then "" else notes
            let event : PeriodCloseEvent =
                { EventId     = Guid.NewGuid()
                  PeriodId    = period.PeriodId
                  PriorStatus = period.Status
                  NewStatus   = newStatus
                  ClosedBy    = closedBy
                  Notes       = normalisedNotes
                  RecordedAt  = now }
            CloseAccepted event

    /// Apply a close event to a period, returning the updated period record.
    let applyClose (period: AccountingPeriod) (event: PeriodCloseEvent) : AccountingPeriod =
            { period with
                Status   = event.NewStatus
                ClosedAt = if event.NewStatus = HardClosed then Some event.RecordedAt else period.ClosedAt }

    let validateCloseTransition
            (period: AccountingPeriod)
            (newStatus: PeriodStatus)
            (allPeriods: AccountingPeriod list) =
        match PeriodStatus.isValidTransition period.Status newStatus with
        | false ->
            Error (sprintf "Cannot transition from %s to %s." (PeriodStatus.asString period.Status) (PeriodStatus.asString newStatus))
        | true ->
            let sameFiscal = allPeriods |> List.filter (fun p -> p.FiscalYear = period.FiscalYear)
            let previous =
                sameFiscal
                |> List.filter (fun candidate -> candidate.PeriodNo = period.PeriodNo - 1)
                |> List.tryHead

            let next =
                sameFiscal
                |> List.filter (fun candidate -> candidate.PeriodNo = period.PeriodNo + 1)
                |> List.tryHead

            let openPrior =
                match previous with
                | Some prev when prev.Status = Open || prev.Status = SoftClosed ->
                    Some (sprintf "Cannot close period %s because prior period %s is not hard-closed." period.Label prev.Label)
                | _ -> None

            let hardBeforeSoft =
                if newStatus = HardClosed then
                    match previous with
                    | Some prev when prev.Status <> HardClosed ->
                        Some (sprintf "Cannot hard-close %s before prior period %s is hard-closed." period.Label prev.Label)
                    | _ -> None
                else
                    None

            let blockedByNext =
                match newStatus = SoftClosed, next with
                | true, Some nextPeriod when nextPeriod.Status = HardClosed ->
                    Some (sprintf "Cannot soft-close %s because next period %s is already hard-closed." period.Label nextPeriod.Label)
                | _ -> None

            match openPrior, hardBeforeSoft, blockedByNext with
            | None, None, None -> Ok()
            | _ ->
                let reasons =
                    [ openPrior; hardBeforeSoft; blockedByNext ]
                    |> List.choose id
                Error (String.concat " " reasons)

    /// Check whether a posting at the given date is acceptable.
    /// Pass <c>isAdjustment = true</c> to apply soft-close override rules.
    let checkPostingDate
            (date: DateOnly)
            (isAdjustment: bool)
            (periods: AccountingPeriod list)
            : PostingCheck =

        let matching =
            periods
            |> List.filter (AccountingPeriod.contains date)
            |> List.sortBy (fun p -> p.StartDate.DayNumber)

        match matching with
        | [] -> PeriodNotFound
        | [ period ] ->
            match period.Status with
            | HardClosed ->
                Denied (sprintf "Period '%s' is hard-closed; no postings or adjustments are permitted." period.Label)
            | SoftClosed when not isAdjustment ->
                Denied (sprintf "Period '%s' is soft-closed; only approved adjustment entries are accepted." period.Label)
            | _ -> Allowed
        | _ ->
            Denied "Posting date is ambiguous; multiple ledger periods match this date."

    /// Return all open periods from a list, sorted by start date ascending.
    let openPeriods (periods: AccountingPeriod list) =
        periods
        |> List.filter (fun p -> p.Status = Open)
        |> List.sortBy (fun p -> p.StartDate)

    /// Return the most recent hard-closed period, if any.
    let lastHardClosed (periods: AccountingPeriod list) =
        periods
        |> List.filter (fun p -> p.Status = HardClosed)
        |> List.sortByDescending (fun p -> p.EndDate)
        |> List.tryHead

    /// Build a sequence of calendar-month periods for a full fiscal year.
    let generateCalendarMonthPeriods (fiscalYear: int) (now: DateTimeOffset) : AccountingPeriod list =
        [ for month in 1 .. 12 do
            let startDate = DateOnly(fiscalYear, month, 1)
            let endDate = startDate.AddMonths(1).AddDays(-1)
            yield AccountingPeriod.create fiscalYear month startDate endDate now ]
