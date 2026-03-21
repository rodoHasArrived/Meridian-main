using System.Text.Json.Serialization;

namespace Meridian.Contracts.Configuration;

/// <summary>
/// Configuration for derivatives (options and futures) data collection.
/// </summary>
public sealed class DerivativesConfigDto
{
    /// <summary>
    /// Whether derivatives tracking is enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>
    /// Underlying symbols to track options for (e.g., ["SPX", "SPY", "QQQ", "AAPL"]).
    /// </summary>
    [JsonPropertyName("underlyings")]
    public string[]? Underlyings { get; set; }

    /// <summary>
    /// Maximum days to expiration for tracked contracts (0 = no limit).
    /// Default: 90 (only track near-term options).
    /// </summary>
    [JsonPropertyName("maxDaysToExpiration")]
    public int MaxDaysToExpiration { get; set; } = 90;

    /// <summary>
    /// Number of strikes above and below ATM to track (0 = all strikes).
    /// Default: 20 (±20 strikes from at-the-money).
    /// </summary>
    [JsonPropertyName("strikeRange")]
    public int StrikeRange { get; set; } = 20;

    /// <summary>
    /// Whether to capture greeks (delta, gamma, theta, vega, rho, IV).
    /// </summary>
    [JsonPropertyName("captureGreeks")]
    public bool CaptureGreeks { get; set; } = true;

    /// <summary>
    /// Whether to capture periodic chain snapshots.
    /// </summary>
    [JsonPropertyName("captureChainSnapshots")]
    public bool CaptureChainSnapshots { get; set; }

    /// <summary>
    /// Interval in seconds between chain snapshots (default: 300 = 5 minutes).
    /// </summary>
    [JsonPropertyName("chainSnapshotIntervalSeconds")]
    public int ChainSnapshotIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Whether to capture open interest updates (typically daily).
    /// </summary>
    [JsonPropertyName("captureOpenInterest")]
    public bool CaptureOpenInterest { get; set; } = true;

    /// <summary>
    /// Filter for expiration types to track.
    /// Values: "Weekly", "Monthly", "Quarterly", "LEAPS", "All".
    /// Default: ["Weekly", "Monthly"] — skip quarterlies and LEAPS unless needed.
    /// </summary>
    [JsonPropertyName("expirationFilter")]
    public string[]? ExpirationFilter { get; set; }

    /// <summary>
    /// Index options configuration (SPX, NDX, RUT, VIX).
    /// These are European-style and cash-settled.
    /// </summary>
    [JsonPropertyName("indexOptions")]
    public IndexOptionsConfigDto? IndexOptions { get; set; }
}

/// <summary>
/// Configuration specific to index options.
/// </summary>
public sealed class IndexOptionsConfigDto
{
    /// <summary>
    /// Whether index options tracking is enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>
    /// Index symbols to track (e.g., ["SPX", "NDX", "RUT", "VIX"]).
    /// </summary>
    [JsonPropertyName("indices")]
    public string[]? Indices { get; set; }

    /// <summary>
    /// Whether to include SPX weekly expirations (0DTE, Mon/Wed/Fri).
    /// These have very high volume but generate significant data.
    /// </summary>
    [JsonPropertyName("includeWeeklies")]
    public bool IncludeWeeklies { get; set; } = true;

    /// <summary>
    /// Whether to include AM-settled expirations (standard monthly SPX).
    /// </summary>
    [JsonPropertyName("includeAmSettled")]
    public bool IncludeAmSettled { get; set; } = true;

    /// <summary>
    /// Whether to include PM-settled expirations (SPXW weeklies).
    /// </summary>
    [JsonPropertyName("includePmSettled")]
    public bool IncludePmSettled { get; set; } = true;
}
