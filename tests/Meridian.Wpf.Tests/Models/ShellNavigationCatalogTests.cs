using Meridian.Ui.Services;
using Meridian.Wpf.Models;

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
    public void ResolveDefaultPanes_GovernanceWithoutPrimaryContext_UsesContextlessPanes()
    {
        var panes = ShellNavigationCatalog.ResolveDefaultPanes(new WorkspaceShellState(
            WorkspaceId: "governance",
            LayoutId: "governance-workspace",
            DisplayName: "Governance Workspace",
            LayoutScopeKey: null,
            WindowMode: BoundedWindowMode.DockFloat,
            LayoutPresetId: null,
            HasPrimaryContext: false));

        panes.Select(pane => pane.PageTag).Should().ContainInOrder("Diagnostics", "NotificationCenter", "SystemHealth");
    }

    [Fact]
    public void ProviderHealth_ShouldBelongToDataOperationsWhileDiagnosticsStaysGovernanceOwned()
    {
        var providerHealth = ShellNavigationCatalog.GetPage("ProviderHealth");
        var diagnostics = ShellNavigationCatalog.GetPage("Diagnostics");

        providerHealth.Should().NotBeNull();
        providerHealth!.WorkspaceId.Should().Be("data-operations");
        ShellNavigationCatalog.GetPagesForWorkspace("data-operations")
            .Select(static page => page.PageTag)
            .Should()
            .Contain("ProviderHealth");
        ShellNavigationCatalog.GetRelatedPages("DataOperationsShell")
            .Select(static page => page.PageTag)
            .Should()
            .Contain("ProviderHealth");

        diagnostics.Should().NotBeNull();
        diagnostics!.WorkspaceId.Should().Be("governance");
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
            "RunPortfolio",
            "PositionBlotter",
            "RunRisk",
            "RunLedger",
            "FundTrialBalance");
        panes.Last().OpenWithoutBoundParameter.Should().BeTrue();
    }

    [Fact]
    public void TradingShellRelatedPages_ShouldExposePortfolioContinuity()
    {
        var relatedPages = ShellNavigationCatalog
            .GetRelatedPages("TradingShell")
            .Select(static page => page.PageTag)
            .ToArray();

        relatedPages.Should().ContainInOrder("RunPortfolio", "PositionBlotter", "RunRisk");
        relatedPages.Should().Contain("FundPortfolio");
    }

    [Fact]
    public void OperatorRoutes_ShouldUseFourWorkspaceLandingLabels()
    {
        ShellNavigationCatalog.GetPage("ResearchShell")!.Title.Should().Be("Research Workspace");
        ShellNavigationCatalog.GetPage("TradingShell")!.Title.Should().Be("Trading Workspace");
        ShellNavigationCatalog.GetPage("DataOperationsShell")!.Title.Should().Be("Data Operations Workspace");
        ShellNavigationCatalog.GetPage("GovernanceShell")!.Title.Should().Be("Governance Workspace");

        ShellNavigationCatalog.GetPage("Workspaces")!.SectionLabel.Should().Be("Workspace layouts");
        ShellNavigationCatalog.GetPage("Dashboard")!.Title.Should().Be("Research operations");
    }

    private static IEnumerable<WorkspacePaneDefinition> EnumeratePanes(WorkspaceShellDefinition shell)
        => shell.DefaultPanes
            .Concat(shell.ContextlessPanes)
            .Concat(shell.PresetPanes.Values.SelectMany(static panes => panes));

    private static void AssertDoesNotContainBannedTerms(string value, string scope)
    {
        foreach (var term in ShellNavigationTextStyleGuide.BannedJargonTerms)
        {
            value.Contains(term, StringComparison.OrdinalIgnoreCase)
                .Should().BeFalse($"{scope} should not include banned jargon '{term}'");
        }
    }
}
