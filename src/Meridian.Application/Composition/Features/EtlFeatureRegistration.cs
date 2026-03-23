using Meridian.Application.Etl;
using Meridian.Application.Pipeline;
using Meridian.Infrastructure.Etl;
using Meridian.Infrastructure.Etl.Sftp;
using Meridian.Storage.Etl;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Application.Composition.Features;

internal sealed class EtlFeatureRegistration : IServiceFeatureRegistration
{
    public IServiceCollection Register(IServiceCollection services, CompositionOptions options)
    {
        services.AddSingleton<IEtlJobDefinitionStore>(sp =>
        {
            var storageOptions = sp.GetRequiredService<Meridian.Storage.StorageOptions>();
            return new EtlJobDefinitionStore(storageOptions.RootPath);
        });
        services.AddSingleton<EtlStagingStore>(sp =>
        {
            var storageOptions = sp.GetRequiredService<Meridian.Storage.StorageOptions>();
            return new EtlStagingStore(storageOptions.RootPath);
        });
        services.AddSingleton<EtlAuditStore>(sp =>
        {
            var storageOptions = sp.GetRequiredService<Meridian.Storage.StorageOptions>();
            return new EtlAuditStore(storageOptions.RootPath);
        });
        services.AddSingleton<EtlRejectSink>(sp =>
        {
            var storageOptions = sp.GetRequiredService<Meridian.Storage.StorageOptions>();
            return new EtlRejectSink(storageOptions.RootPath);
        });
        services.AddSingleton<IPartnerSchemaRegistry, PartnerSchemaRegistry>();
        services.AddSingleton<IPartnerFileParser, CsvPartnerFileParser>();
        services.AddSingleton<EtlNormalizationService>();
        services.AddSingleton<ISftpClientFactory, SftpClientFactory>();
        services.AddSingleton<IEtlSourceReader, LocalFileSourceReader>();
        services.AddSingleton<IEtlSourceReader, SftpFileSourceReader>();
        services.AddSingleton<ISftpFilePublisher, SftpFilePublisher>();
        services.AddSingleton<IEtlExportService>(sp =>
        {
            var storageOptions = sp.GetRequiredService<Meridian.Storage.StorageOptions>();
            return new EtlExportService(storageOptions.RootPath, sp.GetServices<ISftpFilePublisher>());
        });
        services.AddSingleton<EtlJobOrchestrator>();
        services.AddSingleton<IEtlJobService, EtlJobService>();
        return services;
    }
}
