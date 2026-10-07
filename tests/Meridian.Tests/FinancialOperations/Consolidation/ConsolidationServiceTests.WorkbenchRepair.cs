using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Consolidation;

namespace Meridian.Tests.FinancialOperations.Consolidation;

public sealed partial class ConsolidationServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConsolidationWorkbench_RerunRepairsManuallyEditedLinesAndRequiresFreshApproval(bool balancedEdit)
    {
        var fixture = await CreateWorkbenchFixture();
        var initial = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var original = (await fixture.Drafts.GetAsync(Fixture.Profile, Assert.Single(initial.Drafts).JournalEntryId))!;
        var edited = await fixture.Workbench.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(original with
        {
            Lines = original.Lines.Select((line, index) => balancedEdit || index == 0
                ? line with { Amount = line.Amount + 5m } : line).ToArray(),
            Memo = "Operator investigation notes",
            EvidenceLinks = original.EvidenceLinks.Append("review:operator-investigation").ToArray()
        }, "maker", CorrelationId: "manual-edit", LedgerBookId: original.LedgerBookId));
        Assert.Equal(balancedEdit ? ManualJournalEntryStatusDto.Draft : ManualJournalEntryStatusDto.NeedsFix, edited.Status);
        Assert.Equal(original.ConsolidationEvidenceJson, edited.ConsolidationEvidenceJson);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Workbench.SubmitApprovalAsync(
            new SubmitManualJournalEntryApprovalRequest(edited.JournalEntryId, edited.FundProfileId, "maker",
                edited.Version, LedgerBookId: edited.LedgerBookId)));
        Assert.True(Assert.Single((await fixture.Bridge.GetAsync(fixture.Request, null, null)).Drafts).RequiresRenewedReview);

        var refreshed = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var summary = Assert.Single(refreshed.Drafts);
        var repaired = (await fixture.Drafts.GetAsync(Fixture.Profile, summary.JournalEntryId))!;
        Assert.Equal(original.JournalEntryId, repaired.JournalEntryId);
        Assert.Equal(edited.Version + 1, repaired.Version);
        Assert.Equal(ManualJournalEntryStatusDto.Draft, repaired.Status);
        Assert.False(summary.RequiresRenewedReview);
        Assert.Equal(ConsolidationService.Hash(original.Lines), ConsolidationService.Hash(repaired.Lines));
        Assert.Equal(original.ConsolidationEvidenceJson, repaired.ConsolidationEvidenceJson);
        Assert.Equal(original.ConsolidationEvidenceDigest, repaired.ConsolidationEvidenceDigest);
        Assert.Equal(edited.Memo, repaired.Memo);
        Assert.Contains("review:operator-investigation", repaired.EvidenceLinks);
        Assert.Null(repaired.ApprovalId);
        Assert.Null(repaired.SubmittedAtUtc);
        Assert.Null(repaired.ApprovedAtUtc);
        Assert.Empty(fixture.Engine.Records[Fixture.OverlayBookId]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Workbench.ApplyLifecycleActionAsync(
            WorkbenchAction(repaired, JournalEntryLifecycleActionDto.Post)));

        await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var unchanged = (await fixture.Drafts.GetAsync(Fixture.Profile, repaired.JournalEntryId))!;
        Assert.Equal(repaired.Version, unchanged.Version);
        Assert.Equal(repaired.UpdatedAtUtc, unchanged.UpdatedAtUtc);
        await ApproveAndPost(fixture.Workbench, unchanged);
        Assert.Single(fixture.Engine.Records[Fixture.OverlayBookId]);
        Assert.Equal(20m, Assert.Single((await fixture.Bridge.GetAsync(fixture.Request, null, null)).Balances,
            row => row.AccountPath == ConsolidationService.ReceivableAccount).ConsolidatedBalance);
    }

    [Fact]
    public async Task ConsolidationWorkbench_RepairRestoresHeaderOnlyDimensionEditsWithoutRepeatedResaves()
    {
        var fixture = await CreateWorkbenchFixture();
        var initial = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var original = (await fixture.Drafts.GetAsync(Fixture.Profile, Assert.Single(initial.Drafts).JournalEntryId))!;
        var edited = await fixture.Workbench.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(original with
        {
            Dimensions = original.Dimensions! with { SleeveId = "operator-edited-sleeve" }
        }, "maker", LedgerBookId: original.LedgerBookId));
        Assert.Equal(ConsolidationService.Hash(original.Lines), ConsolidationService.Hash(edited.Lines));
        Assert.NotEqual(ConsolidationService.Hash(original.Dimensions), ConsolidationService.Hash(edited.Dimensions));

        await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var repaired = (await fixture.Drafts.GetAsync(Fixture.Profile, original.JournalEntryId))!;
        Assert.Equal(edited.Version + 1, repaired.Version);
        Assert.Equal(ConsolidationService.Hash(original.Dimensions), ConsolidationService.Hash(repaired.Dimensions));
        await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var unchanged = (await fixture.Drafts.GetAsync(Fixture.Profile, original.JournalEntryId))!;
        Assert.Equal(repaired.Version, unchanged.Version);
        Assert.Equal(repaired.UpdatedAtUtc, unchanged.UpdatedAtUtc);
        await ApproveAndPost(fixture.Workbench, unchanged);
        Assert.Single(fixture.Engine.Records[Fixture.OverlayBookId]);
    }

    [Fact]
    public async Task ConsolidationWorkbench_RepairRetainsPriorRejectedReviewHistory()
    {
        var fixture = await CreateWorkbenchFixture();
        var initial = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var original = (await fixture.Drafts.GetAsync(Fixture.Profile, Assert.Single(initial.Drafts).JournalEntryId))!;
        var submitted = await fixture.Workbench.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            original.JournalEntryId, original.FundProfileId, "maker", original.Version, LedgerBookId: original.LedgerBookId));
        var rejected = (await fixture.Workbench.ApplyLifecycleActionAsync(
            WorkbenchAction(submitted, JournalEntryLifecycleActionDto.Reject))).JournalEntry;
        var edited = await fixture.Workbench.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(rejected with
        {
            Lines = rejected.Lines.Select(line => line with { Amount = line.Amount + 5m }).ToArray()
        }, "maker", LedgerBookId: rejected.LedgerBookId));

        await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var repaired = (await fixture.Drafts.GetAsync(Fixture.Profile, original.JournalEntryId))!;
        Assert.Equal(edited.Version + 1, repaired.Version);
        Assert.Equal(ConsolidationService.Hash(rejected.LifecycleTransitions), ConsolidationService.Hash(repaired.LifecycleTransitions));
        Assert.Null(repaired.ApprovalId);
        Assert.Null(repaired.SubmittedAtUtc);
        Assert.Null(repaired.ApprovedAtUtc);
        var resubmitted = await fixture.Workbench.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            repaired.JournalEntryId, repaired.FundProfileId, "maker", repaired.Version, LedgerBookId: repaired.LedgerBookId));
        Assert.Equal(ManualJournalEntryStatusDto.Submitted, resubmitted.Status);
        Assert.Equal(2, resubmitted.LifecycleTransitions.Count(transition => transition.Action == JournalEntryLifecycleActionDto.Submit));
        Assert.Contains(resubmitted.LifecycleTransitions, transition => transition.Action == JournalEntryLifecycleActionDto.Reject);
        Assert.Empty(fixture.Engine.Records[Fixture.OverlayBookId]);
    }

    [Theory]
    [InlineData(ManualJournalEntryStatusDto.Submitted)]
    [InlineData(ManualJournalEntryStatusDto.Approved)]
    [InlineData(ManualJournalEntryStatusDto.Posted)]
    [InlineData(ManualJournalEntryStatusDto.Reversed)]
    [InlineData(ManualJournalEntryStatusDto.Rebooked)]
    [InlineData(ManualJournalEntryStatusDto.CloseLocked)]
    public async Task ConsolidationWorkbench_RerunNeverRepairsProtectedLifecycleRecords(ManualJournalEntryStatusDto status)
    {
        var fixture = await CreateWorkbenchFixture();
        var initial = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var original = (await fixture.Drafts.GetAsync(Fixture.Profile, Assert.Single(initial.Drafts).JournalEntryId))!;
        // A retained inconsistency in a noneditable state requires governed investigation,
        // never automatic replacement of reviewed or historical accounting records.
        var retained = original with
        {
            Status = status,
            Version = original.Version + 1,
            Lines = original.Lines.Select(line => line with { Amount = line.Amount + 5m }).ToArray(),
            ApprovalId = "retained-review"
        };
        await fixture.Drafts.SaveAsync(retained);

        await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);

        var after = (await fixture.Drafts.GetAsync(Fixture.Profile, original.JournalEntryId))!;
        Assert.Equal(ConsolidationService.Hash(retained), ConsolidationService.Hash(after));
        Assert.Single(await fixture.Drafts.ListAsync(Fixture.Profile));
        Assert.Empty(fixture.Engine.Records[Fixture.OverlayBookId]);
    }
}
