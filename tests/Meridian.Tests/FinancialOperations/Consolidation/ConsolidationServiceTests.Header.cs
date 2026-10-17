using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Consolidation;

namespace Meridian.Tests.FinancialOperations.Consolidation;

public sealed partial class ConsolidationServiceTests
{
    public static IEnumerable<object[]> HeaderEdits()
    {
        foreach (var edit in new[]
        {
            "entity", "root", "dimension-entity", "dimension-fund", "dimension-sleeve", "dimension-organization",
            "dimension-book", "dimension-position", "dimension-external", "entry-type", "treasury-date",
            "treasury-event", "treasury-investor", "treasury-payment", "treasury-settlement", "treasury-batch"
        })
            yield return [edit];
    }

    [Theory]
    [MemberData(nameof(HeaderEdits))]
    public async Task ValidateCurrent_RejectsUnreviewedHeaderProvenanceWithUnchangedSourceLines(string edit)
    {
        var fixture = new Fixture();
        var calculation = await fixture.Calculate();
        var original = await fixture.Draft(calculation);
        var changed = EditHeader(original, edit);

        Assert.True(ConsolidationService.HasCanonicalHeader(original, calculation.Evidence));
        Assert.Equal(ConsolidationService.Hash(original.Lines), ConsolidationService.Hash(changed.Lines));
        Assert.Equal(original.ConsolidationEvidenceJson, changed.ConsolidationEvidenceJson);
        Assert.False(ConsolidationService.HasCanonicalHeader(changed, calculation.Evidence));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ValidateCurrentAsync(changed));
        Assert.Contains("renewed review", exception.Message);
    }

    [Theory]
    [InlineData("entity")]
    [InlineData("root")]
    [InlineData("dimension-sleeve")]
    [InlineData("entry-type")]
    [InlineData("treasury-payment")]
    public async Task ConsolidationWorkbench_HeaderEditRequiresRepairAndFreshReviewBeforePosting(string edit)
    {
        var fixture = await CreateWorkbenchFixture();
        var view = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var original = (await fixture.Drafts.GetAsync(Fixture.Profile, Assert.Single(view.Drafts).JournalEntryId))!;
        var edited = await fixture.Workbench.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(
            EditHeader(original, edit) with
            {
                Memo = "Operator description and investigation notes",
                EvidenceLinks = original.EvidenceLinks.Append("review:header-investigation").ToArray()
            }, "maker", LedgerBookId: original.LedgerBookId));
        Assert.Equal(ConsolidationService.Hash(original.Lines), ConsolidationService.Hash(edited.Lines));
        Assert.Equal(original.ConsolidationEvidenceJson, edited.ConsolidationEvidenceJson);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Workbench.SubmitApprovalAsync(
            new SubmitManualJournalEntryApprovalRequest(edited.JournalEntryId, edited.FundProfileId, "maker",
                edited.Version, LedgerBookId: edited.LedgerBookId)));
        Assert.True(Assert.Single((await fixture.Bridge.GetAsync(fixture.Request, null, null)).Drafts).RequiresRenewedReview);

        var refreshed = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var summary = Assert.Single(refreshed.Drafts);
        var repaired = (await fixture.Drafts.GetAsync(Fixture.Profile, summary.JournalEntryId))!;
        Assert.False(summary.RequiresRenewedReview);
        Assert.Equal(original.JournalEntryId, repaired.JournalEntryId);
        Assert.Equal(edited.Version + 1, repaired.Version);
        Assert.Null(repaired.EntityId);
        Assert.Equal(Fixture.RootId.ToString("D"), repaired.FundNodeId);
        Assert.Equal(ConsolidationService.Hash(original.Dimensions), ConsolidationService.Hash(repaired.Dimensions));
        Assert.Equal(original.EntryType, repaired.EntryType);
        Assert.Equal(original.TreasuryContext, repaired.TreasuryContext);
        Assert.Equal(edited.Memo, repaired.Memo);
        Assert.Contains("review:header-investigation", repaired.EvidenceLinks);
        Assert.Null(repaired.ApprovalId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Workbench.ApplyLifecycleActionAsync(
            WorkbenchAction(repaired, JournalEntryLifecycleActionDto.Post)));

        await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var unchanged = (await fixture.Drafts.GetAsync(Fixture.Profile, repaired.JournalEntryId))!;
        Assert.Equal(repaired.Version, unchanged.Version);
        await ApproveAndPost(fixture.Workbench, unchanged);
        var posted = Assert.Single(fixture.Engine.Records[Fixture.OverlayBookId]);
        Assert.Null(posted.Entry.Metadata.PaymentIntentId);
        Assert.Equal(20m, Assert.Single((await fixture.Bridge.GetAsync(fixture.Request, null, null)).Balances,
            balance => balance.AccountPath == ConsolidationService.ReceivableAccount).ConsolidatedBalance);
    }

    [Fact]
    public async Task ConsolidationWorkbench_OperatorDescriptionAndEvidenceNotesRemainEditable()
    {
        var fixture = await CreateWorkbenchFixture();
        var view = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var original = (await fixture.Drafts.GetAsync(Fixture.Profile, Assert.Single(view.Drafts).JournalEntryId))!;
        var edited = await fixture.Workbench.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(original with
        {
            Memo = "Controller explanation of the unmatched twenty",
            EvidenceLinks = original.EvidenceLinks.Append("review:controller-explanation").ToArray()
        }, "maker", LedgerBookId: original.LedgerBookId));

        await fixture.Engine.Service.ValidateCurrentAsync(edited);
        Assert.False(Assert.Single((await fixture.Bridge.GetAsync(fixture.Request, null, null)).Drafts).RequiresRenewedReview);
        await ApproveAndPost(fixture.Workbench, edited);

        var posted = Assert.Single(fixture.Engine.Records[Fixture.OverlayBookId]);
        Assert.Equal(edited.Memo, posted.Entry.Description);
        Assert.Contains("review:controller-explanation", posted.Entry.Metadata.Tags!["evidenceLinks"]);
    }

    private static ManualJournalEntryDraftDto EditHeader(ManualJournalEntryDraftDto draft, string edit) => edit switch
    {
        "entity" => draft with { EntityId = Fixture.FirstId.ToString("D") },
        "root" => draft with { FundNodeId = "another-fund" },
        "dimension-entity" => draft with { Dimensions = draft.Dimensions! with { EntityId = Fixture.FirstId.ToString("D") } },
        "dimension-fund" => draft with { Dimensions = draft.Dimensions! with { FundId = "another-fund" } },
        "dimension-sleeve" => draft with { Dimensions = draft.Dimensions! with { SleeveId = "another-sleeve" } },
        "dimension-organization" => draft with { Dimensions = draft.Dimensions! with { OrganizationId = "another-organization" } },
        "dimension-book" => draft with { Dimensions = draft.Dimensions! with { BookId = "another-book" } },
        "dimension-position" => draft with { Dimensions = draft.Dimensions! with { PositionId = Guid.NewGuid() } },
        "dimension-external" => draft with { Dimensions = draft.Dimensions! with { ExternalGlDimensions = new Dictionary<string, string> { ["Desk"] = "AnotherDesk" } } },
        "entry-type" => draft with { EntryType = ManualJournalEntryTypeDto.Reclassification },
        "treasury-date" => draft with { TreasuryContext = draft.TreasuryContext! with { EffectiveDate = Fixture.Date.AddDays(-1) } },
        "treasury-event" => draft with { TreasuryContext = draft.TreasuryContext! with { FundEventId = "another-event", FundEventType = "another-event-type" } },
        "treasury-investor" => draft with { TreasuryContext = draft.TreasuryContext! with { InvestorId = "another-investor", CapitalAccountId = "another-capital-account" } },
        "treasury-payment" => draft with { TreasuryContext = draft.TreasuryContext! with { PaymentIntentId = "another-payment" } },
        "treasury-settlement" => draft with { TreasuryContext = draft.TreasuryContext! with { SettlementReference = "another-settlement" } },
        "treasury-batch" => draft with { TreasuryContext = draft.TreasuryContext! with { BatchCorrelationId = "another-batch" } },
        _ => throw new ArgumentOutOfRangeException(nameof(edit))
    };
}
