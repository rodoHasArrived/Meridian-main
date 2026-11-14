using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Consolidation;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.FinancialOperations.Consolidation;

/// <summary>
/// Group close under a governed rule where a source journal or an account mapping changes after
/// the controller approved the elimination but before it is posted. Guards against a reviewed
/// elimination being committed against a population or mapping revision nobody reviewed.
/// </summary>
public sealed partial class ConsolidationServiceTests
{
    [Theory]
    [InlineData("w10-v2", "source-journal")]
    [InlineData("w10-v2", "source-mapping")]
    [InlineData("w10-v2", "output-mapping")]
    [InlineData("w10-v4", "source-journal")]
    [InlineData("w10-v4", "output-mapping")]
    public async Task GovernedWorkbench_ChangeBetweenApprovalAndPost_RefusesPostingAndRequiresFreshReview(string version, string change)
    {
        var fixture = await GovernedWorkbench(version);
        var view = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var draft = (await fixture.Drafts.GetAsync(Fixture.Profile, Assert.Single(view.Drafts).JournalEntryId))!;
        await AssertSubmitValidation(fixture.Workbench, draft);
        var submitted = await fixture.Workbench.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            draft.JournalEntryId, draft.FundProfileId, "maker", draft.Version, LedgerBookId: draft.LedgerBookId));
        var approved = (await fixture.Workbench.ApplyLifecycleActionAsync(
            WorkbenchAction(submitted, JournalEntryLifecycleActionDto.Approve))).JournalEntry;

        switch (change)
        {
            case "source-journal":
                // Amount-neutral: the proposed elimination is numerically unchanged, only its source population moved.
                fixture.Engine.AddSource(Fixture.SecondBookId, Fixture.SecondId, 9,
                    ("Assets:Cash", 1m, null), ("Equity:Opening", -1m, null));
                break;
            case "source-mapping":
                var source = fixture.Bindings.Single(binding => binding.LedgerBookId == Fixture.SecondBookId
                    && binding.Role == IntercompanyAccountRoleDto.Payable);
                fixture.Bindings.Add(source with { RevisionId = Guid.NewGuid(), RevisionNumber = source.RevisionNumber + 1 });
                break;
            case "output-mapping":
                var index = fixture.Bindings.FindIndex(binding => binding.LedgerBookId == Fixture.OverlayBookId
                    && binding.Role == IntercompanyAccountRoleDto.Payable);
                fixture.Bindings[index] = fixture.Bindings[index] with
                { RevisionId = Guid.NewGuid(), RevisionNumber = fixture.Bindings[index].RevisionNumber + 1 };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Workbench.ApplyLifecycleActionAsync(
            WorkbenchAction(approved, JournalEntryLifecycleActionDto.Post)));

        Assert.Empty(fixture.Writes);
        Assert.Empty(fixture.Engine.Records[Fixture.OverlayBookId]);
        var renewed = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var stale = Assert.Single(renewed.Drafts, row => row.JournalEntryId == draft.JournalEntryId);
        Assert.True(stale.RequiresRenewedReview);
        var fresh = Assert.Single(renewed.Drafts, row => row.JournalEntryId != draft.JournalEntryId);
        Assert.False(fresh.RequiresRenewedReview);
        Assert.Equal("Draft", fresh.Status);
        var freshDraft = (await fixture.Drafts.GetAsync(Fixture.Profile, fresh.JournalEntryId))!;
        Assert.Equal(ConsolidationService.Hash(draft.Lines.Select(line => new { line.AccountPath, line.Side, line.Amount, line.EntityId })),
            ConsolidationService.Hash(freshDraft.Lines.Select(line => new { line.AccountPath, line.Side, line.Amount, line.EntityId })));
        await ApproveAndPost(fixture.Workbench, freshDraft);
        var write = Assert.Single(fixture.Writes);
        Assert.Equal(freshDraft.JournalEntryId, write.Entry.JournalEntryId);
        Assert.Equal(renewed.SourceFingerprint,
            ConsolidationService.ReadEvidence(write.Entry.Metadata.Tags![ConsolidationService.EvidenceTag])!.SourceFingerprint);
    }
}
