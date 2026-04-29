using System.IO;
using System.Text.Json;
using Meridian.Wpf.Services;
using Meridian.Wpf.Models;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for <see cref="WorkspaceService"/> — workspace CRUD operations,
/// default workspaces, session management, and import/export.
/// </summary>
public sealed class WorkspaceServiceTests : IDisposable
{
    private static string CreateTestSettingsFilePath()
        => Path.Combine(
            Path.GetTempPath(),
            "Meridian.Wpf.Tests",
            "workspace-service-tests",
            $"{Guid.NewGuid():N}.workspace-data.json");

    private static WorkspaceService CreateService(string? settingsFilePath = null, bool resetPersistedState = true)
    {
        settingsFilePath ??= CreateTestSettingsFilePath();
        var service = (WorkspaceService)Activator.CreateInstance(typeof(WorkspaceService), nonPublic: true)!;
        WorkspaceService.SetSettingsFilePathOverrideForTests(settingsFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath)!);
        if (resetPersistedState && File.Exists(settingsFilePath))
        {
            File.Delete(settingsFilePath);
        }

        service.ResetForTests();
        service.LoadWorkspacesAsync().GetAwaiter().GetResult();
        return service;
    }

    public void Dispose()
    {
        WorkspaceService.SetSettingsFilePathOverrideForTests(null);
    }

    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        var a = WorkspaceService.Instance;
        var b = WorkspaceService.Instance;
        a.Should().BeSameAs(b);
    }

    // ── Default Workspaces ───────────────────────────────────────────

    [Fact]
    public void Workspaces_ShouldContainDefaults()
    {
        var svc = CreateService();
        svc.Workspaces.Should().NotBeEmpty();
        svc.Workspaces.Where(w => w.IsBuiltIn).Select(w => w.Id).Should().ContainInOrder(
            "trading",
            "portfolio",
            "accounting",
            "reporting",
            "strategy",
            "data",
            "settings");
    }

    [Fact]
    public void DefaultWorkspaces_ShouldContainStrategy()
    {
        var svc = CreateService();
        svc.Workspaces.Should().Contain(w => w.Name == "Strategy");
    }

    [Fact]
    public void DefaultWorkspaces_ShouldContainTrading()
    {
        var svc = CreateService();
        svc.Workspaces.Should().Contain(w => w.Name == "Trading");
    }

    [Fact]
    public void DefaultWorkspaces_ShouldContainPortfolio()
    {
        var svc = CreateService();
        svc.Workspaces.Should().Contain(w => w.Name == "Portfolio");
    }

    [Fact]
    public void DefaultWorkspaces_ShouldContainAccounting()
    {
        var svc = CreateService();
        svc.Workspaces.Should().Contain(w => w.Name == "Accounting");
    }

    [Fact]
    public void DefaultWorkspaces_ShouldContainReportingDataAndSettings()
    {
        var svc = CreateService();
        svc.Workspaces.Should().Contain(w => w.Name == "Reporting");
        svc.Workspaces.Should().Contain(w => w.Name == "Data");
        svc.Workspaces.Should().Contain(w => w.Name == "Settings");
    }

    [Fact]
    public void StrategyWorkspace_ShouldContainRunMatPage()
    {
        var svc = CreateService();
        var workspace = svc.Workspaces.First(w => w.Name == "Strategy");
        workspace.Pages.Should().Contain(page => page.PageTag == "RunMat");
    }

    [Fact]
    public void StrategyWorkspace_ShouldContainStrategyRunsPage()
    {
        var svc = CreateService();
        var workspace = svc.Workspaces.First(w => w.Name == "Strategy");
        workspace.Pages.Should().Contain(page => page.PageTag == "StrategyRuns");
    }

    [Fact]
    public void TradingWorkspace_ShouldContainTradingDrillIns()
    {
        var svc = CreateService();
        var workspace = svc.Workspaces.First(w => w.Name == "Trading");
        workspace.Pages.Should().Contain(page => page.PageTag == "OrderBook");
        workspace.Pages.Should().Contain(page => page.PageTag == "PositionBlotter");
        workspace.Pages.Should().Contain(page => page.PageTag == "RunRisk");
    }

    [Fact]
    public void PortfolioAndAccountingWorkspaces_ShouldContainFundDrillIns()
    {
        var svc = CreateService();
        var portfolio = svc.Workspaces.First(w => w.Name == "Portfolio");
        var accounting = svc.Workspaces.First(w => w.Name == "Accounting");

        portfolio.Pages.Should().Contain(page => page.PageTag == "RunPortfolio");
        portfolio.Pages.Should().Contain(page => page.PageTag == "FundAccounts");
        accounting.Pages.Should().Contain(page => page.PageTag == "RunLedger");
        accounting.Pages.Should().Contain(page => page.PageTag == "FundLedger");
    }

    [Fact]
    public void DefaultWorkspaces_ShouldBeBuiltIn()
    {
        var svc = CreateService();
        var builtIn = svc.Workspaces.Where(w => w.IsBuiltIn).ToList();
        builtIn.Count.Should().Be(7);
    }

    [Fact]
    public void DefaultWorkspaces_ShouldHavePages()
    {
        var svc = CreateService();
        foreach (var workspace in svc.Workspaces.Where(w => w.IsBuiltIn))
        {
            workspace.Pages.Should().NotBeNullOrEmpty(
                $"Workspace '{workspace.Name}' should have pages");
        }
    }

    // ── CreateWorkspaceAsync ─────────────────────────────────────────

    [Fact]
    public async Task CreateWorkspaceAsync_ShouldAddWorkspace()
    {
        var svc = CreateService();
        var initialCount = svc.Workspaces.Count;
        var name = "TestWorkspace-" + Guid.NewGuid().ToString("N")[..8];

        var workspace = await svc.CreateWorkspaceAsync(
            name, "Test description", WorkspaceCategory.Custom);

        workspace.Should().NotBeNull();
        workspace.Name.Should().Be(name);
        workspace.Id.Should().NotBeNullOrEmpty();
        svc.Workspaces.Count.Should().BeGreaterThan(initialCount);

        // Clean up
        await svc.DeleteWorkspaceAsync(workspace.Id);
    }

    [Fact]
    public async Task CreateWorkspaceAsync_ShouldAssignUniqueId()
    {
        var svc = CreateService();

        var ws1 = await svc.CreateWorkspaceAsync("Test1-" + Guid.NewGuid(), "desc", WorkspaceCategory.Custom);
        var ws2 = await svc.CreateWorkspaceAsync("Test2-" + Guid.NewGuid(), "desc", WorkspaceCategory.Custom);

        ws1.Id.Should().NotBe(ws2.Id);

        // Clean up
        await svc.DeleteWorkspaceAsync(ws1.Id);
        await svc.DeleteWorkspaceAsync(ws2.Id);
    }

    [Fact]
    public async Task CreateWorkspaceAsync_ShouldRaiseEvent()
    {
        var svc = CreateService();
        WorkspaceEventArgs? receivedArgs = null;
        svc.WorkspaceCreated += (_, args) => receivedArgs = args;

        var workspace = await svc.CreateWorkspaceAsync(
            "EventTest-" + Guid.NewGuid(), "desc", WorkspaceCategory.Custom);

        receivedArgs.Should().NotBeNull();
        receivedArgs!.Workspace.Should().NotBeNull();
        receivedArgs.Workspace.Name.Should().Be(workspace.Name);

        // Clean up
        await svc.DeleteWorkspaceAsync(workspace.Id);
    }

    // ── DeleteWorkspaceAsync ─────────────────────────────────────────

    [Fact]
    public async Task DeleteWorkspaceAsync_ShouldRemoveWorkspace()
    {
        var svc = CreateService();
        var workspace = await svc.CreateWorkspaceAsync(
            "DeleteTest-" + Guid.NewGuid(), "desc", WorkspaceCategory.Custom);

        await svc.DeleteWorkspaceAsync(workspace.Id);

        svc.Workspaces.Should().NotContain(w => w.Id == workspace.Id);
    }

    [Fact]
    public async Task DeleteWorkspaceAsync_BuiltIn_ShouldNotDelete()
    {
        var svc = CreateService();
        var builtIn = svc.Workspaces.First(w => w.IsBuiltIn);
        var initialCount = svc.Workspaces.Count;

        await svc.DeleteWorkspaceAsync(builtIn.Id);

        svc.Workspaces.Count.Should().Be(initialCount);
    }

    [Fact]
    public async Task DeleteWorkspaceAsync_ShouldRaiseEvent()
    {
        var svc = CreateService();
        var workspace = await svc.CreateWorkspaceAsync(
            "DeleteEvt-" + Guid.NewGuid(), "desc", WorkspaceCategory.Custom);
        WorkspaceEventArgs? receivedArgs = null;
        svc.WorkspaceDeleted += (_, args) => receivedArgs = args;

        await svc.DeleteWorkspaceAsync(workspace.Id);

        receivedArgs.Should().NotBeNull();
    }

    // ── ActivateWorkspaceAsync ───────────────────────────────────────

    [Fact]
    public async Task ActivateWorkspaceAsync_ShouldSetActiveWorkspace()
    {
        var svc = CreateService();
        var workspace = svc.Workspaces.First();

        await svc.ActivateWorkspaceAsync(workspace.Id);

        svc.ActiveWorkspace.Should().NotBeNull();
        svc.ActiveWorkspace!.Id.Should().Be(workspace.Id);
    }

    [Fact]
    public async Task ActivateWorkspaceAsync_ShouldRestoreWorkspaceSpecificPreferredPage()
    {
        var svc = CreateService();
        var trading = svc.Workspaces.First(w => w.Id == "trading");

        await svc.ActivateWorkspaceAsync(trading.Id);

        svc.GetLastSessionState().Should().NotBeNull();
        svc.GetLastSessionState()!.ActiveWorkspaceId.Should().Be(trading.Id);
        svc.GetLastSessionState()!.ActivePageTag.Should().Be(trading.PreferredPageTag);
    }

    [Fact]
    public async Task ActivateWorkspaceAsync_ShouldRaiseEvent()
    {
        var svc = CreateService();
        var workspace = svc.Workspaces.First();
        WorkspaceEventArgs? receivedArgs = null;
        svc.WorkspaceActivated += (_, args) => receivedArgs = args;

        await svc.ActivateWorkspaceAsync(workspace.Id);

        receivedArgs.Should().NotBeNull();
        receivedArgs!.Workspace.Should().NotBeNull();
        receivedArgs.Workspace!.Id.Should().Be(workspace.Id);
    }

    // ── UpdateWorkspaceAsync ─────────────────────────────────────────

    [Fact]
    public async Task UpdateWorkspaceAsync_ShouldUpdateExisting()
    {
        var svc = CreateService();
        var workspace = await svc.CreateWorkspaceAsync(
            "UpdateTest-" + Guid.NewGuid(), "original", WorkspaceCategory.Custom);

        workspace.Description = "updated description";
        await svc.UpdateWorkspaceAsync(workspace);

        var updated = svc.Workspaces.First(w => w.Id == workspace.Id);
        updated.Description.Should().Be("updated description");

        // Clean up
        await svc.DeleteWorkspaceAsync(workspace.Id);
    }

    [Fact]
    public async Task UpdateWorkspaceAsync_ShouldRaiseEvent()
    {
        var svc = CreateService();
        var workspace = await svc.CreateWorkspaceAsync(
            "UpdateEvt-" + Guid.NewGuid(), "desc", WorkspaceCategory.Custom);
        WorkspaceEventArgs? receivedArgs = null;
        svc.WorkspaceUpdated += (_, args) => receivedArgs = args;

        workspace.Description = "new desc";
        await svc.UpdateWorkspaceAsync(workspace);

        receivedArgs.Should().NotBeNull();

        // Clean up
        await svc.DeleteWorkspaceAsync(workspace.Id);
    }

    // ── Session State ────────────────────────────────────────────────

    [Fact]
    public async Task SaveSessionStateAsync_ShouldPersistState()
    {
        var svc = CreateService();
        await svc.ActivateWorkspaceAsync("research");
        var state = new SessionState
        {
            ActiveWorkspaceId = "research",
            ActivePageTag = "StrategyRuns",
            OpenPages = new List<WorkspacePage>
            {
                new() { PageTag = "Dashboard", Title = "Dashboard" }
            },
            ActiveFilters = new Dictionary<string, string> { ["symbol"] = "SPY" },
            WorkspaceContext = new Dictionary<string, string> { ["focus"] = "review" },
            RecentPages = new List<string> { "StrategyRuns", "Dashboard" }
        };

        await svc.SaveSessionStateAsync(state);

        var retrieved = svc.GetLastSessionState();
        retrieved.Should().NotBeNull();
        retrieved!.OpenPages.Should().NotBeEmpty();
        retrieved.ActiveWorkspaceId.Should().Be("strategy");
        retrieved.ActivePageTag.Should().Be("StrategyRuns");
        retrieved.SavedAt.Should().BeOnOrAfter(DateTime.UtcNow.AddSeconds(-5));

        var strategy = svc.Workspaces.First(w => w.Id == "strategy");
        strategy.SessionSnapshot.Should().NotBeNull();
        strategy.LastActivePageTag.Should().Be("StrategyRuns");
        strategy.Context.Should().ContainKey("focus");
    }

    [Fact]
    public async Task SaveSessionStateAsync_ShouldRealignWorkspaceToUniquePageOwner()
    {
        var svc = CreateService();
        await svc.ActivateWorkspaceAsync("governance");

        await svc.SaveSessionStateAsync(new SessionState
        {
            ActiveWorkspaceId = "governance",
            ActivePageTag = "DataOperationsShell",
            RecentPages = new List<string> { "DataOperationsShell" }
        });

        svc.ActiveWorkspace.Should().NotBeNull();
        svc.ActiveWorkspace!.Id.Should().Be("data");

        var restored = svc.GetLastSessionState();
        restored.Should().NotBeNull();
        restored!.ActiveWorkspaceId.Should().Be("data");
        restored.ActivePageTag.Should().Be("DataOperationsShell");

        svc.Workspaces.First(w => w.Id == "data").SessionSnapshot.Should().NotBeNull();
        svc.Workspaces.First(w => w.Id == "data").SessionSnapshot!.ActivePageTag.Should().Be("DataOperationsShell");
        svc.Workspaces.First(w => w.Id == "accounting").LastActivePageTag.Should().NotBe("DataOperationsShell");
    }

    [Fact]
    public async Task GetLastSessionStateForContext_ShouldRestoreLegacyFundScopedSession()
    {
        var svc = CreateService();

        await svc.SaveSessionStateAsync(new SessionState
        {
            ActiveWorkspaceId = "data-operations",
            ActivePageTag = "AddProviderWizard",
            RecentPages = new List<string> { "AddProviderWizard" }
        }, "alpha-credit");

        var restored = svc.GetLastSessionStateForContext(
            WorkstationOperatingContext.CreateContextKey(OperatingContextScopeKind.Fund, "alpha-credit"));

        restored.Should().NotBeNull();
        restored!.ActiveWorkspaceId.Should().Be("data");
        restored.ActivePageTag.Should().Be("AddProviderWizard");
        svc.LastSelectedOperatingContextKey.Should().Be("Fund:alpha-credit");
    }

    [Fact]
    public async Task SaveSessionStateAsync_WithFundOperatingContextKey_ShouldBeReachableByFundProfileId()
    {
        var svc = CreateService();

        await svc.SaveSessionStateAsync(new SessionState
        {
            ActiveWorkspaceId = "data-operations",
            ActivePageTag = "Options",
            RecentPages = new List<string> { "Options" }
        }, WorkstationOperatingContext.CreateContextKey(OperatingContextScopeKind.Fund, "alpha-credit"));

        var restored = svc.GetLastSessionState("alpha-credit");

        restored.Should().NotBeNull();
        restored!.ActiveWorkspaceId.Should().Be("data");
        restored.ActivePageTag.Should().Be("Options");
    }

    [Fact]
    public async Task LoadWorkspacesAsync_ShouldRoundTripCamelCasedScopedSessionFromDisk()
    {
        var settingsFilePath = CreateTestSettingsFilePath();
        var svc = CreateService(settingsFilePath);

        await svc.SaveSessionStateAsync(new SessionState
        {
            ActiveWorkspaceId = "data-operations",
            ActivePageTag = "AddProviderWizard",
            OpenPages = new List<WorkspacePage>
            {
                new() { PageTag = "AddProviderWizard", Title = "Add Provider Wizard", IsDefault = true }
            },
            RecentPages = new List<string> { "AddProviderWizard" }
        }, WorkstationOperatingContext.CreateContextKey(OperatingContextScopeKind.Fund, "alpha-credit"));

        svc = CreateService(settingsFilePath, resetPersistedState: false);

        var restored = svc.GetLastSessionStateForContext(
            WorkstationOperatingContext.CreateContextKey(OperatingContextScopeKind.Fund, "alpha-credit"));

        restored.Should().NotBeNull();
        restored!.ActiveWorkspaceId.Should().Be("data");
        restored.ActivePageTag.Should().Be("AddProviderWizard");
        restored.OpenPages.Should().ContainSingle(page => page.PageTag == "AddProviderWizard");
    }

    [Fact]
    public void LoadWorkspacesAsync_ShouldMigrateLegacyBuiltInWorkspaceIds()
    {
        var settingsFilePath = CreateTestSettingsFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath)!);
        File.WriteAllText(settingsFilePath, """
        {
            "workspaces": [
                { "id": "research", "name": "Research", "category": 0, "isBuiltIn": true, "pages": [] },
                { "id": "trading", "name": "Trading", "category": 1, "isBuiltIn": true, "pages": [] },
                { "id": "data-operations", "name": "Data Operations", "category": 2, "isBuiltIn": true, "pages": [] },
                { "id": "governance", "name": "Governance", "category": 3, "isBuiltIn": true, "pages": [] }
            ],
            "activeWorkspaceId": "research",
            "lastSession": {
                "activeWorkspaceId": "research",
                "activePageTag": "StrategyRuns",
                "recentPages": [ "StrategyRuns" ]
            },
            "sessionsByFundProfileId": {
                "Fund:alpha-credit": {
                    "activeWorkspaceId": "data-operations",
                    "activePageTag": "AddProviderWizard",
                    "recentPages": [ "AddProviderWizard" ]
                }
            },
            "workspaceLayouts": {},
            "dockLayouts": {}
        }
        """);

        var svc = CreateService(settingsFilePath, resetPersistedState: false);

        svc.Workspaces.Select(workspace => workspace.Id).Should().Contain([
            "trading",
            "portfolio",
            "accounting",
            "reporting",
            "strategy",
            "data",
            "settings"
        ]);
        svc.Workspaces.Select(workspace => workspace.Id).Should().NotContain([
            "research",
            "data-operations",
            "governance"
        ]);
        svc.ActiveWorkspace.Should().NotBeNull();
        svc.ActiveWorkspace!.Id.Should().Be("strategy");

        var restored = svc.GetLastSessionState();
        restored.Should().NotBeNull();
        restored!.ActiveWorkspaceId.Should().Be("strategy");
        restored.ActivePageTag.Should().Be("StrategyRuns");

        var scoped = svc.GetLastSessionStateForContext(
            WorkstationOperatingContext.CreateContextKey(OperatingContextScopeKind.Fund, "alpha-credit"));
        scoped.Should().NotBeNull();
        scoped!.ActiveWorkspaceId.Should().Be("data");
        scoped.ActivePageTag.Should().Be("AddProviderWizard");
    }

    [Fact]
    public async Task ActivateWorkspaceAsync_ShouldPreservePendingScopedSessionForSameWorkspace()
    {
        var svc = CreateService();

        await svc.SaveSessionStateAsync(new SessionState
        {
            ActiveWorkspaceId = "data-operations",
            ActivePageTag = "AddProviderWizard",
            OpenPages = new List<WorkspacePage>
            {
                new() { PageTag = "AddProviderWizard", Title = "Add Provider Wizard", IsDefault = true }
            },
            RecentPages = new List<string> { "AddProviderWizard" }
        }, WorkstationOperatingContext.CreateContextKey(OperatingContextScopeKind.Fund, "alpha-credit"));

        await svc.ActivateWorkspaceAsync("data-operations");

        var restored = svc.GetLastSessionState();
        restored.Should().NotBeNull();
        restored!.ActiveWorkspaceId.Should().Be("data");
        restored.ActivePageTag.Should().Be("AddProviderWizard");
    }

    [Fact]
    public async Task ActivateWorkspaceAsync_ShouldIgnoreSnapshotFromDifferentWorkspace()
    {
        var svc = CreateService();
        var accounting = svc.Workspaces.First(w => w.Id == "accounting");
        accounting.LastActivePageTag = "DataOperationsShell";
        accounting.SessionSnapshot = new SessionState
        {
            ActiveWorkspaceId = "accounting",
            ActivePageTag = "DataOperationsShell",
            RecentPages = new List<string> { "DataOperationsShell" }
        };

        await svc.ActivateWorkspaceAsync("accounting");

        var restored = svc.GetLastSessionState();
        restored.Should().NotBeNull();
        restored!.ActiveWorkspaceId.Should().Be("accounting");
        restored.ActivePageTag.Should().Be("AccountingShell");
    }

    [Fact]
    public void GetLastSessionState_InitialState_MayBeNullOrPreviouslySaved()
    {
        var svc = CreateService();
        // LastSession may be null initially or restored from previous tests
        var act = () => svc.GetLastSessionState();
        act.Should().NotThrow();
    }

    // ── Page Filter State ────────────────────────────────────────────

    [Fact]
    public void UpdatePageFilterState_ShouldStoreValue()
    {
        var svc = CreateService();

        svc.UpdatePageFilterState("Symbols", "SearchText", "SPY");

        svc.GetPageFilterState("Symbols", "SearchText").Should().Be("SPY");
    }

    [Fact]
    public void UpdatePageFilterState_NullValue_ShouldRemoveEntry()
    {
        var svc = CreateService();
        svc.UpdatePageFilterState("Symbols", "SearchText", "SPY");

        svc.UpdatePageFilterState("Symbols", "SearchText", null);

        svc.GetPageFilterState("Symbols", "SearchText").Should().BeNull();
    }

    [Fact]
    public void UpdatePageFilterState_ShouldScopeKeyByPage()
    {
        var svc = CreateService();

        svc.UpdatePageFilterState("Symbols", "Filter", "Trades");
        svc.UpdatePageFilterState("DataBrowser", "Filter", "All");

        svc.GetPageFilterState("Symbols", "Filter").Should().Be("Trades");
        svc.GetPageFilterState("DataBrowser", "Filter").Should().Be("All");
    }

    [Fact]
    public void GetPageFilterState_UnknownKey_ShouldReturnNull()
    {
        var svc = CreateService();

        svc.GetPageFilterState("NonExistentPage", "NonExistentKey").Should().BeNull();
    }

    [Fact]
    public void UpdatePageFilterState_OverwriteExisting_ShouldUpdateValue()
    {
        var svc = CreateService();
        svc.UpdatePageFilterState("Backfill", "Granularity", "Daily");

        svc.UpdatePageFilterState("Backfill", "Granularity", "Hourly");

        svc.GetPageFilterState("Backfill", "Granularity").Should().Be("Hourly");
    }

    [Fact]
    public async Task UpdatePageFilterState_ShouldBePersisted_AfterSaveSession()
    {
        var svc = CreateService();
        svc.UpdatePageFilterState("Symbols", "SearchText", "AAPL");

        // Simulate session save
        var session = svc.GetLastSessionState() ?? new SessionState();
        await svc.SaveSessionStateAsync(session);

        var restored = svc.GetLastSessionState();
        restored.Should().NotBeNull();
        restored!.ActiveFilters.Should().ContainKey("Symbols.SearchText");
        restored.ActiveFilters["Symbols.SearchText"].Should().Be("AAPL");
    }

    // ── CaptureCurrentStateAsync ─────────────────────────────────────

    [Fact]
    public async Task CaptureCurrentStateAsync_ShouldCreateWorkspace()
    {
        var svc = CreateService();
        await svc.ActivateWorkspaceAsync("accounting");
        var name = "CapturedState-" + Guid.NewGuid().ToString("N")[..8];

        var workspace = await svc.CaptureCurrentStateAsync(name, "Captured");

        workspace.Should().NotBeNull();
        workspace.Name.Should().Be(name);
        workspace.Category.Should().Be(WorkspaceCategory.Custom);
        workspace.PreferredPageTag.Should().NotBeNullOrWhiteSpace();

        // Clean up
        await svc.DeleteWorkspaceAsync(workspace.Id);
    }

    [Fact]
    public async Task ActivateWorkspaceAsync_ShouldRestoreWorkspaceSpecificSnapshot()
    {
        var svc = CreateService();

        await svc.ActivateWorkspaceAsync("strategy");
        await svc.SaveSessionStateAsync(new SessionState
        {
            ActiveWorkspaceId = "strategy",
            ActivePageTag = "StrategyRuns",
            OpenPages = new List<WorkspacePage> { new() { PageTag = "StrategyRuns", Title = "Strategy Runs" } },
            ActiveFilters = new Dictionary<string, string> { ["strategy.filter"] = "momentum" },
            RecentPages = new List<string> { "StrategyRuns", "Dashboard" }
        });

        await svc.ActivateWorkspaceAsync("settings");
        await svc.SaveSessionStateAsync(new SessionState
        {
            ActiveWorkspaceId = "settings",
            ActivePageTag = "Diagnostics",
            OpenPages = new List<WorkspacePage> { new() { PageTag = "Diagnostics", Title = "Diagnostics" } },
            ActiveFilters = new Dictionary<string, string> { ["settings.filter"] = "open-breaks" },
            RecentPages = new List<string> { "Diagnostics", "SystemHealth" }
        });

        await svc.ActivateWorkspaceAsync("strategy");

        var restored = svc.GetLastSessionState();
        restored.Should().NotBeNull();
        restored!.ActiveWorkspaceId.Should().Be("strategy");
        restored.ActivePageTag.Should().Be("StrategyRuns");
        restored.ActiveFilters.Should().ContainKey("strategy.filter");
        restored.ActiveFilters.Should().NotContainKey("settings.filter");
    }

    [Fact]
    public async Task SaveWorkspaceLayoutStateAsync_ShouldPersistLayoutsPerWorkspaceAndFund()
    {
        var svc = CreateService();
        var layout = new WorkstationLayoutState
        {
            LayoutId = "strategy-studio",
            DisplayName = "Strategy Studio",
            ActivePaneId = "pane-2",
            DockLayoutXml = "<layout />",
            Panes =
            [
                new WorkstationPaneState
                {
                    PaneId = "pane-1",
                    PageTag = "Backtest",
                    Title = "Backtest Studio",
                    DockZone = "document",
                    IsActive = false,
                    Order = 0
                },
                new WorkstationPaneState
                {
                    PaneId = "pane-2",
                    PageTag = "StrategyRuns",
                    Title = "Run Browser",
                    DockZone = "left",
                    IsToolPane = true,
                    IsActive = true,
                    Order = 1
                }
            ]
        };

        await svc.SaveWorkspaceLayoutStateAsync("strategy", layout, "alpha-credit");

        var restored = await svc.GetWorkspaceLayoutStateAsync("strategy", "alpha-credit");
        restored.Should().NotBeNull();
        restored!.LayoutId.Should().Be("strategy-studio");
        restored.Panes.Should().HaveCount(2);
        restored.Panes.Should().ContainSingle(pane => pane.PageTag == "StrategyRuns" && pane.DockZone == "left");

        var otherFund = await svc.GetWorkspaceLayoutStateAsync("strategy", "beta-macro");
        otherFund.Should().BeNull();
    }

    [Fact]
    public async Task SaveDockLayoutAsync_WithLegacyWorkspaceId_ShouldWriteCanonicalKey()
    {
        var settingsFilePath = CreateTestSettingsFilePath();
        var svc = CreateService(settingsFilePath);

        await svc.SaveDockLayoutAsync("research", "<layout canonical=\"true\" />");

        (await svc.GetDockLayoutAsync("strategy")).Should().Be("<layout canonical=\"true\" />");
        (await svc.GetDockLayoutAsync("research")).Should().Be("<layout canonical=\"true\" />");

        using var json = JsonDocument.Parse(File.ReadAllText(settingsFilePath));
        var dockLayouts = json.RootElement.GetProperty("dockLayouts");
        dockLayouts.TryGetProperty("strategy", out _).Should().BeTrue();
        dockLayouts.TryGetProperty("research", out _).Should().BeFalse();
    }

    [Fact]
    public async Task LoadWorkspacesAsync_ShouldReadLegacyDockAndLayoutKeysAsFallback()
    {
        var settingsFilePath = CreateTestSettingsFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath)!);
        File.WriteAllText(settingsFilePath, """
        {
            "workspaces": [],
            "sessionsByFundProfileId": {},
            "workspaceLayouts": {
                "governance::Fund:alpha-credit": {
                    "layoutId": "legacy-accounting-review",
                    "displayName": "Legacy Accounting Review",
                    "dockLayoutXml": "<layout legacy=\"accounting\" />",
                    "panes": [
                        {
                            "paneId": "ledger",
                            "pageTag": "FundLedger",
                            "title": "Fund Ledger",
                            "dockZone": "document",
                            "order": 0
                        }
                    ]
                }
            },
            "dockLayouts": {
                "research": "<layout legacy=\"strategy\" />"
            }
        }
        """);

        var svc = CreateService(settingsFilePath, resetPersistedState: false);

        (await svc.GetDockLayoutAsync("strategy")).Should().Be("<layout legacy=\"strategy\" />");
        (await svc.GetDockLayoutAsync("research")).Should().Be("<layout legacy=\"strategy\" />");

        var restored = await svc.GetWorkspaceLayoutStateForContextAsync("accounting", "Fund:alpha-credit");
        restored.Should().NotBeNull();
        restored!.LayoutId.Should().Be("legacy-accounting-review");
        restored.Panes.Should().ContainSingle(pane => pane.PageTag == "FundLedger");

        using var json = JsonDocument.Parse(File.ReadAllText(settingsFilePath));
        var dockLayouts = json.RootElement.GetProperty("dockLayouts");
        var workspaceLayouts = json.RootElement.GetProperty("workspaceLayouts");
        dockLayouts.TryGetProperty("strategy", out _).Should().BeTrue();
        workspaceLayouts.TryGetProperty("accounting::Fund:alpha-credit", out _).Should().BeTrue();
    }

    [Fact]
    public async Task SaveWorkspaceLayoutStateForContextAsync_ShouldPersistOperatingContextWindowModeAndPreset()
    {
        var svc = CreateService();
        var layout = new WorkstationLayoutState
        {
            LayoutId = "accounting-review",
            DisplayName = "Accounting Review",
            OperatingContextKey = "Fund:alpha-credit",
            WindowMode = BoundedWindowMode.WorkbenchPreset,
            LayoutPresetId = "accounting-review",
            DockLayoutXml = "<layout />",
            Panes =
            [
                new WorkstationPaneState
                {
                    PaneId = "ledger",
                    PageTag = "FundLedger",
                    Title = "Operations",
                    DockZone = "document",
                    Order = 0
                },
                new WorkstationPaneState
                {
                    PaneId = "trial-balance",
                    PageTag = "FundTrialBalance",
                    Title = "Accounting",
                    DockZone = "right",
                    Order = 1
                }
            ]
        };

        await svc.SaveWorkspaceLayoutStateForContextAsync("accounting", layout, "Fund:alpha-credit");

        var restored = await svc.GetWorkspaceLayoutStateForContextAsync("accounting", "Fund:alpha-credit");

        restored.Should().NotBeNull();
        restored!.OperatingContextKey.Should().Be("Fund:alpha-credit");
        restored.WindowMode.Should().Be(BoundedWindowMode.WorkbenchPreset);
        restored.LayoutPresetId.Should().Be("accounting-review");
        restored.Panes.Should().ContainSingle(pane => pane.PageTag == "FundTrialBalance" && pane.DockZone == "right");
    }
    // ── Export/Import ────────────────────────────────────────────────

    [Fact]
    public async Task ExportWorkspaceAsync_ExistingWorkspace_ShouldReturnJson()
    {
        var svc = CreateService();
        var workspace = await svc.CreateWorkspaceAsync(
            "ExportTest-" + Guid.NewGuid().ToString("N")[..8],
            "Export test workspace",
            WorkspaceCategory.Custom);

        var json = await svc.ExportWorkspaceAsync(workspace.Id);

        json.Should().NotBeNullOrEmpty();
        json.Should().Contain(workspace.Name);
        await svc.DeleteWorkspaceAsync(workspace.Id);
    }

    [Fact]
    public async Task ExportWorkspaceAsync_NonExistentId_ShouldReturnEmpty()
    {
        var svc = CreateService();

        var json = await svc.ExportWorkspaceAsync("nonexistent-" + Guid.NewGuid());

        json.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportWorkspaceAsync_ValidJson_ShouldCreateWorkspace()
    {
        var svc = CreateService();
        var json = """
        {
            "Id": "temp-id",
            "Name": "Imported Workspace",
            "Description": "From import",
            "Category": 0,
            "IsBuiltIn": true,
            "Pages": []
        }
        """;

        var imported = await svc.ImportWorkspaceAsync(json);

        imported.Should().NotBeNull();
        imported!.Name.Should().Be("Imported Workspace");
        imported.Id.Should().NotBe("temp-id"); // New ID assigned
        imported.IsBuiltIn.Should().BeFalse(); // Forced to false on import

        // Clean up
        await svc.DeleteWorkspaceAsync(imported.Id);
    }

    [Fact]
    public async Task ImportWorkspaceAsync_InvalidJson_ShouldReturnNull()
    {
        var svc = CreateService();

        var imported = await svc.ImportWorkspaceAsync("not valid json");

        imported.Should().BeNull();
    }

    // ── Data Model: WorkspaceTemplate ────────────────────────────────

    [Fact]
    public void WorkspaceTemplate_ShouldHaveDefaults()
    {
        var template = new WorkspaceTemplate();
        template.Id.Should().BeEmpty();
        template.Name.Should().BeEmpty();
        template.Description.Should().BeEmpty();
        template.PreferredPageTag.Should().BeEmpty();
        template.IsBuiltIn.Should().BeFalse();
        template.RecentPageTags.Should().BeEmpty();
        template.Context.Should().BeEmpty();
        template.SessionSnapshot.Should().BeNull();
    }

    // ── Data Model: WorkspacePage ────────────────────────────────────

    [Fact]
    public void WorkspacePage_ShouldHaveDefaults()
    {
        var page = new WorkspacePage();
        page.PageTag.Should().BeEmpty();
        page.Title.Should().BeEmpty();
        page.IsDefault.Should().BeFalse();
    }

    // ── Data Model: WorkspaceCategory Enum ───────────────────────────

    [Theory]
    [InlineData(WorkspaceCategory.Research)]
    [InlineData(WorkspaceCategory.Trading)]
    [InlineData(WorkspaceCategory.DataOperations)]
    [InlineData(WorkspaceCategory.Governance)]
    [InlineData(WorkspaceCategory.Custom)]
    public void WorkspaceCategory_AllValues_ShouldBeDefined(WorkspaceCategory category)
    {
        Enum.IsDefined(typeof(WorkspaceCategory), category).Should().BeTrue();
    }
}
