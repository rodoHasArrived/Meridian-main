using Meridian.Wpf.Tests.Features;
using Meridian.Ui.Services;
using Meridian.Ui.Services.DataQuality;
using Meridian.Ui.Services.ProviderDiagnostics;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Features.Data;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Tests.Features.Data;

public sealed class DataFeatureServiceRegistrationTests
{
    [Fact]
    public void Register_ShouldOwnDataFeatureServicesWithConfiguredLifetimes()
    {
        var services = new ServiceCollection();

        new DataFeatureModule().Register(services);

        services.SingleDescriptor<BackfillProviderConfigService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<BackfillCheckpointService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<BackfillApiService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<CollectionSessionService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<ScheduleManagerService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<StorageService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<WorkspaceStateTokenStore>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<BatchExportSchedulerService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<ActivityFeedService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<CommandPaletteService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<SymbolManagementService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<BackfillService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<IDataQualityApiClient>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<IDataQualityPresentationService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<IDataQualityRefreshService>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<IProviderDiagnosticsApiClient>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<BackfillViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<ProviderViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<DataQualityViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<CollectionSessionViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<WatchlistViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<QualityArchivePage>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<QualityArchiveViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
    }
}
