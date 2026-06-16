using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Config;
using Meridian.DataIntegration.Canonicalization;
using Meridian.DataIntegration.Monitoring.DataQuality;
using Meridian.Domain.Events;
using Meridian.Storage;
using Meridian.Storage.Sinks;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Application.Composition;

[Collection("Sequential")]
public sealed class PipelineFeatureRegistrationTests : IDisposable
{
    private readonly List<string> _tempDirectories = new();
    private readonly List<string> _tempFiles = new();

    [Fact]
    public async Task Register_UsesConfiguredDataRootForQualityReportOutput()
    {
        var root = CreateTempDirectory();
        var dataRoot = Path.Combine(root, "persistent-data");
        var configPath = WriteConfig(root, new AppConfig(DataRoot: "persistent-data"));

        var services = new ServiceCollection();
        services.AddLogging();

        var options = CompositionOptions.WebDashboard with { ConfigPath = configPath };
        new ConfigurationFeatureRegistration().Register(services, options);
        new PipelineFeatureRegistration().Register(services, options);

        await using var provider = services.BuildServiceProvider();
        var monitoring = provider.GetRequiredService<DataQualityMonitoringService>();

        var outputDirectory = GetReportOutputDirectory(monitoring.ReportGenerator);
        outputDirectory.Should().Be(Path.Combine(dataRoot, "reports"));
        Directory.Exists(outputDirectory).Should().BeTrue();
    }

    [Fact]
    public async Task Register_UsesDurableStreamingBackpressurePolicyForMarketDataPipeline()
    {
        var root = CreateTempDirectory();
        var dataRoot = Path.Combine(root, "persistent-data");
        var configPath = WriteConfig(root, new AppConfig(DataRoot: "persistent-data"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new StorageOptions { RootPath = dataRoot });

        var options = CompositionOptions.WebDashboard with { ConfigPath = configPath };
        new ConfigurationFeatureRegistration().Register(services, options);
        new PipelineFeatureRegistration().Register(services, options);

        await using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<EventPipeline>();

        pipeline.QueueCapacity.Should().Be(50_000);
        pipeline.QueueFullMode.Should().Be(BoundedChannelFullMode.Wait);

        var stats = pipeline.GetStatistics();
        stats.QueueFullMode.Should().Be(BoundedChannelFullMode.Wait);
    }

    [Fact]
    public async Task Register_EnablesBoundedJsonlBatchingForMarketDataPersistence()
    {
        var root = CreateTempDirectory();
        var dataRoot = Path.Combine(root, "persistent-data");
        var configPath = WriteConfig(root, new AppConfig(DataRoot: "persistent-data"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new StorageOptions { RootPath = dataRoot });

        var options = CompositionOptions.WebDashboard with { ConfigPath = configPath };
        new ConfigurationFeatureRegistration().Register(services, options);
        new PipelineFeatureRegistration().Register(services, options);

        await using var provider = services.BuildServiceProvider();
        var sink = provider.GetRequiredService<JsonlStorageSink>();

        sink.IsBatchingEnabled.Should().BeTrue();
        sink.BatchSize.Should().Be(JsonlBatchOptions.Default.BatchSize);
        sink.FlushInterval.Should().Be(JsonlBatchOptions.Default.FlushInterval);
    }

    [Fact]
    public async Task Register_ResolvesDualPathPublisherForTradeIngress()
    {
        var root = CreateTempDirectory();
        var dataRoot = Path.Combine(root, "persistent-data");
        var configPath = WriteConfig(root, new AppConfig(DataRoot: "persistent-data"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new StorageOptions { RootPath = dataRoot });

        var options = CompositionOptions.WebDashboard with { ConfigPath = configPath };
        new ConfigurationFeatureRegistration().Register(services, options);
        new PipelineFeatureRegistration().Register(services, options);

        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IMarketEventPublisher>();
        var dualPath = provider.GetRequiredService<DualPathEventPipeline>();

        publisher.TryPublish(CreateTradeEvent("SPY", 1)).Should().BeTrue();
        dualPath.HotTradePublished.Should().Be(1);
    }

    [Fact]
    public async Task Register_WithCanonicalizationEnabled_KeepsDualPathTradeIngress()
    {
        var root = CreateTempDirectory();
        var dataRoot = Path.Combine(root, "persistent-data");
        var configPath = WriteConfig(root, new AppConfig(
            DataRoot: "persistent-data",
            Canonicalization: new CanonicalizationConfig(
                Enabled: true,
                EnableDualWrite: false)));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new StorageOptions { RootPath = dataRoot });

        var options = CompositionOptions.WebDashboard with { ConfigPath = configPath };
        new ConfigurationFeatureRegistration().Register(services, options);
        new PipelineFeatureRegistration().Register(services, options);
        new CanonicalizationFeatureRegistration().Register(services, options);
        services.AddSingleton<IEventCanonicalizer, PassthroughCanonicalizer>();

        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IMarketEventPublisher>();
        var dualPath = provider.GetRequiredService<DualPathEventPipeline>();

        publisher.TryPublish(CreateTradeEvent("SPY", 1)).Should().BeTrue();
        dualPath.HotTradePublished.Should().Be(1);
    }

    private static string GetReportOutputDirectory(DataQualityReportGenerator generator)
    {
        var field = typeof(DataQualityReportGenerator).GetField("_outputDirectory", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(generator).Should().BeOfType<string>().Subject;
    }

    private static MarketEvent CreateTradeEvent(string symbol, long sequence)
    {
        var now = DateTimeOffset.UtcNow;
        var trade = new Trade(
            Timestamp: now,
            Symbol: symbol,
            Price: 100.50m,
            Size: 100L,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: sequence);

        return MarketEvent.Trade(now, symbol, trade, sequence);
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meridian-pipeline-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    private string WriteConfig(string root, AppConfig config)
    {
        var path = Path.Combine(root, "appsettings.json");
        var json = JsonSerializer.Serialize(config, AppConfigJsonOptions.Write);
        File.WriteAllText(path, json);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        foreach (var directory in _tempDirectories)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class PassthroughCanonicalizer : IEventCanonicalizer
    {
        public MarketEvent Canonicalize(MarketEvent evt, CancellationToken ct = default) => evt;
    }
}
