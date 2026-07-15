using System.Net.WebSockets;
using Meridian.Infrastructure.Resilience;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Provider contract for safe connection lifecycle diagnostics.
/// </summary>
/// <remarks>
/// <see cref="Meridian.Infrastructure.IMarketDataClient"/> inherits this contract, so every
/// streaming adapter has a safe diagnostic surface. Adapters with supervised lifecycle state
/// should override the conservative default members below; other provider families may opt in.
/// </remarks>
public interface IProviderConnectionDiagnosticsSource
{
    /// <summary>
    /// Raised when the provider connection lifecycle diagnostics change. The compatibility
    /// default is a no-op because adapters without a lifecycle supervisor cannot publish changes.
    /// </summary>
    event Action<WebSocketConnectionDiagnostics>? ConnectionDiagnosticsChanged
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Gets a safe diagnostics snapshot for provider health, logs, and tests.
    /// </summary>
    /// <remarks>
    /// The compatibility default never claims a live connection. Enabled streaming clients are
    /// reported as configured; disabled clients are reported as disabled. Adapters should
    /// override this member when they can prove richer runtime state.
    /// </remarks>
    WebSocketConnectionDiagnostics GetConnectionDiagnosticsSnapshot()
    {
        var providerName = this is IProviderMetadata metadata &&
            !string.IsNullOrWhiteSpace(metadata.ProviderDisplayName)
                ? metadata.ProviderDisplayName
                : GetType().Name;
        var lifecycleState = this is Meridian.Infrastructure.IMarketDataClient client
            ? client.IsEnabled
                ? ProviderConnectionLifecycleState.Configured
                : ProviderConnectionLifecycleState.Disabled
            : ProviderConnectionLifecycleState.NotConfigured;

        return new WebSocketConnectionDiagnostics(
            ProviderName: providerName,
            LifecycleState: lifecycleState,
            WebSocketState: WebSocketState.None,
            IsConnected: false,
            IsReconnecting: false,
            ReconnectAttempts: 0,
            LastConnectedAt: null,
            LastDisconnectedAt: null,
            LastHeartbeatReceivedAt: null,
            LastMessageReceivedAt: null,
            LastReconnectAttemptAt: null,
            LastError: null,
            LastFailureKind: null,
            ConnectionAge: null,
            IdleDuration: null);
    }
}
