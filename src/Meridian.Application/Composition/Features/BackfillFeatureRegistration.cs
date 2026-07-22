using Meridian.Application.Backfill;
using Meridian.Core.Config;
using Meridian.Contracts.Coordination;
using Meridian.Application.Pipeline;
using Meridian.Application.Scheduling;
using Meridian.Application.Subscriptions.Services;
using Meridian.Application.UI;
using Meridian.DataIntegration.Monitoring.DataQuality;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Core.SymbolResolution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Application.Composition.Features;

/// <summary>
/// Registers backfill and scheduling services.
/// </summary>
/// <remarks>
/// Requires provider services to be registered first to ensure
/// <see cref="ProviderRegistry"/> and <see cref="ProviderFactory"/> are available.
/// </remarks>
internal sealed class BackfillFeatureRegistration : IServiceFeatureRegistration
{
    public IServiceCollection Register(IServiceCollection services, CompositionOptions options)
    {
        // The background-worker stack is created by this factory outside the normal
        // BackfillCoordinator path. Resolve it from DI so it receives the same canonical,
        // provider-scoped symbol resolver as the coordinator and ProviderFactory.
        services.AddSingleton<BackfillServiceFactory>(sp =>
            new BackfillServiceFactory(symbolResolver: sp.GetService<ISymbolResolver>()));

        // BackfillCoordinator - uses ProviderRegistry for unified provider discovery and
        // the canonical-registry symbol resolution spine when registered.
        services.AddSingleton<BackfillCoordinator>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var registry = sp.GetService<ProviderRegistry>();
            var factory = sp.GetService<ProviderFactory>();
            var symbolResolver = sp.GetService<Meridian.Infrastructure.Adapters.Core.SymbolResolution.ISymbolResolver>();
            var symbolTimelineResolver = sp.GetService<Meridian.Contracts.SecurityMaster.IHistoricalSymbolTimelineResolver>();
            return new BackfillCoordinator(
                configStore,
                registry,
                factory,
                symbolResolver: symbolResolver,
                symbolTimelineResolver: symbolTimelineResolver);
        });
        services.AddSingleton<IBackfillExecutionGateway, BackfillCoordinatorExecutionGateway>();

        // SchedulingService - symbol subscription scheduling
        services.AddSingleton<SchedulingService>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            return new SchedulingService(configStore);
        });

        // Backfill execution history and schedule manager. Persist the bounded history below the
        // resolved DataRoot so typed remediation SLA evidence survives host restarts.
        services.AddSingleton<BackfillExecutionHistory>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var dataRoot = configStore.GetDataRoot(configStore.Load());
            var historyPath = Path.Combine(dataRoot, "_status", "backfill-execution-history.json");
            return new BackfillExecutionHistory(historyPath);
        });
        services.AddSingleton<IngestionJobService>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            var persistenceDir = Path.Combine(config.DataRoot, "_ingestion_jobs");
            var ownershipService = sp.GetService<IScheduledWorkOwnershipService>();
            return new IngestionJobService(persistenceDir, ownershipService);
        });
        services.AddSingleton<BackfillScheduleManager>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<BackfillScheduleManager>();
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            var history = sp.GetRequiredService<BackfillExecutionHistory>();
            return new BackfillScheduleManager(logger, config.DataRoot, history);
        });
        services.AddSingleton<IOptions<AutoGapRemediationPolicy>>(sp =>
        {
            var config = sp.GetRequiredService<ConfigStore>().Load().Backfill?.AutoRemediation;
            return Options.Create(CreateAutoGapRemediationPolicy(config));
        });
        services.AddSingleton<AutoGapRemediationService>(sp =>
        {
            var gateway = sp.GetRequiredService<IBackfillExecutionGateway>();
            var history = sp.GetRequiredService<BackfillExecutionHistory>();
            var quality = sp.GetService<DataQualityMonitoringService>();
            var policy = sp.GetRequiredService<IOptions<AutoGapRemediationPolicy>>().Value;
            return new AutoGapRemediationService(gateway, history, quality, policy);
        });
        services.AddSingleton<IDataQualityGapRemediationService>(sp =>
            sp.GetRequiredService<AutoGapRemediationService>());

        return services;
    }

    internal static AutoGapRemediationPolicy CreateAutoGapRemediationPolicy(
        AutoGapRemediationConfig? config)
    {
        if (config is null)
            return AutoGapRemediationPolicy.Default;

        return AutoGapRemediationPolicy.Create(
            minimumGapDuration: TimeSpan.FromSeconds(Math.Max(0, config.MinimumGapDurationSeconds)),
            minimumGapSize: Math.Max(1, config.MinimumGapSize),
            symbolCooldown: TimeSpan.FromSeconds(Math.Max(0, config.SymbolCooldownSeconds)),
            providerCooldown: TimeSpan.FromSeconds(Math.Max(0, config.ProviderCooldownSeconds)),
            maxConcurrentRemediations: Math.Max(1, config.MaxConcurrentRemediations),
            defaultProvider: string.IsNullOrWhiteSpace(config.DefaultProvider)
                ? AutoGapRemediationPolicy.Default.DefaultProvider
                : config.DefaultProvider.Trim());
    }
}
