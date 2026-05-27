module Meridian.FSharp.Tests.PeriodManagementTests

open System
open Xunit
open FsUnit.Xunit
open Meridian.FSharp.Ledger

let private statuses =
    [ Open; SoftClosed; HardClosed ]

let private statusCases =
    [
        Open, "Open"
        SoftClosed, "SoftClosed"
        HardClosed, "HardClosed"
    ]

let private openedAt =
    DateTimeOffset.Parse("2026-01-01T00:00:00Z")

let private closedAt =
    DateTimeOffset.Parse("2026-02-01T00:00:00Z")

let private date year month day =
    DateOnly(year, month, day)

let private periodWithStatus status =
    {
        PeriodId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
        FiscalYear = 2026
        PeriodNo = 1
        Label = "2026-P01"
        StartDate = date 2026 1 1
        EndDate = date 2026 1 31
        Status = status
        OpenedAt = openedAt
        ClosedAt = if status = HardClosed then Some closedAt else None
    }

let private assertDeniedContains (expectedText: string) result =
    match result with
    | Denied reason -> reason.Contains(expectedText) |> should equal true
    | other -> failwithf "Expected Denied containing '%s' but got %A" expectedText other

[<Fact>]
let ``PeriodStatus fromString round-trips known status labels`` () =
    for status, label in statusCases do
        PeriodStatus.asString status |> should equal label
        PeriodStatus.fromString label |> should equal (Some status)
        PeriodStatus.fromString (PeriodStatus.asString status) |> should equal (Some status)

    PeriodStatus.fromString "open" |> should equal None
    PeriodStatus.fromString "Closed" |> should equal None
    PeriodStatus.fromString "" |> should equal None

[<Fact>]
let ``PeriodStatus isPostable allows only open periods`` () =
    let cases =
        [
            Open, true
            SoftClosed, false
            HardClosed, false
        ]

    for status, expected in cases do
        PeriodStatus.isPostable status |> should equal expected

[<Fact>]
let ``PeriodStatus isAdjustable allows open and soft-closed periods`` () =
    let cases =
        [
            Open, true
            SoftClosed, true
            HardClosed, false
        ]

    for status, expected in cases do
        PeriodStatus.isAdjustable status |> should equal expected

[<Fact>]
let ``PeriodStatus isValidTransition matches the exhaustive status matrix`` () =
    let cases =
        [
            Open, Open, false
            Open, SoftClosed, true
            Open, HardClosed, true
            SoftClosed, Open, false
            SoftClosed, SoftClosed, false
            SoftClosed, HardClosed, true
            HardClosed, Open, false
            HardClosed, SoftClosed, false
            HardClosed, HardClosed, false
        ]

    cases.Length |> should equal (statuses.Length * statuses.Length)

    for fromStatus, targetStatus, expected in cases do
        PeriodStatus.isValidTransition fromStatus targetStatus |> should equal expected

[<Fact>]
let ``PeriodManagement close accepts valid period status transitions`` () =
    let cases =
        [
            Open, SoftClosed
            Open, HardClosed
            SoftClosed, HardClosed
        ]

    for fromStatus, targetStatus in cases do
        let period = periodWithStatus fromStatus

        match PeriodManagement.close period targetStatus "controller" "month-end approved" closedAt with
        | CloseAccepted closeEvent ->
            closeEvent.PeriodId |> should equal period.PeriodId
            closeEvent.PriorStatus |> should equal fromStatus
            closeEvent.NewStatus |> should equal targetStatus
            closeEvent.ClosedBy |> should equal "controller"
            closeEvent.Notes |> should equal "month-end approved"
            closeEvent.RecordedAt |> should equal closedAt

            let updated = PeriodManagement.applyClose period closeEvent
            updated.Status |> should equal targetStatus
            if targetStatus = HardClosed then
                updated.ClosedAt |> should equal (Some closedAt)
            else
                updated.ClosedAt |> should equal period.ClosedAt
        | other -> failwithf "Expected CloseAccepted for %A -> %A but got %A" fromStatus targetStatus other

[<Fact>]
let ``PeriodManagement close rejects invalid non-terminal transitions`` () =
    let cases =
        [
            Open, Open
            SoftClosed, Open
            SoftClosed, SoftClosed
        ]

    for fromStatus, targetStatus in cases do
        let period = periodWithStatus fromStatus

        match PeriodManagement.close period targetStatus "controller" "" closedAt with
        | InvalidTransition reason ->
            reason.Contains(PeriodStatus.asString fromStatus) |> should equal true
            reason.Contains(PeriodStatus.asString targetStatus) |> should equal true
        | other -> failwithf "Expected InvalidTransition for %A -> %A but got %A" fromStatus targetStatus other

[<Fact>]
let ``PeriodManagement close reports hard-closed periods as already closed`` () =
    let period = periodWithStatus HardClosed

    for targetStatus in statuses do
        PeriodManagement.close period targetStatus "controller" "" closedAt
        |> should equal AlreadyClosed

[<Fact>]
let ``PeriodManagement checkPostingDate guards ordinary postings for every period status`` () =
    let postingDate = date 2026 1 15
    let cases =
        [
            Open, Allowed
            SoftClosed, Denied "soft-closed"
            HardClosed, Denied "hard-closed"
        ]

    for status, expected in cases do
        let result = PeriodManagement.checkPostingDate postingDate false [ periodWithStatus status ]

        match expected with
        | Allowed -> result |> should equal Allowed
        | PeriodNotFound -> result |> should equal PeriodNotFound
        | Denied text -> result |> assertDeniedContains text

[<Fact>]
let ``PeriodManagement checkPostingDate allows approved adjustment override in soft-closed periods`` () =
    let postingDate = date 2026 1 15

    PeriodManagement.checkPostingDate postingDate true [ periodWithStatus Open ]
    |> should equal Allowed

    PeriodManagement.checkPostingDate postingDate true [ periodWithStatus SoftClosed ]
    |> should equal Allowed

    PeriodManagement.checkPostingDate postingDate true [ periodWithStatus HardClosed ]
    |> assertDeniedContains "hard-closed"

[<Fact>]
let ``PeriodManagement checkPostingDate returns period-not-found when no period contains the posting date`` () =
    PeriodManagement.checkPostingDate (date 2026 2 1) false [ periodWithStatus Open ]
    |> should equal PeriodNotFound

[<Fact>]
let ``PeriodManagement generateCalendarMonthPeriods creates a complete fiscal calendar`` () =
    let generatedAt = DateTimeOffset.Parse("2024-01-01T09:30:00Z")
    let periods = PeriodManagement.generateCalendarMonthPeriods 2024 generatedAt

    periods.Length |> should equal 12
    periods |> List.map (fun period -> period.PeriodNo) |> should equal [ 1 .. 12 ]
    periods |> List.map (fun period -> period.Label) |> should equal [ for month in 1 .. 12 -> sprintf "2024-P%02d" month ]
    periods |> List.forall (fun period -> period.FiscalYear = 2024) |> should equal true
    periods |> List.forall (fun period -> period.Status = Open) |> should equal true
    periods |> List.forall (fun period -> period.OpenedAt = generatedAt) |> should equal true
    periods |> List.forall (fun period -> period.ClosedAt = None) |> should equal true

    let january = periods |> List.find (fun period -> period.PeriodNo = 1)
    january.StartDate |> should equal (date 2024 1 1)
    january.EndDate |> should equal (date 2024 1 31)

    let february = periods |> List.find (fun period -> period.PeriodNo = 2)
    february.StartDate |> should equal (date 2024 2 1)
    february.EndDate |> should equal (date 2024 2 29)

    let december = periods |> List.find (fun period -> period.PeriodNo = 12)
    december.StartDate |> should equal (date 2024 12 1)
    december.EndDate |> should equal (date 2024 12 31)
