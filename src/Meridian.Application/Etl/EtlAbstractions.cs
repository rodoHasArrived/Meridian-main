using Meridian.Contracts.Etl;
using Meridian.Contracts.Pipeline;
using Meridian.Domain.Events;
using Meridian.Storage.Packaging;

namespace Meridian.Application.Etl;

public interface IEtlJobDefinitionStore
{
    Task SaveAsync(EtlJobDefinition definition, CancellationToken ct = default);
    Task<EtlJobDefinition?> GetAsync(string jobId, CancellationToken ct = default);
    Task<IReadOnlyList<EtlJobDefinition>> ListAsync(CancellationToken ct = default);
    Task DeleteAsync(string jobId, CancellationToken ct = default);
}

public interface IEtlJobService
{
    Task<IngestionJob> CreateJobAsync(EtlJobDefinition definition, CancellationToken ct = default);
    Task<EtlJobDefinition?> GetDefinitionAsync(string jobId, CancellationToken ct = default);
    Task<EtlRunResult> RunAsync(string jobId, CancellationToken ct = default);
}

public interface IEtlSourceReader
{
    EtlSourceKind Kind { get; }
    Task<IReadOnlyList<EtlRemoteFile>> ListFilesAsync(EtlSourceDefinition source, CancellationToken ct = default);
    Task<EtlStagedFile> StageFileAsync(string jobId, EtlSourceDefinition source, EtlRemoteFile file, CancellationToken ct = default);
}

public interface IPartnerFileParser
{
    string SchemaId { get; }
    bool CanParse(EtlStagedFile file);
    IAsyncEnumerable<PartnerRecordEnvelope> ParseAsync(EtlStagedFile file, EtlCheckpointToken? checkpoint, CancellationToken ct = default);
}

public interface IPartnerSchemaRegistry
{
    CsvSchemaDefinition GetCsvSchema(string schemaId);
    bool IsSupported(string schemaId);
}

public interface IEtlExportService
{
    Task<EtlExportResult> ExportAsync(IngestionJob job, EtlJobDefinition definition, CancellationToken ct = default);
}

public sealed class CsvSchemaDefinition
{
    public required string SchemaId { get; init; }
    public bool HasHeaderRow { get; init; } = true;
    public char Delimiter { get; init; } = ',';
    public required IReadOnlyDictionary<string, string> Columns { get; init; }
}

public sealed class EtlRemoteFile
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public long SizeBytes { get; init; }
    public DateTimeOffset? LastModifiedUtc { get; init; }
}

public sealed class EtlStagedFile
{
    public required string OriginalPath { get; init; }
    public required string StagedPath { get; init; }
    public required string FileName { get; init; }
    public required string ChecksumSha256 { get; init; }
    public long SizeBytes { get; init; }
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
    public bool Success { get; init; }
    public int FilesProcessed { get; init; }
    public long RecordsProcessed { get; init; }
    public long RecordsAccepted { get; init; }
    public long RecordsRejected { get; init; }
    public long RecordsDeduplicated { get; init; }
    public string[] Errors { get; init; } = [];
    public EtlExportResult? ExportResult { get; init; }
}

public sealed class EtlExportResult
{
    public bool Success { get; init; }
    public string[] ArtifactPaths { get; init; } = [];
    public PackageResult? PackageResult { get; init; }
    public string? Error { get; init; }
}
