using Meridian.Wpf.Tests.Features;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Workflow.EnvironmentDesign;
using Meridian.Wpf.Features.Settings;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Tests.Features.Settings;

public sealed class SettingsFeatureServiceRegistrationTests
{
    [Fact]
    public void Register_ShouldOwnSettingsFeatureServicesWithConfiguredLifetimes()
    {
        var services = new ServiceCollection();

        new SettingsFeatureModule().Register(services);

        services.SingleDescriptor<ProviderManagementService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<AdminMaintenanceServiceBase>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<IAdminMaintenanceService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<SearchService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<SetupWizardService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<ISetupWizardStateService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<ISetupWizardNotificationSink>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<ISetupWizardNavigator>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<ISetupWizardLinkLauncher>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<EnvironmentDesignerService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<IEnvironmentDesignService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<IEnvironmentValidationService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<IEnvironmentPublishService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<IEnvironmentRuntimeProjectionService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<CredentialService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<PluginManagementPage>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<PluginManagementViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<SettingsViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<SetupWizardViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<WorkflowLibraryViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<CredentialManagementViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
    }
}
