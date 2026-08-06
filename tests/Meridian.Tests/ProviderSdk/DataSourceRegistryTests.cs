using System.Reflection;
using System.Reflection.Emit;
using FluentAssertions;
using Meridian.Infrastructure;
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
    public void DiscoverFromAssemblies_InfrastructureAssembly_PreservesDisjointProviderFamilyCapabilities()
    {
        var registry = new DataSourceRegistry();

        registry.DiscoverFromAssemblies(typeof(NoOpMarketDataClient).Assembly);

        var alpaca = registry.Sources
            .Where(source => source.Id.Equals("alpaca", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        alpaca.Should().HaveCountGreaterThanOrEqualTo(2);
        alpaca.SelectMany(source => source.CapabilityKeys).Should().Contain([
            DataSourceCapabilityContracts.MarketDataClient,
            DataSourceCapabilityContracts.HistoricalDataProvider]);
        alpaca.SelectMany(source => source.CapabilityKeys)
            .Should().OnlyHaveUniqueItems("one provider family cannot claim a capability twice");
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
    public void DiscoverFromAssemblyWithResult_SecondScanClassifiesExactDuplicate()
    {
        var registry = new DataSourceRegistry();
        var assembly = CreateDataSourceAssembly(
            new DynamicDataSource("exact-provider", "Exact provider", DataSourceCapabilityContracts.MarketDataClient));

        var first = registry.DiscoverFromAssemblyWithResult(assembly);
        var second = registry.DiscoverFromAssemblyWithResult(assembly);

        first.Committed.Should().BeTrue();
        first.Outcomes.Should().ContainSingle(outcome =>
            outcome.Disposition == DataSourceRegistrationDisposition.Added);
        second.Committed.Should().BeTrue();
        second.Outcomes.Should().ContainSingle(outcome =>
            outcome.Disposition == DataSourceRegistrationDisposition.Duplicate);
        registry.Sources.Should().ContainSingle();
    }

    [Fact]
    public void DiscoverFromAssemblyWithResult_DisjointCapabilitiesInProviderFamilyAreAdded()
    {
        var registry = new DataSourceRegistry();
        var assembly = CreateDataSourceAssembly(
            new DynamicDataSource("family-provider", "Streaming", DataSourceCapabilityContracts.MarketDataClient),
            new DynamicDataSource("family-provider", "Historical", DataSourceCapabilityContracts.HistoricalDataProvider));

        var result = registry.DiscoverFromAssemblyWithResult(assembly);

        result.Committed.Should().BeTrue();
        result.Outcomes.Should().OnlyContain(outcome =>
            outcome.Disposition == DataSourceRegistrationDisposition.Added);
        registry.Sources.Should().HaveCount(2);
        registry.Sources.SelectMany(source => source.CapabilityKeys).Should().Contain([
            DataSourceCapabilityContracts.MarketDataClient,
            DataSourceCapabilityContracts.HistoricalDataProvider]);
    }

    [Fact]
    public void DiscoverFromAssemblyWithResult_OverlappingExistingCapabilityConflictsAndRollsBack()
    {
        var registry = new DataSourceRegistry();
        registry.DiscoverFromAssemblyWithResult(CreateDataSourceAssembly(
            new DynamicDataSource("conflict-provider", "Original", DataSourceCapabilityContracts.MarketDataClient)));
        var candidate = CreateDataSourceAssembly(
            new DynamicDataSource("conflict-provider", "Replacement", DataSourceCapabilityContracts.MarketDataClient),
            new DynamicDataSource("new-provider", "Must roll back", DataSourceCapabilityContracts.HistoricalDataProvider));

        var result = registry.DiscoverFromAssemblyWithResult(candidate);

        result.Committed.Should().BeFalse();
        result.Outcomes.Should().ContainSingle(outcome =>
            outcome.Candidate.Id == "conflict-provider"
            && outcome.Disposition == DataSourceRegistrationDisposition.Conflict);
        result.Outcomes.Should().ContainSingle(outcome =>
            outcome.Candidate.Id == "new-provider"
            && outcome.Disposition == DataSourceRegistrationDisposition.Added);
        registry.Sources.Should().ContainSingle(source => source.DisplayName == "Original");
        registry.Sources.Should().NotContain(source => source.Id == "new-provider");
    }

    [Fact]
    public void DiscoverFromAssemblyWithResult_IntraAssemblyCapabilityConflictRollsBackAllCandidates()
    {
        var registry = new DataSourceRegistry();
        var assembly = CreateDataSourceAssembly(
            new DynamicDataSource("ambiguous-provider", "First", DataSourceCapabilityContracts.SymbolSearchProvider),
            new DynamicDataSource("ambiguous-provider", "Second", DataSourceCapabilityContracts.SymbolSearchProvider),
            new DynamicDataSource("unrelated-provider", "Unrelated", DataSourceCapabilityContracts.HistoricalDataProvider));

        var result = registry.DiscoverFromAssemblyWithResult(assembly);

        result.Committed.Should().BeFalse();
        result.ConflictCount.Should().Be(2);
        result.Outcomes.Should().ContainSingle(outcome =>
            outcome.Candidate.Id == "unrelated-provider"
            && outcome.Disposition == DataSourceRegistrationDisposition.Added);
        registry.Sources.Should().BeEmpty();
    }

    [Fact]
    public void DiscoverFromAssemblies_LegacyApiThrowsTypedConflictAndDoesNotPartiallyRegisterAssembly()
    {
        var registry = new DataSourceRegistry();
        var assembly = CreateDataSourceAssembly(
            new DynamicDataSource("legacy-conflict", "First", DataSourceCapabilityContracts.CorporateActionProvider),
            new DynamicDataSource("legacy-conflict", "Second", DataSourceCapabilityContracts.CorporateActionProvider),
            new DynamicDataSource("legacy-unique", "Must roll back", DataSourceCapabilityContracts.HistoricalDataProvider));

        var act = () => registry.DiscoverFromAssemblies(assembly);

        var exception = act.Should().Throw<DataSourceRegistrationConflictException>().Which;
        exception.Result.Committed.Should().BeFalse();
        exception.Result.ConflictCount.Should().Be(2);
        registry.Sources.Should().BeEmpty();
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

    private static Assembly CreateDataSourceAssembly(params DynamicDataSource[] definitions)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"Meridian.ProviderDiscovery.Tests.{Guid.NewGuid():N}"),
            AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("providers");
        var capabilityInterfaces = definitions
            .Select(static definition => definition.CapabilityKey)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                static capability => capability,
                capability => module
                    .DefineType(
                        capability,
                        TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract)
                    .CreateType()!,
                StringComparer.Ordinal);
        var attributeConstructor = typeof(DataSourceAttribute).GetConstructor([
            typeof(string),
            typeof(string),
            typeof(DataSourceType),
            typeof(DataSourceCategory)])!;

        foreach (var definition in definitions)
        {
            var type = module.DefineType(
                $"DynamicProviders.Provider_{Guid.NewGuid():N}",
                TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed);
            type.AddInterfaceImplementation(capabilityInterfaces[definition.CapabilityKey]);
            type.DefineDefaultConstructor(MethodAttributes.Public);
            type.SetCustomAttribute(new CustomAttributeBuilder(
                attributeConstructor,
                [definition.Id, definition.DisplayName, DataSourceType.Hybrid, DataSourceCategory.Aggregator]));
            type.CreateType();
        }

        return assembly;
    }

    private sealed record DynamicDataSource(
        string Id,
        string DisplayName,
        string CapabilityKey);

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

    [Fact]
    public void GetRegistrationReport_ReturnsImmutablePointInTimeSnapshotWithCumulativeCounts()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
        var registry = new DataSourceRegistry(timeProvider: timeProvider);
        registry.ConfigureModule("ds-registry-requires-config", new ProviderModuleContext { Enabled = true });
        var services = new ServiceCollection();

        registry.RegisterModules(services, typeof(DataSourceRegistryTests).Assembly);
        var first = registry.GetRegistrationReport();
        timeProvider.Advance(TimeSpan.FromMinutes(5));
        registry.RegisterModules(new ServiceCollection(), typeof(DataSourceRegistryTests).Assembly);
        var second = registry.GetRegistrationReport();

        first.GeneratedAt.Should().Be(new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
        second.GeneratedAt.Should().Be(first.GeneratedAt.AddMinutes(5));
        first.ModuleCandidateCount.Should().BeGreaterThan(0);
        first.ModuleActivationAttemptCount.Should().BeGreaterThan(0);
        first.ModuleRegistrationAttemptCount.Should().BeGreaterThan(0);
        first.RegisteredModuleCount.Should().BeGreaterThan(0);
        second.ModuleCandidateCount.Should().Be(first.ModuleCandidateCount * 2);
        first.Failures.Should().NotBeSameAs(second.Failures);
        ((IList<DataSourceDiscoveryFailure>)first.Failures).IsReadOnly.Should().BeTrue();
    }

    #endregion
}

internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan duration) => _utcNow += duration;
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
