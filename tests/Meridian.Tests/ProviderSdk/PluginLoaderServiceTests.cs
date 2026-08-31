using System.Reflection;
using System.Reflection.Emit;
using FluentAssertions;
using Meridian.Infrastructure.DataSources;
using Meridian.ProviderSdk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.ProviderSdk;

public sealed class PluginLoaderServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        $"meridian-plugin-loader-tests-{Guid.NewGuid():N}");

    public PluginLoaderServiceTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public async Task LoadPluginsAsync_MissingDirectory_ReturnsEmptyResults()
    {
        var service = CreateService();
        var missingDirectory = Path.Combine(_tempRoot, "missing");

        var results = await service.LoadPluginsAsync(missingDirectory, CancellationToken.None);

        results.Should().BeEmpty();
        service.LoadedPlugins.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadPluginsAsync_EmptyDirectory_ReturnsEmptyResults()
    {
        var service = CreateService();
        var pluginsDirectory = Path.Combine(_tempRoot, "plugins");
        Directory.CreateDirectory(pluginsDirectory);

        var results = await service.LoadPluginsAsync(pluginsDirectory, CancellationToken.None);

        results.Should().BeEmpty();
        service.LoadedPlugins.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadPluginsAsync_InvalidDll_ReturnsFailureResult()
    {
        var service = CreateService();
        var pluginsDirectory = Path.Combine(_tempRoot, "plugins");
        Directory.CreateDirectory(pluginsDirectory);
        var invalidAssembly = Path.Combine(pluginsDirectory, "not-a-plugin.dll");
        await File.WriteAllTextAsync(invalidAssembly, "not a managed assembly");

        var results = await service.LoadPluginsAsync(pluginsDirectory, CancellationToken.None);

        results.Should().ContainSingle();
        var result = results.Single();
        result.AssemblyPath.Should().Be(invalidAssembly);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        result.RegisteredTypes.Should().BeEmpty();
        service.LoadedPlugins.Should().BeEquivalentTo(results);
    }

    [Fact]
    public async Task LoadPluginsAsync_CanceledBeforeDllScan_ThrowsOperationCanceledException()
    {
        var service = CreateService();
        var pluginsDirectory = Path.Combine(_tempRoot, "plugins");
        Directory.CreateDirectory(pluginsDirectory);
        await File.WriteAllTextAsync(Path.Combine(pluginsDirectory, "candidate.dll"), "not a managed assembly");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await service.LoadPluginsAsync(pluginsDirectory, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        service.LoadedPlugins.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadPluginsAsync_ExactDuplicateReportsDuplicateWithoutClaimingRegistration()
    {
        var pluginsDirectory = PrepareTestPlugin();
        var pluginPath = Directory.GetFiles(pluginsDirectory, "*.dll").Single();
        var pluginAssembly = Assembly.LoadFrom(pluginPath);
        var registry = new DataSourceRegistry();
        registry.DiscoverFromAssemblies(pluginAssembly);
        var service = CreateService(registry);

        var results = await service.LoadPluginsAsync(pluginsDirectory, CancellationToken.None);

        var result = results.Should().ContainSingle().Which;
        result.Success.Should().BeTrue();
        result.RegisteredTypes.Should().BeEmpty();
        result.Registration.Should().NotBeNull();
        result.Registration!.Committed.Should().BeTrue();
        result.Registration.Outcomes.Should().ContainSingle(outcome =>
            outcome.Candidate.Id == PluginLoaderTestDataSource.ProviderId
            && outcome.Disposition == DataSourceRegistrationDisposition.Duplicate);
        registry.Sources.Count(source => source.Id == PluginLoaderTestDataSource.ProviderId)
            .Should().Be(1);
    }

    [Fact]
    public async Task LoadPluginsAsync_ConflictingImplementationFailsClosedAndReportsRollback()
    {
        var pluginsDirectory = PrepareTestPlugin();
        var registry = new DataSourceRegistry();
        registry.DiscoverFromAssemblies(CreateConflictingPluginAssembly());
        var service = CreateService(registry);

        var results = await service.LoadPluginsAsync(pluginsDirectory, CancellationToken.None);

        var result = results.Should().ContainSingle().Which;
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Registry conflict");
        result.RegisteredTypes.Should().BeEmpty();
        result.Registration.Should().NotBeNull();
        result.Registration!.Committed.Should().BeFalse();
        result.Registration.Outcomes.Should().ContainSingle(outcome =>
            outcome.Candidate.Id == PluginLoaderTestDataSource.ProviderId
            && outcome.Disposition == DataSourceRegistrationDisposition.Conflict);
        registry.Sources.Should().ContainSingle(source =>
            source.Id == PluginLoaderTestDataSource.ProviderId
            && source.DisplayName == "Existing implementation");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private string PrepareTestPlugin()
    {
        var pluginsDirectory = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pluginsDirectory);
        File.Copy(
            typeof(PluginLoaderTestDataSource).Assembly.Location,
            Path.Combine(pluginsDirectory, "provider-plugin.dll"));
        return pluginsDirectory;
    }

    private static Assembly CreateConflictingPluginAssembly()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"Meridian.PluginConflict.Tests.{Guid.NewGuid():N}"),
            AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("plugin");
        var type = module.DefineType(
            $"PluginConflict.Existing_{Guid.NewGuid():N}",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed,
            typeof(PluginLoaderTestDataSourceBase));
        type.DefineDefaultConstructor(MethodAttributes.Public);
        var attributeConstructor = typeof(DataSourceAttribute).GetConstructor([
            typeof(string),
            typeof(string),
            typeof(DataSourceType),
            typeof(DataSourceCategory)])!;
        type.SetCustomAttribute(new CustomAttributeBuilder(
            attributeConstructor,
            [
                PluginLoaderTestDataSource.ProviderId,
                "Existing implementation",
                DataSourceType.Hybrid,
                DataSourceCategory.Aggregator
            ]));
        type.CreateType();
        return assembly;
    }

    private static PluginLoaderService CreateService(DataSourceRegistry? registry = null)
        => new(registry ?? new DataSourceRegistry(), NullLogger<PluginLoaderService>.Instance);
}

public class PluginLoaderTestDataSourceBase : IDataSource
{
    public string Id => PluginLoaderTestDataSource.ProviderId;
    public string DisplayName => "Plugin loader test source";
    public string Description => "Discovery-only test provider";
    public DataSourceType Type => DataSourceType.Hybrid;
    public DataSourceCategory Category => DataSourceCategory.Aggregator;
    public int Priority => 100;
    public DataSourceCapabilities Capabilities => DataSourceCapabilities.None;
    public DataSourceCapabilityInfo CapabilityInfo => DataSourceCapabilityInfo.Default(Capabilities);
    public IReadOnlySet<string> SupportedMarkets =>
        System.Collections.Immutable.ImmutableHashSet<string>.Empty;
    public IReadOnlySet<AssetClass> SupportedAssetClasses =>
        System.Collections.Immutable.ImmutableHashSet<AssetClass>.Empty;
    public DataSourceHealth Health => DataSourceHealth.Healthy();
    public DataSourceStatus Status => DataSourceStatus.Uninitialized;
    public RateLimitState RateLimitState => RateLimitState.Available;
    public IObservable<DataSourceHealthChanged> HealthChanges =>
        throw new NotSupportedException("The discovery test source is never activated.");
    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> ValidateCredentialsAsync(CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> TestConnectivityAsync(CancellationToken ct = default) => Task.FromResult(true);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

[DataSource(
    ProviderId,
    "Plugin loader test source",
    DataSourceType.Hybrid,
    DataSourceCategory.Aggregator)]
public sealed class PluginLoaderTestDataSource : PluginLoaderTestDataSourceBase
{
    public const string ProviderId = "plugin-loader-test-provider";
}
