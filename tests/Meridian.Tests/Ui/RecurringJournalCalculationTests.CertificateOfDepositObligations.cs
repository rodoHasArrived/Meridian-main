using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.Ledger;
using Meridian.Storage.SecurityMaster;
using Meridian.Tests.SecurityMaster;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.Ui;

public sealed partial class RecurringJournalCalculationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CertificateOfDeposit_TwoSchedulesCannotOwnOverlappingLotDays(bool differentCadence)
    {
        await SeedCdScheduleAsync(JanuaryEnd);
        var registry = new FileRecurringJournalStore(Path.Combine(_root, "recurring"));
        await using (var session = await registry.OpenSessionAsync())
            await session.RegisterScheduleAsync(new RecurringJournalSchedule("second-cd-schedule", TemplateId,
                new(Fund, "Fund"), differentCadence ? RecurringJournalCadence.Weekly : RecurringJournalCadence.Monthly,
                differentCadence ? new(2026, 1, 15) : JanuaryEnd, "controller", Registered,
                endsOn: differentCadence ? new(2026, 1, 15) : JanuaryEnd), Scope, [Source()], "controller", 0, Registered);
        var fixture = await CreateCdAccrualFixtureAsync();

        var runs = await fixture.Runner.RunDueAsync(Due(JanuaryEnd));

        runs.Should().HaveCount(2);
        runs.Should().ContainSingle(row => row.State == AutomatedJournalScheduleStateDto.DraftReady);
        runs.Should().ContainSingle(row => row.State == AutomatedJournalScheduleStateDto.Blocked)
            .Which.Blockers.Should().Contain(reason => reason.Contains("economic obligation", StringComparison.OrdinalIgnoreCase));
        var draft = (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        await ReviewAndPostRetainedCalculationAsync(fixture, draft);
        await fixture.Runner.RunDueAsync(Due(JanuaryEnd).AddHours(1));
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle();
        fixture.Posted.Should().ContainSingle();
        await using var restarted = await registry.OpenSessionAsync();
        restarted.Occurrences.Should().ContainSingle(row => row.EconomicObligationKeys != null);
    }

    [Fact]
    public async Task CertificateOfDeposit_NextWindowClaimsOnlyNewDaysAndTemplateChangeCannotRedraft()
    {
        await SeedCdScheduleAsync(FebruaryEnd);
        var fixture = await CreateCdAccrualFixtureAsync();
        await fixture.Runner.RunDueAsync(Due(JanuaryEnd));
        await ReviewAndPostRetainedCalculationAsync(fixture, (await fixture.Drafts.ListAsync(Fund, Book)).Single());

        await fixture.Runner.RunDueAsync(Due(FebruaryEnd));

        var drafts = await fixture.Drafts.ListAsync(Fund, Book);
        drafts.Should().HaveCount(2);
        var february = drafts.Single(draft => draft.AccountingDate == FebruaryEnd);
        february.TotalDebits.Should().Be(28m);
        await ReviewAndPostRetainedCalculationAsync(fixture, february);
        var registry = new FileRecurringJournalStore(Path.Combine(_root, "recurring"));
        await using (var session = await registry.OpenSessionAsync())
        {
            var january = session.Occurrences.Single(row => row.EffectiveDate == JanuaryEnd).EconomicObligationKeys!;
            var next = session.Occurrences.Single(row => row.EffectiveDate == FebruaryEnd).EconomicObligationKeys!;
            january.Should().HaveCount(31);
            next.Should().HaveCount(28);
            january.Intersect(next, StringComparer.Ordinal).Should().BeEmpty("prior coupon history supports rounding but is not owned again");
            await session.RegisterTemplateAsync(new JournalTemplate(TemplateId, "Reviewed template revision", "Same economic obligation", [],
                calculation: new JournalTemplateCalculation(FixedIncomeAccrualCalculation.CalculationKind, CdInputs())),
                "controller", 1, Due(FebruaryEnd).AddMinutes(1));
        }
        await fixture.Runner.RunDueAsync(Due(FebruaryEnd).AddHours(1));
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().HaveCount(2);
        fixture.Posted.Should().HaveCount(2);
    }

    [Fact]
    public async Task CertificateOfDeposit_IndependentEconomicClaimCannotBeReplacedByARehashedDraft()
    {
        await SeedCdScheduleAsync(JanuaryEnd);
        var fixture = await CreateCdAccrualFixtureAsync();
        await fixture.Runner.RunDueAsync(Due(JanuaryEnd));
        var original = (await fixture.Drafts.ListAsync(Fund, Book)).Single();
        var evidence = RecurringJournalEvidenceGuard.Deserialize(original.RecurringJournalEvidenceJson!)!;
        evidence.EconomicObligationDigest.Should().NotBeNull();
        var withoutClaim = RecurringJournalEvidenceGuard.Serialize(evidence with { EconomicObligationDigest = null });
        RecurringJournalEvidenceGuard.Validate(original with
        {
            RecurringJournalEvidenceJson = withoutClaim,
            RecurringJournalEvidenceDigest = Sha256Digest.ComputeUtf8(withoutClaim)
        }).Should().Contain("economic obligation digest");
        var forged = RecurringJournalEvidenceGuard.Serialize(evidence with { EconomicObligationDigest = new string('d', 64) });
        await fixture.Drafts.SaveAsync(original with
        {
            RecurringJournalEvidenceJson = forged,
            RecurringJournalEvidenceDigest = Sha256Digest.ComputeUtf8(forged)
        });
        var submit = () => fixture.Workbench.SubmitApprovalAsync(new(original.JournalEntryId, Fund, "controller", original.Version,
            LedgerBookId: Book, TenantId: Tenant, CompanyId: Company));

        await submit.Should().ThrowAsync<InvalidOperationException>().WithMessage("*economic obligations differ*");

        fixture.Posted.Should().BeEmpty();
        await fixture.Drafts.SaveAsync(original);
        await ReviewAndPostRetainedCalculationAsync(fixture, original);
        fixture.Posted.Should().ContainSingle();
    }

    [Fact]
    public async Task CertificateOfDeposit_RestoringAClaimBeforeItsEconomicPinCannotReplayOrApproveTheSavedDraft()
    {
        await SeedCdScheduleAsync(JanuaryEnd);
        var registry = new FileRecurringJournalStore(Path.Combine(_root, "recurring"));
        await using (var session = await registry.OpenSessionAsync())
            await session.ClaimAsync(ScheduleId, JanuaryEnd, 1, 1, Due(JanuaryEnd));
        var statePath = Path.Combine(_root, "recurring", "recurring-journals.json");
        var beforePin = await File.ReadAllBytesAsync(statePath);
        var fixture = await CreateCdAccrualFixtureAsync();
        await fixture.Runner.RunDueAsync(Due(JanuaryEnd));
        var original = (await fixture.Drafts.ListAsync(Fund, Book)).Single();
        await File.WriteAllBytesAsync(statePath, beforePin);
        var unavailable = new Mock<ISecurityMasterCashFlowService>(MockBehavior.Strict);
        var restarted = await CreateFixtureAsync(cashFlows: unavailable.Object);

        var retried = (await restarted.Runner.RunDueAsync(Due(JanuaryEnd).AddHours(1))).Single();

        retried.State.Should().Be(AutomatedJournalScheduleStateDto.Blocked);
        retried.Blockers.Should().Contain(reason => reason.Contains("economic obligations differ", StringComparison.Ordinal));
        unavailable.VerifyNoOtherCalls();
        var submit = () => restarted.Workbench.SubmitApprovalAsync(new(original.JournalEntryId, Fund, "controller", original.Version,
            LedgerBookId: Book, TenantId: Tenant, CompanyId: Company));
        await submit.Should().ThrowAsync<InvalidOperationException>().WithMessage("*economic obligations differ*");
        restarted.Posted.Should().BeEmpty();
    }

    private static Dictionary<string, string> CdInputs() => new()
    { ["securityId"] = SecurityId.ToString("D"), ["ticker"] = Ticker, ["bookPositionId"] = Position };

    private Task SeedCdScheduleAsync(DateOnly endsOn)
        => SeedAsync(FixedIncomeAccrualCalculation.CalculationKind, new(), CdInputs(), endsOn: endsOn);

    private Task<Fixture> CreateCdAccrualFixtureAsync()
    {
        var security = SecurityRecurringAccrualPolicyTests.Security() with
        { Identifiers = [new(SecurityIdentifierKind.Ticker, Ticker, true, Registered)] };
        var query = new Mock<ISecurityMasterQueryService>(MockBehavior.Strict);
        query.Setup(value => value.GetByIdAsync(SecurityId, It.IsAny<CancellationToken>())).ReturnsAsync(security);
        query.Setup(value => value.GetByIdAsOfAsync(SecurityId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(security);
        var source = new Mock<ISecurityMasterCashFlowStore>(MockBehavior.Strict);
        source.Setup(value => value.GetSourceAsync(SecurityId, It.IsAny<CancellationToken>())).ReturnsAsync(
            new SecurityCashFlowSourceDto(SecurityId, StructuredCashFlowSourceKind.CalculatedBullet,
                DateTimeOffset.UtcNow, false, null, null));
        var flows = new SecurityMasterCashFlowService(source.Object, [], query.Object,
            NullLogger<SecurityMasterCashFlowService>.Instance,
            receiptStore: new FileSecurityCashFlowReceiptStore(Path.Combine(_root, "cd-receipts")));
        var lot = new FaceValueLot("cd-par-lot", SecurityId, new(2025, 12, 31), 10_000m, 100m);
        var holdings = Enumerable.Range(0, 60).Select(day => new FaceValueAccrualHolding(
            new DateOnly(2025, 12, 31).AddDays(day), lot, 1m, 1m,
            AmortizationMethod: BondAmortizationMethod.NoAmortization)).ToArray();
        return CreateFixtureAsync(cashFlows: flows, securities: query.Object, accrualHoldings: holdings);
    }
}
