using System.Reflection;
using FluentAssertions;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.ProviderSdk;

/// <summary>
/// Tests for ProviderModuleLoader discovery, validation, and load reporting.
/// </summary>
public sealed class ProviderModuleLoaderTests
{
    // -----------------------------------------------------------------------
    // Guard rails
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LoadFromAssembliesAsync_NullServices_ThrowsArgumentNullException()
    {
        var loader = new ProviderModuleLoader();
        var registry = new DataSourceRegistry();

        var act = async () => await loader.LoadFromAssembliesAsync(
            null!, registry, CancellationToken.None,
            typeof(ProviderModuleLoader).Assembly);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task LoadFromAssembliesAsync_NullRegistry_ThrowsArgumentNullException()
    {
        var loader = new ProviderModuleLoader();
        var services = new ServiceCollection();

        var act = async () => await loader.LoadFromAssembliesAsync(
            services, null!, CancellationToken.None,
            typeof(ProviderModuleLoader).Assembly);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task LoadFromAssembliesAsync_NoAssemblies_ThrowsArgumentException()
    {
        var loader = new ProviderModuleLoader();
        var services = new ServiceCollection();
        var registry = new DataSourceRegistry();

        var act = async () => await loader.LoadFromAssembliesAsync(
            services, registry, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("assemblies");
    }

    [Fact]
    public async Task LoadModulesAsync_NullModules_ThrowsArgumentNullException()
    {
        var loader = new ProviderModuleLoader();
        var services = new ServiceCollection();
        var registry = new DataSourceRegistry();

        var act = async () => await loader.LoadModulesAsync(
            services, registry, null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Assembly with no modules
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LoadFromAssembliesAsync_AssemblyWithNoModules_ReturnsEmptyReport()
    {
        var loader = new ProviderModuleLoader();
        var services = new ServiceCollection();
        var registry = new DataSourceRegistry();

        // System.Private.CoreLib has no IProviderModule implementations.
        var report = await loader.LoadFromAssembliesAsync(
            services, registry, CancellationToken.None,
            typeof(object).Assembly);

        report.LoadedCount.Should().Be(0);
        report.FailedCount.Should().Be(0);
        report.TotalDiscovered.Should().Be(0);
        report.AllLoaded.Should().BeTrue("no modules means 0 failures");
        report.AnyLoaded.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Explicit module list
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LoadModulesAsync_ValidModule_LoadsSuccessfully()
    {
        var loader = new ProviderModuleLoader();
        var services = new ServiceCollection();
        var registry = new DataSourceRegistry();

        var module = new StubProviderModule();

        var report = await loader.LoadModulesAsync(
            services, registry, new[] { module });

        report.LoadedCount.Should().Be(1);
        report.FailedCount.Should().Be(0);
        report.AllLoaded.Should().BeTrue();
        report.Loaded[0].ModuleId.Should().Be(module.ModuleId);
        report.Loaded[0].DisplayName.Should().Be(module.ModuleDisplayName);

        module.RegisterWasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task LoadModulesAsync_MultipleModules_AllLoaded()
    {
        var loader = new ProviderModuleLoader();
        var services = new ServiceCollection();
        var registry = new DataSourceRegistry();

        var moduleA = new StubProviderModule("provider-a", "Provider A");
        var moduleB = new StubProviderModule("provider-b", "Provider B");

        var report = await loader.LoadModulesAsync(
            services, registry, new[] { moduleA, moduleB });

        report.LoadedCount.Should().Be(2);
        report.FailedCount.Should().Be(0);
        report.TotalDiscovered.Should().Be(2);
        moduleA.RegisterWasCalled.Should().BeTrue();
        moduleB.RegisterWasCalled.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Validation failures
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LoadModulesAsync_InvalidModule_SkipsAndReportsFailure()
    {
        var loader = new ProviderModuleLoader();
        var services = new ServiceCollection();
        var registry = new DataSourceRegistry();

        var module = new FailingValidationProviderModule("Credentials not configured.");

        var report = await loader.LoadModulesAsync(
            services, registry, new[] { module });

        report.LoadedCount.Should().Be(0);
        report.FailedCount.Should().Be(1);
        report.AllLoaded.Should().BeFalse();
        report.AnyLoaded.Should().BeFalse();
        report.Failed[0].FailureReason.Should().Contain("Credentials not configured.");

        module.RegisterWasCalled.Should().BeFalse("Register should not be called after failed validation");
    }

    [Fact]
    public async Task LoadModulesAsync_MixOfValidAndInvalid_PartialReport()
    {
        var loader = new ProviderModuleLoader();
        var services = new ServiceCollection();
        var registry = new DataSourceRegistry();

        var good = new StubProviderModule("good", "Good Module");
        var bad = new FailingValidationProviderModule("Missing API key");

        var report = await loader.LoadModulesAsync(
            services, registry, new IProviderModule[] { good, bad });

        report.LoadedCount.Should().Be(1);
        report.FailedCount.Should().Be(1);
        report.AllLoaded.Should().BeFalse();
        report.AnyLoaded.Should().BeTrue();
        good.RegisterWasCalled.Should().BeTrue();
        bad.RegisterWasCalled.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Exception handling
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LoadModulesAsync_RegisterThrows_ReportsFailureWithoutBubbling()
    {
        var loader = new ProviderModuleLoader();
        var services = new ServiceCollection();
        var registry = new DataSourceRegistry();

        var module = new ThrowingRegisterProviderModule();

        var report = await loader.LoadModulesAsync(
            services, registry, new[] { module });

        report.LoadedCount.Should().Be(0);
        report.FailedCount.Should().Be(1);
        report.Failed[0].Exception.Should().NotBeNull();
        report.Failed[0].Exception.Should().BeOfType<InvalidOperationException>();
    }

    // -----------------------------------------------------------------------
    // Capability advertisement
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LoadModulesAsync_ModuleWithCapabilities_ReportsCapabilitiesInLoadedInfo()
    {
        var loader = new ProviderModuleLoader();
        var services = new ServiceCollection();
        var registry = new DataSourceRegistry();

        var caps = new[]
        {
            ProviderCapabilities.Streaming(),
            ProviderCapabilities.BackfillBarsOnly
        };

        var module = new StubProviderModule("cap-test", "Cap Test", caps);

        var report = await loader.LoadModulesAsync(
            services, registry, new[] { module });

        report.LoadedCount.Should().Be(1);
        report.Loaded[0].Capabilities.Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Cancellation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LoadModulesAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var loader = new ProviderModuleLoader();
        var services = new ServiceCollection();
        var registry = new DataSourceRegistry();
        var module = new StubProviderModule();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await loader.LoadModulesAsync(
            services, registry, new[] { module }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // -----------------------------------------------------------------------
    // Default interface implementations
    // -----------------------------------------------------------------------

    [Fact]
    public void IProviderModule_DefaultModuleId_DerivedFromTypeName()
    {
        // MinimalProviderModule has no overrides — exercises default interface impls.
        IProviderModule module = new MinimalProviderModule();

        // "MinimalProviderModule" → strip "ProviderModule" → "Minimal" → lower → "minimal"
        module.ModuleId.Should().Be("minimal");
    }

    [Fact]
    public void IProviderModule_DefaultModuleDisplayName_DerivedFromTypeName()
    {
        IProviderModule module = new MinimalProviderModule();

        // "MinimalProviderModule" → strip "ProviderModule" → "Minimal"
        module.ModuleDisplayName.Should().Be("Minimal");
    }

    [Fact]
    public async Task IProviderModule_DefaultValidation_ReturnsValid()
    {
        IProviderModule module = new MinimalProviderModule();

        var result = await module.ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public void IProviderModule_DefaultCapabilities_IsEmpty()
    {
        IProviderModule module = new MinimalProviderModule();

        module.Capabilities.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // ModuleValidationResult helpers
    // -----------------------------------------------------------------------

    [Fact]
    public void ModuleValidationResult_Valid_IsValid()
    {
        ModuleValidationResult.Valid.IsValid.Should().BeTrue();
        ModuleValidationResult.Valid.FailureReason.Should().BeNull();
    }

    [Fact]
    public void ModuleValidationResult_Failure_IsNotValid()
    {
        var result = ModuleValidationResult.Failure("missing key");

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("missing key");
    }
}

// -----------------------------------------------------------------------
// Test-only IProviderModule implementations
// -----------------------------------------------------------------------

internal sealed class StubProviderModule : IProviderModule
{
    private readonly string _id;
    private readonly string _displayName;
    private readonly ProviderCapabilities[] _capabilities;

    public bool RegisterWasCalled { get; private set; }

    public StubProviderModule(
        string id = "stub",
        string displayName = "Stub",
        ProviderCapabilities[]? capabilities = null)
    {
        _id = id;
        _displayName = displayName;
        _capabilities = capabilities ?? Array.Empty<ProviderCapabilities>();
    }

    public string ModuleId => _id;
    public string ModuleDisplayName => _displayName;
    public ProviderCapabilities[] Capabilities => _capabilities;

    public void Register(IServiceCollection services, DataSourceRegistry registry)
    {
        RegisterWasCalled = true;
    }
}

internal sealed class FailingValidationProviderModule : IProviderModule
{
    private readonly string _reason;

    public bool RegisterWasCalled { get; private set; }

    public FailingValidationProviderModule(string reason) => _reason = reason;

    public string ModuleId => "failing";
    public string ModuleDisplayName => "Failing Validation Module";

    public ValueTask<ModuleValidationResult> ValidateAsync(CancellationToken ct = default)
        => ValueTask.FromResult(ModuleValidationResult.Failure(_reason));

    public void Register(IServiceCollection services, DataSourceRegistry registry)
    {
        RegisterWasCalled = true;
    }
}

internal sealed class ThrowingRegisterProviderModule : IProviderModule
{
    public string ModuleId => "throwing";
    public string ModuleDisplayName => "Throwing Register Module";

    public void Register(IServiceCollection services, DataSourceRegistry registry)
        => throw new InvalidOperationException("Simulated registration failure.");
}

/// <summary>
/// Implements IProviderModule with no overrides — used to test default interface implementations.
/// </summary>
internal sealed class MinimalProviderModule : IProviderModule
{
    public void Register(IServiceCollection services, DataSourceRegistry registry) { }
}
