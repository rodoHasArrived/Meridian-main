using System.Text.Json.Serialization;

namespace Meridian.Contracts.Api;

/// <summary>
/// Response payload for Security Master ingest status.
/// </summary>
public sealed class SecurityMasterIngestStatusResponse
{
    [JsonPropertyName("openConflicts")]
    public int OpenConflicts { get; set; }

    [JsonPropertyName("isImportActive")]
    public bool IsImportActive { get; set; }

    [JsonPropertyName("activeImport")]
    public SecurityMasterActiveImportStatusResponse? ActiveImport { get; set; }

    [JsonPropertyName("lastCompleted")]
    public SecurityMasterCompletedImportStatusResponse? LastCompleted { get; set; }

    [JsonPropertyName("retrievedAtUtc")]
    public DateTimeOffset RetrievedAtUtc { get; set; }
}

/// <summary>
/// Active Security Master ingest progress.
/// </summary>
public sealed class SecurityMasterActiveImportStatusResponse
{
    [JsonPropertyName("fileExtension")]
    public string FileExtension { get; set; } = string.Empty;

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("processed")]
    public int Processed { get; set; }

    [JsonPropertyName("imported")]
    public int Imported { get; set; }

    [JsonPropertyName("skipped")]
    public int Skipped { get; set; }

    [JsonPropertyName("failed")]
    public int Failed { get; set; }

    [JsonPropertyName("startedAtUtc")]
    public DateTimeOffset StartedAtUtc { get; set; }

    [JsonPropertyName("updatedAtUtc")]
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>
/// Summary of the last completed Security Master ingest.
/// </summary>
public sealed class SecurityMasterCompletedImportStatusResponse
{
    [JsonPropertyName("fileExtension")]
    public string FileExtension { get; set; } = string.Empty;

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("processed")]
    public int Processed { get; set; }

    [JsonPropertyName("imported")]
    public int Imported { get; set; }

    [JsonPropertyName("skipped")]
    public int Skipped { get; set; }

    [JsonPropertyName("failed")]
    public int Failed { get; set; }

    [JsonPropertyName("conflictsDetected")]
    public int ConflictsDetected { get; set; }

    [JsonPropertyName("errorCount")]
    public int ErrorCount { get; set; }

    [JsonPropertyName("startedAtUtc")]
    public DateTimeOffset StartedAtUtc { get; set; }

    [JsonPropertyName("completedAtUtc")]
    public DateTimeOffset CompletedAtUtc { get; set; }
}
