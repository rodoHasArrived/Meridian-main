using Meridian.Application.Pipeline;
using Meridian.Contracts.Etl;
using Meridian.DataIntegration.Etl;
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
        services.AddSingleton<EtlPreviewService>();
        services.AddSingleton<ISftpCapabilityService, SftpCapabilityService>();
        services.AddSingleton<ISftpCredentialResolver, EnvironmentSftpCredentialResolver>();
        services.AddSingleton<ISftpClientFactory, SftpClientFactory>();
        services.AddSingleton<IEtlSourceReader, LocalFileSourceReader>();
        // Both directions resolve through DI so imports and exports share one credential resolver
        // and one capability gate. Container-selected constructors would otherwise fork them: the
        // reader picked the two-argument overload and defaulted its own resolver and capability,
        // which is how the read path stayed ungated while the write path was fixed.
        services.AddSingleton<IEtlSourceReader>(sp => new SftpFileSourceReader(
            sp.GetRequiredService<EtlStagingStore>(),
            sp.GetRequiredService<ISftpClientFactory>(),
            sp.GetRequiredService<ISftpCredentialResolver>(),
            sp.GetRequiredService<ISftpCapabilityService>()));
        services.AddSingleton<ISftpFilePublisher>(sp => new SftpFilePublisher(
            sp.GetRequiredService<ISftpClientFactory>(),
            sp.GetRequiredService<ISftpCredentialResolver>(),
            sp.GetRequiredService<ISftpCapabilityService>()));
        services.AddSingleton<IEtlExportService>(sp =>
        {
            var storageOptions = sp.GetRequiredService<Meridian.Storage.StorageOptions>();
            return new EtlExportService(storageOptions.RootPath, sp.GetServices<ISftpFilePublisher>());
        });
        services.AddSingleton<IEtlIngestionJobCoordinator>(sp => sp.GetRequiredService<IngestionJobService>());
        services.AddSingleton<IEtlEventPipeline>(sp => sp.GetRequiredService<EventPipeline>());
        services.AddSingleton<EtlJobOrchestrator>();
        services.AddSingleton<IEtlJobService, EtlJobService>();
        return services;
    }
}
