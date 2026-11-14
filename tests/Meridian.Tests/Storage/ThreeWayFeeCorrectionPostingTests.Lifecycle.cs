using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Storage;

public sealed partial class ThreeWayFeeCorrectionPostingTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task MissingFee_PreparesRealDraft_RequiresIndependentApproval_PostsAndReconciles_ThenCreatesLinkedReversal()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var book = Book(Guid.NewGuid(), Guid.NewGuid());
        var period = await SeedBookAsync(database, book);
        await database.JournalStore.AppendAsync(Opening(book, period));
        var root = Path.Combine(Path.GetTempPath(), "meridian-reviewed-fee-workflow", Guid.NewGuid().ToString("N"));
        var options = new ThreeWayReconciliationOptions
        {
            RootDirectory = root,
            CashAccountPath = "Assets:Cash",
            FeeExpenseAccountPath = "Expenses:Fees"
        };
        var scope = new ThreeWayReconciliationScopeDto(Tenant, Company, book.FundProfileId, Guid.NewGuid(),
            book.FundStructureNodeId, book.LedgerBookId, period.PeriodId, "USD", "SyntheticBroker", "External-A");
        var access = new ReconciliationBreakQueueScope(Tenant, Company);
        var observedAt = FeeAt.AddHours(2);
        var reader = Substitute.For<IThreeWayReconciliationSourceReader>();
        reader.AuthorizeAsync(scope.FinancialAccountId, Arg.Any<ReconciliationBreakQueueScope>(), book.LedgerBookId,
            period.PeriodId, Arg.Any<CancellationToken>()).Returns(scope);
        reader.ReadAsync(scope, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(_ => ReadSources());
        var evidence = new FileThreeWayReconciliationEvidenceStore(options);
        var queue = new FileReconciliationBreakQueueRepository(root, NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var comparison = new ThreeWayReconciliationService(reader, evidence, queue, options, new FeeClock(observedAt));
        var configuration = Substitute.For<IAccountingConfigurationService>();
        configuration.GetWorkspaceAsync(book.FundProfileId, book.LedgerBookId, Arg.Any<CancellationToken>(), Tenant, Company)
            .Returns(new AccountingConfigurationWorkspaceDto(book.FundProfileId, book.LedgerBookId,
                AccountingConfigurationStatusDto.Active, "fee-mapping-v1", observedAt, [],
                [new("cash", "Assets:Cash", "Cash", "Asset"), new("fees", "Expenses:Fees", "Fees", "Expense")],
                [], [], [], [], TenantId: Tenant, CompanyId: Company));
        var draftStore = new InMemoryManualJournalEntryDraftStore();
        using var target = new DurableLedgerPostingTarget(database.JournalStore);
        var workbench = new ManualJournalEntryWorkbenchService(draftStore, configuration,
            new InMemoryAccountingActionAuditStore(), journalStore: database.JournalStore, postingTarget: target);
        var preparer = new ThreeWayAdjustmentDraftService(reader, evidence, options,
            database.JournalStore, configuration, workbench, draftStore, timeProvider: new FeeClock(observedAt));
        try
        {
            var run = await comparison.RunAsync(scope.FinancialAccountId, access,
                new(book.LedgerBookId, period.PeriodId, "before-fee", observedAt));
            var proposal = run.AdjustmentProposals.Single(item => item.Status == "Ready");
            var draft = await preparer.PrepareAsync(scope.FinancialAccountId, access,
                new(book.LedgerBookId, period.PeriodId, "before-fee", proposal.CheckId, period.Version, proposal.EvidenceHash),
                "fee-preparer");
            draft.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
            draft.CurrencyEvidenceVersion.Should().Be(1, "new server-owned drafts retain explicit materialization authority");
            draft.IsAdjustingEntry.Should().BeTrue();
            draft.ThreeWayFeeCorrection.Should().NotBeNull();
            draft.ValidationIssues.Should().NotContain(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical,
                string.Join(" | ", draft.ValidationIssues.Select(issue => issue.Code + ": " + issue.Message)));
            Func<Task> forgedIntake = () => workbench.SaveDraftAsync(new(
                draft with { JournalEntryId = Guid.NewGuid(), Version = 0 }, "browser-preparer",
                LedgerBookId: book.LedgerBookId, TenantId: Tenant, CompanyId: Company));
            await forgedIntake.Should().ThrowAsync<InvalidOperationException>().WithMessage("*server-authorized preparation*");
            var submitted = await workbench.SubmitApprovalAsync(new(draft.JournalEntryId, book.FundProfileId,
                "fee-preparer", draft.Version, LedgerBookId: book.LedgerBookId, TenantId: Tenant, CompanyId: Company));
            submitted.Status.Should().Be(ManualJournalEntryStatusDto.Submitted);
            Func<Task> selfApproval = () => workbench.ApplyLifecycleActionAsync(Lifecycle(submitted,
                JournalEntryLifecycleActionDto.Approve, "fee-preparer", "approval"));
            await selfApproval.Should().ThrowAsync<InvalidOperationException>().WithMessage("*independent actor*");
            var approved = (await workbench.ApplyLifecycleActionAsync(Lifecycle(submitted,
                JournalEntryLifecycleActionDto.Approve, "independent-fee-reviewer", "approval"))).JournalEntry;
            approved.Status.Should().Be(ManualJournalEntryStatusDto.Approved);
            var posted = (await workbench.ApplyLifecycleActionAsync(Lifecycle(approved,
                JournalEntryLifecycleActionDto.Post, "independent-fee-reviewer", "posting"))).JournalEntry;
            posted.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
            var feeJournal = (await database.JournalStore.GetByPeriodAsync(period.PeriodId))
                .Single(record => record.Entry.JournalEntryId == posted.JournalEntryId);
            feeJournal.Entry.Lines.Should().OnlyContain(line => line.Currency != null &&
                line.Currency.TransactionCurrency == "USD" && line.Currency.FunctionalCurrency == "USD");
            feeJournal.AdjustmentApproval!.ApprovedBy.Should().Be("independent-fee-reviewer");
            var matched = await comparison.RunAsync(scope.FinancialAccountId, access,
                new(book.LedgerBookId, period.PeriodId, "after-fee", observedAt));
            matched.Status.Should().Be("Matched");
            matched.AdjustmentProposals.Should().BeEmpty();

            var reversal = await workbench.ApplyLifecycleActionAsync(Lifecycle(posted,
                JournalEntryLifecycleActionDto.Reverse, "independent-fee-reviewer", "reversal"));
            var inverse = reversal.GeneratedJournalEntries.Should().ContainSingle().Which;
            inverse.ThreeWayFeeCorrection.Should().BeNull("a linked reversal does not claim the original fee's economic identity");
            inverse.IsAdjustingEntry.Should().BeTrue("new linked correction retains typed-currency materialization");
            inverse.CurrencyEvidenceVersion.Should().Be(1);
            inverse.ReversalOfJournalEntryId.Should().Be(posted.JournalEntryId);
            inverse.TreasuryContext!.IdempotencyKey.Should().NotBe(posted.TreasuryContext!.IdempotencyKey);
            var inverseSubmitted = await workbench.SubmitApprovalAsync(new(inverse.JournalEntryId, book.FundProfileId,
                inverse.PreparedBy, inverse.Version, LedgerBookId: book.LedgerBookId, TenantId: Tenant, CompanyId: Company));
            var inverseApproved = (await workbench.ApplyLifecycleActionAsync(Lifecycle(inverseSubmitted,
                JournalEntryLifecycleActionDto.Approve, "second-independent-reviewer", "approval"))).JournalEntry;
            var inversePosted = (await workbench.ApplyLifecycleActionAsync(Lifecycle(inverseApproved,
                JournalEntryLifecycleActionDto.Post, "second-independent-reviewer", "posting"))).JournalEntry;
            inversePosted.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
            var afterReversal = await database.JournalStore.GetByPeriodAsync(period.PeriodId);
            afterReversal.Should().HaveCount(3);
            afterReversal.SelectMany(record => record.Entry.Lines).Where(line => line.Account.Name == "Cash")
                .Sum(line => line.Debit - line.Credit).Should().Be(100m);
            afterReversal.Single(record => record.SourceJournalEntryId == posted.JournalEntryId).Entry.Lines
                .Should().OnlyContain(line => line.Currency != null);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        JournalEntryLifecycleActionRequestDto Lifecycle(ManualJournalEntryDraftDto draft,
            JournalEntryLifecycleActionDto action, string actor, string intent)
        {
            var subject = intent == "reversal" ? draft.JournalEntryId.ToString("D") : period.PeriodId.ToString("D");
            var proof = $"/api/workstation/evidence/subjects/accounting-record/{intent}/ledger-book/{book.LedgerBookId:D}/{subject}?tenantId={Tenant}&companyId={Company}";
            return new(draft.JournalEntryId, book.FundProfileId, action, actor, draft.Version,
                Notes: "Reviewed retained synthetic broker fee and its linked correction.", EvidenceLinks: [proof],
                LedgerBookId: book.LedgerBookId, TenantId: Tenant, CompanyId: Company);
        }

        async Task<IReadOnlyList<ThreeWaySourceSnapshotDto>> ReadSources()
        {
            var rows = await database.JournalStore.GetByPeriodAsync(period.PeriodId);
            var cash = rows.SelectMany(record => record.Entry.Lines).Where(line => line.Account.Name == "Cash")
                .Sum(line => line.Debit - line.Credit);
            var ids = rows.Select(record => record.Entry.Metadata.Tags is { } tags && tags.TryGetValue("externalTransactionId", out var id) ? id : null)
                .Where(id => id is not null).Select(id => id!).ToArray();
            var proof = "urn:sha256:" + Sha256Digest.ComputeUtf8(string.Join("|", rows.Select(row => row.Entry.JournalEntryId.ToString("D"))));
            var convention = ThreeWayReconciliationOptions.SupportedBasisConvention;
            return [new("ledger", observedAt, proof, cash, [], convention, [], "simulated")
                { AccountingPeriodVersion = period.Version, TransactionIdentityCoverageComplete = true, PostedExternalTransactionIds = ids },
                new("oms", observedAt, "synthetic:independent-oms", 99m, [], convention, [], "simulated"),
                new("broker", observedAt, "synthetic:independent-broker", 99m, [], convention, [], "simulated")
                { CashActivities = [new("Fee-A", "FEE", -1m, "USD", FeeAt, "synthetic://broker/fees/Fee-A")] }];
        }
    }

    private sealed class FeeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
