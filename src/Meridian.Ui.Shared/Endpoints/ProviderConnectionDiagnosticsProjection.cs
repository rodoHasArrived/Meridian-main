using Meridian.Core.Diagnostics;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Resilience;

namespace Meridian.Ui.Shared.Endpoints;

internal sealed record ProviderConnectionDiagnosticsSummary(
    string ProviderName,
    string LifecycleState,
    string WebSocketState,
    bool IsConnected,
    bool IsReconnecting,
    int ReconnectAttempts,
    DateTimeOffset? LastConnectedAt,
    DateTimeOffset? LastDisconnectedAt,
    DateTimeOffset? LastHeartbeatReceivedAt,
    DateTimeOffset? LastMessageReceivedAt,
    DateTimeOffset? LastReconnectAttemptAt,
    string? LastFailureKind,
    TimeSpan? ConnectionAge,
    TimeSpan? IdleDuration,
    int ActiveSubscriptions,
    int FailedSubscriptions,
    int RecoveringSubscriptions,
    DateTimeOffset? LastSubscriptionMessageAt,
    IReadOnlyList<ProviderStreamDiagnostics>? Streams);

internal static class ProviderConnectionDiagnosticsProjection
{
    public static IReadOnlyList<ProviderConnectionDiagnosticsSummary> BuildUniqueSummaries(
        ProviderRegistry? registry)
    {
        if (registry is null)
        {
            return [];
        }

        return registry.GetAllProviderMetadata()
            .OfType<IProviderConnectionDiagnosticsSource>()
            .Select(source => FromSnapshot(source.GetConnectionDiagnosticsSnapshot()))
            .OrderBy(summary => summary.ProviderName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyDictionary<string, ProviderConnectionDiagnosticsSummary> BuildByProviderId(
        ProviderRegistry? registry)
    {
        if (registry is null)
        {
            return new Dictionary<string, ProviderConnectionDiagnosticsSummary>(StringComparer.OrdinalIgnoreCase);
        }

        var diagnostics = new Dictionary<string, ProviderConnectionDiagnosticsSummary>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in registry.GetAllProviderMetadata())
        {
            if (provider is not IProviderConnectionDiagnosticsSource source)
            {
                continue;
            }

            var summary = FromSnapshot(source.GetConnectionDiagnosticsSnapshot());
            AddIfPresent(diagnostics, provider.ProviderId, summary);
            AddIfPresent(diagnostics, provider.ProviderDisplayName, summary);
            AddIfPresent(diagnostics, summary.ProviderName, summary);
        }

        return diagnostics;
    }

    public static ProviderConnectionDiagnosticsSummary? Find(
        IReadOnlyDictionary<string, ProviderConnectionDiagnosticsSummary> diagnosticsByProviderId,
        params string?[] keys)
    {
        foreach (var key in keys)
        {
            if (!string.IsNullOrWhiteSpace(key) &&
                diagnosticsByProviderId.TryGetValue(key, out var diagnostics))
            {
                return diagnostics;
            }
        }

        return null;
    }

    public static ProviderConnectionDiagnosticsSummary FromSnapshot(WebSocketConnectionDiagnostics snapshot)
        => new(
            ProviderName: RuntimeDiagnosticRedactor.SanitizeText(snapshot.ProviderName),
            LifecycleState: snapshot.LifecycleState.ToString(),
            WebSocketState: snapshot.WebSocketState.ToString(),
            IsConnected: snapshot.IsConnected,
            IsReconnecting: snapshot.IsReconnecting,
            ReconnectAttempts: snapshot.ReconnectAttempts,
            LastConnectedAt: snapshot.LastConnectedAt,
            LastDisconnectedAt: snapshot.LastDisconnectedAt,
            LastHeartbeatReceivedAt: snapshot.LastHeartbeatReceivedAt,
            LastMessageReceivedAt: snapshot.LastMessageReceivedAt,
            LastReconnectAttemptAt: snapshot.LastReconnectAttemptAt,
            LastFailureKind: RuntimeDiagnosticRedactor.SanitizeText(snapshot.LastFailureKind?.ToString()),
            ConnectionAge: snapshot.ConnectionAge,
            IdleDuration: snapshot.IdleDuration,
            ActiveSubscriptions: snapshot.ActiveSubscriptions,
            FailedSubscriptions: snapshot.FailedSubscriptions,
            RecoveringSubscriptions: snapshot.RecoveringSubscriptions,
            LastSubscriptionMessageAt: snapshot.LastSubscriptionMessageAt,
            Streams: snapshot.Streams);

    private static void AddIfPresent(
        IDictionary<string, ProviderConnectionDiagnosticsSummary> diagnostics,
        string? key,
        ProviderConnectionDiagnosticsSummary summary)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            diagnostics[key] = summary;
        }
    }
}
