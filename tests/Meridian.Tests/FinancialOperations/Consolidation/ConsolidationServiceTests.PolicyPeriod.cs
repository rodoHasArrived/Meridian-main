using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Consolidation;
using Meridian.Storage.Ledger;
using Moq;

namespace Meridian.Tests.FinancialOperations.Consolidation;

public sealed partial class ConsolidationServiceTests
{
    [Fact]
    public async Task CalculateAndDraft_AnotherPolicyVersionCannotReplaceTheBookBinding()
    {
        var fixture = new Fixture();
        var before = await fixture.Calculate();
        var originalDraft = await fixture.Draft(before);
        // The replacement version ranks first for an unpinned query and does not support
        // consolidation. Existing books still explicitly bind the supported w10-v1 version.
        await fixture.Policies.CreatePolicyAsync(new CreateAccountingPolicyRequest(AccountingBasisKindDto.Primary,
            "consolidation-v1", "w10-v2", "Another policy version", Fixture.Date.AddMonths(-1), IsDefault: true));

        var current = await fixture.Calculate();
        var draft = await fixture.Draft(current);

        Assert.Equal("w10-v1", current.Policy.Version);
        Assert.Equal(before.Evidence.SourceFingerprint, current.Evidence.SourceFingerprint);
        Assert.Equal(originalDraft.JournalEntryId, draft.JournalEntryId);
        await fixture.Service.ValidateCurrentAsync(draft);
        await fixture.Service.ValidateEvidenceCurrentAsync(current.Evidence);
    }

    [Fact]
    public async Task ConsolidationWorkbench_OverlappingPeriodsKeepPendingDraftsSeparateAndPostedTotalsCumulative()
    {
        var fixture = await CreateWorkbenchFixture();
        var otherPeriodId = Guid.NewGuid();
        fixture.Engine.Store.Setup(store => store.GetPeriodAsync(otherPeriodId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LedgerAccountingPeriod(otherPeriodId, Fixture.OverlayBookId, 2026, 7,
                "Overlapping adjustment period", new DateOnly(2026, 6, 1), Fixture.Date, "Open", Fixture.Time, null, 0));
        var otherRequest = fixture.Request with { PeriodId = otherPeriodId };

        var firstView = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var firstDraft = Assert.Single(firstView.Drafts);
        var secondView = await fixture.Bridge.CreateDraftAsync(otherRequest, "maker", null, null);
        var secondDraft = Assert.Single(secondView.Drafts);
        Assert.NotEqual(firstDraft.JournalEntryId, secondDraft.JournalEntryId);
        Assert.False(firstDraft.RequiresRenewedReview);
        Assert.False(secondDraft.RequiresRenewedReview);
        Assert.Equal(firstDraft.JournalEntryId,
            Assert.Single((await fixture.Bridge.GetAsync(fixture.Request, null, null)).Drafts).JournalEntryId);
        Assert.Equal(secondDraft.JournalEntryId,
            Assert.Single((await fixture.Bridge.GetAsync(otherRequest, null, null)).Drafts).JournalEntryId);

        var toPost = (await fixture.Drafts.GetAsync(Fixture.Profile, firstDraft.JournalEntryId))!;
        await ApproveAndPost(fixture.Workbench, toPost);
        var afterPosting = await fixture.Bridge.GetAsync(otherRequest, null, null);

        Assert.Equal("Posted", Assert.Single(afterPosting.Drafts, draft =>
            draft.JournalEntryId == firstDraft.JournalEntryId).Status);
        Assert.True(Assert.Single(afterPosting.Drafts, draft =>
            draft.JournalEntryId == secondDraft.JournalEntryId).RequiresRenewedReview);
        var receivable = Assert.Single(afterPosting.Balances, balance => balance.AccountPath == ConsolidationService.ReceivableAccount);
        Assert.Equal(-80m, receivable.PostedEliminations);
        Assert.Equal(0m, receivable.ProposedEliminations);
        Assert.Equal(20m, receivable.ConsolidatedBalance);
    }
}
