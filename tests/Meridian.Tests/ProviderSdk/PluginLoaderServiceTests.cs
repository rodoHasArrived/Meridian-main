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

    private static PluginLoaderService CreateService()
        => new(new DataSourceRegistry(), NullLogger<PluginLoaderService>.Instance);
}
