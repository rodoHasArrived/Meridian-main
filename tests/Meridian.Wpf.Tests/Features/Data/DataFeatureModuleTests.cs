using Meridian.Wpf.Features.Data;
using Meridian.Wpf.Features.Data.Shell;
using Meridian.Wpf.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Tests.Features.Data;

public sealed class DataFeatureModuleTests
{
    [Fact]
    public void DescribePages_ReturnsExpectedPageTagsWithoutDuplicates()
    {
        DesktopFeatureModuleTestAssertions.AssertPageTags(
            new DataFeatureModule(),
            "DataShell",
            "Provider",
            "Backfill",
            "Symbols",
            "Storage",
            "DataExport",
            "DataSources",
            "ProviderHealth",
            "DataQuality",
            "SecurityMaster",
            "SymbolMapping",
            "SymbolStorage",
            "Schedules",
            "CollectionSessions",
            "PackageManager",
            "DataBrowser",
            "DataCalendar",
            "DataSampling",
            "TimeSeriesAlignment",
            "IndexSubscription",
            "Options",
            "AddProviderWizard",
            "ArchiveHealth",
            "StorageOptimization",
            "RetentionAssurance");
    }

    [Fact]
    public void DescribeWorkspace_ReturnsDataWorkspaceShell()
    {
        var workspace = DesktopFeatureModuleTestAssertions.AssertWorkspace(new DataFeatureModule(), "data", "DataShell");

        workspace.ShellDefinition.ViewModelType.Should().Be(typeof(DataWorkspaceShellViewModel));
        workspace.ShellDefinition.StateProviderType.Should().Be(typeof(DataWorkspaceShellStateProvider));
        workspace.ShellDefinition.DefaultPanes.Select(static pane => pane.PageTag)
            .Should().Equal("Provider", "Backfill", "Storage", "DataQuality");
    }

    [Fact]
    public void DeclareCapabilities_ReturnsStableDataCapabilityKeys()
    {
        DesktopFeatureModuleTestAssertions.AssertCapabilityKeys(
            new DataFeatureModule(),
            "desktop.data.workspace",
            "desktop.data.security-master",
            "desktop.data.retention");
    }

    [Fact]
    public void Register_AddsDataShellServicesViewModelAndPageWithIntendedLifetimes()
    {
        var services = new ServiceCollection();

        new DataFeatureModule().Register(services);

        DesktopFeatureModuleTestAssertions.AssertRegistered<IDataWorkspaceShellSnapshotService, DataWorkspaceShellSnapshotService>(services, ServiceLifetime.Scoped);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IDataWorkspaceShellPresentationService, DataWorkspaceShellPresentationService>(services, ServiceLifetime.Scoped);
        DesktopFeatureModuleTestAssertions.AssertRegistered<DataWorkspaceShellViewModel>(services, ServiceLifetime.Transient);
        DesktopFeatureModuleTestAssertions.AssertRegistered<DataWorkspaceShellPage>(services, ServiceLifetime.Transient);
    }

    [Theory]
    [InlineData("DataShell", "DataShell", "data")]
    [InlineData("DataOperationsShell", "DataShell", "data")]
    [InlineData("DataOperationsWorkspace", "DataShell", "data")]
    [InlineData("Providers", "Provider", "data")]
    public void ShellRegistry_ResolvesDataAliasesAndRootNavigationTags(string requestedTag, string canonicalTag, string workspaceId)
    {
        DesktopFeatureModuleTestAssertions.AssertRouteResolves(requestedTag, canonicalTag, workspaceId);
    }
}
