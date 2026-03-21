using Meridian.Application.Config;
using Meridian.Application.Scheduling;
using Meridian.Application.UI;
using Meridian.Storage;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Maintenance;
using Meridian.Storage.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition.Features;

/// <summary>
/// Registers archive maintenance and cleanup services.
/// </summary>
internal sealed class MaintenanceFeatureRegistration : IServiceFeatureRegistration
{
    public IServiceCollection Register(IServiceCollection services, CompositionOptions options)
    {
        // Maintenance history and schedule manager
        services.AddSingleton<MaintenanceExecutionHistory>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            return new MaintenanceExecutionHistory(config.DataRoot);
        });

        services.AddSingleton<ArchiveMaintenanceScheduleManager>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<ArchiveMaintenanceScheduleManager>();
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            var history = sp.GetRequiredService<MaintenanceExecutionHistory>();
            return new ArchiveMaintenanceScheduleManager(logger, config.DataRoot, history);
        });

        services.AddSingleton<ScheduledArchiveMaintenanceService>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<ScheduledArchiveMaintenanceService>();
            var schedManager = sp.GetRequiredService<ArchiveMaintenanceScheduleManager>();
            var fileMaint = sp.GetRequiredService<IFileMaintenanceService>();
            var tierMigration = sp.GetRequiredService<ITierMigrationService>();
            var storageOpts = sp.GetRequiredService<StorageOptions>();
            return new ScheduledArchiveMaintenanceService(logger, schedManager, fileMaint, tierMigration, storageOpts);
        });

        return services;
    }
}
