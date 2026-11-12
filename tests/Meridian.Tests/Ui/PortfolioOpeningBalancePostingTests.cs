using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Subscriptions.Services;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Config;
using Meridian.Core.Subscriptions.Models;
using Meridian.Storage.Ledger;
using Meridian.Tests.Storage;
using Meridian.Ui.Shared.Services;
using Meridian.PortfolioRecords.FundAccounts;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class PortfolioOpeningBalancePostingTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task TwoSymbolImport_PreparesReviewsAndPostsOneBalancedOpeningJournal()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var root = Path.Combine(Path.GetTempPath(), "meridian-opening-post-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string tenant = "tenant-opening";
            const string company = "company-opening";
            var at = new DateTimeOffset(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);
            var fund = Guid.NewGuid().ToString("D");
            var account = Guid.NewGuid();
            var entity = Guid.NewGuid();
            var book = Guid.NewGuid();
            var period = Guid.NewGuid();
            await database.JournalStore.SaveLedgerBookAsync(new(book, fund, account,
                FundStructureNodeKindDto.Account, "Imported brokerage", "USD", at.AddDays(-1), at.AddDays(-1)));
            await database.JournalStore.SavePeriodAsync(new(period, book, 2026, 11, "2026-11",
                new(2026, 11, 1), new(2026, 11, 30), "Open", at.AddDays(-1), null, 0), 0);
            var securities = Substitute.For<ISecurityMasterQueryService>();
            foreach (var symbol in new[] { "AAPL", "MSFT" })
            {
                var detail = Security(symbol, at);
                securities.GetByIdentifierAsync(SecurityIdentifierKind.Ticker, symbol, null, Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
                    .Returns(detail);
                securities.GetByIdAsync(detail.SecurityId, Arg.Any<CancellationToken>()).Returns(detail);
                securities.GetByIdAsOfAsync(detail.SecurityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(detail);
            }
            var configurationStore = new InMemoryAccountingConfigurationStore();
            await configurationStore.SaveAsync(new AccountingConfigurationWorkspaceDto(fund, book,
                AccountingConfigurationStatusDto.Draft, "v1", at, [],
                [new("cash", "Assets:Cash", "Cash", "Asset"),
                 new("securities", "Assets:Securities", "Securities", "Asset"),
                 new("capital", "Equity:Capital", "Capital Account", "Equity")], [], [], [], [], TenantId: tenant, CompanyId: company));
            var audit = new InMemoryAccountingActionAuditStore();
            var configuration = new AccountingConfigurationService(configurationStore, audit);
            var drafts = new InMemoryManualJournalEntryDraftStore();
            using var postingTarget = new DurableLedgerPostingTarget(database.JournalStore);
            var workbench = new ManualJournalEntryWorkbenchService(drafts, configuration, audit, securities,
                database.JournalStore, postingTarget: postingTarget);
            var intake = new AutomatedJournalDraftIntakeService(workbench, drafts, configuration);
            var configPath = Path.Combine(root, "appsettings.json");
            await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(new AppConfig(DataRoot: root)));
            var accountAuthority = Substitute.For<IFundAccountService>();
            accountAuthority.GetAccountAsync(account, Arg.Any<CancellationToken>()).Returns(new AccountSummaryDto(
                account, AccountTypeDto.Brokerage, entity, Guid.Parse(fund), null, null, "synthetic-account", "Imported brokerage",
                "USD", "alpaca", true, at.AddYears(-1), null, null, null, null, null));
            var queue = new PortfolioOpeningBalanceQueue(intake, new ConfigStore(configPath), database.JournalStore, securities, accountAuthority);
            var request = new PortfolioImportRunRequest(Guid.NewGuid(), "alpaca", new(BrokerAccountId: "synthetic-account"),
                new(tenant, company, fund, account, book, period, new(2026, 11, 12), "import-preparer", entity), true);
            var snapshot = new PortfolioSummary("alpaca", "synthetic-account", 375m, 25m, null,
                [new("AAPL", 2m, 200m, 100m, null, "us_equity", null),
                 new("MSFT", 3m, 150m, 50m, null, "us_equity", null)], at);
            var run = new PortfolioImportRun(request, "synthetic-command-hash", PortfolioImportRunStatus.Partial, at, at,
                snapshot, PortfolioImportRunService.SnapshotHash(snapshot));
            var id = await queue.QueueAsync(run);
            (await database.JournalStore.GetByPeriodAsync(period)).Should().BeEmpty("intake must never approve or post automatically");
            var draft = (await drafts.GetAsync(fund, id, tenantId: tenant, companyId: company))!;
            var validated = await workbench.ValidateDraftAsync(new(draft, "import-preparer", LedgerBookId: book, TenantId: tenant, CompanyId: company));
            validated.ValidationIssues.Should().NotContain(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
            draft.ValidationIssues.Should().NotContain(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
            draft.Lines.Where(line => line.LedgerAccountSymbol is not null).Should().OnlyContain(line => line.SecurityId.HasValue);
            var review = $"evidence://review/tenant/{tenant}/company/{company}/book/{book:D}/journal/{id:D}/approval-posting";
            var submitted = await workbench.SubmitApprovalAsync(new(id, fund, "import-preparer", draft.Version,
                Notes: "Submit retained broker opening", EvidenceLinks: [review], LedgerBookId: book, TenantId: tenant, CompanyId: company));
            var approved = await workbench.ApplyLifecycleActionAsync(new(id, fund, JournalEntryLifecycleActionDto.Approve,
                "controller", submitted.Version, Notes: "Reviewed source cost and cash", EvidenceLinks: [review],
                LedgerBookId: book, TenantId: tenant, CompanyId: company));
            var posted = await workbench.ApplyLifecycleActionAsync(new(id, fund, JournalEntryLifecycleActionDto.Post,
                "controller", approved.JournalEntry.Version, Notes: "Post reviewed opening balances", EvidenceLinks: [review],
                LedgerBookId: book, TenantId: tenant, CompanyId: company));
            posted.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
            var retained = (await database.JournalStore.GetByPeriodAsync(period)).Should().ContainSingle().Subject.Entry;
            retained.Lines.Sum(line => line.Debit).Should().Be(375m);
            retained.Lines.Sum(line => line.Credit).Should().Be(375m);
            retained.Lines.Where(line => line.Account.Symbol is not null).Should().HaveCount(2)
                .And.OnlyContain(line => line.Dimensions!.InstrumentId.HasValue);
            retained.Metadata.SecurityId.Should().BeNull("mixed journals use the proven per-line identities");
            retained.Metadata.Tags!["securityMaster.lineageMode"].Should().Be("per-line");
            (await queue.QueueAsync(run)).Should().Be(id, "receipt recovery cannot create another opening");
            (await database.JournalStore.GetByPeriodAsync(period)).Should().ContainSingle();
        }
        finally { Directory.Delete(root, true); }
    }

    private static SecurityDetailDto Security(string symbol, DateTimeOffset at)
        => new(Guid.NewGuid(), "Equity", SecurityStatusDto.Active, symbol, "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }),
            [new(SecurityIdentifierKind.Ticker, symbol, true, at.AddYears(-1), null, null)], [], 1, at.AddYears(-1), null);
}
