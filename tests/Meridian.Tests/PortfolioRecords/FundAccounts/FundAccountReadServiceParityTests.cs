using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Wpf.Services;

namespace Meridian.Tests.PortfolioRecords.FundAccounts;

public sealed class FundAccountReadServiceParityTests
{
    [Fact]
    public async Task DesktopAccountAdapter_CountsActiveResultsWhilePreservingHistoricalRunTotals()
    {
        var accounts = new InMemoryFundAccountService();
        const string profile = "adapter-reconciliation";
        var account = await accounts.CreateAccountAsync(new(Guid.NewGuid(), AccountTypeDto.Bank, "ADAPTER", "Adapter",
            "USD", DateTimeOffset.UtcNow, "ops", FundId: FundProfileKeyTranslator.ToFundId(profile)));
        var date = new DateOnly(2026, 10, 15);
        await accounts.RecordBalanceSnapshotAsync(new(account.AccountId, date, "USD", 100m, "internal"));
        var batch = Guid.NewGuid();
        await accounts.IngestBankStatementAsync(new(batch, account.AccountId, date, "Bank", null,
            [new(Guid.NewGuid(), batch, account.AccountId, date, date, 90m, "USD", "credit", "balance", null, 90m)], "ops"));
        var original = await accounts.ReconcileAccountAsync(new(account.AccountId, date, "ops"));
        var adapter = new FundAccountReadService(accounts);
        (await adapter.GetAccountsAsync(profile)).Single().OpenBreaks.Should().Be(1);

        await accounts.RecordBalanceSnapshotAsync(new(account.AccountId, date, "USD", 90m, "internal"));
        await accounts.ReconcileAccountAsync(new(account.AccountId, date, "ops"));
        var summary = (await adapter.GetAccountsAsync(profile)).Single();
        summary.OpenBreaks.Should().Be(0);
        summary.ReconciliationRuns.Should().Be(2);
        (await accounts.GetReconciliationRunsAsync(account.AccountId))
            .Single(run => run.ReconciliationRunId == original.ReconciliationRunId).TotalBreaks.Should().Be(1);
    }

    [Fact]
    public async Task DesktopBankAdapter_UsesRevisionAndSourceSequenceAndKeepsCompetingEvidenceUnavailable()
    {
        var accounts = new InMemoryFundAccountService();
        const string profile = "adapter-statement";
        var account = await accounts.CreateAccountAsync(new(Guid.NewGuid(), AccountTypeDto.Bank, "STATEMENT", "Statement",
            "USD", DateTimeOffset.UtcNow, "ops", FundId: FundProfileKeyTranslator.ToFundId(profile)));
        var date = new DateOnly(2026, 10, 15);
        async Task Ingest(int revision, decimal closing)
        {
            var batch = Guid.NewGuid();
            await accounts.IngestBankStatementAsync(new(batch, account.AccountId, date, "Bank", null,
                [new(Guid.NewGuid(), batch, account.AccountId, date, date, 100m, "USD", "credit", "first", null, 100m, LineSequence: 1),
                 new(Guid.NewGuid(), batch, account.AccountId, date.AddDays(-1), date.AddDays(-1), 5m, "USD", "credit", "last", null, closing, LineSequence: 2)],
                "ops", StatementRevision: revision));
        }
        await Ingest(2, 200m);
        await Ingest(1, 110m);
        var adapter = new FundAccountReadService(accounts);
        var authoritative = (await adapter.GetBankSnapshotsAsync(profile)).Single();
        authoritative.CurrentBalance.Should().Be(200m);
        authoritative.LastStatementDate.Should().Be(date);

        await Ingest(2, 300m);
        var ambiguous = (await adapter.GetBankSnapshotsAsync(profile)).Single();
        ambiguous.CurrentBalance.Should().BeNull();
        ambiguous.LastStatementDate.Should().Be(date);
    }
}
