using Meridian.Wpf.Features.Settings;
using Meridian.Wpf.Features.Settings.Shell;
using Meridian.Wpf.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Tests.Features.Settings;

public sealed class SettingsFeatureModuleTests
{
    [Fact]
    public void DescribePages_ReturnsExpectedPageTagsWithoutDuplicates()
    {
        DesktopFeatureModuleTestAssertions.AssertPageTags(
            new SettingsFeatureModule(),
            "SettingsShell",
            "Settings",
            "CredentialManagement",
            "SystemHealth",
            "Diagnostics",
            "ServiceManager",
            "AdminMaintenance",
            "EnvironmentDesigner",
            "MessagingHub",
            "NotificationCenter",
            "ActivityLog",
            "KeyboardShortcuts",
            "Help",
            "SetupWizard",
            "Workspaces",
            "WorkflowLibrary",
            "Welcome");
    }

    [Fact]
    public void DescribeWorkspace_ReturnsSettingsWorkspaceShell()
    {
        var workspace = DesktopFeatureModuleTestAssertions.AssertWorkspace(new SettingsFeatureModule(), "settings", "SettingsShell");

        workspace.ShellDefinition.ViewModelType.Should().Be(typeof(SettingsWorkspaceShellViewModel));
        workspace.ShellDefinition.StateProviderType.Should().Be(typeof(SettingsWorkspaceShellStateProvider));
        workspace.ShellDefinition.DefaultPanes.Select(static pane => pane.PageTag)
            .Should().Equal("Settings", "Diagnostics", "SystemHealth", "NotificationCenter");
    }

    [Fact]
    public void DeclareCapabilities_ReturnsStableSettingsCapabilityKeys()
    {
        DesktopFeatureModuleTestAssertions.AssertCapabilityKeys(
            new SettingsFeatureModule(),
            "desktop.settings.workspace",
            "desktop.settings.capabilities",
            "desktop.settings.workflow-library");
    }

    [Fact]
    public void Register_AddsSettingsShellServicesViewModelAndPageWithIntendedLifetimes()
    {
        var services = new ServiceCollection();

        new SettingsFeatureModule().Register(services);

        DesktopFeatureModuleTestAssertions.AssertRegistered<ISettingsWorkspaceShellSnapshotService, SettingsWorkspaceShellSnapshotService>(services, ServiceLifetime.Scoped);
        DesktopFeatureModuleTestAssertions.AssertRegistered<ISettingsWorkspaceShellPresentationService, SettingsWorkspaceShellPresentationService>(services, ServiceLifetime.Scoped);
        DesktopFeatureModuleTestAssertions.AssertRegistered<SettingsWorkspaceShellViewModel>(services, ServiceLifetime.Transient);
        DesktopFeatureModuleTestAssertions.AssertRegistered<SettingsWorkspaceShellPage>(services, ServiceLifetime.Transient);
    }

    [Theory]
    [InlineData("SettingsShell", "SettingsShell", "settings")]
    [InlineData("Preferences", "Settings", "settings")]
    [InlineData("Alerts", "NotificationCenter", "settings")]
    public void ShellRegistry_ResolvesSettingsAliasesAndRootNavigationTags(string requestedTag, string canonicalTag, string workspaceId)
    {
        DesktopFeatureModuleTestAssertions.AssertRouteResolves(requestedTag, canonicalTag, workspaceId);
    }
}
