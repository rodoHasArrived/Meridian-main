using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.FundAccounts;

namespace Meridian.Tests.Storage.FundAccounts;

public sealed class PostgresFundAccountStoreTenantAdmissionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("all")]
    [InlineData(" ALL ")]
    public async Task ScopedReads_UnscopedStrictCaller_RejectBeforeOpeningConnection(string? tenant)
    {
        var store = CreateStore(tenant);

        await FluentActions.Awaiting(() => store.GetAccountAsync(Guid.NewGuid()))
            .Should().ThrowAsync<TenantScopeRejectedException>();
        await FluentActions.Awaiting(() => store.QueryAccountsAsync(new AccountStructureQuery()))
            .Should().ThrowAsync<TenantScopeRejectedException>();
    }

    [Fact]
    public async Task QueryAccountsAcrossTenantsAsync_UnscopedStrictCaller_RetainsExplicitCallerPredicateBypass()
    {
        var store = CreateStore(null);

        // Reaching the invalid connection string proves the named cross-tenant read did
        // not apply the ordinary caller rejection. No database instance is required.
        await FluentActions.Awaiting(() => store.QueryAccountsAcrossTenantsAsync(new AccountStructureQuery()))
            .Should().ThrowAsync<ArgumentException>();
    }

    private static PostgresFundAccountStore CreateStore(string? tenant)
        => new(new FundAccountStoreOptions { ConnectionString = "Unsupported=connection-setting" },
            new FixedTenantAccessor(tenant), TenantScopeEnforcementOptions.FailClosed);

    private sealed class FixedTenantAccessor(string? tenant) : IFundScopeTenantAccessor
    {
        public string? ResolveCallerTenant() => tenant;
    }
}
