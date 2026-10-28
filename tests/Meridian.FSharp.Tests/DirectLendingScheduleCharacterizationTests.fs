/// Characterization pins for the F# direct-lending lane, recorded against base 7d072638 before the
/// schedule kernel (ADR-025) folds direct lending in behind its fences.
///
/// Tests tagged Category=KnownDefect assert TODAY'S defective behavior from the registry in
/// tests/fixtures/schedules/KNOWN-DEFECTS.md and name the TARGET behavior in a comment. When a fix
/// lands the pin fails; the fixing PR flips the assertion to the target and updates the registry in
/// the same change.
module Meridian.FSharp.Tests.DirectLendingScheduleCharacterizationTests

open System
open System.Collections.Generic
open Xunit
open FsUnit.Xunit
open Meridian.Contracts.DirectLending
open Meridian.FSharp.DirectLending.Aggregates
open Meridian.FSharp.DirectLendingInterop

// ----- Terms hash (same three canonical DTOs and hex as DirectLendingTermsHashCharacterizationTests.cs) -----

[<Literal>]
let private FixedBulletHash = "BF089DF1AF7A481E240BBF3F3C56F0C3FDD2E8164E62684B65BA7FEB85AB9724"

[<Literal>]
let private FloatingWithFloorCapAndSecurityMasterHash = "1D87D653D4189C9FEED4F9848DC9096AD6BF8AFB95023C9E4D9508AC3A42A2BB"

[<Literal>]
let private InterestOnlyWithGraceHash = "BB66416FE686BA4860EE7FE5AA4A0016D6DCE9CC8006F58DAC9E177CC6BB6E89"

let private fixedBulletTerms () =
    DirectLendingTermsDto(
        DateOnly(2026, 1, 15),
        DateOnly(2029, 1, 15),
        5_000_000m,
        CurrencyCode.USD,
        RateTypeKind.Fixed,
        Nullable 0.0725m,
        null,
        Nullable(),
        Nullable(),
        Nullable(),
        DayCountBasis.Act360,
        PaymentFrequency.Quarterly,
        AmortizationType.Bullet,
        Nullable 0.005m,
        Nullable 200m,
        true,
        """{"leverage":"<=4.0x"}""")

let private floatingWithFloorCapAndSecurityMasterTerms () =
    DirectLendingTermsDto(
        DateOnly(2026, 3, 22),
        DateOnly(2031, 3, 22),
        25_000_000m,
        CurrencyCode.EUR,
        RateTypeKind.Floating,
        Nullable(),
        "EURIBOR-3M",
        Nullable 475m,
        Nullable 0.01m,
        Nullable 0.12m,
        DayCountBasis.Act365F,
        PaymentFrequency.Monthly,
        AmortizationType.StraightLine,
        Nullable 0.0025m,
        Nullable(),
        false,
        null,
        EffectiveRateFloor = Nullable 0.045m,
        EffectiveRateCap = Nullable 0.15m,
        SecurityMasterReference =
            DirectLendingSecurityMasterReferenceDto(
                Guid.Parse("5c4ed0a5-0000-4000-8000-000000000001"),
                "NWFLT31",
                "source:security-master;definition:loan-facility",
                "sm-approval:nwflt31-controller",
                "ledger-map:nwflt31-direct-lending",
                "Active"))

let private interestOnlyWithGraceTerms () =
    DirectLendingTermsDto(
        DateOnly(2025, 11, 30),
        DateOnly(2030, 11, 30),
        12_500_000.50m,
        CurrencyCode.GBP,
        RateTypeKind.Fixed,
        Nullable 0.0915m,
        null,
        Nullable(),
        Nullable(),
        Nullable(),
        DayCountBasis.Thirty360,
        PaymentFrequency.SemiAnnual,
        AmortizationType.InterestOnly,
        Nullable(),
        Nullable 300m,
        true,
        """{"minLiquidity":2500000,"tests":["dscr","ltv"]}""",
        InterestOnlyMonths = 18,
        GracePeriodDays = Nullable 10,
        PrepaymentPenaltyRate = Nullable 0.02m,
        PurchasePrice = Nullable 99.5m)

let private createLoanWith (terms: DirectLendingTermsDto) =
    let request =
        CreateLoanRequest(
            Nullable(),
            "Schedule Characterization Loan",
            BorrowerInfoDto(Guid.Parse("5c4ed006-0000-4000-8000-0000000000b0"), "Characterization Borrower", Nullable()),
            terms.OriginationDate,
            terms)
    DirectLendingAggregateInterop.CreateLoan
        (Guid.Parse("5c4ed006-0000-4000-8000-000000000006"))
        request
        (DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))

[<Fact>]
let ``Terms hash for the fixed bullet DTO matches the base capture and the C# lane`` () =
    let decision = createLoanWith (fixedBulletTerms ())

    decision.TermsVersion.TermsHash |> should equal FixedBulletHash
    decision.Contract.TermsVersions.[0].TermsHash |> should equal FixedBulletHash

[<Fact>]
let ``Terms hash for the floating DTO with floor cap and Security Master reference matches the base capture`` () =
    let decision = createLoanWith (floatingWithFloorCapAndSecurityMasterTerms ())

    decision.TermsVersion.TermsHash |> should equal FloatingWithFloorCapAndSecurityMasterHash

[<Fact>]
let ``Terms hash for the interest-only DTO with grace matches the base capture`` () =
    let decision = createLoanWith (interestOnlyWithGraceTerms ())

    decision.TermsVersion.TermsHash |> should equal InterestOnlyWithGraceHash

// ----- SCH-DEF-006: 30/360 accrues per calendar day over 360 -----

let private thirty360Terms () =
    DirectLendingTermsDto(
        DateOnly(2026, 1, 1),
        DateOnly(2028, 1, 1),
        1_000_000m,
        CurrencyCode.USD,
        RateTypeKind.Fixed,
        Nullable 0.06m,
        null,
        Nullable(),
        Nullable(),
        Nullable(),
        DayCountBasis.Thirty360,
        PaymentFrequency.Monthly,
        AmortizationType.InterestOnly,
        Nullable(),
        Nullable(),
        true,
        null)

[<Fact>]
[<Trait("Category", "KnownDefect")>]
let ``SCH-DEF-006 Thirty360 daily accrual is one calendar day over 360`` () =
    // TARGET (accrual formula v2, period-to-date with residual carry): a 30/360 month accrues
    // 30/360 of a year however many calendar days it has, so 31 January adds nothing.
    let result =
        DirectLendingInterop.CalculateDailyAccrualAmountForDate(
            1_000_000m, 0.06m, int DayCountBasis.Thirty360, DateOnly(2026, 1, 31))

    // CURRENT defective behavior: principal x rate x (1/360) for every calendar day, 31 January
    // included. The fraction is taken first, so the product carries decimal dust in the last place.
    result |> should equal (1_000_000m * 0.06m * (1m / 360m))
    Math.Round(result, 2, MidpointRounding.AwayFromZero) |> should equal 166.67m

[<Fact>]
[<Trait("Category", "KnownDefect")>]
let ``SCH-DEF-006 posting every day of January under Thirty360 accrues 31 days`` () =
    // TARGET (accrual formula v2): 5,000.00 in total for January, with 31 January posting 0.00.
    let terms = thirty360Terms ()
    let created = createLoanWith terms
    let activated =
        DirectLendingAggregateInterop.ActivateLoan created.Contract created.Servicing (ActivateLoanRequest(DateOnly(2026, 1, 1)))
    let drawn =
        DirectLendingAggregateInterop.BookDrawdown
            activated.Servicing
            (BookDrawdownRequest(1_000_000m, DateOnly(2026, 1, 1), DateOnly(2026, 1, 1), "wire-sch-def-006"))

    let entries, servicing =
        [ 1..31 ]
        |> List.fold
            (fun (entries, servicing) day ->
                let decision =
                    DirectLendingAggregateInterop.PostDailyAccrual
                        servicing
                        terms
                        (PostDailyAccrualRequest(DateOnly(2026, 1, day)))
                (entries @ [ decision.Entry ], decision.Servicing))
            ([], drawn.Servicing)

    // CURRENT defective behavior: 31 x 166.67 = 5,166.77.
    entries |> List.length |> should equal 31
    entries |> List.forall (fun entry -> entry.InterestAmount = 166.67m) |> should equal true
    entries |> List.sumBy _.InterestAmount |> should equal 5_166.77m
    (entries |> List.last).AccrualDate |> should equal (DateOnly(2026, 1, 31))
    (entries |> List.last).InterestAmount |> should equal 166.67m
    servicing.Balances.InterestAccruedUnpaid |> should equal 5_166.77m

// ----- SCH-DEF-008 regression: activation and amendment preserve trailing servicing facts -----

[<Theory>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``SCH-DEF-008 activation and amendment preserve PIK collateral discount and premium`` (amend: bool) =
    let created = createLoanWith (thirty360Terms ())
    let collateral: IReadOnlyList<CollateralDto> =
        [| CollateralDto(
               Guid.Parse("5c4ed008-0000-4000-8000-0000000000c0"),
               CollateralType.RealEstate,
               "Characterization warehouse",
               750_000m,
               CurrencyCode.USD,
               DateOnly(2025, 12, 15)) |]
    let servicing =
        LoanServicingStateDto(
            created.Servicing.LoanId,
            created.Servicing.Status,
            created.Servicing.CurrentCommitment,
            created.Servicing.TotalDrawn,
            created.Servicing.AvailableToDraw,
            created.Servicing.Balances,
            created.Servicing.DrawdownLots,
            created.Servicing.CurrentRateReset,
            created.Servicing.LastAccrualDate,
            created.Servicing.LastPaymentDate,
            created.Servicing.ServicingRevision,
            created.Servicing.RevisionHistory,
            created.Servicing.AccrualEntries,
            Collateral = collateral,
            UnamortizedDiscount = 125m,
            UnamortizedPremium = 25m,
            IsPikToggled = true)

    let updated =
        if amend then
            (DirectLendingAggregateInterop.AmendTerms
                created.Contract
                servicing
                (AmendLoanTermsRequest(thirty360Terms (), "preservation regression"))
                (DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))).Servicing
        else
            (DirectLendingAggregateInterop.ActivateLoan
                created.Contract servicing (ActivateLoanRequest(DateOnly(2026, 1, 1)))).Servicing

    updated.Status |> should equal (if amend then LoanStatus.Draft else LoanStatus.Active)
    updated.IsPikToggled |> should equal true
    updated.Collateral |> Seq.toArray |> should equal (collateral |> Seq.toArray)
    updated.UnamortizedDiscount |> should equal 125m
    updated.UnamortizedPremium |> should equal 25m


// Frozen formula-one oracle captured before the servicing day interval moved onto ADR-025's
// kernel. Keep multiplication order as well as the known Thirty360 calendar-day behavior.
let private legacyDailyFraction (basis: DayCountBasis) (date: DateOnly) =
    match basis with
    | DayCountBasis.Act365F -> 1m / 365m
    | DayCountBasis.ActualActualISDA -> 1m / (if DateTime.IsLeapYear date.Year then 366m else 365m)
    | _ -> 1m / 360m

[<Fact>]
let ``Kernel daily cash and PIK fractions match frozen formula one across leap year and terminal dates`` () =
    let dates =
        [ DateOnly.MinValue; DateOnly(2023, 12, 31); DateOnly(2024, 1, 1)
          DateOnly(2024, 2, 29); DateOnly(2024, 12, 31); DateOnly(2025, 1, 1)
          DateOnly(2026, 1, 31); DateOnly.MaxValue ]
    for basis in Enum.GetValues<DayCountBasis>() do
        for date in dates do
            for principal in [ 0m; 1m; 1_000_000.25m ] do
                for rate in [ 0m; 0.06m; 0.0725m ] do
                    let expected =
                        if principal <= 0m || rate <= 0m then 0m
                        else principal * rate * legacyDailyFraction basis date
                    DirectLendingInterop.CalculateDailyAccrualAmountForDate(principal, rate, int basis, date)
                    |> should equal expected
                    DirectLendingInterop.CalculatePikAccrual(principal, rate, int basis, date)
                    |> should equal expected

[<Theory>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``Manual cash and PIK requests keep their explicit date and do not fill a skipped accrual range`` (pik: bool) =
    let terms = thirty360Terms ()
    let created = createLoanWith terms
    let activated = DirectLendingAggregateInterop.ActivateLoan created.Contract created.Servicing (ActivateLoanRequest(DateOnly(2026, 1, 1)))
    let drawn = DirectLendingAggregateInterop.BookDrawdown activated.Servicing (BookDrawdownRequest(1_000_000m, DateOnly(2026, 1, 1), DateOnly(2026, 1, 1), "manual-range-pin"))
    let basis = drawn.Servicing
    let servicing =
        LoanServicingStateDto(
            basis.LoanId, basis.Status, basis.CurrentCommitment, basis.TotalDrawn, basis.AvailableToDraw,
            basis.Balances, basis.DrawdownLots, basis.CurrentRateReset, basis.LastAccrualDate, basis.LastPaymentDate,
            basis.ServicingRevision, basis.RevisionHistory, basis.AccrualEntries, IsPikToggled = pik)
    let first = DirectLendingAggregateInterop.PostDailyAccrual servicing terms (PostDailyAccrualRequest(DateOnly(2026, 1, 1)))
    let secondDate = DateOnly(2026, 2, 1)
    let second = DirectLendingAggregateInterop.PostDailyAccrual first.Servicing terms (PostDailyAccrualRequest(secondDate))
    let principal = first.Servicing.Balances.PrincipalOutstanding
    let expected = Math.Round(principal * 0.06m * legacyDailyFraction terms.DayCountBasis secondDate, 2, MidpointRounding.AwayFromZero)
    second.Entry.AccrualDate |> should equal secondDate
    second.Servicing.LastAccrualDate |> should equal (Nullable secondDate)
    second.Servicing.AccrualEntries.Count |> should equal 2
    second.Entry.InterestAmount |> should equal (if pik then 0m else expected)
    second.Entry.PikInterestAmount |> should equal (if pik then expected else 0m)

[<Fact>]
let ``Manual floating resets retain their explicit backdated and future dates and frozen rate arithmetic`` () =
    let terms = floatingWithFloorCapAndSecurityMasterTerms ()
    let created = createLoanWith terms
    // These are existing manual operation semantics, not an inferred reset grid or approval of
    // automatic application. A versioned scheduled-reset contract must define any stronger rule.
    for effectiveDate in [ DateOnly(2024, 2, 29); DateOnly(2026, 3, 22); DateOnly(2032, 1, 1) ] do
        for observed in [ -0.05m; 0m; 0.03m; 0.20m ] do
            let spread = 475m
            let expected = max 0.01m (observed + spread / 10000m) |> min 0.12m
            let request = ApplyRateResetRequest(effectiveDate, observed, Nullable spread, "manual-reset-pin")
            let decision = DirectLendingAggregateInterop.ApplyRateReset created.Servicing terms request
            decision.AllInRate |> should equal expected
            decision.Servicing.CurrentRateReset.EffectiveDate |> should equal effectiveDate
            decision.Servicing.CurrentRateReset.ObservedRate |> should equal observed
            decision.Servicing.CurrentRateReset.IndexName |> should equal "EURIBOR-3M"
            decision.Servicing.CurrentRateReset.SourceRef |> should equal "manual-reset-pin"
