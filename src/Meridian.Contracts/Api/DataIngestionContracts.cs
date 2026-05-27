namespace Meridian.Contracts.Api;

/// <summary>
/// Demo mode configuration and seeded market data support.
/// </summary>
public sealed record DemoModeConfig(
    bool Enabled = false,
    string[] DemoSymbols = null!,
    DateTime LastUpdated = default
);

/// <summary>
/// Seeded market data for demo mode.
/// </summary>
public sealed record DemoMarketSnapshot(
    string Symbol,
    decimal LastPrice,
    decimal Bid,
    decimal Ask,
    long BidSize,
    long AskSize,
    long Volume,
    DateTime Timestamp,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close
);

/// <summary>
/// Historical data fixture for demo/testing.
/// </summary>
public sealed record DemoHistoricalBar(
    DateTime Date,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume
);

/// <summary>
/// Symbol universe management request.
/// </summary>
public sealed record SymbolUniverseRequest(
    string Symbol,
    string? Exchange = null,
    string? Currency = null,
    bool SubscribeTrades = true,
    bool SubscribeDepth = true,
    int DepthLevels = 10,
    string? Description = null,
    string[]? Tags = null
);

/// <summary>
/// Symbol universe management response.
/// </summary>
public sealed record SymbolUniverseResponse(
    string Symbol,
    bool Configured = false,
    int DataFilesCount = 0,
    DateTime? LastDataPoint = null,
    string Status = "Unconfigured",
    string? Note = null
);

/// <summary>
/// Backfill validation result.
/// </summary>
public sealed record BackfillValidationResult(
    string Symbol,
    bool IsComplete = false,
    int TotalDays = 0,
    int CoveredDays = 0,
    double Completeness = 0.0,
    string[]? Gaps = null,
    DateTime? FirstDataPoint = null,
    DateTime? LastDataPoint = null,
    string Status = "Incomplete"
);

/// <summary>
/// Gap detection result for backfill validation.
/// </summary>
public sealed record BackfillGapInfo(
    string Symbol,
    DateTime StartDate,
    DateTime EndDate,
    int DaysGap,
    string Reason = "No data"
);

/// <summary>
/// Backfill completeness summary.
/// </summary>
public sealed record BackfillCompletenessSummary(
    int TotalSymbols = 0,
    int CompleteSymbols = 0,
    int IncompleteSymbols = 0,
    double AverageCompleteness = 0.0,
    BackfillValidationResult[]? Symbols = null
);

/// <summary>
/// Provider credential validation request.
/// </summary>
public sealed record ProviderCredentialRequest(
    string Provider,
    Dictionary<string, string> Credentials
);

/// <summary>
/// Provider credential validation result.
/// </summary>
public sealed record ProviderCredentialValidationResult(
    bool IsValid = false,
    string Status = "Unknown",
    string? ErrorMessage = null,
    DateTime? LastVerified = null,
    Dictionary<string, string>? Details = null
);

/// <summary>
/// Provider connection test result.
/// </summary>
public sealed record ProviderConnectionTest(
    string Provider,
    bool IsConnected = false,
    int? ResponseTimeMs = null,
    string Status = "Unknown",
    string? ErrorMessage = null,
    DateTime TestedAt = default
);
