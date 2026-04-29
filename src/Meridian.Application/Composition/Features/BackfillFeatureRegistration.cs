using Meridian.Application.Backfill;
using Meridian.Application.Config;
using Meridian.Application.Coordination;
using Meridian.Application.Pipeline;
using Meridian.Application.Scheduling;
using Meridian.Application.Subscriptions.Services;
using Meridian.Application.UI;
using Meridian.Infrastructure.Adapters.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        // BackfillCoordinator - uses ProviderRegistry for unified provider discovery
        services.AddSingleton<BackfillCoordinator>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var registry = sp.GetService<ProviderRegistry>();
            var factory = sp.GetService<ProviderFactory>();
            return new BackfillCoordinator(configStore, registry, factory);
        });
        services.AddSingleton<IBackfillExecutionGateway, BackfillCoordinatorExecutionGateway>();

        // SchedulingService - symbol subscription scheduling
        services.AddSingleton<SchedulingService>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            return new SchedulingService(configStore);
        });

        // Backfill execution history and schedule manager
        services.AddSingleton<BackfillExecutionHistory>();
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
        services.AddSingleton<AutoGapRemediationService>(sp =>
        {
            var gateway = sp.GetRequiredService<IBackfillExecutionGateway>();
            var history = sp.GetRequiredService<BackfillExecutionHistory>();
            var quality = sp.GetService<Monitoring.DataQuality.DataQualityMonitoringService>();
            return new AutoGapRemediationService(gateway, history, quality);
        });

        return services;
    }
}
