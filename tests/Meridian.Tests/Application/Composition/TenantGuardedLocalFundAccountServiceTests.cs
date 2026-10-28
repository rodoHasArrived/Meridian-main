using FluentAssertions;
using Meridian.Application.Tenancy;
using Meridian.Contracts.Tenancy;
using Meridian.PortfolioRecords.Accounts;
using Meridian.PortfolioRecords.FundAccounts;
using Xunit;

namespace Meridian.Tests.Application.Composition;

public sealed class TenantGuardedLocalFundAccountServiceTests
{
    [Fact]
    public async Task BatchedReconciliationResults_MigrationHost_ForwardsToLocalStore()
    {
        var guarded = new TenantGuardedLocalFundAccountService(new InMemoryFundAccountService(),
            new LocalTenantMigrationGate(TenantScopeEnforcementOptions.DeploymentBoundary));

        // Both interfaces resolve to the guard; neither may fall through to the NotSupported default.
        var viaFundAccounts = await ((IFundAccountService)guarded)
            .GetReconciliationResultsForAccountsAsync([Guid.NewGuid()], openOnly: true);
        var viaAccountQuery = await ((IAccountQueryService)guarded)
            .GetReconciliationResultsForAccountsAsync([Guid.NewGuid()]);

        viaFundAccounts.Should().BeEmpty();
        viaAccountQuery.Should().BeEmpty();
    }

    [Fact]
    public async Task BatchedReconciliationResults_StrictHost_RefusesThroughGate()
    {
        var guarded = new TenantGuardedLocalFundAccountService(new InMemoryFundAccountService(),
            new LocalTenantMigrationGate(TenantScopeEnforcementOptions.FailClosed));

        var read = () => ((IFundAccountService)guarded).GetReconciliationResultsForAccountsAsync([Guid.NewGuid()]);

        await read.Should().ThrowAsync<LocalTenantMigrationRequiredException>();
    }
}
