using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.DataQuality;
using Meridian.Core.Config;
using Meridian.DataIntegration.Monitoring.DataQuality;
using Meridian.Application.Pipeline;
using Meridian.DataIntegration.Canonicalization;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
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
    public async Task Register_CollectorAndBackfillWorkloads_OwnSeparateDurabilityDirectories()
    {
        var root = CreateTempDirectory();
        var dataRoot = Path.Combine(root, "persistent-data");
        var configPath = WriteConfig(root, new AppConfig(DataRoot: "persistent-data"));

        // State left by the shared pre-workload layout: one segment and one ledger.
        var legacyWal = Path.Combine(dataRoot, "_wal");
        await using (var legacy = new Meridian.Storage.Archival.WriteAheadLog(legacyWal))
        {
            await legacy.InitializeAsync();
            await legacy.AppendAsync("legacy-record", "Trade");
            await legacy.FlushAsync();
        }
        Directory.CreateDirectory(Path.Combine(dataRoot, "_dedup"));
        await File.WriteAllTextAsync(Path.Combine(dataRoot, "_dedup", "dedup_ledger.jsonl"), string.Empty);

        await using var collector = BuildPipelineProvider(dataRoot, CompositionOptions.Streaming with { ConfigPath = configPath });
        await using var backfill = BuildPipelineProvider(dataRoot, CompositionOptions.BackfillOnly with { ConfigPath = configPath });

        var collectorWal = collector.GetRequiredService<Meridian.Storage.Archival.WriteAheadLog>();
        var backfillWal = backfill.GetRequiredService<Meridian.Storage.Archival.WriteAheadLog>();
        collectorWal.WalDirectory.Should().Be(Path.Combine(dataRoot, "_wal", "collector"));
        backfillWal.WalDirectory.Should().Be(Path.Combine(dataRoot, "_wal", "backfill"));

        // Both workloads initialize against one data root at the same time.
        await backfillWal.InitializeAsync();
        await collectorWal.InitializeAsync();

        Directory.GetFiles(legacyWal, "*.wal").Should().BeEmpty("the collector adopts legacy segments");
        var collectorRecords = new List<string>();
        await foreach (var record in collectorWal.GetUncommittedRecordsAsync())
            collectorRecords.Add(record.DeserializePayload<string>()!);
        collectorRecords.Should().Equal("legacy-record");
        var backfillRecords = 0;
        await foreach (var _ in backfillWal.GetUncommittedRecordsAsync())
            backfillRecords++;
        backfillRecords.Should().Be(0, "only one workload may replay legacy segments");

        collector.GetRequiredService<PersistentDedupLedger>().LedgerPath
            .Should().Be(Path.Combine(dataRoot, "_dedup", "collector", "dedup_ledger.jsonl"));
        backfill.GetRequiredService<PersistentDedupLedger>().LedgerPath
            .Should().Be(Path.Combine(dataRoot, "_dedup", "backfill", "dedup_ledger.jsonl"));
        File.Exists(Path.Combine(dataRoot, "_dedup", "collector", "dedup_ledger.jsonl")).Should().BeTrue();
        File.Exists(Path.Combine(dataRoot, "_dedup", "dedup_ledger.jsonl")).Should().BeFalse("the collector adopted the legacy ledger");
    }

    [Fact]
    public void CompositionPresets_SelectTheExpectedPipelineWorkload()
    {
        CompositionOptions.BackfillOnly.PipelineWorkload.Should().Be(PipelineWorkload.Backfill);
        CompositionOptions.Streaming.PipelineWorkload.Should().Be(PipelineWorkload.Collector);
        CompositionOptions.Default.PipelineWorkload.Should().Be(PipelineWorkload.Collector);
        CompositionOptions.WebDashboard.PipelineWorkload.Should().Be(PipelineWorkload.Collector);
    }

    private static ServiceProvider BuildPipelineProvider(string dataRoot, CompositionOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new StorageOptions { RootPath = dataRoot });
        new ConfigurationFeatureRegistration().Register(services, options);
        new PipelineFeatureRegistration().Register(services, options);
        return services.BuildServiceProvider();
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

    [Fact]
    public async Task Register_FeedsLiveTradeIngressIntoQualityMonitoring()
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
        var monitoring = provider.GetRequiredService<DataQualityMonitoringService>();

        publisher.Should().BeOfType<QualityMonitoringPublisher>();
        publisher.TryPublish(CreateTradeEvent("QQQ", 1)).Should().BeTrue();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (monitoring.GetSymbolHealth("QQQ") is null && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        monitoring.GetSymbolHealth("QQQ").Should().NotBeNull(
            "live trade ingress must reach the data-quality monitoring suite");
        monitoring.GetRealTimeMetrics().ActiveSymbols.Should().Be(1);
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

        return MarketEvent.Trade(now, symbol, trade, "TEST", sequence);
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
