using Meridian.Application.Canonicalization;
using Meridian.Application.Config;
using Meridian.Application.Monitoring;
using Meridian.Application.Pipeline;
using Meridian.Application.UI;
using Meridian.Domain.Events;
using Meridian.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Application.Composition.Features;

/// <summary>
/// Registers canonicalization services: mapping tables, the canonicalizer,
/// and the <see cref="CanonicalizingPublisher"/> decorator that wraps
/// <see cref="IMarketEventPublisher"/>.
/// </summary>
/// <remarks>
/// Must be called <b>after</b> pipeline services because
/// the canonicalizing publisher decorates the existing IMarketEventPublisher
/// registration (PipelinePublisher).
/// </remarks>
internal sealed class CanonicalizationFeatureRegistration : IServiceFeatureRegistration
{
    public IServiceCollection Register(IServiceCollection services, CompositionOptions options)
    {
        // ICanonicalizationMetrics as a DI singleton
        services.AddSingleton<ICanonicalizationMetrics>(sp =>
        {
            var instance = new DefaultCanonicalizationMetrics();
            CanonicalizationMetrics.Current = instance;
            return instance;
        });

        // Mapping tables (loaded once at startup, frozen for hot-path reads)
        services.AddSingleton<ConditionCodeMapper>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            var path = config.Canonicalization?.ConditionCodesPath ?? "config/condition-codes.json";
            return ConditionCodeMapper.LoadFromFile(path);
        });

        services.AddSingleton<VenueMicMapper>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            var path = config.Canonicalization?.VenueMappingPath ?? "config/venue-mapping.json";
            return VenueMicMapper.LoadFromFile(path);
        });

        // EventCanonicalizer - the core canonicalization logic
        services.AddSingleton<IEventCanonicalizer>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            var symbols = sp.GetRequiredService<Contracts.Catalog.ICanonicalSymbolRegistry>();
            var conditions = sp.GetRequiredService<ConditionCodeMapper>();
            var venues = sp.GetRequiredService<VenueMicMapper>();
            var version = (byte)(config.Canonicalization?.Version ?? 1);
            return new EventCanonicalizer(symbols, conditions, venues, version);
        });

        // CanonicalizingPublisher - wraps the inner IMarketEventPublisher (PipelinePublisher).
        services.AddSingleton<CanonicalizingPublisher>(sp =>
        {
            var pipeline = sp.GetRequiredService<EventPipeline>();
            var metrics = sp.GetRequiredService<IEventMetrics>();
            var innerPublisher = new PipelinePublisher(pipeline, metrics);

            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            var canonConfig = config.Canonicalization;
            var canonicalizer = sp.GetRequiredService<IEventCanonicalizer>();

            // Optional quarantine sink
            Pipeline.DeadLetterSink? quarantine = null;
            if (canonConfig?.Enabled == true)
            {
                var storageOptions = sp.GetRequiredService<StorageOptions>();
                var qLogger = sp.GetService<Microsoft.Extensions.Logging.ILogger<Pipeline.DeadLetterSink>>();
                quarantine = new Pipeline.DeadLetterSink(
                    Path.Combine(storageOptions.RootPath, "_quarantine"), qLogger);
            }

            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<CanonicalizingPublisher>>();

            return new CanonicalizingPublisher(
                innerPublisher,
                canonicalizer,
                canonConfig?.PilotSymbols,
                canonConfig?.EnableDualWrite ?? true,
                quarantine,
                logger);
        });

        return services;
    }
}
