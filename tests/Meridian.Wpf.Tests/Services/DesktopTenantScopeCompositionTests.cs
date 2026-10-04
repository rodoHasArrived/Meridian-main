using Meridian.Contracts.Tenancy;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Wpf.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static Meridian.Wpf.Tests.Services.DesktopAuthenticationSessionTests;

namespace Meridian.Wpf.Tests.Services;

[Collection("DesktopAuthenticationEnvironment")]
public sealed class DesktopTenantScopeCompositionTests : IDisposable
{
    private readonly EnvironmentVariableScope _environment = new EnvironmentVariableScope()
        .Set(TenantScopeEnforcementOptions.EnvironmentVariable, null)
        .Set("MDC_USERS", null).Set("MDC_USERNAME", null).Set("MDC_PASSWORD_HASH", null)
        .Set("MDC_DEMO_USERS", null).Set("MDC_AUTH_MODE", null);

    [Fact]
    public void DesktopDefault_UsesStrictPostureAndLiveSessionWithoutBorrowingWorkerAuthority()
    {
        using var provider = CreateServices().BuildServiceProvider();
        var posture = provider.GetRequiredService<TenantScopeEnforcementOptions>();
        var accessor = provider.GetRequiredService<IFundScopeTenantAccessor>();
        var session = provider.GetRequiredService<DesktopAuthenticationSession>();
        posture.IsFailClosed.Should().BeTrue();
        accessor.Should().BeOfType<DesktopFundScopeTenantAccessor>();

        using (FundScopeTenantAuthority.Enter("worker-tenant", "retained work item"))
        {
            accessor.ResolveCallerTenant().Should().BeNull();
            TenantReadPredicate.ShouldRejectRead(accessor.ResolveCallerTenant(), posture.Mode).Should().BeTrue();
            session.SignIn("desktop-alpha", "pw").Succeeded.Should().BeTrue();
            accessor.ResolveCallerTenant().Should().Be("company-alpha");
            provider.GetRequiredService<IWorkstationTenantContextAccessor>()
                .GetRequired().TenantId.Should().Be("company-alpha");
            session.SignOut();
            accessor.ResolveCallerTenant().Should().BeNull();
            TenantReadPredicate.ShouldRejectRead(accessor.ResolveCallerTenant(), posture.Mode).Should().BeTrue();
        }
    }

    [Fact]
    public void DesktopRegistry_UsesConfiguredPostureForMultipleCompanyAuthentication()
    {
        using var provider = CreateServices(includeSecondCompany: true).BuildServiceProvider();
        var registry = provider.GetRequiredService<UserProfileRegistry>();
        registry.ValidateDeploymentScope();
        registry.Authenticate("desktop-alpha", "pw")!.CompanyId.Should().Be("company-alpha");
        registry.Authenticate("desktop-beta", "pw")!.CompanyId.Should().Be("company-beta");
    }

    [Fact]
    public void DesktopRegistry_RefusesMultipleCompaniesUnderExplicitMigrationPosture()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["TenantScopeEnforcement"] = "deployment-boundary" }).Build();
        var services = CreateServices(includeSecondCompany: true);
        services.AddSingleton<IConfiguration>(configuration);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<TenantScopeEnforcementOptions>().IsFailClosed.Should().BeFalse();
        Action validate = () => provider.GetRequiredService<UserProfileRegistry>().ValidateDeploymentScope();
        validate.Should().Throw<InvalidOperationException>().WithMessage("*Multiple companies*");
    }

    [Fact]
    public void DesktopTenantRegistration_PreservesAnExplicitHostAccessor()
    {
        var services = new ServiceCollection();
        var explicitAccessor = Substitute.For<IFundScopeTenantAccessor>();
        services.AddSingleton(explicitAccessor);
        services.AddDesktopTenantScopeServices();
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IFundScopeTenantAccessor>().Should().BeSameAs(explicitAccessor);
    }

    private static ServiceCollection CreateServices(bool includeSecondCompany = false)
    {
        var passwordHash = PasswordHashing.HashPassword("pw");
        var accounts = new List<UserAccountConfig>
        {
            new("desktop-alpha", passwordHash, UserRole.Admin, CompanyId: "company-alpha")
        };
        if (includeSecondCompany)
            accounts.Add(new("desktop-beta", passwordHash, UserRole.Admin, CompanyId: "company-beta"));
        var accountStore = Substitute.For<IUserAccountStore>();
        accountStore.LoadAccounts().Returns(accounts);
        var services = new ServiceCollection();
        services.AddSingleton(accountStore);
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment("Production"));
        services.AddSingleton<LoginSessionService>();
        services.AddSingleton<DesktopAuthenticationSession>();
        services.AddDesktopTenantScopeServices();
        return services;
    }

    public void Dispose() => _environment.Dispose();
}
