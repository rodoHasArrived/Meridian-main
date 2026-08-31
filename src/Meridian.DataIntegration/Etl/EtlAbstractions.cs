using Meridian.Contracts.Etl;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Pipeline;
using Meridian.Domain.Events;
using Meridian.Storage.Packaging;

namespace Meridian.DataIntegration.Etl;

public interface IEtlJobService
{
    Task<IngestionJob> CreateJobAsync(EtlJobDefinition definition, CancellationToken ct = default);
    Task<EtlJobDefinition?> GetDefinitionAsync(string jobId, CancellationToken ct = default);
    Task<EtlRunResult> RunAsync(string jobId, CancellationToken ct = default);
}

public interface IEtlExportService
{
    Task<EtlExportResult> ExportAsync(IngestionJob job, EtlJobDefinition definition, CancellationToken ct = default);
}

public interface IEtlIngestionJobCoordinator
{
    Task<IngestionJob> CreateJobAsync(
        IngestionWorkloadType workloadType,
        string[] symbols,
        string provider,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        IngestionSla? sla = null,
        CancellationToken ct = default);

    IngestionJob? GetJob(string jobId);

    Task<bool> TransitionAsync(
        string jobId,
        IngestionJobState newState,
        string? errorMessage = null,
        CancellationToken ct = default);

    Task UpdateCheckpointAsync(
        string jobId,
        IngestionCheckpointToken checkpoint,
        CancellationToken ct = default);
}

public interface IEtlEventPipeline
{
    long DeduplicatedCount { get; }
    ValueTask PublishAsync(MarketEvent evt, CancellationToken ct = default);
    Task FlushAsync(CancellationToken ct = default);
}

public sealed class NormalizationOutcome
{
    public required EtlRecordDisposition Disposition { get; init; }
    public MarketEvent? Event { get; init; }
    public string? RejectCode { get; init; }
    public string? RejectMessage { get; init; }
    public string? RecordHash { get; init; }
}

public sealed class EtlRunResult
{
    public required VerifiedOperationOutcome Outcome { get; init; }
    public bool Success => Outcome.IsSuccessful;
    public EtlRunStatus Status => Success
        ? EtlRunStatus.Completed
        : RecordsAccepted > 0
            ? EtlRunStatus.Partial
            : EtlRunStatus.Failed;
    public int FilesProcessed { get; init; }
    public long RecordsProcessed { get; init; }
    public long RecordsAccepted { get; init; }
    public long RecordsRejected { get; init; }
    public long RecordsDeduplicated { get; init; }
    public string[] Errors { get; init; } = [];
    public string[] Warnings { get; init; } = [];
    public EtlExportResult? ExportResult { get; init; }
}

public enum EtlRunStatus
{
    Failed,
    Partial,
    Completed
}

public sealed class EtlExportResult
{
    public bool Success { get; init; }
    public string[] ArtifactPaths { get; init; } = [];
    public PackageResult? PackageResult { get; init; }
    public string? Error { get; init; }
}
