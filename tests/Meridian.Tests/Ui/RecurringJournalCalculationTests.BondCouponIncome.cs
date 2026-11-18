using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.FixedIncome;
using Meridian.Ui.Shared.Services;
using Moq;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// W10-JRNL-001 / W10-BOND-001: a scheduled bond coupon occurrence runs the governed coupon
/// recognition and income preparation services through the recurring-journal runner and leaves
/// the resulting accounting-event candidate in review, never a second manual draft.
/// </summary>
public sealed partial class RecurringJournalCalculationTests
{
    private static readonly Guid CouponAccount = Guid.Parse("3c0f2e1a-1111-4222-8333-444455556666");
    private static readonly Guid CouponLot = Guid.Parse("8d4e5f60-7777-4888-9999-aaaabbbbcccc");
    private static readonly Guid CouponEvent = Guid.Parse("0b0c0d0e-1f20-4a3b-8c4d-5e6f70819203");

    [Fact]
    public async Task BondCouponIncome_RunnerPreparesCouponRecognitionThroughSpine_WithoutManualDraft()
    {
        await SeedAsync(BondCouponIncomeCalculation.CalculationKind, new(), CouponInputs(), endsOn: JanuaryEnd);
        var harness = new CouponHarness(_root);
        harness.PreviewReturns(hasChanges: true, earned: 4_166.67m);
        var fixture = await CreateFixtureAsync(extraCalculations: [harness.Calculation]);

        var result = (await fixture.Runner.RunDueAsync(Due(JanuaryEnd))).Should().ContainSingle().Subject;

        result.State.Should().Be(AutomatedJournalScheduleStateDto.NoDraftRequired);
        result.Summary.Should().Contain("prepared for accounting review").And.Contain(CouponEvent.ToString("D"))
            .And.Contain("4166.67 earned");
        var expectedRun = BondCouponIncomeCalculation.RunId(ScheduleId, JanuaryEnd);
        harness.Coupons.Verify(service => service.PrepareAsync(
            It.Is<AccountCashForecastScopeDto>(scope => scope.TenantId == Tenant && scope.CompanyId == Company
                && scope.AccountId == CouponAccount.ToString("D") && scope.BookId == Book.ToString("D") && scope.BaseCurrency == "USD"),
            It.Is<BondCouponRecognitionRequest>(request => request.RunId == expectedRun && request.TaxLotRecordId == CouponLot
                && request.PeriodId == January && request.ThroughDate == JanuaryEnd && request.CashAllocations.Count == 0),
            "fingerprint-" + expectedRun, $"recurring-journal:{ScheduleId}", It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Once);
        harness.Spine.Verify(spine => spine.ProjectAsync(It.Is<ProjectAssetAccountingEventRequestDto>(request =>
            request.Actor == "coupon-prep:" + expectedRun), It.IsAny<CancellationToken>()), Times.Once);
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().BeEmpty("the spine candidate, not a manual draft, carries the coupon posting");
        harness.IncomePreparation.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task BondCouponIncome_NextOccurrenceWaitsUntilEarlierCouponIsPosted()
    {
        await SeedAsync(BondCouponIncomeCalculation.CalculationKind, new(), CouponInputs());
        var harness = new CouponHarness(_root);
        harness.PreviewReturns(hasChanges: true, earned: 4_166.67m);
        var fixture = await CreateFixtureAsync(extraCalculations: [harness.Calculation]);
        await fixture.Runner.RunDueAsync(Due(JanuaryEnd));

        var waiting = (await fixture.Runner.RunDueAsync(Due(FebruaryEnd)))
            .Single(result => result.ScheduledForUtc.Month == 2);
        waiting.State.Should().Be(AutomatedJournalScheduleStateDto.Blocked);
        waiting.Blockers.Should().ContainSingle().Which.Should().Contain("awaiting approval and posting");
        harness.Coupons.Verify(service => service.PrepareAsync(It.IsAny<AccountCashForecastScopeDto>(),
            It.Is<BondCouponRecognitionRequest>(request => request.ThroughDate == FebruaryEnd), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);

        harness.MarkPosted(BondCouponIncomeCalculation.RunId(ScheduleId, JanuaryEnd));
        var released = (await fixture.Runner.RunDueAsync(Due(FebruaryEnd).AddHours(1)))
            .Single(result => result.ScheduledForUtc.Month == 2);

        released.State.Should().Be(AutomatedJournalScheduleStateDto.NoDraftRequired);
        harness.Coupons.Verify(service => service.PrepareAsync(It.IsAny<AccountCashForecastScopeDto>(),
            It.Is<BondCouponRecognitionRequest>(request => request.ThroughDate == FebruaryEnd && request.PeriodId == February),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BondCouponIncome_IncludeAmortization_RunsBondIncomePreparationAgainstFreshPreview()
    {
        var inputs = CouponInputs();
        inputs["includeAmortization"] = "true";
        await SeedAsync(BondCouponIncomeCalculation.CalculationKind, new(), inputs, endsOn: JanuaryEnd);
        var harness = new CouponHarness(_root);
        harness.PreviewReturns(hasChanges: false, earned: 0m);
        var fixture = await CreateFixtureAsync(extraCalculations: [harness.Calculation]);

        var result = (await fixture.Runner.RunDueAsync(Due(JanuaryEnd))).Should().ContainSingle().Subject;

        result.State.Should().Be(AutomatedJournalScheduleStateDto.NoDraftRequired);
        harness.IncomePreparation.Verify(service => service.PrepareAsync(
            It.Is<BondIncomePreparationRequestDto>(request => request.ExpectedFingerprint == "income-fingerprint"
                && request.PeriodId == January && request.Request.TaxLotRecordId == CouponLot
                && request.Request.LedgerBookId == Book && request.Request.ThroughDate == JanuaryEnd
                && request.Request.AccountId == CouponAccount),
            Tenant, Company, $"recurring-journal:{ScheduleId}", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        // No coupon movement remained, so nothing was prepared or projected for the coupon leg.
        harness.Coupons.Verify(service => service.PrepareAsync(It.IsAny<AccountCashForecastScopeDto>(),
            It.IsAny<BondCouponRecognitionRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Never);
        harness.Spine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task BondCouponIncome_WithoutGovernedServices_BlocksTheOccurrence()
    {
        await SeedAsync(BondCouponIncomeCalculation.CalculationKind, new(), CouponInputs(), endsOn: JanuaryEnd);
        var fixture = await CreateFixtureAsync(extraCalculations: [new BondCouponIncomeCalculation()]);

        var result = (await fixture.Runner.RunDueAsync(Due(JanuaryEnd))).Should().ContainSingle().Subject;

        result.State.Should().Be(AutomatedJournalScheduleStateDto.Blocked);
        result.Blockers.Should().ContainSingle().Which.Should().Contain("recurring coupon income is blocked");
        fixture.Runner.MissingCalculationInputs(new JournalTemplateCalculation(BondCouponIncomeCalculation.CalculationKind, null))
            .Should().BeEquivalentTo("accountId", "taxLotRecordId");
    }

    private static Dictionary<string, string> CouponInputs() => new()
    {
        ["accountId"] = CouponAccount.ToString("D"),
        ["taxLotRecordId"] = CouponLot.ToString("D")
    };

    private sealed class CouponHarness
    {
        private readonly HashSet<string> _posted = new(StringComparer.Ordinal);

        public CouponHarness(string root)
        {
            Spine.Setup(spine => spine.ProjectAsync(It.IsAny<ProjectAssetAccountingEventRequestDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AssetAccountingEventSpineAppendResultDto(null!, false));
            Receipts.Setup(store => store.GetCouponRecognitionRunAsync(It.IsAny<AccountCashForecastScopeDto>(), CouponLot,
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((AccountCashForecastScopeDto _, Guid _, string runId, CancellationToken _) =>
                    Task.FromResult(_posted.Contains(runId) ? new BondCouponRecognitionReceiptDto(runId, Guid.NewGuid(), DateTimeOffset.UtcNow, null!) : null));
            Income.Setup(service => service.PreviewAsync(It.IsAny<BondIncomePreviewRequestDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((BondIncomePreviewRequestDto request, CancellationToken _) =>
                    new BondIncomePreviewDto(request.LedgerBookId, request.TaxLotRecordId, "income-fingerprint", null!, null!, null!));
            IncomePreparation.Setup(service => service.PrepareAsync(It.IsAny<BondIncomePreparationRequestDto>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BondIncomePreviewDto(Book, CouponLot, "income-fingerprint", null!, null!, null!));
            Calculation = new BondCouponIncomeCalculation(Coupons.Object, Spine.Object, new BondWorkspaceSnapshotStore(root),
                Receipts.Object, Income.Object, IncomePreparation.Object);
        }

        public Mock<IBondCouponRecognitionService> Coupons { get; } = new(MockBehavior.Strict);
        public Mock<IAssetAccountingEventSpineService> Spine { get; } = new(MockBehavior.Strict);
        public Mock<IBondCouponRecognitionStore> Receipts { get; } = new(MockBehavior.Strict);
        public Mock<IBondIncomeService> Income { get; } = new(MockBehavior.Strict);
        public Mock<IBondIncomePreparationService> IncomePreparation { get; } = new(MockBehavior.Strict);
        public BondCouponIncomeCalculation Calculation { get; }

        public void MarkPosted(string runId) => _posted.Add(runId);

        public void PreviewReturns(bool hasChanges, decimal earned)
        {
            Coupons.Setup(service => service.PreviewAsync(It.IsAny<AccountCashForecastScopeDto>(),
                    It.IsAny<BondCouponRecognitionRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AccountCashForecastScopeDto _, BondCouponRecognitionRequest request, CancellationToken _) =>
                    Preview(request, hasChanges, earned));
            Coupons.Setup(service => service.PrepareAsync(It.IsAny<AccountCashForecastScopeDto>(),
                    It.IsAny<BondCouponRecognitionRequest>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AccountCashForecastScopeDto _, BondCouponRecognitionRequest request, string _, string _,
                    DateTimeOffset _, CancellationToken _) => new BondCouponRecognitionPreparationDto(
                        Preview(request, hasChanges, earned),
                        new ProjectAssetAccountingEventRequestDto(AssetAccountingEventKindDto.Income, null!, null!, null!, null!,
                            earned, "USD", 1, "coupon-prep:" + request.RunId, DateTimeOffset.UnixEpoch)));
        }

        private static BondCouponRecognitionPreviewDto Preview(BondCouponRecognitionRequest request, bool hasChanges, decimal earned)
            => new("fingerprint-" + request.RunId, earned, 0m, earned, hasChanges,
                new BondCouponRecognitionInstructionDto(BondCouponRecognitionCalculator.ModelVersion, null!, request, null!,
                    CouponEvent, null, earned, 0m, 0m, [], [], []));
    }
}
