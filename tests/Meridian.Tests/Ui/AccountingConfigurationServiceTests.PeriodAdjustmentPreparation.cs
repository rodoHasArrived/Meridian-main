using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Moq;

namespace Meridian.Tests.Ui;

public sealed partial class AccountingConfigurationServiceTests
{
    [Fact]
    public async Task GovernedDailyMark_PostsNativeCurrency_AndCanPrepareNextPeriodReversalWithoutChangingSource()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        await configuration.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest("fund-alpha",
            new("securities-aapl", "Assets:Securities:AAPL", "Securities", "Asset", Symbol: "AAPL"), "valuation-preparer"));
        await configuration.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest("fund-alpha",
            new("unrealized-gain-aapl", "Income:Unrealized Gain:AAPL", "Unrealized Gain", "Revenue", FinancialAccountId: "AAPL"), "valuation-preparer"));
        var journalStore = WritableManualJournalLedgerJournalStore.Default(AccountingBasisKindDto.Primary);
        var drafts = new InMemoryManualJournalEntryDraftStore();
        using var target = new DurableLedgerPostingTarget(journalStore);
        var workbench = CreateManualJournalEntryWorkbenchService(configuration, journalStore, postingTarget: target, draftStore: drafts);
        var intake = new AutomatedJournalDraftIntakeService(workbench, drafts, configuration);
        var asOf = new DateTimeOffset(2026, 6, 30, 16, 0, 0, TimeSpan.Zero);
        var projection = DailyPortfolioPricingProjector.Project(new DailyPortfolioPricingInput(
            new DailyPortfolioPricingPolicy("fund-alpha", "daily-close", "Daily close", "market-close", "controller", asOf.AddDays(-1)),
            ManualJournalPeriodId.ToString("D"), asOf, "USD",
            [new DailyPortfolioPriceMark("AAPL", 1m, 100m, 110m, "official-close", "price://aapl/close",
                SecurityId: DailyValuationAaplSecurityId, PriceObservedOn: new DateOnly(2026, 6, 30))], entityId: "entity-master"));
        var sourceDraft = (await intake.IntakeDraftsAsync(new AutomatedJournalPreparedDraftIntakeRequest(
            "fund-alpha", "USD", DailyPortfolioPricingDraftBuilder.BuildDrafts(projection), "valuation-preparer",
            ManualJournalLedgerBookId, ManualJournalPeriodId.ToString("D"), EntityId: "entity-master"))).Created.Single();
        var validatedSource = await workbench.ValidateDraftAsync(new(sourceDraft, "valuation-preparer",
            LedgerBookId: ManualJournalLedgerBookId));
        validatedSource.ValidationIssues.Where(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical)
            .Should().BeEmpty("the synthetic governed mark must carry complete source and entity evidence before submission");
        var submitted = (await workbench.ApplyLifecycleActionAsync(new(sourceDraft.JournalEntryId, "fund-alpha",
            JournalEntryLifecycleActionDto.Submit, "controller", sourceDraft.Version, Notes: "Submit retained mark.",
            EvidenceLinks: ["evidence://accounting/valuation/submit"], LedgerBookId: ManualJournalLedgerBookId))).JournalEntry;
        var approved = (await workbench.ApplyLifecycleActionAsync(new(submitted.JournalEntryId, "fund-alpha",
            JournalEntryLifecycleActionDto.Approve, "controller", submitted.Version, Notes: "Approve retained mark.",
            EvidenceLinks: [ManualJournalApprovalEvidence(submitted)], LedgerBookId: ManualJournalLedgerBookId))).JournalEntry;
        var posted = (await workbench.ApplyLifecycleActionAsync(new(approved.JournalEntryId, "fund-alpha",
            JournalEntryLifecycleActionDto.Post, "controller", approved.Version, Notes: "Post approved mark.",
            EvidenceLinks: [ManualJournalPostingEvidence(approved)], LedgerBookId: ManualJournalLedgerBookId))).JournalEntry;
        var source = (await journalStore.GetByPeriodAsync(ManualJournalPeriodId)).Single();
        source.Entry.Metadata.ActivityType.Should().Be("ManualJournalEntry");
        source.Entry.Lines.Should().OnlyContain(line => line.Currency != null && line.Currency.IsFunctionalCurrency);
        source.Entry.Lines.Single(line => LedgerAccounts.UsesInstrumentSymbol(line.Account)).Account.FinancialAccountId.Should().BeNull();
        journalStore.SetPeriodStatus("HardClosed");
        var closed = (await journalStore.GetPeriodAsync(ManualJournalPeriodId))!;
        var next = closed with { PeriodId = Guid.NewGuid(), StartDate = new(2026, 7, 1), EndDate = new(2026, 7, 31), Status = "Open", ClosedAt = null };
        var reads = new Mock<ILedgerJournalStore>();
        reads.Setup(x => x.GetLedgerBookAsync(ManualJournalLedgerBookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(await journalStore.GetLedgerBookAsync(ManualJournalLedgerBookId));
        reads.Setup(x => x.GetPeriodAsync(ManualJournalPeriodId, It.IsAny<CancellationToken>())).ReturnsAsync(closed);
        reads.Setup(x => x.GetPeriodAsync(next.PeriodId, It.IsAny<CancellationToken>())).ReturnsAsync(next);
        reads.Setup(x => x.QueryAsync(It.Is<LedgerJournalEntryQuery>(q => q.JournalEntryId == source.Entry.JournalEntryId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { source });
        var scopedConfigStore = new InMemoryAccountingConfigurationStore();
        await scopedConfigStore.SaveAsync((await configuration.GetWorkspaceAsync("fund-alpha")) with
        { LedgerBookId = null, TenantId = "synthetic-tenant", CompanyId = "synthetic-company" });
        var scopedConfig = new AccountingConfigurationService(scopedConfigStore, new InMemoryAccountingActionAuditStore());
        var nextDrafts = new InMemoryManualJournalEntryDraftStore();
        var nextWorkbench = new ManualJournalEntryWorkbenchService(nextDrafts, scopedConfig, new InMemoryAccountingActionAuditStore(),
            new DailyValuationSecurityMasterQueryService(), reads.Object);
        var preparation = new PeriodAdjustmentPreparationService(reads.Object,
            new AutomatedJournalDraftIntakeService(nextWorkbench, nextDrafts, scopedConfig));
        var reversal = await preparation.PrepareAsync(new(ManualJournalLedgerBookId, source.Entry.JournalEntryId,
            next.PeriodId, next.StartDate, "reverse-june-mark", "Explicit next-period reversal policy", true),
            "next-period-preparer", "synthetic-tenant", "synthetic-company");
        reversal.Created.Should().ContainSingle().Which.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        (await drafts.GetAsync("fund-alpha", posted.JournalEntryId))!.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
        journalStore.Appended.Should().ContainSingle();
        (await journalStore.GetPeriodAsync(ManualJournalPeriodId))!.Status.Should().Be("HardClosed");
    }
}
