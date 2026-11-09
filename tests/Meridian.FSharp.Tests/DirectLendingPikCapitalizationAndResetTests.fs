module Meridian.FSharp.Tests.DirectLendingPikCapitalizationAndResetTests

// PIK capitalization policy and floating-rate reset scheduling through the public
// DirectLendingAggregateInterop and DirectLendingServicingSchedule surfaces.
// Design: docs/engineering/blueprints/direct-lending-pik-capitalization-and-rate-resets.md.

open System
open Xunit
open FsUnit.Xunit
open Meridian.Contracts.DirectLending
open Meridian.FSharp.DirectLending.Aggregates

let private origination = DateOnly(2026, 1, 15)
let private maturity = DateOnly(2029, 1, 15)

let private buildTerms
    (rateType: RateTypeKind)
    (pik: PikCapitalizationPolicy)
    (schedule: FloatingRateResetSchedule) =
    DirectLendingTermsDto(
        origination,
        maturity,
        1_000_000m,
        CurrencyCode.USD,
        rateType,
        (if rateType = RateTypeKind.Fixed then Nullable 0.08m else Nullable()),
        (if rateType = RateTypeKind.Fixed then null else "SOFR"),
        (if rateType = RateTypeKind.Fixed then Nullable() else Nullable 250m),
        Nullable(),
        Nullable(),
        DayCountBasis.Act360,
        PaymentFrequency.Quarterly,
        AmortizationType.Bullet,
        Nullable(),
        Nullable(),
        true,
        null,
        PikCapitalization = pik,
        RateResetSchedule = schedule)

let private monthlyPik (mode: PikElection) (share: DeclaredRate) =
    PikCapitalizationPolicy(mode, share, PikCapitalizationFrequency.Monthly, DateOnly(2026, 2, 15))

let private quarterlySchedule (floor: DeclaredRate) (appliesTo: FloorCapApplication) =
    FloatingRateResetSchedule(
        "SOFR",
        RateResetFrequency.Quarterly,
        origination,
        2,
        LookbackDayKind.Business,
        DeclaredRate.BasisPoints 250m,
        floor,
        null,
        appliesTo,
        null)

/// Active loan, fully drawn (1,000,000) on origination.
let private activeDrawnLoan (terms: DirectLendingTermsDto) =
    let created =
        DirectLendingAggregateInterop.CreateLoan
            (Guid.NewGuid())
            (CreateLoanRequest(Nullable(), "PIK Loan", BorrowerInfoDto(Guid.NewGuid(), "Borrower", Nullable()), origination, terms))
            (DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero))
    let activated = DirectLendingAggregateInterop.ActivateLoan created.Contract created.Servicing (ActivateLoanRequest(origination))
    let drawn = DirectLendingAggregateInterop.BookDrawdown activated.Servicing (BookDrawdownRequest(1_000_000m, origination, origination, "wire"))
    drawn.Servicing

let private accrue (terms: DirectLendingTermsDto) (servicing: LoanServicingStateDto) (date: DateOnly) =
    DirectLendingAggregateInterop.PostDailyAccrual servicing terms (PostDailyAccrualRequest(date))

let private accrueThrough terms servicing (first: DateOnly) (last: DateOnly) =
    let mutable current = servicing
    let mutable date = first
    while date <= last do
        current <- (accrue terms current date).Servicing
        date <- date.AddDays 1
    current

[<Fact>]
let ``policy defers the PIK share of daily interest instead of capitalizing it`` () =
    let terms = buildTerms RateTypeKind.Fixed (monthlyPik PikElection.Always (DeclaredRate.Percent 25m)) null
    let decision = accrue terms (activeDrawnLoan terms) (DateOnly(2026, 1, 16))

    // 1,000,000 x 8% / 360 = 222.22; 25% PIK = 55.555 -> 55.56 (away from zero); cash 166.66.
    decision.Entry.PikInterestAmount |> should equal 55.56m
    decision.Entry.InterestAmount |> should equal 166.66m
    decision.Entry.PikCapitalizationDeferred |> should equal true
    decision.Servicing.Balances.PrincipalOutstanding |> should equal 1_000_000m
    decision.Servicing.Balances.InterestAccruedUnpaid |> should equal 166.66m
    decision.Servicing.PikInterestAccruedUnpaid |> should equal 55.56m

[<Fact>]
let ``legacy PIK toggle without a policy still capitalizes the whole day into principal`` () =
    let terms = buildTerms RateTypeKind.Fixed null null
    let servicing = activeDrawnLoan terms
    let toggled =
        LoanServicingStateDto(
            servicing.LoanId, servicing.Status, servicing.CurrentCommitment, servicing.TotalDrawn, servicing.AvailableToDraw,
            servicing.Balances, servicing.DrawdownLots, servicing.CurrentRateReset, servicing.LastAccrualDate,
            servicing.LastPaymentDate, servicing.ServicingRevision, servicing.RevisionHistory, servicing.AccrualEntries,
            IsPikToggled = true)
    let decision = accrue terms toggled (DateOnly(2026, 1, 16))

    decision.Entry.PikInterestAmount |> should equal 222.22m
    decision.Entry.InterestAmount |> should equal 0m
    decision.Entry.PikCapitalizationDeferred |> should equal false
    decision.Servicing.Balances.PrincipalOutstanding |> should equal 1_000_222.22m
    decision.Servicing.PikInterestAccruedUnpaid |> should equal 0m

[<Fact>]
let ``toggle-elected policy pays cash interest while the toggle is off`` () =
    let terms = buildTerms RateTypeKind.Fixed (monthlyPik PikElection.WhenToggled (DeclaredRate.Fraction 1m)) null
    let decision = accrue terms (activeDrawnLoan terms) (DateOnly(2026, 1, 16))

    decision.Entry.PikInterestAmount |> should equal 0m
    decision.Entry.InterestAmount |> should equal 222.22m
    decision.Entry.PikCapitalizationDeferred |> should equal false

[<Fact>]
let ``capitalization moves accrued PIK into principal and interest compounds from the next day`` () =
    let terms = buildTerms RateTypeKind.Fixed (monthlyPik PikElection.Always (DeclaredRate.Percent 25m)) null
    let accrued = accrueThrough terms (activeDrawnLoan terms) (DateOnly(2026, 1, 16)) (DateOnly(2026, 2, 15))

    // 31 accrual days x 55.56.
    accrued.PikInterestAccruedUnpaid |> should equal 1_722.36m

    let capitalized = DirectLendingAggregateInterop.CapitalizePik accrued terms (CapitalizePikRequest(DateOnly(2026, 2, 15)))
    capitalized.Capitalization.Amount |> should equal 1_722.36m
    capitalized.Capitalization.PeriodStart |> should equal origination
    capitalized.Capitalization.PeriodEnd |> should equal (DateOnly(2026, 2, 15))
    capitalized.Servicing.Balances.PrincipalOutstanding |> should equal 1_001_722.36m
    capitalized.Servicing.PikInterestAccruedUnpaid |> should equal 0m
    capitalized.Servicing.LastPikCapitalizationDate |> should equal (Nullable(DateOnly(2026, 2, 15)))
    capitalized.Servicing.PikCapitalizations.Count |> should equal 1

    // 1,001,722.36 x 8% / 360 = 222.604969 -> 222.60; PIK 25% = 55.65124 -> 55.65.
    let nextDay = accrue terms capitalized.Servicing (DateOnly(2026, 2, 16))
    nextDay.Entry.PikInterestAmount + nextDay.Entry.InterestAmount |> should equal 222.60m
    nextDay.Entry.PikInterestAmount |> should equal 55.65m

[<Fact>]
let ``capitalization refuses a non-policy date and a date not yet accrued`` () =
    let terms = buildTerms RateTypeKind.Fixed (monthlyPik PikElection.Always (DeclaredRate.Percent 25m)) null
    let accrued = accrueThrough terms (activeDrawnLoan terms) (DateOnly(2026, 1, 16)) (DateOnly(2026, 2, 10))

    (fun () -> DirectLendingAggregateInterop.CapitalizePik accrued terms (CapitalizePikRequest(DateOnly(2026, 2, 10))) |> ignore)
    |> should throw typeof<Exception>
    (fun () -> DirectLendingAggregateInterop.CapitalizePik accrued terms (CapitalizePikRequest(DateOnly(2026, 2, 15))) |> ignore)
    |> should throw typeof<Exception>

[<Fact>]
let ``scheduled reset floors the index before adding the spread`` () =
    let terms = buildTerms RateTypeKind.Floating null (quarterlySchedule (DeclaredRate.Percent 1m) FloorCapApplication.Index)
    let resetDate = DateOnly(2026, 4, 15) // Wednesday
    let observation = DirectLendingServicingSchedule.FixingObservationDate(terms.RateResetSchedule, resetDate)
    observation |> should equal (DateOnly(2026, 4, 13)) // two weekdays earlier (Monday)

    let decision =
        DirectLendingAggregateInterop.ApplyScheduledRateReset (activeDrawnLoan terms) terms resetDate observation 0.005m "SOFR-feed"

    // max(0.5%, 1% floor) + 250 bp = 3.5%.
    decision.AllInRate |> should equal 0.035m
    let reset = decision.Servicing.CurrentRateReset
    reset.SpreadBps |> should equal 250m
    reset.ResetKind |> should equal (Nullable RateResetKind.Scheduled)
    reset.FixingObservationDate |> should equal (Nullable observation)
    decision.Servicing.RateResetHistory.Count |> should equal 1

[<Fact>]
let ``scheduled reset can floor the all-in rate instead`` () =
    let terms = buildTerms RateTypeKind.Floating null (quarterlySchedule (DeclaredRate.Percent 1m) FloorCapApplication.AllIn)
    let resetDate = DateOnly(2026, 4, 15)
    let decision =
        DirectLendingAggregateInterop.ApplyScheduledRateReset (activeDrawnLoan terms) terms resetDate (DateOnly(2026, 4, 13)) 0.005m null

    // 0.5% + 250 bp = 3.0%, above the 1% all-in floor.
    decision.AllInRate |> should equal 0.03m

[<Fact>]
let ``scheduled reset caps a high fixing and supports a negative index`` () =
    let schedule =
        FloatingRateResetSchedule(
            "SOFR", RateResetFrequency.Quarterly, origination, 0, LookbackDayKind.Calendar,
            DeclaredRate.BasisPoints 250m, DeclaredRate.Fraction -0.01m, DeclaredRate.Percent 6m, FloorCapApplication.Index, null)
    let terms = buildTerms RateTypeKind.Floating null schedule
    let servicing = activeDrawnLoan terms

    let high = DirectLendingAggregateInterop.ApplyScheduledRateReset servicing terms (DateOnly(2026, 4, 15)) (DateOnly(2026, 4, 15)) 0.09m null
    high.AllInRate |> should equal 0.085m // 6% index cap + 2.5%

    let negative = DirectLendingAggregateInterop.ApplyScheduledRateReset servicing terms (DateOnly(2026, 4, 15)) (DateOnly(2026, 4, 15)) -0.004m null
    negative.AllInRate |> should equal 0.021m // -0.4% (above the -1% floor) + 2.5%

[<Fact>]
let ``scheduled reset refuses a non-reset date and the wrong observation date`` () =
    let terms = buildTerms RateTypeKind.Floating null (quarterlySchedule null FloorCapApplication.Index)
    let servicing = activeDrawnLoan terms

    (fun () -> DirectLendingAggregateInterop.ApplyScheduledRateReset servicing terms (DateOnly(2026, 4, 16)) (DateOnly(2026, 4, 14)) 0.04m null |> ignore)
    |> should throw typeof<Exception>
    (fun () -> DirectLendingAggregateInterop.ApplyScheduledRateReset servicing terms (DateOnly(2026, 4, 15)) (DateOnly(2026, 4, 15)) 0.04m null |> ignore)
    |> should throw typeof<Exception>

[<Fact>]
let ``floating loan with a schedule refuses to accrue before any reset`` () =
    let terms = buildTerms RateTypeKind.Floating null (quarterlySchedule null FloorCapApplication.Index)
    (fun () -> accrue terms (activeDrawnLoan terms) (DateOnly(2026, 1, 16)) |> ignore)
    |> should throw typeof<Exception>

[<Fact>]
let ``reset dates step from the anchor, clip at maturity and honour explicit dates`` () =
    let schedule = quarterlySchedule null FloorCapApplication.Index
    let dates = DirectLendingServicingSchedule.ResetDates(schedule, maturity)
    dates.Length |> should equal 13
    dates[0] |> should equal origination
    dates[12] |> should equal maturity

    let monthEnd =
        FloatingRateResetSchedule(
            "SOFR", RateResetFrequency.Monthly, DateOnly(2026, 1, 31), 0, LookbackDayKind.Calendar,
            DeclaredRate.BasisPoints 0m, null, null, FloorCapApplication.Index, null)
    let monthEndDates = DirectLendingServicingSchedule.ResetDates(monthEnd, DateOnly(2026, 4, 30))
    monthEndDates |> should equal [| DateOnly(2026, 1, 31); DateOnly(2026, 2, 28); DateOnly(2026, 3, 31); DateOnly(2026, 4, 30) |]

    let explicitDates =
        FloatingRateResetSchedule(
            "SOFR", RateResetFrequency.Quarterly, origination, 0, LookbackDayKind.Calendar,
            DeclaredRate.BasisPoints 0m, null, null, FloorCapApplication.Index,
            [| DateOnly(2026, 6, 1); DateOnly(2026, 2, 1); DateOnly(2026, 6, 1) |])
    DirectLendingServicingSchedule.ResetDates(explicitDates, maturity) |> should equal [| DateOnly(2026, 2, 1); DateOnly(2026, 6, 1) |]
    DirectLendingServicingSchedule.LatestResetOnOrBefore(explicitDates, maturity, DateOnly(2026, 5, 31)) |> should equal (Nullable(DateOnly(2026, 2, 1)))
    DirectLendingServicingSchedule.LatestResetOnOrBefore(explicitDates, maturity, DateOnly(2026, 1, 31)) |> should equal (Nullable<DateOnly>())

[<Fact>]
let ``business lookback rolls a weekend reset back to Friday first`` () =
    let lookback days =
        FloatingRateResetSchedule(
            "SOFR", RateResetFrequency.Quarterly, origination, days, LookbackDayKind.Business,
            DeclaredRate.BasisPoints 0m, null, null, FloorCapApplication.Index, null)
    let saturday = DateOnly(2026, 8, 15)
    DirectLendingServicingSchedule.FixingObservationDate(lookback 0, saturday) |> should equal (DateOnly(2026, 8, 14))
    DirectLendingServicingSchedule.FixingObservationDate(lookback 2, saturday) |> should equal (DateOnly(2026, 8, 12))
    // Monday minus one weekday is the previous Friday.
    DirectLendingServicingSchedule.FixingObservationDate(lookback 1, DateOnly(2026, 8, 17)) |> should equal (DateOnly(2026, 8, 14))

[<Fact>]
let ``capitalization dates follow the policy cadence and always include maturity`` () =
    let terms = buildTerms RateTypeKind.Fixed (monthlyPik PikElection.Always (DeclaredRate.Percent 25m)) null
    let dates = DirectLendingServicingSchedule.CapitalizationDates(terms.PikCapitalization, terms)
    dates[0] |> should equal (DateOnly(2026, 2, 15))
    dates[1] |> should equal (DateOnly(2026, 3, 15))
    dates |> Array.last |> should equal maturity

    let onPayment =
        buildTerms
            RateTypeKind.Fixed
            (PikCapitalizationPolicy(PikElection.Always, DeclaredRate.Percent 100m, PikCapitalizationFrequency.OnInterestPaymentDate, DateOnly(2026, 4, 15)))
            null
    let paymentDates = DirectLendingServicingSchedule.CapitalizationDates(onPayment.PikCapitalization, onPayment)
    paymentDates[0..1] |> should equal [| DateOnly(2026, 4, 15); DateOnly(2026, 7, 15) |] // quarterly payment frequency

[<Fact>]
let ``PIK accrual and capitalization carry prepaid interest and unapplied cash through unchanged`` () =
    let terms = buildTerms RateTypeKind.Fixed (monthlyPik PikElection.Always (DeclaredRate.Percent 25m)) null
    let seeded = activeDrawnLoan terms |> DirectLendingTestBalances.withExcessBalances 50m 75m
    let accrued = accrueThrough terms seeded (DateOnly(2026, 1, 16)) (DateOnly(2026, 2, 15))

    let capitalized = DirectLendingAggregateInterop.CapitalizePik accrued terms (CapitalizePikRequest(DateOnly(2026, 2, 15)))

    (accrued.Balances.PrepaidInterest, accrued.Balances.UnappliedCash) |> should equal (50m, 75m)
    (capitalized.Servicing.Balances.PrepaidInterest, capitalized.Servicing.Balances.UnappliedCash) |> should equal (50m, 75m)
