using System.Collections.Concurrent;
using Meridian.Contracts.Configuration;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Failover;

namespace Meridian.Application.Composition.Startup;

/// <summary>Collector-owned streaming evidence shared with the dashboard's separate container.</summary>
public sealed class StreamingProviderActivation : IDisposable
{
    private static readonly ConcurrentDictionary<string, StreamingProviderActivation> Registrations =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly string _configPath;
    private readonly IReadOnlyDictionary<string, IMarketDataClient> _clients;
    private readonly HashSet<string> _failed;
    private readonly FailoverAwareMarketDataClient? _failoverClient;
    private readonly StreamingFailoverService? _failoverService;
    private readonly object _gate = new();
    private DateTimeOffset? _startedAtUtc;
    private bool _disposed;

    internal StreamingProviderActivation(
        string configPath,
        IReadOnlyDictionary<string, IMarketDataClient> clients,
        IEnumerable<string> failed,
        FailoverAwareMarketDataClient? failoverClient,
        StreamingFailoverService? failoverService)
    {
        _configPath = Path.GetFullPath(configPath);
        _clients = clients;
        _failed = failed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _failoverClient = failoverClient;
        _failoverService = failoverService;
        Registrations[_configPath] = this;
    }

    public static IReadOnlyList<ProviderActivationEntry> Read(string configPath)
        => Registrations.TryGetValue(Path.GetFullPath(configPath), out var registration)
            ? registration.ReadEntries()
            : [];

    internal void MarkConnected()
    {
        lock (_gate)
            _startedAtUtc = DateTimeOffset.UtcNow;
    }

    internal void MarkConnectionFailed()
    {
        lock (_gate)
        {
            if (_failoverClient is { } failover)
                _failed.Add(failover.ActiveProviderId);
            else
                _failed.UnionWith(_clients.Keys);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Registrations.TryRemove(new KeyValuePair<string, StreamingProviderActivation>(_configPath, this));
        }
    }

    private ProviderActivationEntry[] ReadEntries()
    {
        lock (_gate)
        {
            if (_disposed)
                return [];

            return _clients.Where(pair => !_failed.Contains(pair.Key))
                .Select(pair => DescribeClient(pair.Key, pair.Value))
                .Concat(_failed.Select(id => Streaming(id, ProviderActivationStates.FailedToStart, null,
                    "The host could not start this streaming provider. Review provider diagnostics.")))
                .ToArray();
        }
    }

    private ProviderActivationEntry DescribeClient(string id, IMarketDataClient client)
    {
        var isStandby = _failoverClient is not null && !ProviderIdentity.EqualsId(_failoverClient.ActiveProviderId, id);
        if (isStandby && _failoverService?.IsProviderEnabled(id) == false)
            return Streaming(id, ProviderActivationStates.Disabled, null, "Excluded from the running failover group.");
        if (!client.IsEnabled)
            return Streaming(id, ProviderActivationStates.Disabled, null, "The running client is disabled.");
        if (_startedAtUtc is null)
            return Streaming(id, ProviderActivationStates.Unavailable, null, "The streaming client is starting.");
        if (isStandby)
            return Streaming(id, ProviderActivationStates.Active, _startedAtUtc, "Prepared as a failover standby.");

        var diagnostics = client.GetConnectionDiagnosticsSnapshot();
        return Streaming(id, ProviderActivationStates.Active, diagnostics.LastConnectedAt ?? _startedAtUtc,
            "The collector started this streaming client.");
    }

    private static ProviderActivationEntry Streaming(string id, string state, DateTimeOffset? at, string reason)
        => new(id, "streaming", state, at, reason);
}
