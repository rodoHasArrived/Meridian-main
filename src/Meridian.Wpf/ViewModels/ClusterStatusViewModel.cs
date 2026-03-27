using System.Collections.ObjectModel;
using System.Windows.Media;
using Meridian.Application.Coordination;
using Meridian.Application.Logging;
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

    private ObservableCollection<ClusterNodeItem> _nodes = new();
    private bool _isMeshEnabled;
    private string _localNodeId = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isLoading;

    public ClusterStatusViewModel(ILeaseManager? leaseManager = null)
    {
        _leaseManager = leaseManager;
        _log = LoggingSetup.ForContext<ClusterStatusViewModel>();

        _isMeshEnabled = leaseManager?.Enabled ?? false;
        _localNodeId = leaseManager?.InstanceId ?? Environment.MachineName;
        
        if (!_isMeshEnabled)
        {
            _statusMessage = "Mesh mode disabled — running standalone";
        }

        StartPolling();
    }

    public ObservableCollection<ClusterNodeItem> Nodes
    {
        get => _nodes;
        private set => SetProperty(ref _nodes, value);
    }

    public bool IsMeshEnabled
    {
        get => _isMeshEnabled;
        private set => SetProperty(ref _isMeshEnabled, value);
    }

    public string LocalNodeId
    {
        get => _localNodeId;
        private set => SetProperty(ref _localNodeId, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
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

    private async Task RefreshClusterStatusAsync(CancellationToken ct)
    {
        if (_leaseManager is null)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var snapshot = await _leaseManager.GetSnapshotAsync(ct).ConfigureAwait(false);
            
            // Group leases by owner instance (extract instance from resource paths or lease owner)
            var nodeGroups = snapshot.HeldLeases
                .GroupBy(l => l.InstanceId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            // Build cluster node items
            var nodes = new ObservableCollection<ClusterNodeItem>();
            
            foreach (var (nodeId, leases) in nodeGroups)
            {
                var symbolLeases = leases.Count(l => l.ResourceId.StartsWith("symbols/", StringComparison.OrdinalIgnoreCase));
                var isLocal = string.Equals(nodeId, LocalNodeId, StringComparison.OrdinalIgnoreCase);
                
                // Calculate time since last renewal
                var lastRenewed = leases.Max(l => l.LastRenewedAtUtc);
                var ageSpan = DateTimeOffset.UtcNow - lastRenewed;
                var ageStr = FormatTimeAgo(ageSpan);

                var statusColor = isLocal
                    ? new SolidColorBrush(Color.FromRgb(76, 175, 80))     // Green
                    : new SolidColorBrush(Color.FromRgb(255, 193, 7));    // Amber

                nodes.Add(new ClusterNodeItem
                {
                    NodeId = nodeId,
                    OwnedSymbolCount = symbolLeases,
                    LastSeenAgo = ageStr,
                    IsLocal = isLocal,
                    StatusColor = statusColor
                });
            }

            Nodes = nodes;

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

    private static string FormatTimeAgo(TimeSpan age)
    {
        return age switch
        {
            _ when age.TotalSeconds < 60 => $"{(int)age.TotalSeconds}s ago",
            _ when age.TotalMinutes < 60 => $"{(int)age.TotalMinutes}m ago",
            _ when age.TotalHours < 24 => $"{(int)age.TotalHours}h ago",
            _ => $"{(int)age.TotalDays}d ago"
        };
    }

    public void Dispose()
    {
        _statusTimer?.Dispose();
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
