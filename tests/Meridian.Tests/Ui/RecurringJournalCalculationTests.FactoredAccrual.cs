using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed partial class RecurringJournalCalculationTests
{
    [Theory]
    [InlineData("Bond")]
    [InlineData("StructuredCredit")]
    public async Task FixedIncome_EligibleSegmentedProjection_DraftsOnlyInterestForActualDailyFace(string assetClass)
    {
        // This test supplies an eligible projection to isolate the recurring consumer. It does
        // not bypass the production Bond projection's existing missing-rate-unit blocker.
        await SeedAsync(FixedIncomeAccrualCalculation.CalculationKind, new(),
            new() { ["securityId"] = SecurityId.ToString("D"), ["ticker"] = Ticker, ["bookPositionId"] = Position },
            endsOn: JanuaryEnd);
        var fixture = await CreateFixtureAsync(cashFlows: FactoredCouponCashFlows(), assetClass: assetClass,
            accrualHoldings: FactoredHoldings());

        var result = (await fixture.Runner.RunDueAsync(Due(JanuaryEnd))).Should().ContainSingle().Subject;

        result.State.Should().Be(AutomatedJournalScheduleStateDto.DraftReady);
        var draft = (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        // First lot: 1,200 before the paydown, 420 afterward, then 270 after a half disposal.
        // The later lot was booked at .6 and earns 660 after its 20 January acquisition.
        draft.TotalDebits.Should().Be(2_550m);
        draft.TotalCredits.Should().Be(2_550m);
        draft.Lines.Should().HaveCount(2);
        draft.Lines.Should().OnlyContain(line => line.AccountPath == "Assets:Accrued Interest Receivable UST30"
            || line.AccountPath == "Revenue:Coupon Income");
        draft.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        _ledger.Journal.Should().BeEmpty("the calculation creates an approval draft, never a posted journal");

        await fixture.Runner.RunDueAsync(Due(JanuaryEnd).AddHours(1));
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle();
    }

    [Fact]
    public async Task FixedIncome_StructuredCredit_ProjectsDeclaredRateAndDraftsFactoredAccrual()
    {
        await SeedAsync(FixedIncomeAccrualCalculation.CalculationKind, new(),
            new() { ["securityId"] = SecurityId.ToString("D"), ["ticker"] = Ticker, ["bookPositionId"] = Position },
            endsOn: JanuaryEnd);
        var terms = JsonSerializer.SerializeToElement(new
        {
            assetClassTermsVersion = 6,
            issueDate = "2025-12-31",
            maturity = "2026-12-31",
            originalFace = 1_000_000m,
            fixedCouponRate = 36.5m,
            rateUnit = "Percent",
            paymentFrequency = "Monthly",
            dayCountConvention = "ACT/365",
            currentFactor = 0.8m,
            factorDate = "2025-12-31",
            factorScheduleEntries = new[]
            {
                new { asOfDate = "2025-12-31", factor = 0.8m },
                new { asOfDate = "2026-01-15", factor = 0.6m }
            }
        });
        var security = new SecurityDetailDto(SecurityId, "StructuredCredit", SecurityStatusDto.Active, Ticker, "USD",
            JsonSerializer.SerializeToElement(new { }), terms, [], [], 1, Registered, null);
        var query = new Mock<ISecurityMasterQueryService>(MockBehavior.Strict);
        query.Setup(value => value.GetByIdAsync(SecurityId, It.IsAny<CancellationToken>())).ReturnsAsync(security);
        query.Setup(value => value.GetByIdAsOfAsync(SecurityId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(security);
        var source = new Mock<ISecurityMasterCashFlowStore>(MockBehavior.Strict);
        source.Setup(value => value.GetSourceAsync(SecurityId, It.IsAny<CancellationToken>())).ReturnsAsync(
            new SecurityCashFlowSourceDto(SecurityId, StructuredCashFlowSourceKind.CalculatedBullet,
                DateTimeOffset.UtcNow, false, null, null));
        var receiptStore = new FileSecurityCashFlowReceiptStore(Path.Combine(_root, "cash-flow-receipts"));
        var cashFlows = new SecurityMasterCashFlowService(source.Object, [], query.Object,
            NullLogger<SecurityMasterCashFlowService>.Instance, receiptStore: receiptStore);
        var fixture = await CreateFixtureAsync(cashFlows: cashFlows, assetClass: "StructuredCredit",
            accrualHoldings: FactoredHoldings(), securities: query.Object);

        var result = (await fixture.Runner.RunDueAsync(Due(JanuaryEnd))).Should().ContainSingle().Subject;

        result.State.Should().Be(AutomatedJournalScheduleStateDto.DraftReady);
        var draft = (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        draft.TotalDebits.Should().Be(2_550m);
        draft.TotalCredits.Should().Be(2_550m);
        draft.Lines.Should().HaveCount(2, "the source's 200,000 principal paydown is excluded from recurring journals");
        var provenance = RecurringJournalEvidenceGuard.Deserialize(draft.RecurringJournalEvidenceJson!);
        var retainedInput = provenance!.SourceEvidence.Should().ContainSingle(reference =>
            reference.Kind == "cash-flow-calculation-receipt").Subject;
        retainedInput.SourceReference.Should().Be(SecurityMasterCashFlowService.ContextCalculationVersion);
        draft.EvidenceLinks.Should().Contain(retainedInput.Uri);
        var retainedReceipt = await receiptStore.GetAsync(retainedInput.ContentHash!);
        retainedReceipt.Should().NotBeNull();
        retainedReceipt!.ModelVersion.Should().Be(SecurityMasterCashFlowService.ContextCalculationVersion);
        retainedReceipt.Security.AssetSpecificTerms.GetProperty("rateUnit").GetString().Should().Be("Percent");
        RecurringJournalEvidenceGuard.Validate(draft).Should().BeNull();

        // Restart with an unavailable source. The existing approved-input draft remains the
        // authoritative calculation; retry must not replace its receipt or reload the terms.
        var unavailableFlows = new Mock<ISecurityMasterCashFlowService>(MockBehavior.Strict);
        var restarted = await CreateFixtureAsync(cashFlows: unavailableFlows.Object, assetClass: "StructuredCredit", securities: query.Object);
        await restarted.Runner.RunDueAsync(Due(JanuaryEnd).AddHours(1));
        var restored = (await restarted.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        restored.RecurringJournalEvidenceJson.Should().Be(draft.RecurringJournalEvidenceJson);
        restored.RecurringJournalEvidenceDigest.Should().Be(draft.RecurringJournalEvidenceDigest);
        restored.EvidenceLinks.Should().Contain(retainedInput.Uri);
        RecurringJournalEvidenceGuard.Validate(restored).Should().BeNull();
        await ReviewAndPostRetainedCalculationAsync(restarted, restored);
        restarted.Posted.Should().ContainSingle();
        var postedEvidence = restarted.Posted[0].Entry.Metadata.Tags![RecurringJournalEvidenceGuard.EvidenceTag];
        postedEvidence.Should().Be(draft.RecurringJournalEvidenceJson);
        unavailableFlows.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FixedIncome_SegmentedProjectionWithoutRetainedV5Receipt_BlocksBeforeReadingHoldings()
    {
        await SeedAsync(FixedIncomeAccrualCalculation.CalculationKind, new(),
            new() { ["securityId"] = SecurityId.ToString("D"), ["ticker"] = Ticker, ["bookPositionId"] = Position },
            endsOn: JanuaryEnd);
        var fixture = await CreateFixtureAsync(cashFlows: FactoredCouponCashFlows(retainedEvidence: false),
            assetClass: "StructuredCredit", accrualHoldings: FactoredHoldings());

        var result = (await fixture.Runner.RunDueAsync(Due(JanuaryEnd))).Should().ContainSingle().Subject;

        result.State.Should().Be(AutomatedJournalScheduleStateDto.Blocked);
        result.Blockers.Should().ContainSingle().Which.Should().Contain("retained v5 calculation receipt");
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().BeEmpty();
    }

    [Fact]
    public async Task FixedIncome_MissingAccrualHistory_BlocksWithoutDrafting()
    {
        await SeedAsync(FixedIncomeAccrualCalculation.CalculationKind, new(),
            new() { ["securityId"] = SecurityId.ToString("D"), ["ticker"] = Ticker, ["bookPositionId"] = Position },
            endsOn: JanuaryEnd);
        var fixture = await CreateFixtureAsync(cashFlows: FactoredCouponCashFlows(), assetClass: "StructuredCredit",
            accrualReadFailure: new LedgerValidationException("Historical quantity cannot be established from complete retained mutation evidence."));

        var result = (await fixture.Runner.RunDueAsync(Due(JanuaryEnd))).Should().ContainSingle().Subject;

        result.State.Should().Be(AutomatedJournalScheduleStateDto.Blocked);
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().BeEmpty();
    }

    [Fact]
    public async Task FixedIncome_FullPaydown_EarnsTheFinalDayWithoutDraftingPrincipal()
    {
        await SeedAsync(FixedIncomeAccrualCalculation.CalculationKind, new(),
            new() { ["securityId"] = SecurityId.ToString("D"), ["ticker"] = Ticker, ["bookPositionId"] = Position },
            endsOn: JanuaryEnd);
        var holdings = Enumerable.Range(1, 31).Select(day =>
        {
            var date = new DateOnly(2026, 1, day);
            return new FaceValueAccrualHolding(date,
                new FaceValueLot("paid-pool", SecurityId, new DateOnly(2025, 12, 31), 80_000m, 100m,
                    bookedFactor: 0.8m, currentFactor: date == JanuaryEnd ? 0m : 0.8m),
                1m, 0.8m);
        }).ToArray();
        var fixture = await CreateFixtureAsync(cashFlows: FactoredCouponCashFlows(fullPaydown: true),
            assetClass: "StructuredCredit", accrualHoldings: holdings);

        var result = (await fixture.Runner.RunDueAsync(Due(JanuaryEnd))).Should().ContainSingle().Subject;

        result.State.Should().Be(AutomatedJournalScheduleStateDto.DraftReady);
        var draft = (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        draft.TotalDebits.Should().Be(2_480m, "31 interest days include the final paydown date");
        draft.TotalCredits.Should().Be(2_480m);
        draft.Lines.Should().HaveCount(2);
        draft.Lines.Should().NotContain(line => line.AccountPath.Contains("Cash", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FixedIncome_SourceAndLotFactorDisagree_BlocksWithoutDrafting()
    {
        await SeedAsync(FixedIncomeAccrualCalculation.CalculationKind, new(),
            new() { ["securityId"] = SecurityId.ToString("D"), ["ticker"] = Ticker, ["bookPositionId"] = Position },
            endsOn: JanuaryEnd);
        var holdings = FactoredHoldings().Select(holding => holding with { AccrualFactor = 0.7m }).ToArray();
        var fixture = await CreateFixtureAsync(cashFlows: FactoredCouponCashFlows(), assetClass: "StructuredCredit",
            accrualHoldings: holdings);

        var result = (await fixture.Runner.RunDueAsync(Due(JanuaryEnd))).Should().ContainSingle().Subject;

        result.State.Should().Be(AutomatedJournalScheduleStateDto.Blocked);
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().BeEmpty();
    }

    [Fact]
    public async Task FixedIncome_StructuredCreditWithoutSegmentEvidence_Blocks()
    {
        await SeedAsync(FixedIncomeAccrualCalculation.CalculationKind, new(),
            new() { ["securityId"] = SecurityId.ToString("D"), ["ticker"] = Ticker, ["bookPositionId"] = Position },
            endsOn: JanuaryEnd);
        var fixture = await CreateFixtureAsync(cashFlows: MonthlyCouponCashFlows(), assetClass: "StructuredCredit");

        var result = (await fixture.Runner.RunDueAsync(Due(JanuaryEnd))).Should().ContainSingle().Subject;

        result.State.Should().Be(AutomatedJournalScheduleStateDto.Blocked);
        result.Blockers.Should().ContainSingle().Which.Should().Contain("retained interest segments");
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().BeEmpty();
    }

    [Fact]
    public async Task FixedIncome_AccrualHistoryRead_ForwardsExactBookPositionAndDateScope()
    {
        var position = Guid.Parse(Position);
        var from = new DateOnly(2025, 12, 31);
        var expected = FactoredHoldings();
        var store = new Mock<ILedgerJournalStore>(MockBehavior.Strict);
        store.Setup(value => value.ListTaxLotAccrualSnapshotsAsync(Book, SecurityId, position, from, JanuaryEnd,
                It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var result = await new LedgerRecurringJournalCalculationInputs(store.Object)
            .ListAccrualHoldingsAsync(Scope, SecurityId, position, from, JanuaryEnd, default);

        result.Should().BeSameAs(expected);
        store.VerifyAll();
    }

    [Fact]
    public async Task FixedIncome_LaterAccountingWindow_ReadsHoldingsFromTheCouponStart()
    {
        var start = new DateOnly(2025, 12, 31);
        var februaryStart = new DateOnly(2026, 2, 1);
        var inputs = new Mock<IRecurringJournalCalculationInputs>(MockBehavior.Strict);
        FaceValueAccrualHolding[] history =
        [
            new(new DateOnly(2026, 1, 1), new FaceValueLot("pool", SecurityId, start, 80_000m, 100m,
                bookedFactor: 0.8m), 1m, 0.8m),
            new(februaryStart, new FaceValueLot("pool", SecurityId, start, 80_000m, 100m,
                bookedFactor: 0.8m), 1m, 0.8m)
        ];
        inputs.Setup(value => value.ListAccrualHoldingsAsync(Scope, SecurityId, Guid.Parse(Position), start, FebruaryEnd,
            It.IsAny<CancellationToken>())).ReturnsAsync(history);
        var flows = new Mock<ISecurityMasterCashFlowService>(MockBehavior.Strict);
        var projectionAsOf = new DateTimeOffset(februaryStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var projection = new StructuredCashFlowProjectionDto(SecurityId, StructuredCashFlowSourceKind.CalculatedBullet,
            StructuredCashFlowScenario.Base, projectionAsOf,
            [new(At(FebruaryEnd), 0m, 47_200m, 0.8m)
            {
                RowVersion = 2,
                InterestMeasuredFrom = start,
                InterestMeasuredTo = FebruaryEnd,
                InterestSegments = [new(start, FebruaryEnd, 800_000m, 47_200m, 0.8m, start, FebruaryEnd) { FactorAfter = 0.8m }]
            }], StructuredCashFlowStaleness.Fresh, projectionAsOf,
            TermsUsed: StructuredCashFlowTerms.Empty with { PrincipalFace = 1_000_000m });
        projection = WithSyntheticReceipt(projection);
        flows.Setup(value => value.GetProjectionAsOfAsync(SecurityId, StructuredCashFlowScenario.Base,
            projectionAsOf, It.IsAny<CancellationToken>())).ReturnsAsync(projection);
        flows.Setup(value => value.GetProjectionAsync(It.IsAny<SecurityDetailDto>(), StructuredCashFlowScenario.Base,
            It.Is<BondCalculationContext>(capture => capture.AsOf == projectionAsOf && capture.SecurityVersion == 1),
            It.IsAny<CancellationToken>())).ReturnsAsync(projection);
        var bridge = new Mock<ISecurityMasterAmortizationLedgerBridge>(MockBehavior.Strict);
        bridge.Setup(value => value.PostProjectedCashFlowsAsync(SecurityId, Ticker, It.IsAny<Meridian.Ledger.Ledger>(),
                It.Is<AmortizationLedgerPostingContext>(posting => posting.OpenLots == null
                    && posting.AccrualHoldings == history && posting.AccrualWindow!.Start == februaryStart
                    && posting.RetainedProjection == projection),
                It.IsAny<CancellationToken>())).ReturnsAsync(0);
        var calculation = new FixedIncomeAccrualCalculation(inputs.Object, bridge.Object, flows.Object,
            Securities("USD", "StructuredCredit"));

        var result = await calculation.CalculateAsync(new RecurringJournalCalculationContext(Scope, ScheduleId,
            FebruaryEnd, februaryStart, Due(FebruaryEnd), February.ToString("D"),
            new Dictionary<string, decimal>(),
            new Dictionary<string, string> { ["securityId"] = SecurityId.ToString("D"), ["ticker"] = Ticker, ["bookPositionId"] = Position },
            [Source()]), default);

        result.Draft.Should().BeNull();
        inputs.VerifyAll();
        bridge.VerifyAll();
    }

    private static IReadOnlyList<FaceValueAccrualHolding> FactoredHoldings()
    {
        var holdings = new List<FaceValueAccrualHolding>();
        for (var date = new DateOnly(2026, 1, 1); date <= JanuaryEnd; date = date.AddDays(1))
        {
            var currentFactor = date < new DateOnly(2026, 1, 15) ? 0.8m : 0.6m;
            var accrualFactor = date <= new DateOnly(2026, 1, 15) ? 0.8m : 0.6m;
            var first = new FaceValueLot("pool-a", SecurityId, new DateOnly(2025, 12, 31), 80_000m, 100m,
                bookedFactor: 0.8m, currentFactor: currentFactor);
            holdings.Add(new(date, first, date < new DateOnly(2026, 1, 23) ? 1m : 0.5m, accrualFactor));
            if (date >= new DateOnly(2026, 1, 20))
                holdings.Add(new(date, new FaceValueLot("pool-b", SecurityId, new DateOnly(2026, 1, 20),
                    60_000m, 1m, bookedFactor: 0.6m, parBasis: 1m), 1m, 0.6m));
        }
        return holdings;
    }

    private static ISecurityMasterCashFlowService FactoredCouponCashFlows(bool fullPaydown = false, bool retainedEvidence = true)
    {
        var service = new Mock<ISecurityMasterCashFlowService>(MockBehavior.Strict);
        StructuredCashFlowProjectionDto Projection(Guid securityId, StructuredCashFlowScenario scenario, DateTimeOffset asOf)
        {
            var start = new DateOnly(2025, 12, 31);
            var paydown = new DateOnly(2026, 1, 15);
            StructuredCashFlowScheduleEntry[] rows =
            [
                new(At(paydown), 200_000m, 0m, 0.6m) { RowVersion = 2 },
                new(At(JanuaryEnd), 0m, 21_600m, 0.6m)
                {
                    RowVersion = 2,
                    InterestMeasuredFrom = start,
                    InterestMeasuredTo = JanuaryEnd,
                    InterestSegments =
                    [
                        new(start, paydown, 800_000m, 12_000m, 0.8m, start, JanuaryEnd) { FactorAfter = 0.6m },
                        new(paydown, JanuaryEnd, 600_000m, 9_600m, 0.6m, start, JanuaryEnd) { FactorAfter = 0.6m }
                    ]
                }
            ];
            if (fullPaydown)
                rows =
                [
                    new(At(JanuaryEnd), 800_000m, 24_800m, 0m)
                    {
                        RowVersion = 2,
                        InterestMeasuredFrom = start,
                        InterestMeasuredTo = JanuaryEnd,
                        InterestSegments = [new(start, JanuaryEnd, 800_000m, 24_800m, 0.8m, start, JanuaryEnd) { FactorAfter = 0m }]
                    }
                ];
            return new StructuredCashFlowProjectionDto(securityId, StructuredCashFlowSourceKind.CalculatedBullet,
                scenario, asOf, rows, StructuredCashFlowStaleness.Fresh, asOf,
                TermsUsed: StructuredCashFlowTerms.Empty with
                {
                    MaturityDate = new DateOnly(2026, 12, 31),
                    DayCountConvention = "ACT/365",
                    PrincipalFace = 1_000_000m
                });
        }
        service.Setup(value => value.GetProjectionAsOfAsync(SecurityId, StructuredCashFlowScenario.Base,
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid securityId, StructuredCashFlowScenario scenario, DateTimeOffset asOf, CancellationToken _) =>
                Projection(securityId, scenario, asOf));
        service.Setup(value => value.GetProjectionAsync(It.IsAny<SecurityDetailDto>(), StructuredCashFlowScenario.Base,
                It.IsAny<BondCalculationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SecurityDetailDto selected, StructuredCashFlowScenario scenario, BondCalculationContext context, CancellationToken _) =>
                retainedEvidence ? WithSyntheticReceipt(Projection(selected.SecurityId, scenario, context.AsOf))
                    : Projection(selected.SecurityId, scenario, context.AsOf));
        return service.Object;
    }

    // Consumer tests deliberately substitute an already retained receipt; the real source test
    // above uses FileSecurityCashFlowReceiptStore and proves the actual retention round trip.
    private static StructuredCashFlowProjectionDto WithSyntheticReceipt(StructuredCashFlowProjectionDto projection)
        => projection with
        {
            InputManifest =
            [
                new("CalculationModel", SecurityMasterCashFlowService.ContextCalculationVersion),
                new("CalculationReceipt", new string('c', 64))
            ]
        };
}
