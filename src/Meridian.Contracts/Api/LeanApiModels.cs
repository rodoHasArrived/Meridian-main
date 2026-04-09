using System.Text.Json.Serialization;

namespace Meridian.Contracts.Api;

/// <summary>
/// Lightweight Lean backtest metric summary used by launcher/status surfaces.
/// </summary>
public sealed record LeanBacktestResultsSummaryDto
{
    [JsonPropertyName("algorithmName")]
    public string? AlgorithmName { get; init; }

    [JsonPropertyName("totalReturn")]
    public decimal TotalReturn { get; init; }

    [JsonPropertyName("annualizedReturn")]
    public decimal AnnualizedReturn { get; init; }

    [JsonPropertyName("sharpeRatio")]
    public decimal SharpeRatio { get; init; }

    [JsonPropertyName("maxDrawdown")]
    public decimal MaxDrawdown { get; init; }

    [JsonPropertyName("totalTrades")]
    public int TotalTrades { get; init; }

    [JsonPropertyName("winRate")]
    public decimal WinRate { get; init; }

    [JsonPropertyName("profitFactor")]
    public decimal ProfitFactor { get; init; }
}

/// <summary>
/// Runtime Lean backtest-results response returned by launcher/status endpoints.
/// </summary>
public sealed record LeanBacktestResultsResponseDto
{
    [JsonPropertyName("backtestId")]
    public string BacktestId { get; init; } = string.Empty;

    [JsonPropertyName("algorithmName")]
    public string? AlgorithmName { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("results")]
    public LeanBacktestResultsSummaryDto? Results { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>
/// Shared request for loading or ingesting a Lean results artifact from disk.
/// </summary>
public sealed record LeanResultsImportRequestDto
{
    [JsonPropertyName("resultsFilePath")]
    public string ResultsFilePath { get; init; } = string.Empty;

    [JsonPropertyName("backtestId")]
    public string? BacktestId { get; init; }

    [JsonPropertyName("algorithmName")]
    public string? AlgorithmName { get; init; }
}

/// <summary>
/// Presence flags for major sections within a Lean results payload.
/// </summary>
public sealed record LeanResultsArtifactSectionsDto
{
    [JsonPropertyName("hasAlgorithmConfiguration")]
    public bool HasAlgorithmConfiguration { get; init; }

    [JsonPropertyName("hasParameters")]
    public bool HasParameters { get; init; }

    [JsonPropertyName("hasStatistics")]
    public bool HasStatistics { get; init; }

    [JsonPropertyName("hasRuntimeStatistics")]
    public bool HasRuntimeStatistics { get; init; }

    [JsonPropertyName("hasCharts")]
    public bool HasCharts { get; init; }

    [JsonPropertyName("hasOrders")]
    public bool HasOrders { get; init; }

    [JsonPropertyName("hasClosedTrades")]
    public bool HasClosedTrades { get; init; }
}

/// <summary>
/// Raw file reference that contributed to a Lean importable result.
/// </summary>
public sealed record LeanRawArtifactFileDto
{
    [JsonPropertyName("artifactType")]
    public string ArtifactType { get; init; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("exists")]
    public bool Exists { get; init; }

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; init; }

    [JsonPropertyName("lastWriteTimeUtc")]
    public DateTimeOffset? LastWriteTimeUtc { get; init; }
}

/// <summary>
/// Parsed Lean artifact summary used before canonical normalization into a shared run.
/// </summary>
public sealed record LeanResultsArtifactSummaryDto
{
    [JsonPropertyName("sourceFormat")]
    public string SourceFormat { get; init; } = "lean-backtest-json";

    [JsonPropertyName("backtestId")]
    public string? BacktestId { get; init; }

    [JsonPropertyName("algorithmName")]
    public string AlgorithmName { get; init; } = string.Empty;

    [JsonPropertyName("resultsFilePath")]
    public string ResultsFilePath { get; init; } = string.Empty;

    [JsonPropertyName("statistics")]
    public IReadOnlyDictionary<string, string> Statistics { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    [JsonPropertyName("parameters")]
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    [JsonPropertyName("sections")]
    public LeanResultsArtifactSectionsDto Sections { get; init; } = new();

    [JsonPropertyName("artifacts")]
    public IReadOnlyList<LeanRawArtifactFileDto> Artifacts { get; init; } = Array.Empty<LeanRawArtifactFileDto>();

    [JsonPropertyName("totalReturn")]
    public decimal? TotalReturn { get; init; }

    [JsonPropertyName("annualizedReturn")]
    public decimal? AnnualizedReturn { get; init; }

    [JsonPropertyName("sharpeRatio")]
    public decimal? SharpeRatio { get; init; }

    [JsonPropertyName("maxDrawdown")]
    public decimal? MaxDrawdown { get; init; }

    [JsonPropertyName("totalTrades")]
    public int? TotalTrades { get; init; }

    [JsonPropertyName("winRate")]
    public decimal? WinRate { get; init; }

    [JsonPropertyName("profitFactor")]
    public decimal? ProfitFactor { get; init; }
}

/// <summary>
/// Lean results-ingest response that keeps the raw artifact visible for later normalization.
/// </summary>
public sealed record LeanResultsIngestResponseDto
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("backtestId")]
    public string? BacktestId { get; init; }

    [JsonPropertyName("algorithmName")]
    public string? AlgorithmName { get; init; }

    [JsonPropertyName("totalReturn")]
    public decimal? TotalReturn { get; init; }

    [JsonPropertyName("annualizedReturn")]
    public decimal? AnnualizedReturn { get; init; }

    [JsonPropertyName("sharpeRatio")]
    public decimal? SharpeRatio { get; init; }

    [JsonPropertyName("maxDrawdown")]
    public decimal? MaxDrawdown { get; init; }

    [JsonPropertyName("totalTrades")]
    public int? TotalTrades { get; init; }

    [JsonPropertyName("winRate")]
    public decimal? WinRate { get; init; }

    [JsonPropertyName("profitFactor")]
    public decimal? ProfitFactor { get; init; }

    [JsonPropertyName("artifactSummary")]
    public LeanResultsArtifactSummaryDto? ArtifactSummary { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }
}
