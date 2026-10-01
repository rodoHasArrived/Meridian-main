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

        var validation = new AppConfigValidator().Validate(new AppConfig(TenantScopeEnforcement: setting));
        validation.Errors.Should().NotContain(error => error.PropertyName == nameof(AppConfig.TenantScopeEnforcement));

        var configuredServices = new ServiceCollection().AddFundScopeTenantServices();
        configuredServices.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["TenantScopeEnforcement"] = setting }).Build());
        using var configuredProvider = configuredServices.BuildServiceProvider();
        configuredProvider.GetRequiredService<TenantScopeEnforcementOptions>().IsFailClosed.Should().Be(strict);
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

    [Theory]
    [InlineData("open", false)]
    [InlineData("boundary", false)]
    [InlineData("deploymentboundary", false)]
    [InlineData("closed", true)]
    [InlineData("strict", true)]
    [InlineData("failclosed", true)]
    [InlineData("  OPEN  ", false)]
    [InlineData("  Strict  ", true)]
    public void EnvironmentAliasesRemainCompatibleAndOverrideApplicationSetting(string setting, bool strict)
    {
        _environment.Set(TenantScopeEnforcementOptions.EnvironmentVariable, setting);
        var services = new ServiceCollection().AddFundScopeTenantServices();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["TenantScopeEnforcement"] = strict ? "deployment-boundary" : "fail-closed"
            }).Build());
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<TenantScopeEnforcementOptions>().IsFailClosed.Should().Be(strict);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("boundary")]
    [InlineData("deploymentboundary")]
    [InlineData("closed")]
    [InlineData("strict")]
    [InlineData("failclosed")]
    [InlineData("fail_closed")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("FAIL-CLOSED")]
    [InlineData(" fail-closed ")]
    [InlineData("DEPLOYMENT-BOUNDARY")]
    [InlineData(" deployment-boundary ")]
    public void ApplicationSettingRejectsAliasesAndNoncanonicalValues(string setting)
    {
        var result = new AppConfigValidator().Validate(new AppConfig(TenantScopeEnforcement: setting));
        result.Errors.Should().Contain(error => error.PropertyName == nameof(AppConfig.TenantScopeEnforcement));

        var path = Path.Combine(_directory, "appsettings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new AppConfig(TenantScopeEnforcement: setting)));
        var fileServices = new ServiceCollection().AddFundScopeTenantServices();
        fileServices.AddSingleton(new ConfigStore(path));
        using var fileProvider = fileServices.BuildServiceProvider();
        Action resolveFile = () => fileProvider.GetRequiredService<TenantScopeEnforcementOptions>();
        resolveFile.Should().Throw<ArgumentException>().WithMessage("*TenantScopeEnforcement*");

        var configuredServices = new ServiceCollection().AddFundScopeTenantServices();
        configuredServices.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["TenantScopeEnforcement"] = setting }).Build());
        using var configuredProvider = configuredServices.BuildServiceProvider();
        Action resolveConfiguration = () => configuredProvider.GetRequiredService<TenantScopeEnforcementOptions>();
        resolveConfiguration.Should().Throw<ArgumentException>().WithMessage("*TenantScopeEnforcement*");
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
