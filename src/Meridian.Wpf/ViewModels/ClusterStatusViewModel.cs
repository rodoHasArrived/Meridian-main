using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Media;
using Meridian.Contracts.Coordination;
using Meridian.Core.Logging;
using Meridian.Wpf.Models;
using Meridian.Wpf.Workstation.Models;
using Serilog;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Item representing a single cluster node in the coordination mesh.
/// </summary>
public sealed record ClusterNodeItem
{
    public required string NodeId { get; init; }
    public required int OwnedSymbolCount { get; init; }
    public required string LastSeenAgo { get; init; }
    public required bool IsLocal { get; init; }
    public required Brush StatusColor { get; init; }

    public string RoleText => IsLocal ? "Local" : "Peer";
}

/// <summary>
/// ViewModel for displaying multi-instance collector mesh cluster status.
/// </summary>
public sealed class ClusterStatusViewModel : BindableBase, IDisposable
{
    private readonly ILeaseManager? _leaseManager;
    private readonly ILogger _log;
    private PeriodicTimer? _statusTimer;
    private CancellationTokenSource? _cts;

    private ClusterNodeItem? _selectedNode;
    private bool _isMeshEnabled;
    private string _localNodeId = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isLoading;
    private WorkstationStateModel _clusterPostureState = BuildClusterPostureState(
        isMeshEnabled: false,
        hasLeaseManager: false,
        isLoading: false,
        nodeCount: 0,
        statusMessage: "Cluster status unavailable",
        localNodeId: string.Empty);
    private InspectorPanelModel _clusterSummaryInspector = BuildClusterSummaryInspector(
        isMeshEnabled: false,
        hasLeaseManager: false,
        isLoading: false,
        nodeCount: 0,
        statusMessage: "Cluster status unavailable",
        localNodeId: string.Empty);
    private InspectorPanelModel _selectedNodeInspector = BuildSelectedNodeInspector(null);
    private InspectorPanelModel _clusterActionInspector = BuildClusterActionInspector(
        isMeshEnabled: false,
        hasLeaseManager: false,
        isLoading: false,
        nodeCount: 0,
        statusMessage: "Cluster status unavailable");

    public ClusterStatusViewModel(ILeaseManager? leaseManager = null)
    {
        _leaseManager = leaseManager;
        _log = LoggingSetup.ForContext<ClusterStatusViewModel>();

        _isMeshEnabled = leaseManager?.Enabled ?? false;
        _localNodeId = leaseManager?.InstanceId ?? Environment.MachineName;
        _statusMessage = _isMeshEnabled
            ? "Cluster snapshot pending"
            : "Mesh mode disabled - running standalone";

        NodesTable = new WorkstationTableModel<ClusterNodeItem>(
            Nodes,
            [
                new("Node ID", nameof(ClusterNodeItem.NodeId), 260),
                new("Role", nameof(ClusterNodeItem.RoleText), 90),
                new("Symbols", nameof(ClusterNodeItem.OwnedSymbolCount), 95),
                new("Last Seen", nameof(ClusterNodeItem.LastSeenAgo), 110)
            ],
            "Cluster ownership nodes",
            "No cluster nodes",
            "Enable mesh coordination and refresh the lease snapshot before operator ownership rows appear.");
        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(), () => CanRefreshCluster);

        UpdateClusterPresentation();

        StartPolling();
    }

    public ObservableCollection<ClusterNodeItem> Nodes { get; } = [];

    public WorkstationTableModel<ClusterNodeItem> NodesTable { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public ClusterNodeItem? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (SetProperty(ref _selectedNode, value))
            {
                SelectedNodeInspector = BuildSelectedNodeInspector(value);
            }
        }
    }

    public bool IsMeshEnabled
    {
        get => _isMeshEnabled;
        private set
        {
            if (SetProperty(ref _isMeshEnabled, value))
            {
                UpdateClusterPresentation();
            }
        }
    }

    public string LocalNodeId
    {
        get => _localNodeId;
        private set => SetProperty(ref _localNodeId, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                UpdateClusterPresentation();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                UpdateClusterPresentation();
            }
        }
    }

    public bool CanRefreshCluster => IsMeshEnabled && _leaseManager is not null && !IsLoading;

    public string RefreshTooltip => BuildRefreshTooltip(IsMeshEnabled, _leaseManager is not null, IsLoading);

    public string ClusterActionStateTitle => CanRefreshCluster
        ? "Cluster refresh ready"
        : IsLoading
            ? "Cluster refresh running"
            : "Cluster refresh unavailable";

    public string ClusterActionStateDetail => CanRefreshCluster
        ? "Refresh reads the latest coordination leases without changing ownership."
        : RefreshTooltip;

    public WorkstationStateModel ClusterPostureState
    {
        get => _clusterPostureState;
        private set => SetProperty(ref _clusterPostureState, value);
    }

    public InspectorPanelModel ClusterSummaryInspector
    {
        get => _clusterSummaryInspector;
        private set => SetProperty(ref _clusterSummaryInspector, value);
    }

    public InspectorPanelModel SelectedNodeInspector
    {
        get => _selectedNodeInspector;
        private set => SetProperty(ref _selectedNodeInspector, value);
    }

    public InspectorPanelModel ClusterActionInspector
    {
        get => _clusterActionInspector;
        private set => SetProperty(ref _clusterActionInspector, value);
    }

    private void StartPolling()
    {
        if (!IsMeshEnabled)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _statusTimer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        _ = PollStatusLoopAsync(_cts.Token);
    }

    private async Task PollStatusLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _statusTimer!.WaitForNextTickAsync(ct).ConfigureAwait(false);
                await RefreshClusterStatusAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Error polling cluster status");
            }
        }
    }

    internal Task RefreshAsync(CancellationToken ct = default)
        => RefreshClusterStatusAsync(ct);

    private async Task RefreshClusterStatusAsync(CancellationToken ct)
    {
        if (!IsMeshEnabled || _leaseManager is null)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var snapshot = await _leaseManager.GetSnapshotAsync(ct).ConfigureAwait(false);
            var nodes = BuildClusterNodeItems(snapshot.HeldLeases, LocalNodeId, DateTimeOffset.UtcNow);
            ReplaceNodes(nodes);

            if (nodes.Count == 0)
            {
                StatusMessage = "No active nodes in cluster";
            }
            else
            {
                StatusMessage = $"Cluster active: {nodes.Count} node(s)";
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    internal static IReadOnlyList<ClusterNodeItem> BuildClusterNodeItems(
        IReadOnlyList<LeaseRecord> leases,
        string localNodeId,
        DateTimeOffset capturedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(leases);

        return leases
            .GroupBy(l => l.InstanceId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var nodeId = group.Key;
                var symbolLeases = group.Count(l => l.ResourceId.StartsWith("symbols/", StringComparison.OrdinalIgnoreCase));
                var isLocal = string.Equals(nodeId, localNodeId, StringComparison.OrdinalIgnoreCase);
                var lastRenewed = group.Max(l => l.LastRenewedAtUtc);
                var ageSpan = capturedAtUtc - lastRenewed;
                var statusColor = isLocal
                    ? new SolidColorBrush(Color.FromRgb(76, 175, 80))
                    : new SolidColorBrush(Color.FromRgb(255, 193, 7));

                return new ClusterNodeItem
                {
                    NodeId = nodeId,
                    OwnedSymbolCount = symbolLeases,
                    LastSeenAgo = FormatTimeAgo(ageSpan),
                    IsLocal = isLocal,
                    StatusColor = statusColor
                };
            })
            .OrderByDescending(node => node.IsLocal)
            .ThenBy(node => node.NodeId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string BuildRefreshTooltip(bool isMeshEnabled, bool hasLeaseManager, bool isLoading)
    {
        if (!hasLeaseManager)
        {
            return "Cluster refresh is disabled because no coordination lease manager is registered.";
        }

        if (!isMeshEnabled)
        {
            return "Cluster refresh is disabled while mesh mode is off.";
        }

        if (isLoading)
        {
            return "Cluster refresh is already reading the coordination lease snapshot.";
        }

        return "Refresh cluster ownership from the coordination lease snapshot.";
    }

    internal static WorkstationStateModel BuildClusterPostureState(
        bool isMeshEnabled,
        bool hasLeaseManager,
        bool isLoading,
        int nodeCount,
        string statusMessage,
        string localNodeId)
    {
        if (!hasLeaseManager)
        {
            return WorkstationStateModel.Blocked(
                "Cluster status unavailable",
                "No coordination lease manager is registered for this desktop session.",
                new WorkstationActionPostureModel(
                    "No refresh target",
                    "Start Meridian with coordination services registered before reviewing cluster ownership.",
                    "Data > Cluster Status",
                    "Desktop shell",
                    WorkstationReadinessTone.Blocked,
                    WorkspaceTone.Danger));
        }

        if (!isMeshEnabled)
        {
            return WorkstationStateModel.Blocked(
                "Mesh mode disabled",
                "This desktop session is running standalone, so cluster ownership rows stay hidden.",
                new WorkstationActionPostureModel(
                    "Enable mesh mode",
                    "Configure coordination storage before using multi-instance ownership monitoring.",
                    "Coordination configuration",
                    "Data operations",
                    WorkstationReadinessTone.Blocked,
                    WorkspaceTone.Warning));
        }

        if (isLoading)
        {
            return WorkstationStateModel.Loading(
                "Refreshing cluster snapshot",
                "Reading coordination leases and retaining the current table until the refresh completes.");
        }

        if (nodeCount == 0)
        {
            return WorkstationStateModel.Empty(
                "No active cluster nodes",
                "Refresh returned no held leases. Confirm collectors are running and holding symbol, schedule, job, or leader leases.",
                "Refresh snapshot",
                "Coordination lease snapshot");
        }

        return WorkstationStateModel.Ready(
            "Cluster active",
            $"{nodeCount} node(s) are holding coordination leases.",
            "Monitor ownership",
            string.IsNullOrWhiteSpace(localNodeId) ? "Local node unavailable" : $"Local: {localNodeId}",
            statusMessage);
    }

    internal static InspectorPanelModel BuildClusterSummaryInspector(
        bool isMeshEnabled,
        bool hasLeaseManager,
        bool isLoading,
        int nodeCount,
        string statusMessage,
        string localNodeId)
        => new()
        {
            Title = "Cluster posture",
            Subtitle = string.IsNullOrWhiteSpace(localNodeId) ? "Local node unavailable" : localNodeId,
            Detail = statusMessage,
            Badge = new WorkstationBadgeModel(
                "Mesh",
                hasLeaseManager && isMeshEnabled ? "Enabled" : "Blocked",
                hasLeaseManager && isMeshEnabled ? "\uE73E" : "\uE783",
                hasLeaseManager && isMeshEnabled ? WorkspaceTone.Success : WorkspaceTone.Warning),
            Facts =
            [
                new("Refresh", isLoading ? "Running" : "Idle"),
                new("Visible nodes", nodeCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new("Mode", isMeshEnabled ? "Mesh" : "Standalone"),
                new("Lease manager", hasLeaseManager ? "Registered" : "Missing")
            ]
        };

    internal static InspectorPanelModel BuildSelectedNodeInspector(ClusterNodeItem? selected)
    {
        if (selected is null)
        {
            return new InspectorPanelModel
            {
                Title = "No node selected",
                Subtitle = "Cluster ownership",
                Detail = "Select a node row to inspect locality, symbol ownership, and the retained lease freshness signal.",
                Badge = new WorkstationBadgeModel("Selection", "Waiting", "\uE946", WorkspaceTone.Neutral),
                Facts =
                [
                    new("Node", "Select row"),
                    new("Symbols", "-"),
                    new("Last seen", "-"),
                    new("Role", "-")
                ]
            };
        }

        return new InspectorPanelModel
        {
            Title = selected.NodeId,
            Subtitle = selected.IsLocal ? "Local collector node" : "Peer collector node",
            Detail = "Lease ownership is read from the coordination snapshot and does not mutate ownership from this screen.",
            Badge = new WorkstationBadgeModel(
                "Role",
                selected.RoleText,
                selected.IsLocal ? "\uE73E" : "\uE902",
                selected.IsLocal ? WorkspaceTone.Success : WorkspaceTone.Info),
            Facts =
            [
                new("Symbols owned", selected.OwnedSymbolCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new("Last seen", selected.LastSeenAgo),
                new("Local node", selected.IsLocal ? "Yes" : "No"),
                new("Action", selected.IsLocal ? "Monitor local leases" : "Monitor peer freshness")
            ]
        };
    }

    internal static InspectorPanelModel BuildClusterActionInspector(
        bool isMeshEnabled,
        bool hasLeaseManager,
        bool isLoading,
        int nodeCount,
        string statusMessage)
        => new()
        {
            Title = hasLeaseManager && isMeshEnabled ? "Refresh control" : "Refresh unavailable",
            Subtitle = "Coordination snapshot",
            Detail = BuildRefreshTooltip(isMeshEnabled, hasLeaseManager, isLoading),
            Badge = new WorkstationBadgeModel(
                "Refresh",
                hasLeaseManager && isMeshEnabled && !isLoading ? "Ready" : "Blocked",
                hasLeaseManager && isMeshEnabled && !isLoading ? "\uE72C" : "\uE783",
                hasLeaseManager && isMeshEnabled && !isLoading ? WorkspaceTone.Success : WorkspaceTone.Warning),
            Facts =
            [
                new("Current state", statusMessage),
                new("Rows", nodeCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new("Mutation", "Read-only"),
                new("Cadence", "5s polling")
            ]
        };

    private static string FormatTimeAgo(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        return age switch
        {
            _ when age.TotalSeconds < 60 => $"{(int)age.TotalSeconds}s ago",
            _ when age.TotalMinutes < 60 => $"{(int)age.TotalMinutes}m ago",
            _ when age.TotalHours < 24 => $"{(int)age.TotalHours}h ago",
            _ => $"{(int)age.TotalDays}d ago"
        };
    }

    private void ReplaceNodes(IReadOnlyList<ClusterNodeItem> nodes)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => ReplaceNodesOnCurrentThread(nodes));
            return;
        }

        ReplaceNodesOnCurrentThread(nodes);
    }

    private void ReplaceNodesOnCurrentThread(IReadOnlyList<ClusterNodeItem> nodes)
    {
        var selectedNodeId = SelectedNode?.NodeId;
        Nodes.Clear();
        foreach (var node in nodes)
        {
            Nodes.Add(node);
        }

        SelectedNode = selectedNodeId is null
            ? null
            : Nodes.FirstOrDefault(node => string.Equals(node.NodeId, selectedNodeId, StringComparison.OrdinalIgnoreCase));
        UpdateClusterPresentation();
    }

    private void UpdateClusterPresentation()
    {
        OnPropertyChanged(nameof(CanRefreshCluster));
        OnPropertyChanged(nameof(RefreshTooltip));
        OnPropertyChanged(nameof(ClusterActionStateTitle));
        OnPropertyChanged(nameof(ClusterActionStateDetail));

        ClusterPostureState = BuildClusterPostureState(
            IsMeshEnabled,
            _leaseManager is not null,
            IsLoading,
            Nodes.Count,
            StatusMessage,
            LocalNodeId);
        ClusterSummaryInspector = BuildClusterSummaryInspector(
            IsMeshEnabled,
            _leaseManager is not null,
            IsLoading,
            Nodes.Count,
            StatusMessage,
            LocalNodeId);
        ClusterActionInspector = BuildClusterActionInspector(
            IsMeshEnabled,
            _leaseManager is not null,
            IsLoading,
            Nodes.Count,
            StatusMessage);
        RefreshCommand?.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _statusTimer?.Dispose();
        _cts?.Dispose();
    }
}
