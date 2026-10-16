using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Tenancy;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Core.Exceptions;
using Meridian.PortfolioRecords.Accounts;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Storage;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using AuthEnvironmentScope = Meridian.Tests.Identity.EnvironmentVariableScope;

namespace Meridian.Tests.Application.Composition;

[Collection("IdentityEnvironment")]
public sealed class LocalTenantCapabilityCompositionTests : IDisposable
{
    private readonly AuthEnvironmentScope _environment = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-local-tenancy-{Guid.NewGuid():N}");

    public LocalTenantCapabilityCompositionTests()
    {
        foreach (var name in new[]
        {
            "ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT", "MERIDIAN_ENVIRONMENT",
            "MERIDIAN_DEPLOYMENT_ENVIRONMENT", "MERIDIAN_MODE", "MERIDIAN_API_DEPLOYMENT_MODE",
            TenantScopeEnforcementOptions.EnvironmentVariable, MeridianDatabaseEnvironment.UnifiedVariable,
            "MERIDIAN_SCOPED_ACCESS_CONNECTION_STRING"
        }.Concat(MeridianDatabaseEnvironment.PropagatedConnectionStringVariables))
            _environment.Set(name, null);
        _environment.Set("DOTNET_ENVIRONMENT", "Development")
            .Set("ASPNETCORE_ENVIRONMENT", "Development")
            .Set("MERIDIAN_USE_INMEMORY_GOVERNANCE", "true");
        Directory.CreateDirectory(_root);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "tenant-alpha")]
    [InlineData(true, null)]
    [InlineData(true, "tenant-alpha")]
    [InlineData(true, "tenant-beta")]
    public async Task StrictHost_RejectsUnattributedLocalCapabilityAcrossBrowserAndWorkerPaths(bool browser, string? tenant)
    {
        var services = Compose(TenantScopeEnforcementOptions.FailClosed);
        if (browser)
        {
            var http = new DefaultHttpContext();
            if (tenant is not null)
                http.Items[LoginSessionMiddleware.CurrentTenantIdKey] = tenant;
            services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = http });
            services.AddWorkstationSharedServices();
        }
        using var authority = !browser && tenant is not null ? FundScopeTenantAuthority.Enter(tenant, "retained worker") : null;
        using var provider = services.BuildServiceProvider();
        var accounts = provider.GetRequiredService<IFundAccountService>();
        var structure = provider.GetRequiredService<IFundStructureService>();
        accounts.Should().BeOfType<TenantGuardedLocalFundAccountService>();
        structure.Should().BeOfType<TenantGuardedLocalFundStructureService>();
        provider.GetRequiredService<IAccountManagementService>().Should().BeSameAs(accounts);
        provider.GetRequiredService<IAccountQueryService>().Should().BeSameAs(accounts);
        provider.GetRequiredService<IFundScopeTenantAccessor>().ResolveCallerTenant().Should().Be(tenant);
        Func<Task> accountRead = () => accounts.QueryAccountsAsync(new());
        Func<Task> graphRead = () => structure.GetOrganizationStructureAsync(new());
        Func<Task> write = () => provider.GetRequiredService<IAccountManagementService>()
            .CreateAccountAsync(new(Guid.NewGuid(), AccountTypeDto.Bank, "REFUSED", "Refused account", "USD",
                DateTimeOffset.UtcNow, "operator"));
        var refusal = await accountRead.Should().ThrowAsync<MeridianException>().WithMessage(LocalTenantMigrationGate.RefusalMessage);
        refusal.Which.Should().BeOfType<LocalTenantMigrationRequiredException>();
        await graphRead.Should().ThrowAsync<LocalTenantMigrationRequiredException>().WithMessage(LocalTenantMigrationGate.RefusalMessage);
        await write.Should().ThrowAsync<LocalTenantMigrationRequiredException>().WithMessage(LocalTenantMigrationGate.RefusalMessage);
    }

    [Fact]
    public async Task ExplicitMigrationHost_CanReadAndWriteRetainedLocalCapability()
    {
        using var provider = Compose(TenantScopeEnforcementOptions.DeploymentBoundary).BuildServiceProvider();
        var accounts = provider.GetRequiredService<IFundAccountService>();
        var created = await accounts.CreateAccountAsync(new(Guid.NewGuid(), AccountTypeDto.Bank, "LEGACY", "Legacy account",
            "USD", DateTimeOffset.UtcNow, "operator"));
        (await provider.GetRequiredService<IAccountQueryService>().GetAccountAsync(created.AccountId))
            .Should().BeEquivalentTo(created);
        (await provider.GetRequiredService<IFundStructureService>().GetOrganizationStructureAsync(new()))
            .Organizations.Should().BeEmpty();
    }

    private IServiceCollection Compose(TenantScopeEnforcementOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(options);
        services.AddMarketDataServices(CompositionOptions.Minimal with { DataRoot = _root });
        return services;
    }

    public void Dispose()
    {
        _environment.Dispose();
        Directory.Delete(_root, recursive: true);
    }
}
