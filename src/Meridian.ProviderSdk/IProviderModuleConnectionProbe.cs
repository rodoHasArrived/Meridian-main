namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Optional interface implemented by provider modules that support a real network connectivity probe.
/// When implemented, <c>TestModuleAsync</c> calls this after credential validation succeeds.
/// </summary>
public interface IProviderModuleConnectionProbe
{
    /// <summary>
    /// Attempts a live network probe and returns latency and connectivity status.
    /// </summary>
    Task<ModuleProbeResult> ProbeConnectionAsync(CancellationToken ct = default);
}

/// <summary>
/// Result from <see cref="IProviderModuleConnectionProbe.ProbeConnectionAsync"/>.
/// </summary>
public sealed record ModuleProbeResult(
    bool Connected,
    string? FailureReason = null,
    double? LatencyMs = null,
    string? ProbeDetail = null)
{
    public static ModuleProbeResult Success(double latencyMs, string? detail = null)
        => new(Connected: true, LatencyMs: latencyMs, ProbeDetail: detail);

    public static ModuleProbeResult Failure(string reason)
        => new(Connected: false, FailureReason: reason);
}
