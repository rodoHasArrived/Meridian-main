using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Consolidation;

namespace Meridian.Tests.Ui;

public sealed partial class AccountingConfigurationServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsolidationManualWorkflow_RecoveryRechecksChartOnlyBeforePostingCommits(bool committed)
    {
        using var fixture = await CreateConsolidationManualFixtureAsync();
        var guard = new ChangingConsolidationGuard();
        var service = ConsolidationManualService(fixture, guard);
        var saved = await service.SaveAutomatedDraftAsync(new SaveManualJournalEntryDraftRequest(
            ConsolidationManualDraft(), "ops-user"), CancellationToken.None);
        var submitted = await service.SubmitApprovalAsync(ConsolidationSubmitRequest(saved));
        var approved = (await service.ApplyLifecycleActionAsync(ConsolidationApprovalRequest(submitted))).JournalEntry;
        using var failure = new RecoveryFailingPostingTarget(fixture.Ledger, committed);
        var request = RecoveryPostRequest(approved);
        await Assert.ThrowsAsync<IOException>(() => ConsolidationManualService(fixture, guard, failure).ApplyLifecycleActionAsync(request));
        await fixture.Configuration.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest("fund-alpha",
            new ChartOfAccountsNodeDto("consolidation-ar", ConsolidationService.ReceivableAccount,
                ConsolidationService.ReceivableAccount, "Asset", FinancialAccountId: "later-bank-account"), "chart-manager"));

        if (committed)
        {
            var recovered = await ConsolidationManualService(fixture, guard).ApplyLifecycleActionAsync(request);
            Assert.Equal(ManualJournalEntryStatusDto.Posted, recovered.JournalEntry.Status);
            Assert.Empty(fixture.PendingFiles());
            Assert.Single(await fixture.Ledger.GetByPeriodAsync(ManualJournalPeriodId));
        }
        else
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ConsolidationManualService(fixture, guard).ApplyLifecycleActionAsync(request));
            Assert.Contains("unscoped", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Single(fixture.PendingFiles());
            Assert.Empty(await fixture.Ledger.GetByPeriodAsync(ManualJournalPeriodId));
        }
        Assert.Equal(1, failure.Calls);
    }
}
