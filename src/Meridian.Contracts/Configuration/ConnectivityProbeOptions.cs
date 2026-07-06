namespace Meridian.Contracts.Configuration;

/// <summary>
/// Operator-tunable timing for connectivity probes shared by setup and diagnostics flows.
/// Bind from configuration using the <see cref="SectionKey"/> section.
/// </summary>
public sealed class ConnectivityProbeOptions
{
    /// <summary>Configuration section key for options binding.</summary>
    public const string SectionKey = "Connectivity:Probes";

    /// <summary>
    /// Timeout applied to TCP connect probes when testing provider or gateway
    /// reachability (for example TWS/IB Gateway socket checks).
    /// </summary>
    public int TcpConnectTimeoutMs { get; set; } = 5000;
}
