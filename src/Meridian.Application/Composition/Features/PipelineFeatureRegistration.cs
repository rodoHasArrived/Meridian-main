using Meridian.Application.Canonicalization;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Application.Monitoring;
using Meridian.Application.Monitoring.DataQuality;
using Meridian.Application.Pipeline;
using Meridian.Application.UI;
using Meridian.Domain.Events;
using Meridian.Storage;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Policies;
using Meridian.Storage.Sinks;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Meridian.Application.Composition.Features;

/// <summary>
/// Registers event pipeline and storage sink services.
/// </summary>
internal sealed class PipelineFeatureRegistration : IServiceFeatureRegistration
{
    public IServiceCollection Register(IServiceCollection services, CompositionOptions options)
    {
        // IEventMetrics - injectable metrics for pipeline and publisher.
        if (options.EnableOpenTelemetry)
        {
            services.AddSingleton<IEventMetrics>(sp =>
                new Tracing.TracedEventMetrics(new DefaultEventMetrics()));
        }
        else
        {
            services.AddSingleton<IEventMetrics, DefaultEventMetrics>();
        }

        // IReconnectionMetrics - injectable metrics for WebSocket reconnection tracking.
        services.AddSingleton<IReconnectionMetrics, PrometheusReconnectionMetrics>();

        // DataQualityMonitoringService - orchestrates all quality monitoring components
        services.AddSingleton<DataQualityMonitoringService>(sp =>
        {
            var eventMetrics = sp.GetRequiredService<IEventMetrics>();
            return new DataQualityMonitoringService(eventMetrics: eventMetrics);
        });

        // DataFreshnessSlaMonitor - monitors data freshness SLA compliance
        services.AddSingleton<DataFreshnessSlaMonitor>();

        // JsonlStoragePolicy - controls file path generation
        services.AddSingleton<JsonlStoragePolicy>(sp =>
        {
            var storageOptions = sp.GetRequiredService<StorageOptions>();
            return new JsonlStoragePolicy(storageOptions);
        });

        // JsonlStorageSink - writes events to JSONL files (always registered)
        services.AddSingleton<JsonlStorageSink>(sp =>
        {
            var storageOptions = sp.GetRequiredService<StorageOptions>();
            var policy = sp.GetRequiredService<JsonlStoragePolicy>();
            return new JsonlStorageSink(storageOptions, policy);
        });

        // ParquetStorageSink - writes events to Parquet files (optional)
        services.AddSingleton<ParquetStorageSink>(sp =>
        {
            var storageOptions = sp.GetRequiredService<StorageOptions>();
            return new ParquetStorageSink(storageOptions);
        });

        // StorageSinkRegistry - discovers storage sink plugins decorated with [StorageSink]
        services.AddSingleton<StorageSinkRegistry>(sp =>
        {
            var registry = new StorageSinkRegistry();
            registry.DiscoverFromAssemblies(typeof(JsonlStorageSink).Assembly);
            return registry;
        });

        // IStorageSink - dynamically composed from the configured ActiveSinks list when set;
        // falls back to the legacy EnableParquetSink flag for backward compatibility.
        services.AddSingleton<IStorageSink>(sp =>
        {
            var storageOptions = sp.GetRequiredService<StorageOptions>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<CompositeSink>>();

            // New plugin-based path: build composite from the configured sink list.
            if (storageOptions.ActiveSinks is { Count: > 0 })
            {
                var registry = sp.GetRequiredService<StorageSinkRegistry>();
                var sinks = BuildSinksFromRegistry(storageOptions.ActiveSinks, registry, sp);
                return sinks.Count == 1 ? sinks[0] : new CompositeSink(sinks, logger);
            }

            // Legacy path: EnableParquetSink flag (retained for backward compatibility).
            var jsonlSink = sp.GetRequiredService<JsonlStorageSink>();
            if (storageOptions.EnableParquetSink)
            {
                var parquetSink = sp.GetRequiredService<ParquetStorageSink>();
                return new CompositeSink(new IStorageSink[] { jsonlSink, parquetSink }, logger);
            }

            return jsonlSink;
        });

        // WriteAheadLog - crash-safe durability for the event pipeline
        services.AddSingleton<Storage.Archival.WriteAheadLog>(sp =>
        {
            var storageOptions = sp.GetRequiredService<StorageOptions>();
            var walDir = Path.Combine(storageOptions.RootPath, "_wal");
            return new Storage.Archival.WriteAheadLog(walDir, new Storage.Archival.WalOptions
            {
                SyncMode = Storage.Archival.WalSyncMode.BatchedSync,
                SyncBatchSize = 1000,
                MaxFlushDelay = TimeSpan.FromSeconds(1)
            });
        });

        // DroppedEventAuditTrail - records events dropped due to backpressure
        services.AddSingleton<Pipeline.DroppedEventAuditTrail>(sp =>
        {
            var storageOptions = sp.GetRequiredService<StorageOptions>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<Pipeline.DroppedEventAuditTrail>>();
            return new Pipeline.DroppedEventAuditTrail(storageOptions.RootPath, logger);
        });

        // DeadLetterSink - persists validation-rejected events to a separate JSONL file
        services.AddSingleton<Pipeline.DeadLetterSink>(sp =>
        {
            var storageOptions = sp.GetRequiredService<StorageOptions>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<Pipeline.DeadLetterSink>>();
            return new Pipeline.DeadLetterSink(storageOptions.RootPath, logger);
        });

        // FSharpEventValidator - optional F# validation stage
        services.AddSingleton<Pipeline.FSharpEventValidator>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            var validationConfig = config.Validation;
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<Pipeline.FSharpEventValidator>>();
            return new Pipeline.FSharpEventValidator(
                symbolConfigs: config.Symbols,
                useRealTimeMode: validationConfig?.UseRealTimeMode ?? false,
                logger: logger);
        });

        // EventPipeline - bounded channel event routing with WAL for durability.
        services.AddSingleton<EventPipeline>(sp =>
        {
            var sink = sp.GetRequiredService<IStorageSink>();
            var metrics = sp.GetRequiredService<IEventMetrics>();
            var wal = sp.GetService<Storage.Archival.WriteAheadLog>();
            var auditTrail = sp.GetService<Pipeline.DroppedEventAuditTrail>();

            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            Pipeline.IEventValidator? validator = null;
            Pipeline.DeadLetterSink? deadLetterSink = null;

            if (config.Validation is { Enabled: true })
            {
                validator = sp.GetRequiredService<Pipeline.FSharpEventValidator>();
                deadLetterSink = sp.GetRequiredService<Pipeline.DeadLetterSink>();

                Log.ForContext<EventPipeline>().Information(
                    "F# validation pipeline enabled (realTimeMode={RealTimeMode})",
                    config.Validation.UseRealTimeMode);
            }

            return new EventPipeline(
                sink,
                EventPipelinePolicy.HighThroughput,
                metrics: metrics,
                wal: wal,
                auditTrail: auditTrail,
                validator: validator,
                deadLetterSink: deadLetterSink,
                consumerCount: wal is null && validator is null && Environment.ProcessorCount > 2 ? 2 : 1);
        });

        // IMarketEventPublisher - facade for publishing events.
        services.AddSingleton<IMarketEventPublisher>(sp =>
        {
            // Check if canonicalization should wrap the publisher
            var canonPublisher = sp.GetService<CanonicalizingPublisher>();
            if (canonPublisher is not null)
            {
                var configStore = sp.GetRequiredService<ConfigStore>();
                var config = configStore.Load();
                if (config.Canonicalization is { Enabled: true })
                    return canonPublisher;
            }

            var pipeline = sp.GetRequiredService<EventPipeline>();
            var metrics = sp.GetRequiredService<IEventMetrics>();
            IMarketEventPublisher publisher = new PipelinePublisher(pipeline, metrics);

            var pipelineConfigStore = sp.GetRequiredService<ConfigStore>();
            var pipelineConfig = pipelineConfigStore.Load();

            if (pipelineConfig.Canonicalization is { Enabled: true })
            {
                var canonConfig = pipelineConfig.Canonicalization;
                var symbolRegistry = sp.GetService<Contracts.Catalog.ICanonicalSymbolRegistry>();
                if (symbolRegistry is null)
                {
                    Log.ForContext<EventPipeline>().Warning(
                        "Canonicalization enabled but ICanonicalSymbolRegistry not registered; skipping");
                    return publisher;
                }
                var conditionsPath = canonConfig.ConditionCodesPath
                    ?? Path.Combine(AppContext.BaseDirectory, "config", "condition-codes.json");
                var conditions = ConditionCodeMapper.LoadFromFile(conditionsPath);
                var venuesPath = canonConfig.VenueMappingPath
                    ?? Path.Combine(AppContext.BaseDirectory, "config", "venue-mapping.json");
                var venues = VenueMicMapper.LoadFromFile(venuesPath);
                var canonicalizer = new EventCanonicalizer(
                    symbolRegistry, conditions, venues, (byte)canonConfig.Version);

                CanonicalizationMetrics.SetActiveVersion(canonConfig.Version);

                publisher = new CanonicalizingPublisher(
                    publisher, canonicalizer, canonConfig.PilotSymbols, canonConfig.EnableDualWrite);

                Log.ForContext<EventPipeline>().Information(
                    "Canonicalization enabled (version={Version}, pilotSymbols={PilotCount}, dualWrite={DualWrite})",
                    canonConfig.Version,
                    canonConfig.PilotSymbols?.Length ?? 0,
                    canonConfig.EnableDualWrite);
            }

            return publisher;
        });

        return services;
    }

    /// <summary>
    /// Resolves each sink ID from the <paramref name="activeIds"/> list using the
    /// <paramref name="registry"/> and the DI service provider.
    /// </summary>
    private static IReadOnlyList<IStorageSink> BuildSinksFromRegistry(
        IReadOnlyList<string> activeIds,
        StorageSinkRegistry registry,
        IServiceProvider sp)
    {
        var sinks = new List<IStorageSink>(activeIds.Count);
        foreach (var id in activeIds)
        {
            if (!registry.TryGetSink(id, out var metadata) || metadata is null)
            {
                Log.Warning(
                    "StorageSink plugin '{SinkId}' is listed in Storage.Sinks but was not found " +
                    "in the StorageSinkRegistry — ensure the assembly containing the " +
                    "[StorageSink(\"{SinkId}\")] class is scanned at startup. Skipping.",
                    id, id);
                continue;
            }

            var instance = sp.GetService(metadata.ImplementationType) as IStorageSink
                ?? (IStorageSink)ActivatorUtilities.CreateInstance(sp, metadata.ImplementationType);

            sinks.Add(instance);
        }

        return sinks;
    }
}
