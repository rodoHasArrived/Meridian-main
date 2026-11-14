using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Instruments.AssetOperations;
using Meridian.Tests.Ui;
using Meridian.Ui.Shared.Services;
using static Meridian.Tests.Ui.SecurityMasterWorkbenchScheduleBookHarness;

namespace Meridian.Tests.FixedIncome;

/// <summary>
/// Declared call and put schedules of the two classes that declare the arrays without a coupon schedule
/// kernel, CertificateOfDeposit and StructuredCredit (ADR-025, increment 8f). Their terms carry no issue
/// date or payment frequency, so the notice watch resolves the declared dates directly: the entry and
/// notice rules are Bond's, an exercise date is the declared date unless a resolved conventions block
/// adjusts it, and whatever cannot be resolved is a blocker, never a scalar fallback. Both rows mature
/// 2031-01-15 and are read at 2026-12-01.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DeclaredExerciseNonBondClassTests
{
    private const string CertificateOfDeposit = "CertificateOfDeposit";
    private const string StructuredCredit = "StructuredCredit";
    private static readonly DateOnly AsOfDate = new(2026, 12, 1);
    private static readonly DateTimeOffset AsOf = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Maturity = new(2031, 1, 15);
    private static readonly IBusinessDayCalendarSource Calendars =
        new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]);

    [Theory]
    [InlineData(CertificateOfDeposit)]
    [InlineData(StructuredCredit)]
    public void EuropeanCallWithCalendarNotice_ResolvesTheDeclaredDate_AndItsNoticeWindow(string assetClass)
    {
        var result = BondExerciseNoticeWatch.EvaluateToMaturity(Row(assetClass, [Call(30, 60, "Calendar")]), Calendars, AsOfDate);

        result.Blockers.Should().BeEmpty();
        result.HorizonDays.Should().Be(Maturity.DayNumber - AsOfDate.DayNumber);
        result.Windows.Should().ContainSingle().Which.Should().Be(new BondExerciseNoticeWindow(
            "Call", new DateOnly(2027, 1, 15), 101m, new DateOnly(2026, 11, 16), new DateOnly(2026, 12, 16),
            BondExerciseNoticeStatus.Open, 15));
        result.Diagnostics.Should().Equal(BondExerciseNoticeWatch.AuthorityDiagnostic);
    }

    [Theory]
    [InlineData(CertificateOfDeposit)]
    [InlineData(StructuredCredit)]
    public void SameEntries_GiveTheDatesABondGives(string assetClass)
    {
        // A European call with calendar notice and a put without bounds: the rules that turn an entry
        // into an exercise date and a notice window are shared, so the windows are a bond's.
        Dictionary<string, object?>[] calls = [Call(30, 60, "Calendar")];
        Dictionary<string, object?>[] puts = [Put("2028-01-18")];
        var bond = BondExerciseNoticeWatch.EvaluateToMaturity(BondExerciseNoticeWatchTests.Bond(calls, puts), Calendars, AsOfDate);
        var other = BondExerciseNoticeWatch.EvaluateToMaturity(Row(assetClass, calls, puts), Calendars, AsOfDate);

        bond.Blockers.Should().BeEmpty();
        other.Blockers.Should().BeEmpty();
        other.Windows.Should().Equal(bond.Windows);
        other.Windows.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(CertificateOfDeposit)]
    [InlineData(StructuredCredit)]
    public void BermudanCall_NeedsItsOwnExerciseFrequency_AndListsEveryDateToItsEnd(string assetClass)
    {
        var bermudan = Call(null, null, null);
        bermudan["exerciseStyle"] = "Bermudan";
        bermudan["callEndDate"] = "2028-01-15";
        bermudan["exerciseFrequency"] = "SemiAnnual";

        var resolved = BondExerciseNoticeWatch.EvaluateToMaturity(Row(assetClass, [bermudan]), Calendars, AsOfDate);

        resolved.Blockers.Should().BeEmpty();
        // No conventions block resolves a payment adjustment, so the Saturday 2028-01-15 stays as declared.
        resolved.Windows.Select(static window => window.ExerciseDate).Should().Equal(
            new DateOnly(2027, 1, 15), new DateOnly(2027, 7, 15), new DateOnly(2028, 1, 15));
        resolved.Windows.Should().OnlyContain(static window => window.Status == BondExerciseNoticeStatus.NotStated);

        bermudan.Remove("exerciseFrequency");
        var blocked = BondExerciseNoticeWatch.EvaluateToMaturity(Row(assetClass, [bermudan]), Calendars, AsOfDate);

        var blocker = blocked.Blockers.Should().ContainSingle().Subject;
        blocker.Code.Should().Be("ExerciseScheduleRefused");
        blocker.Reason.Should().Contain("Bermudan exercise requires", "there is no coupon frequency to fall back to");
        blocked.Windows.Should().BeEmpty();
    }

    [Fact]
    public void ScalarCallableDateThatDisagreesWithTheDeclaredSchedule_IsBlocked()
    {
        var row = Row(CertificateOfDeposit, [Call(30, 60, "Calendar")], extra: new() { ["callableDate"] = "2028-01-15" });

        var blocker = BondExerciseNoticeWatch.EvaluateToMaturity(row, Calendars, AsOfDate).Blockers.Should().ContainSingle().Subject;

        blocker.Code.Should().Be("ExerciseScheduleRefused");
        blocker.Reason.Should().Contain("callableDate must equal the earliest declared call date");
    }

    [Theory]
    [InlineData(CertificateOfDeposit)]
    [InlineData(StructuredCredit)]
    public void BusinessDayNotice_IsBlocked_BecauseNoPaymentCalendarsCanBeDeclared(string assetClass)
    {
        var result = BondExerciseNoticeWatch.EvaluateToMaturity(Row(assetClass, [Call(10, null, "Business")]), Calendars, AsOfDate);

        var blocker = result.Blockers.Should().ContainSingle().Subject;
        blocker.Code.Should().Be("ExerciseScheduleRefused");
        blocker.Reason.Should().Contain("Business notice require block paymentCalendars");
        result.Windows.Should().BeEmpty();
    }

    [Theory]
    [InlineData(CertificateOfDeposit)]
    [InlineData(StructuredCredit)]
    public void PopulatedConventionsBlock_IsBlockedByTheScheduleFactory_NotIgnored(string assetClass)
    {
        // The class's terms declare no schedule anchors, so the one authority for the block refuses it.
        // Reading the entries as if the block were absent would drop a declared payment adjustment.
        var row = Row(assetClass, [Call(30, 60, "Calendar")], extra: new()
        {
            [SecurityVersionedTerms.ScheduleConventionsKey] = new { paymentBusinessDayConvention = "Following", paymentCalendars = new[] { "WEEKENDS" } }
        });

        var result = BondExerciseNoticeWatch.EvaluateToMaturity(row, Calendars, AsOfDate);

        var blocker = result.Blockers.Should().ContainSingle().Subject;
        blocker.Code.Should().Be("DeclaredScheduleRequired");
        blocker.Reason.Should().Contain("issueDate");
        result.Windows.Should().BeEmpty();
    }

    [Theory]
    [InlineData("exerciseStyle", "American")]
    [InlineData("priceType", "MakeWhole")]
    [InlineData("priceBasis", "PercentOfAccretedValue")]
    [InlineData("partialRedemption", "ProRata")]
    public void EntriesBondRefuses_AreRefusedHereToo(string member, string value)
    {
        var call = Call(null, null, null);
        call[member] = value;

        foreach (var assetClass in new[] { CertificateOfDeposit, StructuredCredit })
        {
            var result = BondExerciseNoticeWatch.EvaluateToMaturity(Row(assetClass, [call]), Calendars, AsOfDate);

            result.Blockers.Should().ContainSingle().Which.Code.Should().Be("ExerciseScheduleRefused");
            result.Windows.Should().BeEmpty();
        }
    }

    [Fact]
    public void EntryWithoutAnExerciseStyle_IsRefused()
    {
        var call = Call(null, null, null);
        call.Remove("exerciseStyle");

        BondExerciseNoticeWatch.EvaluateToMaturity(Row(StructuredCredit, [call]), Calendars, AsOfDate)
            .Blockers.Should().ContainSingle().Which.Code.Should().Be("ExerciseScheduleRefused");
    }

    [Theory]
    [InlineData(CertificateOfDeposit)]
    [InlineData(StructuredCredit)]
    public void EvaluationOnOrAfterMaturity_IsOutsideLife(string assetClass)
        => BondExerciseNoticeWatch.EvaluateToMaturity(Row(assetClass, [Call(30, 60, "Calendar")]), Calendars, Maturity)
            .Blockers.Should().ContainSingle().Which.Code.Should().Be("EvaluationOutsideLife");

    [Fact]
    public void StructuredCreditWithoutAMaturity_IsOutsideLife()
    {
        var call = Call(null, null, null);
        call["callEndDate"] = "2027-01-15";
        var row = Row(StructuredCredit, [call], extra: new() { ["maturity"] = null });

        BondExerciseNoticeWatch.EvaluateToMaturity(row, Calendars, AsOfDate)
            .Blockers.Should().ContainSingle().Which.Code.Should().Be("EvaluationOutsideLife");
    }

    [Theory]
    [InlineData(CertificateOfDeposit, 2, "terms version 3")]
    [InlineData(StructuredCredit, 3, "terms version 4")]
    public void StampBelowTheArrays_IsRefusedByTheWatch_AndIsNotADeclaringRecord(string assetClass, int stamp, string expected)
    {
        var row = Row(assetClass, [Call(30, 60, "Calendar")], stamp: stamp);

        var blocker = BondExerciseNoticeWatch.EvaluateToMaturity(row, Calendars, AsOfDate).Blockers.Should().ContainSingle().Subject;
        blocker.Code.Should().Be("ExerciseScheduleRefused");
        blocker.Reason.Should().Contain(expected);
        AssetObligationProjectionService.DeclaresExerciseSchedule(row).Should().BeFalse("a stamp that does not decode the arrays declares nothing");
        AssetObligationProjectionService.ResolveDeclaredExercise(row, AsOf, Calendars).Should().BeNull("such a row keeps the scalar read");
    }

    [Fact]
    public void FactoredStructuredCredit_ResolvesItsCallAndPut()
    {
        var row = Row(StructuredCredit, [Call(30, 60, "Calendar")], [Put("2028-01-18")], extra: new()
        {
            ["currentFactor"] = 0.8235m,
            ["factorScheduleEntries"] = new[] { new { asOfDate = "2026-06-01", factor = 0.8412m }, new { asOfDate = "2026-07-01", factor = 0.8235m } }
        });

        var result = BondExerciseNoticeWatch.EvaluateToMaturity(row, Calendars, AsOfDate);

        result.Blockers.Should().BeEmpty("a factor is how this class carries principal; the windows are dates, not redemption cash");
        result.Windows.Select(static window => (window.Kind, window.ExerciseDate, window.RedemptionPrice)).Should().Equal(
            ("Call", new DateOnly(2027, 1, 15), 101m),
            ("Put", new DateOnly(2028, 1, 18), 100m));
    }

    [Fact]
    public void ExerciseOnTheEvaluationDate_StaysVisible_AndAPastOneIsDropped()
    {
        var today = Call(null, null, null);
        today["callDate"] = "2026-12-01";
        var past = Call(null, null, null);
        past["callDate"] = "2026-06-01";

        BondExerciseNoticeWatch.EvaluateToMaturity(
                Row(CertificateOfDeposit, [today], extra: new() { ["callableDate"] = "2026-12-01" }), Calendars, AsOfDate)
            .Windows.Should().ContainSingle().Which.ExerciseDate.Should().Be(AsOfDate);

        var elapsed = BondExerciseNoticeWatch.EvaluateToMaturity(
            Row(CertificateOfDeposit, [past], extra: new() { ["callableDate"] = "2026-06-01" }), Calendars, AsOfDate);
        elapsed.Blockers.Should().BeEmpty();
        elapsed.Windows.Should().BeEmpty("the watch lists no exercise date before its evaluation date");
    }

    [Fact]
    public void HorizonRead_FiltersWindowsBeyondIt()
    {
        var far = Call(30, 60, "Calendar");
        far["callDate"] = "2029-01-15";
        var row = Row(CertificateOfDeposit, [far], extra: new() { ["callableDate"] = "2029-01-15" });

        BondExerciseNoticeWatch.Evaluate(row, Calendars, AsOfDate, 90).Windows.Should().BeEmpty();
        BondExerciseNoticeWatch.EvaluateToMaturity(row, Calendars, AsOfDate).Windows.Should().ContainSingle();
    }

    [Fact]
    public void ClassWithoutTheArrays_StaysUnsupported()
    {
        var deposit = new SecurityDetailDto(Guid.NewGuid(), "Deposit", SecurityStatusDto.Active, "Deposit", "USD",
            JsonSerializer.SerializeToElement(new { }),
            JsonSerializer.SerializeToElement(new { depositType = "Term", institutionName = "Meridian Bank", maturity = "2031-01-15" }),
            [], [], 1, AsOf.AddYears(-1), null);

        BondExerciseNoticeWatch.EvaluateToMaturity(deposit, Calendars, AsOfDate)
            .Blockers.Should().ContainSingle().Which.Code.Should().Be("UnsupportedAssetClass");
        BondExerciseNoticeWatch.DeclaresExerciseScheduleFields("Deposit").Should().BeFalse();
        BondExerciseNoticeWatch.DeclaresExerciseScheduleFields(CertificateOfDeposit).Should().BeTrue();
        BondExerciseNoticeWatch.DeclaresExerciseScheduleFields(StructuredCredit).Should().BeTrue();
    }

    [Theory]
    [InlineData(CertificateOfDeposit, true)]
    [InlineData(StructuredCredit, true)]
    [InlineData(CertificateOfDeposit, false)]
    [InlineData(StructuredCredit, false)]
    public async Task ScheduleBookAndObligationTimeline_ListTheSameDates_AndTheSameBlocker(string assetClass, bool resolves)
    {
        var call = Call(30, 60, "Calendar");
        if (!resolves)
            call["exerciseStyle"] = "American";
        var security = Row(assetClass, [call], [Put("2028-01-18")]);

        var snapshot = await CreateService(new StoredRowSecurityMasterQueryService(ProjectionOf(security)), Calendars, now: AsOf)
            .GetTrustSnapshotAsync(security.SecurityId, fundProfileId: null);
        var detail = await new AssetObligationProjectionService(null, new FixedTimeProvider(AsOf), Calendars)
            .ProjectFromSecurityMasterAsync(security, new BondCalculationContext(AsOf),
                new StructuredCashFlowProjectionDto(security.SecurityId, StructuredCashFlowSourceKind.CalculatedBullet,
                    StructuredCashFlowScenario.Base, AsOf, []));

        var book = snapshot!.ScheduleBook!;
        var bookRows = book.Events.Where(static row => OptionEventTypes.Contains(row.EventType))
            .Select(static row => (row.EventType, row.EffectiveDate)).OrderBy(static row => row).ToArray();
        var timeline = detail.TermsObligationsTimeline!;
        var timelineRows = timeline.Events.Where(static row => TimelineKindToEventType.ContainsKey(row.EventKind))
            .Select(static row => (TimelineKindToEventType[row.EventKind], row.EffectiveDate)).OrderBy(static row => row).ToArray();

        bookRows.Should().Equal(timelineRows);
        timeline.EngineVersion.Should().EndWith(AssetOperationsEngineVersions.DeclaredObligationTimeline);
        timeline.Events.Should().NotContain(static row => row.EventKind == "CallPutReview", "the declared schedule is the authority");
        var outcome = book.DeclaredExercise!;
        if (resolves)
        {
            outcome.Status.Should().Be(SecurityMasterDeclaredExerciseStatus.Resolved);
            bookRows.Should().Equal(
                ("Call", new DateOnly(2027, 1, 15)),
                ("CallNoticeDeadline", new DateOnly(2026, 12, 16)),
                ("Put", new DateOnly(2028, 1, 18)));
            book.NextLifecycleDate.Should().Be(new DateOnly(2026, 12, 16), "an open notice deadline is the next lifecycle date");
        }
        else
        {
            outcome.Status.Should().Be(SecurityMasterDeclaredExerciseStatus.Blocked);
            outcome.Blockers.Should().ContainSingle().Which.Code.Should().Be("ExerciseScheduleRefused");
            bookRows.Should().BeEmpty("a blocked declared schedule shows no option row and no scalar fallback");
            timeline.Warnings.Should().Contain(warning => warning.Contains("Declared call/put schedule is blocked (ExerciseScheduleRefused)", StringComparison.Ordinal));
            book.Summary.Should().Contain("Declared call/put schedule is blocked (ExerciseScheduleRefused)");
            snapshot.InstrumentPassport!.OperationsWorkbench!.Panels.Single(static panel => panel.PanelId == "terms")
                .Items.Single(static item => item.ItemId == "payment-schedule").Status.Should().Be("Review");
        }
    }

    [Theory]
    [InlineData(CertificateOfDeposit)]
    [InlineData(StructuredCredit)]
    public async Task RecordThatDeclaresNoSchedule_KeepsItsScalarRead(string assetClass)
    {
        var security = Row(assetClass, extra: assetClass == CertificateOfDeposit ? new() { ["callableDate"] = "2027-01-15" } : null);

        var snapshot = await CreateService(new StoredRowSecurityMasterQueryService(ProjectionOf(security)), Calendars, now: AsOf)
            .GetTrustSnapshotAsync(security.SecurityId, fundProfileId: null);

        snapshot!.ScheduleBook!.DeclaredExercise.Should().BeNull();
        snapshot.ScheduleBook.Events.Should().NotContain(static row => row.EventType.Contains("NoticeDeadline", StringComparison.Ordinal));
        AssetObligationProjectionService.ResolveDeclaredExercise(security, AsOf, Calendars).Should().BeNull();
    }

    private static readonly string[] OptionEventTypes =
    [
        SecurityMasterWorkbenchQueryService.CallEventType, SecurityMasterWorkbenchQueryService.CallNoticeDeadlineEventType,
        SecurityMasterWorkbenchQueryService.PutEventType, SecurityMasterWorkbenchQueryService.PutNoticeDeadlineEventType
    ];

    private static readonly Dictionary<string, string> TimelineKindToEventType = new(StringComparer.Ordinal)
    {
        [AssetObligationProjectionService.IssuerCallExerciseEventKind] = SecurityMasterWorkbenchQueryService.CallEventType,
        [AssetObligationProjectionService.IssuerCallNoticeDeadlineEventKind] = SecurityMasterWorkbenchQueryService.CallNoticeDeadlineEventType,
        [AssetObligationProjectionService.HolderPutExerciseEventKind] = SecurityMasterWorkbenchQueryService.PutEventType,
        [AssetObligationProjectionService.HolderPutNoticeDeadlineEventKind] = SecurityMasterWorkbenchQueryService.PutNoticeDeadlineEventType
    };

    private static Dictionary<string, object?> Call(int? minimum, int? maximum, string? dayType)
    {
        var entry = new Dictionary<string, object?>
        {
            ["callDate"] = "2027-01-15",
            ["callPrice"] = 101m,
            ["exerciseStyle"] = "European",
            ["priceBasis"] = "PercentOfPar"
        };
        if (minimum is not null)
            entry["minimumNoticeDays"] = minimum;
        if (maximum is not null)
            entry["maximumNoticeDays"] = maximum;
        if (dayType is not null)
            entry["noticeDayType"] = dayType;
        return entry;
    }

    private static Dictionary<string, object?> Put(string date) => new()
    {
        ["putDate"] = date,
        ["putPrice"] = 100m,
        ["exerciseStyle"] = "European",
        ["priceBasis"] = "PercentOfPar"
    };

    /// <summary>A CertificateOfDeposit or StructuredCredit row at the stamp that decodes its call and put arrays.</summary>
    private static SecurityDetailDto Row(string assetClass, Dictionary<string, object?>[]? calls = null,
        Dictionary<string, object?>[]? puts = null, int? stamp = null, Dictionary<string, object?>? extra = null)
    {
        var certificate = assetClass == CertificateOfDeposit;
        var payload = certificate
            ? new Dictionary<string, object?>
            {
                ["assetClassTermsVersion"] = stamp ?? 3,
                ["issuerName"] = "Meridian Bank",
                ["maturity"] = "2031-01-15",
                ["couponRate"] = 4m,
                ["dayCount"] = "30/360"
            }
            : new Dictionary<string, object?>
            {
                ["assetClassTermsVersion"] = stamp ?? 4,
                ["tranche"] = "A-1",
                ["collateralType"] = "CLO",
                ["originalFace"] = 1_000_000m,
                ["couponOrIndex"] = "SOFR+250",
                ["maturity"] = "2031-01-15"
            };
        if (calls is { Length: > 0 })
        {
            payload[SecurityVersionedTerms.CallScheduleKey] = calls;
            if (certificate)
                payload["callableDate"] = calls.Min(static call => (string)call["callDate"]!);
        }
        if (puts is { Length: > 0 })
            payload[SecurityVersionedTerms.PutScheduleKey] = puts;
        foreach (var (key, value) in extra ?? [])
        {
            if (value is null)
                payload.Remove(key);
            else
                payload[key] = value;
        }

        return new(Guid.NewGuid(), assetClass, SecurityStatusDto.Active, $"Declaring {assetClass}", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(payload), [], [], 3,
            new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), null);
    }
}
