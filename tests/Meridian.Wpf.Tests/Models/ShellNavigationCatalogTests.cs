using Meridian.Ui.Services;
using Meridian.Wpf.Features;
using Meridian.Wpf.Models;
using Meridian.Wpf.Shell.Services;

namespace Meridian.Wpf.Tests.Models;

public sealed class ShellNavigationCatalogTests
{
    [Fact]
    public void NavigationText_ShouldAvoidBannedJargon_AndKeepSubtitlesConcise()
    {
        foreach (var page in ShellNavigationCatalog.Pages)
        {
            page.Subtitle.Length.Should().BeLessThanOrEqualTo(
                ShellNavigationTextStyleGuide.SubtitleMaxLength,
                $"{page.PageTag} subtitle should stay concise for shell navigation");
            AssertDoesNotContainBannedTerms(page.Subtitle, $"{page.PageTag} subtitle");
            AssertDoesNotContainBannedTerms(page.Title, $"{page.PageTag} title");
        }

        foreach (var workspace in ShellNavigationCatalog.Workspaces)
        {
            workspace.Description.Length.Should().BeLessThanOrEqualTo(
                ShellNavigationTextStyleGuide.SubtitleMaxLength,
                $"{workspace.Id} description should stay concise for workspace selection");
            workspace.Summary.Length.Should().BeLessThanOrEqualTo(
                ShellNavigationTextStyleGuide.SubtitleMaxLength,
                $"{workspace.Id} summary should stay concise for workspace selection");
            AssertDoesNotContainBannedTerms(workspace.Description, $"{workspace.Id} description");
            AssertDoesNotContainBannedTerms(workspace.Summary, $"{workspace.Id} summary");
            AssertDoesNotContainBannedTerms(workspace.Title, $"{workspace.Id} title");
        }
    }

    [Fact]
    public void Aliases_ShouldResolveBackToCanonicalDescriptors()
    {
        foreach (var page in ShellNavigationCatalog.Pages)
        {
            foreach (var alias in page.Aliases)
            {
                ShellNavigationCatalog.GetPage(alias).Should().BeSameAs(page);
                ShellNavigationCatalog.GetCanonicalPageTag(alias).Should().Be(page.PageTag);
            }
        }
    }

    [Fact]
    public void EvidenceWorkbenchTargets_ShouldResolveParameterizedSubjectsToAuditTrail()
    {
        var target = "EvidenceWorkbench:accounting-record/accounting-record-2026-05";

        ShellNavigationCatalog.GetPage(target).Should().BeSameAs(ShellNavigationCatalog.GetPage("FundAuditTrail"));
        ShellNavigationCatalog.GetCanonicalPageTag(target).Should().Be("FundAuditTrail");
        ShellNavigationCatalog.InferWorkspaceIdForPageTag(target).Should().Be("accounting");
    }

    [Fact]
    public void WorkspaceShellDefinitions_ShouldOnlyReferenceRegisteredPages()
    {
        foreach (var workspace in ShellNavigationCatalog.Workspaces)
        {
            ShellNavigationCatalog.GetPage(workspace.HomePageTag).Should().NotBeNull(
                $"workspace home page '{workspace.HomePageTag}' should exist in the shell catalog");
        }

        foreach (var shell in ShellNavigationCatalog.WorkspaceShells)
        {
            ShellNavigationCatalog.GetWorkspace(shell.WorkspaceId).Should().NotBeNull(
                $"workspace shell '{shell.WorkspaceId}' should map to a declared workspace");
            shell.StateProviderType.Should().NotBeNull();
            shell.ViewModelType.Should().NotBeNull();

            foreach (var pane in EnumeratePanes(shell))
            {
                ShellNavigationCatalog.GetPage(pane.PageTag).Should().NotBeNull(
                    $"workspace '{shell.WorkspaceId}' references unknown page '{pane.PageTag}'");

                if (!string.IsNullOrWhiteSpace(pane.FallbackPageTag))
                {
                    ShellNavigationCatalog.GetPage(pane.FallbackPageTag).Should().NotBeNull(
                        $"workspace '{shell.WorkspaceId}' references unknown fallback page '{pane.FallbackPageTag}'");
                }
            }
        }
    }

    [Fact]
    public void Workspaces_ShouldExposeSevenCanonicalRoots()
    {
        ShellNavigationCatalog.Workspaces
            .Select(static workspace => workspace.Id)
            .Should()
            .Equal("trading", "portfolio", "accounting", "reporting", "strategy", "data", "settings");

        ShellNavigationCatalog.GetDefaultWorkspace().Id.Should().Be("strategy");
    }

    [Theory]
    [InlineData("research", "strategy")]
    [InlineData("governance", "accounting")]
    [InlineData("Data Operations", "data")]
    [InlineData("portfolio", "portfolio")]
    public void WorkstationNavigationDefaults_ShouldCanonicalizeLegacyWorkspaceIds(
        string workspaceId,
        string expectedWorkspaceId)
    {
        WorkstationNavigationDefaults.NormalizeWorkspaceId(workspaceId).Should().Be(expectedWorkspaceId);
    }

    [Fact]
    public void WorkstationNavigationDefaults_ShouldExposeCanonicalCompatibilityAliases()
    {
        WorkstationNavigationDefaults.WorkspaceCompatibilityAliases.Should().Contain(new Dictionary<string, string>
        {
            ["research"] = "strategy",
            ["governance"] = "accounting",
            ["dataoperations"] = "data",
            ["data-operations"] = "data",
            ["data operations"] = "data"
        });
    }

    [Theory]
    [InlineData("ResearchShell", "StrategyShell")]
    [InlineData("GovernanceShell", "AccountingShell")]
    [InlineData("DataOperationsShell", "DataShell")]
    [InlineData("TradingShell", "TradingShell")]
    public void WorkstationNavigationDefaults_ShouldCanonicalizeLegacyPageTags(
        string pageTag,
        string expectedPageTag)
    {
        WorkstationNavigationDefaults.NormalizePageTag(pageTag).Should().Be(expectedPageTag);
    }

    [Fact]
    public void WorkspaceRegistry_ShouldExposeSevenCanonicalRoots()
    {
        WorkspaceRegistry.All
            .Select(static workspace => workspace.Id)
            .Should()
            .Equal("trading", "portfolio", "accounting", "reporting", "strategy", "data", "settings");

        WorkspaceRegistry.GetDefaultWorkspace().Id.Should().Be("strategy");
    }

    [Theory]
    [InlineData("research", "strategy")]
    [InlineData("governance", "accounting")]
    [InlineData("data-operations", "data")]
    [InlineData("Data Operations", "data")]
    public void WorkspaceRegistry_ShouldResolveLegacyWorkspaceIdsToCanonicalDefinitions(
        string workspaceId,
        string expectedWorkspaceId)
    {
        WorkspaceRegistry.GetWorkspaceById(workspaceId)?.Id.Should().Be(expectedWorkspaceId);
    }

    [Theory]
    [InlineData("trading", ShellPosture.Terminal, "WorkspaceHomeTradingTerminalTemplate")]
    [InlineData("portfolio", ShellPosture.Cockpit, "WorkspaceHomePortfolioCockpitTemplate")]
    [InlineData("accounting", ShellPosture.Cockpit, "WorkspaceHomeAccountingCockpitTemplate")]
    [InlineData("reporting", ShellPosture.Cockpit, "WorkspaceHomeReportingCockpitTemplate")]
    [InlineData("strategy", ShellPosture.Workbench, "WorkspaceHomeStrategyWorkbenchTemplate")]
    [InlineData("data", ShellPosture.Terminal, "WorkspaceHomeDataTerminalTemplate")]
    [InlineData("settings", ShellPosture.Cockpit, "WorkspaceHomeSettingsCockpitTemplate")]
    public void WorkspaceLayouts_ShouldMapCanonicalRootsToPostures(
        string workspaceId,
        ShellPosture expectedPosture,
        string expectedHomeAutomationId)
    {
        var descriptor = ShellNavigationCatalog.GetWorkspaceLayoutDescriptor(workspaceId);

        descriptor.WorkspaceId.Should().Be(workspaceId);
        descriptor.Posture.Should().Be(expectedPosture);
        descriptor.HomeTemplateAutomationId.Should().Be(expectedHomeAutomationId);
        descriptor.EvidenceStripAutomationId.Should().Be($"WorkspaceEvidenceStrip{char.ToUpperInvariant(workspaceId[0])}{workspaceId[1..]}");
        descriptor.CommandSurfaceAutomationId.Should().Be($"WorkspaceCommandSurface{char.ToUpperInvariant(workspaceId[0])}{workspaceId[1..]}");
        descriptor.InspectorHostAutomationId.Should().Be($"WorkspaceInspectorHost{char.ToUpperInvariant(workspaceId[0])}{workspaceId[1..]}");
    }

    [Theory]
    [InlineData("research", "strategy", ShellPosture.Workbench)]
    [InlineData("Data Operations", "data", ShellPosture.Terminal)]
    [InlineData("governance", "accounting", ShellPosture.Cockpit)]
    public void WorkspaceLayouts_ShouldResolveLegacyAliasesToCanonicalRoots(
        string legacyWorkspaceId,
        string expectedWorkspaceId,
        ShellPosture expectedPosture)
    {
        var descriptor = ShellNavigationCatalog.GetWorkspaceLayoutDescriptor(legacyWorkspaceId);

        descriptor.WorkspaceId.Should().Be(expectedWorkspaceId);
        descriptor.Posture.Should().Be(expectedPosture);
    }

    [Theory]
    [InlineData("trading", WorkspaceVisualPriority.Evidence, WorkspaceVisualPriority.Commands, WorkspaceVisualPriority.Inspector)]
    [InlineData("portfolio", WorkspaceVisualPriority.Commands, WorkspaceVisualPriority.Evidence, WorkspaceVisualPriority.Inspector)]
    [InlineData("strategy", WorkspaceVisualPriority.Inspector, WorkspaceVisualPriority.Commands, WorkspaceVisualPriority.Evidence)]
    public void WorkspaceLayouts_ShouldDeclarePostureSpecificVisualPriority(
        string workspaceId,
        WorkspaceVisualPriority first,
        WorkspaceVisualPriority second,
        WorkspaceVisualPriority third)
    {
        ShellNavigationCatalog.GetWorkspaceLayoutDescriptor(workspaceId)
            .VisualPriority
            .Should()
            .Equal(first, second, third);
    }

    [Fact]
    public void ResolveDefaultPanes_AccountingWithoutPrimaryContext_UsesContextlessPanes()
    {
        var panes = ShellNavigationCatalog.ResolveDefaultPanes(new WorkspaceShellState(
            WorkspaceId: "accounting",
            LayoutId: "accounting-workspace",
            DisplayName: "Accounting Workspace",
            LayoutScopeKey: null,
            WindowMode: BoundedWindowMode.DockFloat,
            LayoutPresetId: null,
            HasPrimaryContext: false));

        panes.Select(pane => pane.PageTag).Should().ContainInOrder("FundLedger", "FundTrialBalance", "FundAuditTrail");
    }

    [Fact]
    public void ProviderHealth_ShouldBelongToDataWhileDiagnosticsStaysSettingsOwned()
    {
        var providerHealth = ShellNavigationCatalog.GetPage("ProviderHealth");
        var diagnostics = ShellNavigationCatalog.GetPage("Diagnostics");

        providerHealth.Should().NotBeNull();
        providerHealth!.WorkspaceId.Should().Be("data");
        ShellNavigationCatalog.GetPagesForWorkspace("data")
            .Select(static page => page.PageTag)
            .Should()
            .Contain("ProviderHealth");
        ShellNavigationCatalog.GetRelatedPages("DataShell")
            .Select(static page => page.PageTag)
            .Should()
            .Contain("ProviderHealth");

        diagnostics.Should().NotBeNull();
        diagnostics!.WorkspaceId.Should().Be("settings");
    }

    [Fact]
    public void ModuleRegistry_ShouldAssembleCanonicalCatalogPagesAndUniqueTags()
    {
        var registry = ShellPageRegistryBuilder.BuildDefault();

        registry.WorkspaceCapabilities
            .Select(static capability => capability.Workspace.Id)
            .Should()
            .Equal("trading", "portfolio", "accounting", "reporting", "strategy", "data", "settings");
        registry.Pages.Count.Should().Be(ShellNavigationCatalog.Pages.Count);
        registry.WorkspaceShells.Count.Should().Be(ShellNavigationCatalog.WorkspaceShells.Count);
        registry.Pages.Select(static page => page.PageTag)
            .Should()
            .OnlyHaveUniqueItems("page tags are route keys in the assembled shell registry");

        var aliasCollisions = registry.Pages
            .SelectMany(static page => page.Aliases.Select(alias => (Alias: alias, PageTag: page.PageTag)))
            .GroupBy(static entry => entry.Alias, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Select(entry => entry.PageTag).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        aliasCollisions.Should().BeEmpty("aliases should resolve to exactly one canonical page");
        DesktopFeatureModuleRegistry.GetModules()
            .SelectMany(static module => module.DescribePages())
            .Select(static page => page.PageTag)
            .Should()
            .Contain(["TradingShell", "PortfolioShell", "AccountingShell", "ReportingShell", "StrategyShell", "DataShell", "SettingsShell"]);
    }

    [Fact]
    public void FeatureModules_ShouldOwnAllSevenCanonicalWorkspaces()
    {
        DesktopFeatureModuleRegistry.GetModules()
            .Select(static module => module.DescribeWorkspace()?.Workspace.Id)
            .Where(static workspaceId => !string.IsNullOrWhiteSpace(workspaceId))
            .Select(static workspaceId => workspaceId!)
            .Should()
            .Equal("trading", "portfolio", "accounting", "reporting", "strategy", "data", "settings");

        ShellNavigationCatalog.BuildFallbackWorkspaceCapabilities(["trading", "portfolio", "accounting", "reporting", "strategy", "data", "settings"])
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void DataShell_ShouldUseFeatureOwnedWorkspaceShellPage()
    {
        var dataShell = ShellNavigationCatalog.GetPage("DataShell");

        dataShell.Should().NotBeNull();
        dataShell!.PageType.Should().Be(typeof(Meridian.Wpf.Features.Data.Shell.DataWorkspaceShellPage));
    }

    [Fact]
    public void SettingsShell_ShouldUseFeatureOwnedWorkspaceShellPage()
    {
        var settingsShell = ShellNavigationCatalog.GetPage("SettingsShell");
        var settingsDefinition = ShellNavigationCatalog.GetWorkspaceShell("settings");

        settingsShell.Should().NotBeNull();
        settingsShell!.PageType.Should().Be(typeof(Meridian.Wpf.Features.Settings.Shell.SettingsWorkspaceShellPage));
        settingsDefinition.Should().NotBeNull();
        settingsDefinition!.ViewModelType.Should().Be(typeof(Meridian.Wpf.Features.Settings.Shell.SettingsWorkspaceShellViewModel));
    }

    [Fact]
    public void ResolveDefaultPanes_TradingWorkbenchPreset_UsesPresetPanes()
    {
        var panes = ShellNavigationCatalog.ResolveDefaultPanes(new WorkspaceShellState(
            WorkspaceId: "trading",
            LayoutId: "trading-cockpit",
            DisplayName: "Trading Cockpit",
            LayoutScopeKey: "alpha-credit",
            WindowMode: BoundedWindowMode.WorkbenchPreset,
            LayoutPresetId: "__workbench__",
            HasPrimaryContext: true));

        panes.Select(pane => pane.PageTag).Should().ContainInOrder(
            "LiveData",
            "OrderBook",
            "PositionBlotter",
            "RunRisk",
            "TradingHours");
        panes.Last().OpenWithoutBoundParameter.Should().BeTrue();
    }

    [Fact]
    public void TradingShellRelatedPages_ShouldExposePortfolioContinuity()
    {
        var relatedPages = ShellNavigationCatalog
            .GetRelatedPages("TradingShell")
            .Select(static page => page.PageTag)
            .ToArray();

        relatedPages.Should().ContainInOrder("LiveData", "OrderBook", "PositionBlotter", "RunRisk");
        relatedPages.Should().Contain("PortfolioShell");
    }

    [Fact]
    public void BacktestLeanLiveDataAndPortfolioImport_ShouldCrossLinkForWorkflowHandoffs()
    {
        ShellNavigationCatalog.GetRelatedPages("Backtest")
            .Select(static page => page.PageTag)
            .Should()
            .Contain(["LeanIntegration", "LiveData", "PortfolioImport"]);

        ShellNavigationCatalog.GetRelatedPages("LeanIntegration")
            .Select(static page => page.PageTag)
            .Should()
            .Contain(["Backtest", "LiveData", "PortfolioImport"]);

        ShellNavigationCatalog.GetRelatedPages("LiveData")
            .Select(static page => page.PageTag)
            .Should()
            .Contain(["Backtest", "LeanIntegration", "PortfolioImport"]);

        ShellNavigationCatalog.GetRelatedPages("PortfolioImport")
            .Select(static page => page.PageTag)
            .Should()
            .Contain(["Backtest", "LeanIntegration", "LiveData"]);
    }

    [Fact]
    public void OperatorRoutes_ShouldUseSevenWorkspaceLandingLabels()
    {
        ShellNavigationCatalog.GetPage("TradingShell")!.Title.Should().Be("Trading Workspace");
        ShellNavigationCatalog.GetPage("PortfolioShell")!.Title.Should().Be("Portfolio Workspace");
        ShellNavigationCatalog.GetPage("AccountingShell")!.Title.Should().Be("Accounting Workspace");
        ShellNavigationCatalog.GetPage("ReportingShell")!.Title.Should().Be("Reporting Workspace");
        ShellNavigationCatalog.GetPage("StrategyShell")!.Title.Should().Be("Strategy Workspace");
        ShellNavigationCatalog.GetPage("DataShell")!.Title.Should().Be("Data Workspace");
        ShellNavigationCatalog.GetPage("SettingsShell")!.Title.Should().Be("Settings Workspace");

        ShellNavigationCatalog.GetPage("ResearchShell").Should().BeSameAs(ShellNavigationCatalog.GetPage("StrategyShell"));
        ShellNavigationCatalog.GetPage("DataOperationsShell").Should().BeSameAs(ShellNavigationCatalog.GetPage("DataShell"));
        ShellNavigationCatalog.GetPage("GovernanceShell").Should().BeSameAs(ShellNavigationCatalog.GetPage("AccountingShell"));
        ShellNavigationCatalog.GetPage("AccountingApprovals").Should().BeSameAs(ShellNavigationCatalog.GetPage("FundAuditTrail"));

        ShellNavigationCatalog.GetPage("Workspaces")!.SectionLabel.Should().Be("Workspace layouts");
        ShellNavigationCatalog.GetPage("Dashboard")!.Title.Should().Be("Reporting dashboard");
    }

    [Fact]
    public void ShellNavigationCatalogSource_ShouldIsolateLegacyWorkspaceAliases()
    {
        var catalogSource = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Models\ShellNavigationCatalog.cs"));
        var strategySource = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Models\ShellNavigationCatalog.Strategy.cs"));
        var accountingSource = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Models\ShellNavigationCatalog.Accounting.cs"));

        catalogSource.Should().Contain("LegacyStrategyShellAliases");
        catalogSource.Should().Contain("LegacyStrategyAutomationAliases");
        catalogSource.Should().Contain("LegacyAccountingShellAliases");
        strategySource.Should().Contain("LegacyStrategyShellAliases");
        strategySource.Should().Contain("LegacyStrategyAutomationAliases");
        strategySource.Should().NotContain("[\"ResearchShell\", \"ResearchWorkspace\"]");
        strategySource.Should().NotContain("[\"ResearchAutomation\"]");
        accountingSource.Should().Contain("LegacyAccountingShellAliases");
        accountingSource.Should().NotContain("[\"GovernanceShell\", \"GovernanceWorkspace\", \"AccountingWorkspace\"]");
    }

    private static IEnumerable<WorkspacePaneDefinition> EnumeratePanes(WorkspaceShellDefinition shell)
        => shell.DefaultPanes
            .Concat(shell.ContextlessPanes)
            .Concat(shell.PresetPanes.Values.SelectMany(static panes => panes));

    private static string GetRepositoryFilePath(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }

    private static void AssertDoesNotContainBannedTerms(string value, string scope)
    {
        foreach (var term in ShellNavigationTextStyleGuide.BannedJargonTerms)
        {
            value.Contains(term, StringComparison.OrdinalIgnoreCase)
                .Should().BeFalse($"{scope} should not include banned jargon '{term}'");
        }
    }
}
