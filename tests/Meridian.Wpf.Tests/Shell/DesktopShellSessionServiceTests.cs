using System.IO;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Shell.Session;
using Meridian.Wpf.Tests.Support;

namespace Meridian.Wpf.Tests.Shell;

public sealed class DesktopShellSessionServiceTests : IDisposable
{
    private readonly string _settingsFilePath = Path.Combine(
        Path.GetTempPath(),
        "Meridian.Wpf.Tests",
        "desktop-shell-session-service-tests",
        $"{Guid.NewGuid():N}.workspace-data.json");

    public DesktopShellSessionServiceTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
        WorkspaceService.SetSettingsFilePathOverrideForTests(_settingsFilePath);
    }

    public void Dispose()
    {
        NavigationService.Instance.ResetForTests();
        WorkspaceService.SetSettingsFilePathOverrideForTests(null);
        if (File.Exists(_settingsFilePath))
        {
            File.Delete(_settingsFilePath);
        }
    }

    [Fact]
    public void ActivateOrOpenTab_ReusesExistingTabForCanonicalPage()
    {
        var service = CreateService();

        var first = service.ActivateOrOpenTab("StrategyShell");
        var second = service.ActivateOrOpenTab("ResearchShell");

        second.Should().BeSameAs(first);
        service.OpenTabs.Should().ContainSingle();
        service.ActiveTab.Should().Be(first);
        first.PageTag.Should().Be("StrategyShell");
    }

    [Fact]
    public void CloseTab_ActivatesNeighborWhenActiveTabCloses()
    {
        WpfTestThread.Run(() =>
        {
            var service = CreateServiceWithFrame();
            var strategy = service.ActivateOrOpenTab("StrategyShell");
            var settings = service.ActivateOrOpenTab("Settings");

            service.CloseTab(settings).Should().BeTrue();

            service.OpenTabs.Should().ContainSingle().Which.Should().Be(strategy);
            service.ActiveTab.Should().Be(strategy);
            strategy.IsActive.Should().BeTrue();
        });
    }

    [Fact]
    public void NavigateToTab_OpensAndReactivatesTabsThroughNavigation()
    {
        WpfTestThread.Run(() =>
        {
            var service = CreateServiceWithFrame();

            service.NavigateToTab("StrategyShell").Should().BeTrue();
            service.NavigateToTab("Settings").Should().BeTrue();
            service.NavigateToTab("StrategyShell").Should().BeTrue();

            service.OpenTabs.Select(tab => tab.PageTag).Should().Equal("StrategyShell", "Settings");
            service.ActiveTab!.PageTag.Should().Be("StrategyShell");
            NavigationService.Instance.GetCurrentPageTag().Should().Be("StrategyShell");
        });
    }

    [Fact]
    public async Task BuildRestorePlanForContextAsync_RestoresPersistedTabsAndActiveTab()
    {
        var workspaceService = CreateWorkspaceService();
        await workspaceService.SaveSessionStateAsync(
            new SessionState
            {
                ActivePageTag = "Settings",
                ActiveWorkspaceId = "settings",
                OpenPages =
                [
                    new WorkspacePage { PageTag = "StrategyShell", Title = "Strategy Workspace" },
                    new WorkspacePage { PageTag = "Settings", Title = "Settings" }
                ]
            },
            "fund:alpha");

        var service = CreateService(workspaceService);
        var plan = await service.BuildRestorePlanForContextAsync(
            new WorkstationOperatingContext
            {
                ScopeKind = OperatingContextScopeKind.Fund,
                ScopeId = "alpha",
                DefaultWorkspaceId = "strategy",
                DefaultLandingPageTag = "StrategyShell"
            },
            _ => "StrategyShell");

        plan.Should().NotBeNull();
        plan!.TargetPageTag.Should().Be("Settings");
        plan.OpenTabs.Select(tab => tab.PageTag).Should().Equal("StrategyShell", "Settings");
        service.ActiveTab!.PageTag.Should().Be("Settings");
    }

    private DesktopShellSessionService CreateService(WorkspaceService? workspaceService = null)
        => new(
            workspaceService ?? CreateWorkspaceService(),
            new FundContextService(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.funds.json")),
            new WorkstationOperatingContextService(new FundContextService(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.funds.json"))),
            NavigationService.Instance,
            LoggingService.Instance);

    private DesktopShellSessionService CreateServiceWithFrame()
    {
        NavigationService.Instance.ResetForTests();
        NavigationService.Instance.Initialize(new Frame());
        return CreateService();
    }

    private WorkspaceService CreateWorkspaceService()
    {
        var service = (WorkspaceService)Activator.CreateInstance(typeof(WorkspaceService), nonPublic: true)!;
        service.ResetForTests();
        service.LoadWorkspacesAsync().GetAwaiter().GetResult();
        return service;
    }
}
