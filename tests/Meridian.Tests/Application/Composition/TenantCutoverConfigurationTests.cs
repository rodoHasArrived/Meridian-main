using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.UI;
using Meridian.Contracts.Tenancy;
using Meridian.Core.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AuthEnvironmentScope = Meridian.Tests.Identity.EnvironmentVariableScope;

namespace Meridian.Tests.Application.Composition;

[Collection("IdentityEnvironment")]
public sealed class TenantCutoverConfigurationTests : IDisposable
{
    private readonly AuthEnvironmentScope _environment = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tenant-config-" + Guid.NewGuid().ToString("N"));

    public TenantCutoverConfigurationTests()
    {
        _environment.Set(TenantScopeEnforcementOptions.EnvironmentVariable, null);
        Directory.CreateDirectory(_directory);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("fail-closed", true)]
    [InlineData("deployment-boundary", false)]
    public void SavedAppConfig_RoundTripsSupportedSetting(string? setting, bool strict)
    {
        var path = Path.Combine(_directory, "appsettings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new AppConfig(TenantScopeEnforcement: setting)));
        var services = new ServiceCollection().AddFundScopeTenantServices();
        services.AddSingleton(new ConfigStore(path));
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<TenantScopeEnforcementOptions>().IsFailClosed.Should().Be(strict);
    }

    [Fact]
    public void EnvironmentOverridesConfiguredMigrationPosture()
    {
        _environment.Set(TenantScopeEnforcementOptions.EnvironmentVariable, "fail-closed");
        var services = new ServiceCollection().AddFundScopeTenantServices();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["TenantScopeEnforcement"] = "deployment-boundary" }).Build());
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<TenantScopeEnforcementOptions>().IsFailClosed.Should().BeTrue();
    }

    [Fact]
    public void ConfigValidationRejectsMisspelledSecurityPosture()
    {
        var result = new AppConfigValidator().Validate(new AppConfig(TenantScopeEnforcement: "fail_closed"));
        result.Errors.Should().Contain(error => error.PropertyName == nameof(AppConfig.TenantScopeEnforcement));
    }

    [Theory]
    [InlineData("{\"TenantScopeEnforcement\":\"fail_closed\"}")]
    [InlineData("{\"TenantScopeEnforcement\":false}")]
    [InlineData("{\"TenantScopeEnforcement\":")]
    public void InvalidFileCannotSilentlySelectADifferentPosture(string json)
    {
        var path = Path.Combine(_directory, "appsettings.json");
        File.WriteAllText(path, json);
        var services = new ServiceCollection().AddFundScopeTenantServices();
        services.AddSingleton(new ConfigStore(path));
        using var provider = services.BuildServiceProvider();
        Action resolve = () => provider.GetRequiredService<TenantScopeEnforcementOptions>();
        resolve.Should().Throw<Exception>();
    }

    public void Dispose()
    {
        _environment.Dispose();
        Directory.Delete(_directory, recursive: true);
    }
}
