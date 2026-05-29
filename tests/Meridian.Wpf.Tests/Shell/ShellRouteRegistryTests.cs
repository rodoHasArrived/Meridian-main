using Meridian.Ui.Shared.Workflows;
using Meridian.Wpf.Shell.Services;

namespace Meridian.Wpf.Tests.Shell;

public sealed class ShellRouteRegistryTests
{
    [Theory]
    [InlineData("StrategyShell", "StrategyShell", "strategy")]
    [InlineData("ResearchShell", "StrategyShell", "strategy")]
    [InlineData("DataOperationsShell", "DataShell", "data")]
    [InlineData("GovernanceShell", "AccountingShell", "accounting")]
    [InlineData("OperationsContinuity", "FundLedger", "accounting")]
    [InlineData("OperationsClose", "FundLedger", "accounting")]
    [InlineData("Blotter", "PositionBlotter", "trading")]
    public void GetRoute_CurrentAndCompatibilityTags_ResolveToCanonicalRoute(
        string requestedTag,
        string canonicalTag,
        string workspaceId)
    {
        var registry = new ShellRouteRegistry();

        var route = registry.GetRoute(requestedTag);

        route.Should().NotBeNull();
        route!.PageTag.Should().Be(canonicalTag);
        route.WorkspaceId.Should().Be(workspaceId);
        registry.GetCanonicalPageTag(requestedTag).Should().Be(canonicalTag);
        registry.InferWorkspaceIdForPageTag(requestedTag).Should().Be(workspaceId);
    }

    [Fact]
    public void GetRoutesForWorkspace_DataWorkspace_ReturnsCanonicalDataRoutesOnly()
    {
        var registry = new ShellRouteRegistry();

        var routes = registry.GetRoutesForWorkspace("data");

        routes.Should().NotBeEmpty();
        routes.Should().OnlyContain(route => route.WorkspaceId == "data");
        routes.Select(route => route.PageTag).Should().Contain(["DataShell", "Provider", "ProviderHealth"]);
        routes.SelectMany(route => route.Aliases).Should().Contain("DataOperationsShell");
    }

    [Fact]
    public void IsRegistered_UnknownTag_ReturnsFalse()
    {
        var registry = new ShellRouteRegistry();

        registry.IsRegistered("NotARegisteredShellRoute").Should().BeFalse();
        registry.GetPageType("NotARegisteredShellRoute").Should().BeNull();
    }

    [Fact]
    public void BuiltInWorkflowTargets_AllResolveToVisibleWpfRoutes()
    {
        var registry = new ShellRouteRegistry();
        var workflows = new BuiltInWorkflowDefinitionProvider().GetWorkflowDefinitions();
        var targetTags = workflows
            .Select(workflow => workflow.EntryPageTag)
            .Concat(workflows.SelectMany(workflow => workflow.Actions.Select(action => action.TargetPageTag)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        targetTags.Should().NotBeEmpty();
        targetTags.Should().OnlyContain(
            tag => registry.IsRegistered(tag),
            "shared workflow targets must resolve in the desktop shell instead of becoming browser-only actions");

        var visiblePrimaryTargets = targetTags
            .Select(registry.GetRoute)
            .Where(route => route is not null)
            .Select(route => route!)
            .Where(route => route.VisibilityTier == Meridian.Wpf.Models.ShellNavigationVisibilityTier.Primary)
            .Select(route => route.PageTag)
            .ToArray();

        visiblePrimaryTargets.Should().Contain(["TradingShell", "FundLedger", "FundReconciliation", "ReportingShell", "DataShell"],
            "cross-surface workflow targets need visible operator entry points in WPF");
    }
}
