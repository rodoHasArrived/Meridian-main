using Meridian.Infrastructure.Resilience;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Optional provider contract for adapters that can expose safe connection lifecycle diagnostics.
/// </summary>
public interface IProviderConnectionDiagnosticsSource
{
    /// <summary>
    /// Raised when the provider connection lifecycle diagnostics change.
    /// </summary>
    event Action<WebSocketConnectionDiagnostics>? ConnectionDiagnosticsChanged;

    /// <summary>
    /// Gets a safe diagnostics snapshot for provider health, logs, and tests.
    /// </summary>
    WebSocketConnectionDiagnostics GetConnectionDiagnosticsSnapshot();
}
