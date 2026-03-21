namespace Meridian.Application.Config;

/// <summary>
/// Domain configuration for derivatives (options) data collection.
/// </summary>
public sealed record DerivativesConfig(
    bool Enabled = false,
    string[]? Underlyings = null,
    int MaxDaysToExpiration = 90,
    int StrikeRange = 20,
    bool CaptureGreeks = true,
    bool CaptureChainSnapshots = false,
    int ChainSnapshotIntervalSeconds = 300,
    bool CaptureOpenInterest = true,
    string[]? ExpirationFilter = null,
    IndexOptionsConfig? IndexOptions = null
);

/// <summary>
/// Configuration specific to index options (SPX, NDX, RUT, VIX).
/// </summary>
public sealed record IndexOptionsConfig(
    bool Enabled = false,
    string[]? Indices = null,
    bool IncludeWeeklies = true,
    bool IncludeAmSettled = true,
    bool IncludePmSettled = true
);
