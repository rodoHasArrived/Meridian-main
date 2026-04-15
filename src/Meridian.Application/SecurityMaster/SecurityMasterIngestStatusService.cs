namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Snapshot of Security Master ingest activity for UI/API status surfaces.
/// </summary>
public sealed record SecurityMasterIngestStatusSnapshot(
    SecurityMasterActiveImportStatus? ActiveImport,
    SecurityMasterCompletedImportStatus? LastCompleted);

/// <summary>
/// Active Security Master ingest progress.
/// </summary>
public sealed record SecurityMasterActiveImportStatus(
    string FileExtension,
    int Total,
    int Processed,
    int Imported,
    int Skipped,
    int Failed,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Summary for the last completed Security Master ingest.
/// </summary>
public sealed record SecurityMasterCompletedImportStatus(
    string FileExtension,
    int Total,
    int Processed,
    int Imported,
    int Skipped,
    int Failed,
    int ConflictsDetected,
    int ErrorCount,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);

/// <summary>
/// Exposes runtime Security Master ingest status.
/// </summary>
public interface ISecurityMasterIngestStatusService
{
    /// <summary>
    /// Gets the latest ingest status snapshot.
    /// </summary>
    SecurityMasterIngestStatusSnapshot GetSnapshot();
}
