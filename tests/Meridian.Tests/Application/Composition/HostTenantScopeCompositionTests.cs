using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Contracts.Tenancy;
using Meridian.Storage;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using AuthEnvironmentScope = Meridian.Tests.Identity.EnvironmentVariableScope;

namespace Meridian.Tests.Application.Composition;

[Collection("IdentityEnvironment")]
public sealed class HostTenantScopeCompositionTests : IDisposable
{
    private readonly AuthEnvironmentScope _environment = new();

    public HostTenantScopeCompositionTests()
    {
        foreach (var name in new[]
        {
            "ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT", "MERIDIAN_ENVIRONMENT",
            "MERIDIAN_DEPLOYMENT_ENVIRONMENT", "MERIDIAN_MODE", "MERIDIAN_API_DEPLOYMENT_MODE",
            TenantScopeEnforcementOptions.EnvironmentVariable, MeridianDatabaseEnvironment.UnifiedVariable,
            "MERIDIAN_SCOPED_ACCESS_CONNECTION_STRING"
        }.Concat(MeridianDatabaseEnvironment.PropagatedConnectionStringVariables))
        {
            _environment.Set(name, null);
        }

        _environment.Set("DOTNET_ENVIRONMENT", "Development")
            .Set("ASPNETCORE_ENVIRONMENT", "Development")
            .Set("MERIDIAN_USE_INMEMORY_GOVERNANCE", "true");
    }

    [Fact]
    public async Task CoreOnlyHost_EnforcesTheConfiguredPostureAndTracksRetainedWorkerAuthority()
    {
        _environment.Set(TenantScopeEnforcementOptions.EnvironmentVariable, "fail-closed");
        var services = CreateCoreServices();
        using var provider = services.BuildServiceProvider();
        var posture = provider.GetRequiredService<TenantScopeEnforcementOptions>();
        var accessor = provider.GetRequiredService<IFundScopeTenantAccessor>();
        posture.IsFailClosed.Should().BeTrue();
        TenantReadPredicate.ShouldRejectRead(accessor.ResolveCallerTenant(), posture.Mode).Should().BeTrue();

        using (FundScopeTenantAuthority.Enter(" tenant-alpha ", "scheduled statement import"))
        {
            await Task.Yield();
            accessor.ResolveCallerTenant().Should().Be("tenant-alpha");
            TenantReadPredicate.ShouldRejectRead(accessor.ResolveCallerTenant(), posture.Mode).Should().BeFalse();
            using (FundScopeTenantAuthority.Enter("tenant-beta", "nested retained work"))
            {
                accessor.ResolveCallerTenant().Should().Be("tenant-beta");
            }
            accessor.ResolveCallerTenant().Should().Be("tenant-alpha");
        }

        accessor.ResolveCallerTenant().Should().BeNull();
        TenantReadPredicate.ShouldRejectRead(accessor.ResolveCallerTenant(), posture.Mode).Should().BeTrue();
    }

    [Fact]
    public void CoreOnlyHost_LeavesAnUnconfiguredDeploymentOnItsExistingBoundary()
    {
        using var provider = CreateCoreServices().BuildServiceProvider();
        provider.GetRequiredService<TenantScopeEnforcementOptions>().IsFailClosed.Should().BeFalse();
        provider.GetRequiredService<IFundScopeTenantAccessor>().ResolveCallerTenant().Should().BeNull();
    }

    [Fact]
    public void CoreOnlyHost_RefusesAnInvalidExplicitPostureBeforeBuildingItsStores()
    {
        _environment.Set(TenantScopeEnforcementOptions.EnvironmentVariable, "fail_closed");
        Action compose = () => CreateCoreServices();
        compose.Should().Throw<ArgumentException>().WithMessage("*MERIDIAN_TENANT_SCOPE_ENFORCEMENT*");
    }

    [Fact]
    public void BrowserHost_UsesRequestAuthorityAndDoesNotBorrowWorkerAuthorityForATenantlessRequest()
    {
        _environment.Set(TenantScopeEnforcementOptions.EnvironmentVariable, "fail-closed");
        var services = CreateCoreServices();
        var http = new HttpContextAccessor();
        services.AddSingleton<IHttpContextAccessor>(http);
        services.AddWorkstationSharedServices();
        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IFundScopeTenantAccessor>();
        provider.GetRequiredService<TenantScopeEnforcementOptions>().IsFailClosed.Should().BeTrue();
        accessor.Should().BeOfType<WorkstationFundScopeTenantAccessor>();

        using (FundScopeTenantAuthority.Enter("worker-tenant", "retained background work"))
        {
            accessor.ResolveCallerTenant().Should().Be("worker-tenant");
            http.HttpContext = new DefaultHttpContext();
            http.HttpContext.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "request-tenant";
            accessor.ResolveCallerTenant().Should().Be("request-tenant");
            http.HttpContext.Items.Clear();
            accessor.ResolveCallerTenant().Should().BeNull();
            http.HttpContext = null;
            accessor.ResolveCallerTenant().Should().Be("worker-tenant");
        }
        accessor.ResolveCallerTenant().Should().BeNull();
    }

    [Fact]
    public void HostComposition_PreservesExplicitOptionsAndAccessorAcrossCoreAndBrowserRegistration()
    {
        _environment.Set(TenantScopeEnforcementOptions.EnvironmentVariable, "invalid-but-explicitly-overridden");
        var services = new ServiceCollection();
        var explicitAccessor = new ExplicitTenantAccessor();
        services.AddSingleton(TenantScopeEnforcementOptions.FailClosed);
        services.AddSingleton<IFundScopeTenantAccessor>(explicitAccessor);
        services.AddMarketDataServices(CompositionOptions.Minimal);
        services.AddWorkstationSharedServices();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IFundScopeTenantAccessor>().Should().BeSameAs(explicitAccessor);
        provider.GetRequiredService<TenantScopeEnforcementOptions>().Should().BeSameAs(TenantScopeEnforcementOptions.FailClosed);
    }

    [Fact]
    public void BrowserHost_PreservesAnAccessorSuppliedAfterCoreRegistration()
    {
        var services = CreateCoreServices();
        var explicitAccessor = new ExplicitTenantAccessor();
        services.AddSingleton<IFundScopeTenantAccessor>(explicitAccessor);
        services.AddWorkstationSharedServices();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IFundScopeTenantAccessor>().Should().BeSameAs(explicitAccessor);
        provider.GetServices<IFundScopeTenantAccessor>().Should().ContainSingle();
    }

    [Fact]
    public void BrowserHost_KeepsRequestAuthorityWhenCoreServicesAreAddedAfterward()
    {
        var services = new ServiceCollection();
        services.AddWorkstationSharedServices();
        services.AddMarketDataServices(CompositionOptions.Minimal);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IFundScopeTenantAccessor>().Should().BeOfType<WorkstationFundScopeTenantAccessor>();
        provider.GetServices<IFundScopeTenantAccessor>().Should().ContainSingle();
    }

    private static IServiceCollection CreateCoreServices()
        => new ServiceCollection().AddMarketDataServices(CompositionOptions.Minimal);

    private sealed class ExplicitTenantAccessor : IFundScopeTenantAccessor
    {
        public string? ResolveCallerTenant() => "explicit-host-tenant";
    }

    public void Dispose() => _environment.Dispose();
}
