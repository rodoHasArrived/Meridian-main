using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Contracts.Tenancy;
using Meridian.Identity;
using Meridian.Storage.Reporting;
using Meridian.Ui.Shared.Services;
using Meridian.Contracts.DirectLending;
using Meridian.Storage.Ledger;
using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Application.Composition;

public sealed class InstanceCompositionConfigurationTests
{
    [Fact]
    public void StorageComposition_TwoConfigurations_RetainIndependentConnectionsAndSchemas()
    {
        using var first = CreateStorageProvider("first", "first_schema");
        using var second = CreateStorageProvider("second", "second_schema");

        first.GetRequiredService<LedgerJournalStoreOptions>().ConnectionString
            .Should().Contain("Database=first");
        second.GetRequiredService<LedgerJournalStoreOptions>().ConnectionString
            .Should().Contain("Database=second");
        first.GetRequiredService<SecurityMasterOptions>().Schema.Should().Be("first_schema");
        second.GetRequiredService<SecurityMasterOptions>().Schema.Should().Be("second_schema");
        first.GetRequiredService<DirectLendingOptions>().Schema.Should().Be("first_schema");
        second.GetRequiredService<DirectLendingOptions>().Schema.Should().Be("second_schema");
    }

    [Fact]
    public void ExplicitConfiguration_MissingKeys_DoNotConsultTheProcessEnvironment()
    {
        var configuration = new CompositionConfiguration(new ConfigurationBuilder().Build());

        configuration.UsesEnvironment.Should().BeFalse();
        configuration["PATH"].Should().BeNull();
        configuration.GetConnectionString("MERIDIAN_LEDGER_CONNECTION_STRING").Should().BeNull();
    }

    [Fact]
    public void WorkstationComposition_ReusesExplicitSettingsRegisteredByTheCoreHost()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DOTNET_ENVIRONMENT"] = "Test",
            ["MDC_API_KEY"] = "host-owned-key",
            ["MERIDIAN_REPORTING_CONNECTION_STRING"] = "Host=localhost;Database=host_owned",
            ["MERIDIAN_REPORTING_SCHEMA"] = "host_owned_schema"
        }).Build();
        var services = new ServiceCollection();
        var hostSettings = new CompositionConfiguration(configuration);
        services.AddSingleton(hostSettings);

        services.AddWorkstationSharedServices();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<CompositionConfiguration>().Should().BeSameAs(hostSettings);
        provider.GetRequiredService<AuthenticationConfiguration>()["MDC_API_KEY"].Should().Be("host-owned-key");
        provider.GetRequiredService<ReportingArtifactStoreOptions>().Schema.Should().Be("host_owned_schema");
    }

    [Fact]
    public void ExplicitProductionConfiguration_StillRejectsInMemoryGovernance()
    {
        var services = new ServiceCollection();
        var options = CompositionOptions.Minimal with
        {
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DOTNET_ENVIRONMENT"] = "Production",
                ["MERIDIAN_USE_INMEMORY_GOVERNANCE"] = "true"
            }).Build()
        };

        var compose = () => new StorageFeatureRegistration().Register(services, options);

        compose.Should().Throw<InvalidOperationException>().WithMessage("*forbidden in Production*");
    }

    private static ServiceProvider CreateStorageProvider(string database, string schema)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DOTNET_ENVIRONMENT"] = "Test",
            ["MERIDIAN_USE_INMEMORY_GOVERNANCE"] = "true",
            ["MERIDIAN_DATABASE_URL"] = $"postgres://test@localhost/{database}",
            ["MERIDIAN_SECURITY_MASTER_SCHEMA"] = schema,
            ["MERIDIAN_LEDGER_SCHEMA"] = schema
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton(TenantScopeEnforcementOptions.FailClosed);
        new StorageFeatureRegistration().Register(services, CompositionOptions.Minimal with
        {
            Configuration = configuration
        });
        return services.BuildServiceProvider();
    }
}
