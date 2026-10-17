using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Consolidation;

namespace Meridian.Tests.FinancialOperations.Consolidation;

public sealed partial class ConsolidationServiceTests
{
    [Theory]
    [InlineData(true, true, JournalEntryLifecycleActionDto.Submit)]
    [InlineData(true, false, JournalEntryLifecycleActionDto.Submit)]
    [InlineData(false, true, JournalEntryLifecycleActionDto.Submit)]
    [InlineData(false, false, JournalEntryLifecycleActionDto.Submit)]
    [InlineData(true, true, JournalEntryLifecycleActionDto.Approve)]
    [InlineData(true, false, JournalEntryLifecycleActionDto.Approve)]
    [InlineData(false, true, JournalEntryLifecycleActionDto.Approve)]
    [InlineData(false, false, JournalEntryLifecycleActionDto.Approve)]
    [InlineData(true, true, JournalEntryLifecycleActionDto.Post)]
    [InlineData(true, false, JournalEntryLifecycleActionDto.Post)]
    [InlineData(false, true, JournalEntryLifecycleActionDto.Post)]
    [InlineData(false, false, JournalEntryLifecycleActionDto.Post)]
    public async Task ConsolidationWorkbench_ChangedChartScopeBlocksRetainedLifecycle(
        bool receivable, bool symbol, JournalEntryLifecycleActionDto action)
    {
        var fixture = await CreateWorkbenchFixture();
        var view = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var draft = (await fixture.Drafts.GetAsync(Fixture.Profile, Assert.Single(view.Drafts).JournalEntryId))!;
        if (action != JournalEntryLifecycleActionDto.Submit)
            draft = await fixture.Workbench.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
                draft.JournalEntryId, draft.FundProfileId, "maker", draft.Version, LedgerBookId: draft.LedgerBookId));
        if (action == JournalEntryLifecycleActionDto.Post)
            draft = (await fixture.Workbench.ApplyLifecycleActionAsync(
                WorkbenchAction(draft, JournalEntryLifecycleActionDto.Approve))).JournalEntry;
        var path = receivable ? ConsolidationService.ReceivableAccount : ConsolidationService.PayableAccount;
        var unscoped = new ChartOfAccountsNodeDto(receivable ? "ar" : "ap", path, path,
            receivable ? "Asset" : "Liability");
        await fixture.Configuration.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(Fixture.Profile,
            unscoped with { Symbol = symbol ? "ENTITY-SCOPE" : null, FinancialAccountId = symbol ? null : "bank-account" },
            "chart-manager", LedgerBookId: draft.LedgerBookId));

        var exception = action == JournalEntryLifecycleActionDto.Submit
            ? await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Workbench.SubmitApprovalAsync(
                new SubmitManualJournalEntryApprovalRequest(draft.JournalEntryId, draft.FundProfileId, "maker",
                    draft.Version, LedgerBookId: draft.LedgerBookId)))
            : await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Workbench.ApplyLifecycleActionAsync(
                WorkbenchAction(draft, action)));

        Assert.Contains("unscoped", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ConsolidationService.Hash(draft), ConsolidationService.Hash(
            (await fixture.Drafts.GetAsync(Fixture.Profile, draft.JournalEntryId))!));
        Assert.Empty(fixture.Engine.Records[Fixture.OverlayBookId]);
        await fixture.Configuration.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(Fixture.Profile,
            unscoped, "chart-manager", LedgerBookId: draft.LedgerBookId));
        if (action == JournalEntryLifecycleActionDto.Submit)
            draft = await fixture.Workbench.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
                draft.JournalEntryId, draft.FundProfileId, "maker", draft.Version, LedgerBookId: draft.LedgerBookId));
        if (action != JournalEntryLifecycleActionDto.Post)
            draft = (await fixture.Workbench.ApplyLifecycleActionAsync(
                WorkbenchAction(draft, JournalEntryLifecycleActionDto.Approve))).JournalEntry;
        await fixture.Workbench.ApplyLifecycleActionAsync(WorkbenchAction(draft, JournalEntryLifecycleActionDto.Post));
        Assert.Single(fixture.Engine.Records[Fixture.OverlayBookId]);
    }
}
