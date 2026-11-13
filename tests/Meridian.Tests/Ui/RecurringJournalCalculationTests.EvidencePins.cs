using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed partial class RecurringJournalCalculationTests
{
    [Fact]
    public async Task CalculatedReceipt_SubstitutionAndRecomputedDraftDigestCannotReplaceTheIndependentClaimPin()
    {
        await SeedAsync(FixedIncomeAccrualCalculation.CalculationKind, new(),
            new() { ["securityId"] = SecurityId.ToString("D"), ["ticker"] = Ticker, ["bookPositionId"] = Position }, endsOn: JanuaryEnd);
        var fixture = await CreateFixtureAsync(cashFlows: FactoredCouponCashFlows(), assetClass: "StructuredCredit",
            accrualHoldings: FactoredHoldings());
        await fixture.Runner.RunDueAsync(Due(JanuaryEnd));
        var original = (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        var evidence = RecurringJournalEvidenceGuard.Deserialize(original.RecurringJournalEvidenceJson!)!;
        var retained = evidence.SourceEvidence.Single(reference => reference.Kind == "cash-flow-calculation-receipt");
        var replacement = retained with
        {
            ContentHash = new string('d', 64),
            EvidenceId = "cash-flow-receipt:" + new string('d', 64),
            Uri = "evidence://security-master/cash-flow-receipts/" + new string('d', 64)
        };
        var changedEvidence = evidence with { SourceEvidence = evidence.SourceEvidence.Select(reference => reference == retained ? replacement : reference).ToArray() };
        var changedJson = RecurringJournalEvidenceGuard.Serialize(changedEvidence);
        var tampered = original with
        {
            RecurringJournalEvidenceJson = changedJson,
            RecurringJournalEvidenceDigest = Sha256Digest.ComputeUtf8(changedJson),
            EvidenceLinks = original.EvidenceLinks.Select(link => link == retained.Uri ? replacement.Uri : link).ToArray()
        };
        RecurringJournalEvidenceGuard.Validate(tampered).Should().BeNull("the forged draft is internally self-consistent but has no authority to replace the claim");
        await fixture.Drafts.SaveAsync(tampered);
        var submit = () => fixture.Workbench.SubmitApprovalAsync(new(original.JournalEntryId, Fund, "fund-accountant", original.Version,
            LedgerBookId: Book, TenantId: Tenant, CompanyId: Company));
        await submit.Should().ThrowAsync<InvalidOperationException>().WithMessage("*provenance differs*");
        fixture.Posted.Should().BeEmpty();
        await using (var session = await new FileRecurringJournalStore(Path.Combine(_root, "recurring")).OpenSessionAsync())
            session.Occurrences.Single().DraftEvidenceDigest.Should().Be(original.RecurringJournalEvidenceDigest);
        await fixture.Drafts.SaveAsync(original);
        await ReviewAndPostRetainedCalculationAsync(fixture, original);
        fixture.Posted.Should().ContainSingle();
    }

    [Fact]
    public async Task CalculatedReceipt_PreservesDuplicateOriginalScheduleReferencesInTheirExactOrder()
    {
        var first = Source();
        var second = first with { EvidenceId = "second-source", Uri = "evidence://second-source" };
        await SeedAsync(FixedIncomeAccrualCalculation.CalculationKind, new(),
            new() { ["securityId"] = SecurityId.ToString("D"), ["ticker"] = Ticker, ["bookPositionId"] = Position },
            endsOn: JanuaryEnd, sourceEvidence: [first, second, first]);
        var fixture = await CreateFixtureAsync(cashFlows: FactoredCouponCashFlows(), assetClass: "StructuredCredit",
            accrualHoldings: FactoredHoldings());
        await fixture.Runner.RunDueAsync(Due(JanuaryEnd));
        var draft = (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        var evidence = RecurringJournalEvidenceGuard.Deserialize(draft.RecurringJournalEvidenceJson!)!;
        evidence.SourceEvidence.Take(3).Should().Equal(first, second, first);
        evidence.SourceEvidence.Skip(3).Should().ContainSingle(reference => reference.Kind == "cash-flow-calculation-receipt");
        await ReviewAndPostRetainedCalculationAsync(fixture, draft);
        fixture.Posted.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CalculatedReceipt_CrashGapCannotLatePinOrRecalculateWithoutTheOriginalAuthority(bool pinCommitted)
    {
        await SeedAsync(FixedIncomeAccrualCalculation.CalculationKind, new(),
            new() { ["securityId"] = SecurityId.ToString("D"), ["ticker"] = Ticker, ["bookPositionId"] = Position }, endsOn: JanuaryEnd);
        var registryDirectory = Path.Combine(_root, "recurring");
        var registry = new FileRecurringJournalStore(registryDirectory);
        await using (var session = await registry.OpenSessionAsync())
            await session.ClaimAsync(ScheduleId, JanuaryEnd, session.GetCurrentSchedule(ScheduleId).Version,
                session.GetCurrentTemplate(TemplateId).Version, Due(JanuaryEnd));
        var statePath = Path.Combine(registryDirectory, "recurring-journals.json");
        var claimedBeforeIntake = await File.ReadAllBytesAsync(statePath);
        var flows = FactoredCouponCashFlows();
        var fixture = await CreateFixtureAsync(cashFlows: flows, assetClass: "StructuredCredit", accrualHoldings: FactoredHoldings());
        await fixture.Runner.RunDueAsync(Due(JanuaryEnd));
        var original = (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        var calculationsBeforeRetry = Moq.Mock.Get(flows).Invocations.Count;

        // Restore the genuine earlier committed claim, retaining the independently saved draft.
        // This represents interruption after intake and before the separate pin/outcome commits.
        await File.WriteAllBytesAsync(statePath, claimedBeforeIntake);
        if (pinCommitted)
        {
            await using (var session = await registry.OpenSessionAsync())
                await session.PinDraftEvidenceAsync(session.Occurrences.Single().OccurrenceKey, original.RecurringJournalEvidenceDigest!);
            File.Delete(Path.Combine(_root, "drafts.json"));
            (await fixture.Drafts.GetAsync(Fund, original.JournalEntryId, tenantId: Tenant, companyId: Company)).Should().BeNull();
        }
        var retried = (await fixture.Runner.RunDueAsync(Due(JanuaryEnd).AddHours(1))).Should().ContainSingle().Subject;
        retried.State.Should().Be(AutomatedJournalScheduleStateDto.Blocked);
        retried.Blockers.Should().Contain(reason => reason.Contains(pinCommitted ? "retained occurrence draft is missing" : "independent claim pin", StringComparison.Ordinal));
        Moq.Mock.Get(flows).Invocations.Count.Should().Be(calculationsBeforeRetry, "recovery cannot select or recalculate different source evidence");
        fixture.Posted.Should().BeEmpty();
        await using (var session = await registry.OpenSessionAsync())
            session.Occurrences.Single().DraftEvidenceDigest.Should().Be(pinCommitted ? original.RecurringJournalEvidenceDigest : null);
        if (!pinCommitted)
        {
            var unchanged = (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
            unchanged.RecurringJournalEvidenceJson.Should().Be(original.RecurringJournalEvidenceJson);
            unchanged.RecurringJournalEvidenceDigest.Should().Be(original.RecurringJournalEvidenceDigest);
            var submit = () => fixture.Workbench.SubmitApprovalAsync(new(original.JournalEntryId, Fund, "controller", original.Version,
                LedgerBookId: Book, TenantId: Tenant, CompanyId: Company));
            await submit.Should().ThrowAsync<InvalidOperationException>().WithMessage("*provenance differs*");
            fixture.Posted.Should().BeEmpty();
        }
    }

    private static async Task ReviewAndPostRetainedCalculationAsync(Fixture fixture, ManualJournalEntryDraftDto draft)
    {
        var submitted = (await fixture.Workbench.ApplyLifecycleActionAsync(ReviewAction(draft, JournalEntryLifecycleActionDto.Submit))).JournalEntry;
        var approved = (await fixture.Workbench.ApplyLifecycleActionAsync(ReviewAction(submitted, JournalEntryLifecycleActionDto.Approve))).JournalEntry;
        var posted = (await fixture.Workbench.ApplyLifecycleActionAsync(ReviewAction(approved, JournalEntryLifecycleActionDto.Post))).JournalEntry;
        posted.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
        posted.RecurringJournalEvidenceJson.Should().Be(draft.RecurringJournalEvidenceJson);
        posted.RecurringJournalEvidenceDigest.Should().Be(draft.RecurringJournalEvidenceDigest);
    }

    private static JournalEntryLifecycleActionRequestDto ReviewAction(ManualJournalEntryDraftDto draft, JournalEntryLifecycleActionDto action)
        => new(draft.JournalEntryId, Fund, action, action == JournalEntryLifecycleActionDto.Submit ? "controller" : "independent-controller",
            draft.Version, Notes: "Reviewed retained source definitions and exact calculation receipt.",
            EvidenceLinks: [Evidence(action == JournalEntryLifecycleActionDto.Post ? "posting" : "approval", draft.JournalEntryId)],
            LedgerBookId: Book, TenantId: Tenant, CompanyId: Company);
}
