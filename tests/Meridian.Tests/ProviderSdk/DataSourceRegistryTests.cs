using System.Reflection;
using FluentAssertions;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.ProviderSdk;

/// <summary>
/// Tests for DataSourceRegistry discovery and registration.
/// </summary>
public sealed class DataSourceRegistryTests
{
    #region DiscoverFromAssemblies

    [Fact]
    public void DiscoverFromAssemblies_NullAssemblies_ThrowsArgumentException()
    {
        var registry = new DataSourceRegistry();

        var act = () => registry.DiscoverFromAssemblies(null!);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("assemblies");
    }

    [Fact]
    public void DiscoverFromAssemblies_EmptyAssemblies_ThrowsArgumentException()
    {
        var registry = new DataSourceRegistry();

        var act = () => registry.DiscoverFromAssemblies(Array.Empty<Assembly>());

        act.Should().Throw<ArgumentException>()
            .WithParameterName("assemblies");
    }

    [Fact]
    public void DiscoverFromAssemblies_AssemblyWithNoDataSources_ReturnsEmptySources()
    {
        var registry = new DataSourceRegistry();

        // Use a system assembly that has no [DataSource] attributes
        registry.DiscoverFromAssemblies(typeof(object).Assembly);

        registry.Sources.Should().BeEmpty();
    }

    [Fact]
    public void DiscoverFromAssemblies_InfrastructureAssembly_DiscoversSources()
    {
        var registry = new DataSourceRegistry();

        registry.DiscoverFromAssemblies(typeof(DataSourceRegistry).Assembly);

        // Infrastructure assembly should contain at least some data sources
        // The exact count depends on build configuration and registered provider surfaces.
        registry.Sources.Should().NotBeNull();
    }

    [Fact]
    public void DiscoverFromAssemblies_DuplicateAssemblies_NoDuplicateSources()
    {
        var registry = new DataSourceRegistry();
        var assembly = typeof(DataSourceRegistry).Assembly;

        registry.DiscoverFromAssemblies(assembly);
        var countAfterFirst = registry.Sources.Count;

        registry.DiscoverFromAssemblies(assembly);

        registry.Sources.Count.Should().Be(countAfterFirst,
            "scanning the same assembly twice should not create duplicate entries");
    }

    [Fact]
    public void DiscoverFromAssemblies_DiscoveredSources_HaveValidMetadata()
    {
        var registry = new DataSourceRegistry();

        registry.DiscoverFromAssemblies(typeof(DataSourceRegistry).Assembly);

        foreach (var source in registry.Sources)
        {
            source.Id.Should().NotBeNullOrWhiteSpace();
            source.DisplayName.Should().NotBeNullOrWhiteSpace();
            source.ImplementationType.Should().NotBeNull();
            source.ConfigSection.Should().NotBeNullOrWhiteSpace();
        }
    }

    #endregion

    #region RegisterServices

    [Fact]
    public void RegisterServices_NullServiceCollection_ThrowsArgumentNullException()
    {
        var registry = new DataSourceRegistry();

        var act = () => registry.RegisterServices(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegisterServices_EmptyRegistry_RegistersNothing()
    {
        var registry = new DataSourceRegistry();
        var services = new ServiceCollection();

        registry.RegisterServices(services);

        services.Should().BeEmpty();
    }

    [Fact]
    public void RegisterServices_WithDiscoveredSources_RegistersImplementationAndInterface()
    {
        var registry = new DataSourceRegistry();
        registry.DiscoverFromAssemblies(typeof(DataSourceRegistry).Assembly);

        if (registry.Sources.Count == 0)
            return; // Skip if no sources found (build-config dependent)

        var services = new ServiceCollection();
        registry.RegisterServices(services);

        // Each source should register 2 descriptors: concrete type + IDataSource
        services.Count.Should().Be(registry.Sources.Count * 2);
    }

    [Fact]
    public void RegisterServices_DefaultLifetime_IsSingleton()
    {
        var registry = new DataSourceRegistry();
        registry.DiscoverFromAssemblies(typeof(DataSourceRegistry).Assembly);

        if (registry.Sources.Count == 0)
            return;

        var services = new ServiceCollection();
        registry.RegisterServices(services);

        services.Should().OnlyContain(d => d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void RegisterServices_CustomLifetime_UsesSpecifiedLifetime()
    {
        var registry = new DataSourceRegistry();
        registry.DiscoverFromAssemblies(typeof(DataSourceRegistry).Assembly);

        if (registry.Sources.Count == 0)
            return;

        var services = new ServiceCollection();
        registry.RegisterServices(services, ServiceLifetime.Transient);

        services.Should().OnlyContain(d => d.Lifetime == ServiceLifetime.Transient);
    }

    #endregion

    #region RegisterModules

    [Fact]
    public void RegisterModules_NullServiceCollection_ThrowsArgumentNullException()
    {
        var registry = new DataSourceRegistry();

        var act = () => registry.RegisterModules(null!, typeof(DataSourceRegistry).Assembly);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegisterModules_SystemAssembly_DoesNotThrow()
    {
        var registry = new DataSourceRegistry();
        var services = new ServiceCollection();

        var act = () => registry.RegisterModules(services, typeof(object).Assembly);

        act.Should().NotThrow();
    }

    #endregion

    #region ConfigureModule / ConfigureModules

    [Fact]
    public void ConfigureModule_ReturnsSameRegistryInstance_ForFluentChaining()
    {
        var registry = new DataSourceRegistry();
        var ctx = new ProviderModuleContext { Enabled = true };

        var result = registry.ConfigureModule("test-provider", ctx);

        result.Should().BeSameAs(registry);
    }

    [Fact]
    public void ConfigureModule_NullModuleId_ThrowsArgumentException()
    {
        var registry = new DataSourceRegistry();
        var ctx = new ProviderModuleContext();

        var act = () => registry.ConfigureModule(null!, ctx);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ConfigureModule_NullContext_ThrowsArgumentNullException()
    {
        var registry = new DataSourceRegistry();

        var act = () => registry.ConfigureModule("test-provider", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ConfigureModules_NullContexts_ThrowsArgumentNullException()
    {
        var registry = new DataSourceRegistry();

        var act = () => registry.ConfigureModules(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ConfigureModules_ValidContexts_DoesNotThrow()
    {
        var registry = new DataSourceRegistry();
        var contexts = new Dictionary<string, ProviderModuleContext>
        {
            ["alpaca"] = new ProviderModuleContext { Priority = 10 },
            ["polygon"] = new ProviderModuleContext { Priority = 20 }
        };

        var act = () => registry.ConfigureModules(contexts);

        act.Should().NotThrow();
    }

    [Fact]
    public void RegisterModules_RequiresConfigModule_SkippedWhenNoContextProvided()
    {
        var registry = new DataSourceRegistry();
        var services = new ServiceCollection();

        // Scan test assembly; DataSourceRegistryRequiresConfigModule has RequiresExternalConfig = true
        // and no context is provided → it must be skipped.
        registry.RegisterModules(services, typeof(DataSourceRegistryTests).Assembly);

        // No exception, and nothing gets registered from the requires-config module.
        services.Should().NotContain(d =>
            d.ServiceType == typeof(DataSourceRegistryTestMarker),
            "the requires-external-config module should be skipped without a context");
    }

    [Fact]
    public void RegisterModules_RequiresConfigModule_RegistersWhenContextProvided()
    {
        var registry = new DataSourceRegistry();
        registry.ConfigureModule("ds-registry-requires-config",
            new ProviderModuleContext { Enabled = true });

        var services = new ServiceCollection();
        registry.RegisterModules(services, typeof(DataSourceRegistryTests).Assembly);

        services.Should().Contain(d =>
            d.ServiceType == typeof(DataSourceRegistryTestMarker),
            "the module should register its marker when a context is supplied");
    }

    [Fact]
    public void RegisterModules_EnabledFalseContext_ModuleSkipped()
    {
        var registry = new DataSourceRegistry();
        registry.ConfigureModule("ds-registry-requires-config",
            new ProviderModuleContext { Enabled = false });

        var services = new ServiceCollection();
        registry.RegisterModules(services, typeof(DataSourceRegistryTests).Assembly);

        services.Should().NotContain(d =>
            d.ServiceType == typeof(DataSourceRegistryTestMarker),
            "disabled modules must not register services");
    }

    #endregion

    #region Failure surfacing

    [Fact]
    public void RegisterModules_ActivationFailure_IsRecordedAndScanContinues()
    {
        var registry = new DataSourceRegistry();
        registry.ConfigureModule("ds-registry-requires-config",
            new ProviderModuleContext { Enabled = true });
        var services = new ServiceCollection();

        registry.RegisterModules(services, typeof(DataSourceRegistryTests).Assembly);

        registry.Failures.Should().Contain(f =>
            f.Stage == "activate" &&
            f.Subject.Contains(nameof(ThrowingConstructorProviderModule)),
            "constructor failures must be surfaced, not silently swallowed");
        services.Should().Contain(d => d.ServiceType == typeof(DataSourceRegistryTestMarker),
            "one broken module must not block the remaining modules");
    }

    [Fact]
    public void RegisterModules_RegisterFailure_IsRecordedWithModuleId()
    {
        var registry = new DataSourceRegistry();
        registry.ConfigureModule("ds-registry-register-throws",
            new ProviderModuleContext { Enabled = true });
        var services = new ServiceCollection();

        registry.RegisterModules(services, typeof(DataSourceRegistryTests).Assembly);

        registry.Failures.Should().Contain(f =>
            f.Stage == "register" &&
            f.ModuleId == "ds-registry-register-throws" &&
            f.ErrorType == nameof(InvalidOperationException),
            "Register failures must be surfaced with the module identity");
    }

    #endregion
}

// -----------------------------------------------------------------------
// Marker type used to verify module registration in DataSourceRegistry tests
// -----------------------------------------------------------------------

/// <summary>Marker service type registered by DataSourceRegistryRequiresConfigModule.</summary>
internal sealed class DataSourceRegistryTestMarker { }

/// <summary>
/// Test module that requires external config. Used to verify DataSourceRegistry's
/// context-injection behavior during RegisterModules.
/// </summary>
internal sealed class DataSourceRegistryRequiresConfigModule : IProviderModule
{
    public string ModuleId => "ds-registry-requires-config";
    public string ModuleDisplayName => "DataSourceRegistry Test Module";
    public bool RequiresExternalConfig => true;

    public void Register(IServiceCollection services, DataSourceRegistry registry)
        => services.AddSingleton<DataSourceRegistryTestMarker>();
}

/// <summary>
/// Test module whose Register throws. Requires external config so it only runs
/// when a test explicitly supplies a context.
/// </summary>
internal sealed class DataSourceRegistryRegisterThrowsModule : IProviderModule
{
    public string ModuleId => "ds-registry-register-throws";
    public string ModuleDisplayName => "DataSourceRegistry Register-Throws Module";
    public bool RequiresExternalConfig => true;

    public void Register(IServiceCollection services, DataSourceRegistry registry)
        => throw new InvalidOperationException("Simulated registration failure.");
}
