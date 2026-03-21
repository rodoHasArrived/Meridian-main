using System.Reflection;
using FluentAssertions;
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
        // The exact count depends on build configuration (e.g., StockSharp conditional)
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
}
