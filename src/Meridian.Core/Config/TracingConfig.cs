namespace Meridian.Core.Config;

/// <summary>
/// Restart-required, opt-in distributed tracing settings. Exporters remain disabled unless
/// explicitly selected; an OTLP destination is required when OTLP export is enabled.
/// </summary>
public sealed record TracingConfig
{
    public bool Enabled { get; init; }
    public string ServiceName { get; init; } = "Meridian";
    public string ServiceVersion { get; init; } = "1.0.0";
    public string Environment { get; init; } = "development";
    public bool EnableConsoleExporter { get; init; }
    public bool EnableOtlpExporter { get; init; }
    public string? OtlpEndpoint { get; init; }
    public string? OtlpHeaders { get; init; }
    public double SamplingRatio { get; init; } = 1.0;
    public int FlushTimeoutMilliseconds { get; init; } = 5000;
}
