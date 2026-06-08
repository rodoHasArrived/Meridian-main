using Meridian.Contracts.Coordination;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class ClusterStatusViewModelTests
{
    [Fact]
    public void BuildClusterNodeItems_ShouldGroupLeasesAndPlaceLocalNodeFirst()
    {
        var capturedAtUtc = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
        var leases = new[]
        {
            BuildLease("symbols/MSFT", "peer-b", capturedAtUtc.AddMinutes(-3)),
            BuildLease("jobs/nightly-close", "peer-b", capturedAtUtc.AddMinutes(-2)),
            BuildLease("symbols/AAPL", "node-a", capturedAtUtc.AddSeconds(-25))
        };

        var rows = ClusterStatusViewModel.BuildClusterNodeItems(leases, "node-a", capturedAtUtc);

        rows.Should().HaveCount(2);
        rows[0].NodeId.Should().Be("node-a");
        rows[0].RoleText.Should().Be("Local");
        rows[0].OwnedSymbolCount.Should().Be(1);
        rows[0].LastSeenAgo.Should().Be("25s ago");
        rows[1].NodeId.Should().Be("peer-b");
        rows[1].OwnedSymbolCount.Should().Be(1);
    }

    [Fact]
    public void BuildRefreshTooltip_ShouldExplainFailClosedRefreshStates()
    {
        ClusterStatusViewModel.BuildRefreshTooltip(isMeshEnabled: true, hasLeaseManager: false, isLoading: false)
            .Should().Contain("no coordination lease manager");
        ClusterStatusViewModel.BuildRefreshTooltip(isMeshEnabled: false, hasLeaseManager: true, isLoading: false)
            .Should().Contain("mesh mode is off");
        ClusterStatusViewModel.BuildRefreshTooltip(isMeshEnabled: true, hasLeaseManager: true, isLoading: true)
            .Should().Contain("already reading");
        ClusterStatusViewModel.BuildRefreshTooltip(isMeshEnabled: true, hasLeaseManager: true, isLoading: false)
            .Should().Contain("Refresh cluster ownership");
    }

    [Fact]
    public void BuildClusterPostureState_ShouldBlockStandaloneAndReadyActiveMesh()
    {
        var standalone = ClusterStatusViewModel.BuildClusterPostureState(
            isMeshEnabled: false,
            hasLeaseManager: true,
            isLoading: false,
            nodeCount: 0,
            statusMessage: "Mesh mode disabled - running standalone",
            localNodeId: "node-a");
        var active = ClusterStatusViewModel.BuildClusterPostureState(
            isMeshEnabled: true,
            hasLeaseManager: true,
            isLoading: false,
            nodeCount: 2,
            statusMessage: "Cluster active: 2 node(s)",
            localNodeId: "node-a");

        standalone.Title.Should().Be("Mesh mode disabled");
        standalone.ActionText.Should().Be("Enable mesh mode");
        active.Title.Should().Be("Cluster active");
        active.EvidenceText.Should().Be("Cluster active: 2 node(s)");
    }

    [Fact]
    public async Task RefreshAsync_ShouldPopulateDenseTableRowsInspectorsAndSelectionState()
    {
        var capturedAtUtc = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
        using var viewModel = new ClusterStatusViewModel(new StubLeaseManager(
            enabled: true,
            instanceId: "node-a",
            snapshot: new CoordinationSnapshot
            {
                Enabled = true,
                InstanceId = "node-a",
                Mode = "SharedStorage",
                HeldLeases =
                [
                    BuildLease("symbols/AAPL", "node-a", capturedAtUtc.AddMinutes(-1)),
                    BuildLease("symbols/MSFT", "peer-b", capturedAtUtc.AddMinutes(-5))
                ],
                CapturedAtUtc = capturedAtUtc
            }));

        await viewModel.RefreshAsync();

        viewModel.Nodes.Should().HaveCount(2);
        viewModel.NodesTable.Rows.Should().BeSameAs(viewModel.Nodes);
        viewModel.StatusMessage.Should().Be("Cluster active: 2 node(s)");
        viewModel.ClusterPostureState.Title.Should().Be("Cluster active");
        viewModel.ClusterSummaryInspector.Facts.Should().Contain(fact => fact.Label == "Visible nodes" && fact.Value == "2");

        viewModel.SelectedNode = viewModel.Nodes.Single(node => node.NodeId == "peer-b");

        viewModel.SelectedNodeInspector.Title.Should().Be("peer-b");
        viewModel.SelectedNodeInspector.Badge!.Value.Should().Be("Peer");
        viewModel.ClusterActionInspector.Facts.Should().Contain(fact => fact.Label == "Mutation" && fact.Value == "Read-only");
    }

    [Fact]
    public void ClusterStatusPageSource_ShouldUseCompactDenseTableInspectorsAndActionTooltip()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\ClusterStatusPage.xaml"));
        var viewModel = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\ClusterStatusViewModel.cs"));

        xaml.Should().NotContain("<DataGrid");
        xaml.Should().Contain("ClusterStatusActionStrip");
        xaml.Should().Contain("ClusterStatusPostureCard");
        xaml.Should().Contain("ClusterStatusWorkbench");
        xaml.Should().Contain("DenseDataGridControl");
        xaml.Should().Contain("InspectorPanelControl");
        xaml.Should().Contain("ClusterStatusNodesGrid");
        xaml.Should().Contain("ClusterStatusSummaryInspector");
        xaml.Should().Contain("ClusterStatusSelectionInspector");
        xaml.Should().Contain("ClusterStatusActionInspector");
        xaml.Should().Contain("Command=\"{Binding RefreshCommand}\"");
        xaml.Should().Contain("ToolTip=\"{Binding RefreshTooltip}\"");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
        xaml.Should().Contain("Table=\"{Binding NodesTable}\"");
        xaml.Should().Contain("SelectedItem=\"{Binding SelectedNode, Mode=TwoWay}\"");
        viewModel.Should().Contain("public WorkstationTableModel<ClusterNodeItem> NodesTable { get; }");
        viewModel.Should().Contain("public InspectorPanelModel ClusterSummaryInspector");
        viewModel.Should().Contain("public InspectorPanelModel SelectedNodeInspector");
        viewModel.Should().Contain("public InspectorPanelModel ClusterActionInspector");
    }

    private static LeaseRecord BuildLease(string resourceId, string instanceId, DateTimeOffset lastRenewedAtUtc)
        => new(
            resourceId,
            instanceId,
            LeaseVersion: 1,
            AcquiredAtUtc: lastRenewedAtUtc.AddMinutes(-10),
            ExpiresAtUtc: lastRenewedAtUtc.AddMinutes(5),
            LastRenewedAtUtc: lastRenewedAtUtc);

    private sealed class StubLeaseManager : ILeaseManager
    {
        private readonly CoordinationSnapshot _snapshot;

        public StubLeaseManager(bool enabled, string instanceId, CoordinationSnapshot snapshot)
        {
            Enabled = enabled;
            InstanceId = instanceId;
            _snapshot = snapshot;
        }

        public bool Enabled { get; }

        public string InstanceId { get; }

        public Task<LeaseAcquireResult> TryAcquireAsync(string resourceId, CancellationToken ct = default)
            => Task.FromResult(new LeaseAcquireResult(false, false, null, null, null, "Test stub"));

        public Task<bool> RenewAsync(string resourceId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> ReleaseAsync(string resourceId, CancellationToken ct = default)
            => Task.FromResult(false);

        public bool HoldsLease(string resourceId)
            => false;

        public Task<CoordinationSnapshot> GetSnapshotAsync(CancellationToken ct = default)
            => Task.FromResult(_snapshot);
    }
}
