using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Ledger;
using Xunit;
using DomainLedger = Meridian.Ledger.Ledger;

namespace Meridian.Tests.FinancialOperations.PrivateCapital;

public sealed class DistributionDeclarationProjectionTests
{
    private const string Fund = "fund-a";
    private const string Evidence = "vault://distribution/1";
    private static readonly DateOnly EffectiveDate = new(2026, 4, 30);
    private static readonly DateTimeOffset OccurredAt = new(2026, 4, 30, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("capital-1")]
    [InlineData("capital-2")]
    public void MultiInvestorDeclaration_KeepsEachInvestorVisibleAcrossPosting(string secondCapitalAccount)
    {
        var ledger = new DomainLedger();
        var first = SubmitDistribution("investor-1", "capital-1", 100m);
        var second = SubmitDistribution("investor-2", secondCapitalAccount, 250m);
        ManualJournalEntryDraftDto[] drafts = [RetainDraft(first), RetainDraft(second)];
        var declarationId = first.Draft.Metadata.FundEventId;
        second.Draft.Metadata.FundEventId.Should().Be(declarationId);

        Post(first, ledger);
        var partial = ProjectActivity(PrivateCapitalFundEventLedgerProjector.Project(ledger), drafts);

        partial.FundEventCount.Should().Be(2);
        partial.PostedFundEventCount.Should().Be(1);
        partial.ApprovalQueueCount.Should().Be(1);
        var pending = partial.FundEventRecords.Should().ContainSingle(record => record.InvestorId == "investor-2").Subject;
        pending.IsPosted.Should().BeFalse();
        pending.LedgerImpacts.Should().ContainSingle(impact => impact.JournalEntryId == second.JournalEntryId);
        pending.ReportOutputs.Should().ContainSingle(output => output.FundEventId == pending.FundEventId);

        Post(second, ledger);
        var posted = PrivateCapitalFundEventLedgerProjector.Project(ledger);
        var complete = ProjectActivity(posted, drafts);

        posted.Events.Should().HaveCount(2).And.OnlyContain(item => item.DeclarationFundEventId == declarationId && item.IsPostingReady);
        complete.FundEvents.Should().HaveCount(2).And.OnlyContain(item => item.IsPosted);
        complete.FundEvents.Should().OnlyHaveUniqueItems(item => item.FundEventId);
        complete.FundEvents.Sum(item => item.GrossAmount).Should().Be(350m);
        complete.LedgerImpacts.Should().HaveCount(2);
        var postedSecond = complete.FundEventRecords.Should().ContainSingle(record => record.InvestorId == "investor-2").Subject;
        postedSecond.FundEventId.Should().Be(pending.FundEventId);
        postedSecond.LedgerImpacts.Should().ContainSingle(impact => impact.JournalEntryId == second.JournalEntryId);
        postedSecond.CapitalAccountSubledgerEntries.Should().ContainSingle(item => item.JournalEntryId == second.JournalEntryId);
    }

    private static AutomatedJournalApproval SubmitDistribution(string investorId, string capitalAccountId, decimal amount)
    {
        var waterfall = EuropeanDistributionWaterfall.Distribute(new EuropeanWaterfallInput(1_000m, 0m, amount, 0.20m));
        var draft = FundEconomicsJournalFactory.BuildDistributionWaterfallDraft(
            Fund, investorId, waterfall, EffectiveDate, OccurredAt, "2026-04", "distribution-1",
            [new JournalEvidenceReference("distribution-notice", Evidence, "distribution-notice", "test", OccurredAt, "controller")]);
        return AutomatedJournalApproval.Submit(
            draft with { Metadata = draft.Metadata with { CapitalAccountId = capitalAccountId } },
            "controller", OccurredAt, "submit", [Evidence]);
    }

    private static ManualJournalEntryDraftDto RetainDraft(AutomatedJournalApproval approval)
    {
        var draft = approval.Draft;
        return new ManualJournalEntryDraftDto(
            approval.JournalEntryId, ManualJournalEntryStatusDto.Submitted, Fund, null, AccountingBasisKindDto.Primary,
            EffectiveDate, "2026-04", null, null, "USD", draft.Description, "controller", OccurredAt, OccurredAt, 1,
            [], [Evidence], [], TotalDebits: draft.TotalDebits, TotalCredits: draft.TotalCredits,
            EntryType: ManualJournalEntryTypeDto.Distribution, TreasuryContext: new TreasuryLedgerContextDto(
                draft.Metadata.EffectiveDate, draft.Metadata.IdempotencyKey, draft.Metadata.FundEventId, draft.Metadata.FundEventType,
                draft.Metadata.CapitalAccountId, draft.Metadata.InvestorId));
    }

    private static void Post(AutomatedJournalApproval approval, DomainLedger ledger) =>
        approval.Approve("cfo", OccurredAt, "approve", [Evidence]).PostTo(ledger, "cfo", OccurredAt, "post", [Evidence]);

    private static PrivateCapitalActivityProjectionDto ProjectActivity(
        PrivateCapitalFundEventLedgerProjection projection,
        IReadOnlyList<ManualJournalEntryDraftDto> drafts) =>
        PrivateCapitalActivityProjectionBuilder.Build(new PrivateCapitalActivityProjectionInput(
            Fund, null, drafts, [], [], new PostedPrivateCapitalActivityProjection(projection, new Dictionary<Guid, string>())));
}
