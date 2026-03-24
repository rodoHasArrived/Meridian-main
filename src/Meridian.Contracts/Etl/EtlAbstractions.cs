namespace Meridian.Contracts.Etl;

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
