using System;
using System.Collections.Generic;
using System.Linq;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services.Contracts;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Models;
using Meridian.Wpf.ViewModels;
using System.Windows.Controls;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class HomeWorkspaceViewModelTests
{
    [Fact]
    public void WorkspaceNavigationItems_ExposeCanonicalRootWorkspaces()
    {
        var navigation = new StubNavigationService();
        var viewModel = new HomeWorkspaceViewModel(navigation);

        viewModel.WorkspaceNavigationItems.Select(static item => item.Title)
            .Should()
            .Equal("Trading", "Portfolio", "Accounting", "Reporting", "Strategy", "Data", "Settings");

        viewModel.WorkspaceNavigationItems.Select(static item => item.TargetPageTag)
            .Should()
            .Equal("TradingShell", "PortfolioShell", "AccountingShell", "ReportingShell", "StrategyShell", "DataShell", "SettingsShell");
    }

    [Fact]
    public void WorkspaceNavigationCommand_NavigatesToTargetShell()
    {
        var navigation = new StubNavigationService();
        var viewModel = new HomeWorkspaceViewModel(navigation);

        viewModel.WorkspaceNavigationItems.Single(static item => item.Title == "Accounting").NavigateCommand.Execute(null);

        navigation.LastPageTag.Should().Be("AccountingShell");
    }

    [Fact]
    public void BuildSections_GroupsRequiredStatusAreasAndOrdersBySeverity()
    {
        var summaries = new[]
        {
            Summary("data", "Data", WorkspaceTone.Info, WorkspaceTone.Danger, true),
            Summary("trading", "Trading", WorkspaceTone.Warning, WorkspaceTone.Info, false),
            Summary("accounting", "Accounting", WorkspaceTone.Info, WorkspaceTone.Warning, true),
            Summary("reporting", "Reporting", WorkspaceTone.Success, WorkspaceTone.Neutral, false),
            Summary("portfolio", "Portfolio", WorkspaceTone.Warning, WorkspaceTone.Neutral, false),
            Summary("strategy", "Strategy", WorkspaceTone.Info, WorkspaceTone.Neutral, false),
            Summary("settings", "Settings", WorkspaceTone.Success, WorkspaceTone.Neutral, false)
        };

        var sections = HomeWorkspaceViewModel.BuildSections(summaries);

        sections.Select(static section => section.Title).Should().Equal(
            "Provider health",
            "Data freshness",
            "Reconciliation",
            "Approvals",
            "Accounting/reporting readiness",
            "Recent activity");
        sections[0].Items.Select(static item => item.Title).Should().Equal("Data", "Trading");
        sections[0].Items[0].Tone.Should().Be(WorkspaceTone.Danger);
        sections[0].Items[0].IsBlocking.Should().BeTrue();
    }

    private static WorkspaceWorkflowSummary Summary(string id, string title, string statusTone, string blockerTone, bool blocking)
        => new(
            id,
            title,
            $"{title} status",
            $"{title} detail",
            statusTone,
            new WorkflowNextAction($"Open {title}", "detail", $"{title}Shell", WorkspaceTone.Primary),
            new WorkflowBlockerSummary($"{id}-blocker", $"{title} blocker", $"{title} blocker detail", blockerTone, blocking),
            []);

    private sealed class StubNavigationService : INavigationService
    {
        public string? LastPageTag { get; private set; }
        public bool IsInitialized => true;
        public bool CanGoBack => false;
        public event EventHandler<NavigationEventArgs>? Navigated;
        public void Initialize(Frame frame) { }
        public bool NavigateTo(string pageTag, object? parameter = null)
        {
            LastPageTag = pageTag;
            Navigated?.Invoke(this, new NavigationEventArgs { PageTag = pageTag, Parameter = parameter });
            return true;
        }
        public bool NavigateTo(Type pageType, object? parameter = null) => true;
        public void GoBack() { }
        public Type? GetPageType(string pageTag) => null;
        public IReadOnlyList<NavigationEntry> GetBreadcrumbs() => [];
        public IReadOnlyCollection<string> GetRegisteredPages() => [];
        public bool IsPageRegistered(string pageTag) => true;
    }
}
