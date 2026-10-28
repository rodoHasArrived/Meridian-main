using System.Reflection;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Reporting;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Application.Services;

public sealed partial class FundOperationsWorkspaceReadServiceTests
{
    [Fact]
    public async Task GetWorkspaceAsync_RunCommittedAfterResults_RetriesBeforePublishingCountsAndRunStatus()
    {
        var fixture = await CreateReadCutFixtureAsync();
        var date = new DateOnly(2026, 10, 15);
        fixture.Spy.AfterBatchRead = async () =>
        {
            await fixture.Accounts.RecordBalanceSnapshotAsync(new(fixture.AccountId, date, "USD", 90m, "internal"));
            await fixture.Accounts.ReconcileAccountAsync(new(fixture.AccountId, date, "ops"));
        };

        var view = await fixture.Service.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(fixture.FundProfileId));

        view.Accounts.Should().ContainSingle().Which.OpenBreaks.Should().Be(1);
        view.Reconciliation.OpenBreakCount.Should().Be(1);
        view.Reconciliation.RecentRuns.Should().Contain(run => run.TotalBreaks == 1 && run.Status != "Superseded");
        fixture.Spy.BatchReads.Should().BeGreaterThan(2, "the mismatched read cut must be retried");
    }

    [Fact]
    public async Task GetWorkspaceAsync_ContinuouslyChangingRunHistory_RefusesAnUnverifiedProjection()
    {
        var fixture = await CreateReadCutFixtureAsync();
        fixture.Spy.RepeatAfterBatchRead = true;
        fixture.Spy.AfterBatchRead = async () =>
        {
            await fixture.Accounts.ReconcileAccountAsync(new(fixture.AccountId, new DateOnly(2026, 10, 15), "ops"));
        };

        var read = () => fixture.Service.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(fixture.FundProfileId));

        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*history changed*Retry*");
    }

    private static async Task<(FundOperationsWorkspaceReadService Service, InMemoryFundAccountService Accounts,
        Guid AccountId, string FundProfileId, ReconciliationReadSpy Spy)> CreateReadCutFixtureAsync()
    {
        var fundProfileId = $"fund-{Guid.NewGuid():N}";
        var accounts = new InMemoryFundAccountService();
        var observedService = DispatchProxy.Create<IFundAccountService, ReconciliationReadSpy>();
        var spy = (ReconciliationReadSpy)observedService;
        spy.Inner = accounts;
        var securityMaster = new NullSecurityMasterQueryService();
        var service = new FundOperationsWorkspaceReadService(observedService, new StrategyRunStore(),
            new PortfolioReadService(), new NavAttributionService(securityMaster), new ReportGenerationService(securityMaster));
        var account = await accounts.CreateAccountAsync(new(Guid.NewGuid(), AccountTypeDto.Bank,
            "BANK-CUT", "Read cut test", "USD", DateTimeOffset.UtcNow, "ops",
            FundId: TranslateFundProfileId(fundProfileId), LedgerReference: "LEDGER"));
        var date = new DateOnly(2026, 10, 15);
        await accounts.RecordBalanceSnapshotAsync(new(account.AccountId, date, "USD", 100m, "internal"));
        var batch = Guid.NewGuid();
        await accounts.IngestBankStatementAsync(new(batch, account.AccountId, date, "Bank", null,
            [new(Guid.NewGuid(), batch, account.AccountId, date, date, 100m, "USD", "credit", "balance", null, 100m)], "ops"));
        await accounts.ReconcileAccountAsync(new(account.AccountId, date, "ops"));
        return (service, accounts, account.AccountId, fundProfileId, spy);
    }
}
