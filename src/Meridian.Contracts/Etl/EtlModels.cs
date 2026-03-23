using System.Text.Json.Serialization;
namespace Meridian.Contracts.Etl;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EtlFlowDirection : byte
{
    Import = 0,
    Export = 1,
    RoundTrip = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EtlSourceKind : byte
{
    Local = 0,
    Sftp = 1
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EtlDestinationKind : byte
{
    StorageCatalog = 0,
    Local = 1,
    Sftp = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EtlTransferMode : byte
{
    BatchExchange = 0,
    ScheduledDelivery = 1
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EtlPackageFormat : byte
{
    Zip = 0,
    TarGz = 1
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EtlRecordDisposition : byte
{
    Accepted = 0,
    Rejected = 1,
    SkippedDuplicate = 2,
    SkippedOutOfRange = 3
}

public sealed class EtlJobDefinition
{
    [JsonPropertyName("jobId")]
    public required string JobId { get; init; }

    [JsonPropertyName("flowDirection")]
    public required EtlFlowDirection FlowDirection { get; init; }

    [JsonPropertyName("partnerSchemaId")]
    public required string PartnerSchemaId { get; init; }

    [JsonPropertyName("logicalSourceName")]
    public required string LogicalSourceName { get; init; }

    [JsonPropertyName("source")]
    public required EtlSourceDefinition Source { get; init; }

    [JsonPropertyName("destination")]
    public required EtlDestinationDefinition Destination { get; init; }

    [JsonPropertyName("symbols")]
    public string[] Symbols { get; init; } = [];

    [JsonPropertyName("eventTypes")]
    public string[] EventTypes { get; init; } = [];

    [JsonPropertyName("fromDateUtc")]
    public DateTime? FromDateUtc { get; init; }

    [JsonPropertyName("toDateUtc")]
    public DateTime? ToDateUtc { get; init; }

    [JsonPropertyName("publishPortablePackage")]
    public bool PublishPortablePackage { get; init; }

    [JsonPropertyName("publishNormalizedExtract")]
    public bool PublishNormalizedExtract { get; init; }

    [JsonPropertyName("continueOnRecordError")]
    public bool ContinueOnRecordError { get; init; }

    [JsonPropertyName("validateChecksums")]
    public bool ValidateChecksums { get; init; } = true;

    [JsonPropertyName("failRoundTripOnExportError")]
    public bool FailRoundTripOnExportError { get; init; } = true;

    [JsonPropertyName("checkpointEveryRecords")]
    public int CheckpointEveryRecords { get; init; } = 5_000;

    [JsonPropertyName("rejectSampleLimit")]
    public int RejectSampleLimit { get; init; } = 1_000;

    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; init; }

    [JsonPropertyName("createdAtUtc")]
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class EtlSourceDefinition
{
    [JsonPropertyName("kind")]
    public required EtlSourceKind Kind { get; init; }

    [JsonPropertyName("location")]
    public required string Location { get; init; }

    [JsonPropertyName("filePattern")]
    public string? FilePattern { get; init; }

    [JsonPropertyName("username")]
    public string? Username { get; init; }

    [JsonPropertyName("secretRef")]
    public string? SecretRef { get; init; }

    [JsonPropertyName("deleteAfterSuccess")]
    public bool DeleteAfterSuccess { get; init; }
}

public sealed class EtlDestinationDefinition
{
    [JsonPropertyName("kind")]
    public required EtlDestinationKind Kind { get; init; }

    [JsonPropertyName("location")]
    public string? Location { get; init; }

    [JsonPropertyName("username")]
    public string? Username { get; init; }

    [JsonPropertyName("secretRef")]
    public string? SecretRef { get; init; }

    [JsonPropertyName("transferMode")]
    public EtlTransferMode TransferMode { get; init; } = EtlTransferMode.BatchExchange;

    [JsonPropertyName("packageFormat")]
    public EtlPackageFormat? PackageFormat { get; init; }

    [JsonPropertyName("overwriteIfExists")]
    public bool OverwriteIfExists { get; init; }

    [JsonPropertyName("deliveryWindowStartUtc")]
    public DateTime? DeliveryWindowStartUtc { get; init; }

    [JsonPropertyName("deliveryWindowEndUtc")]
    public DateTime? DeliveryWindowEndUtc { get; init; }

    [JsonPropertyName("deliverySlaMinutes")]
    public int? DeliverySlaMinutes { get; init; }

    [JsonPropertyName("requiresRemoteAck")]
    public bool RequiresRemoteAck { get; init; }
}

public sealed class EtlCheckpointToken
{
    [JsonPropertyName("currentFileName")]
    public string? CurrentFileName { get; init; }

    [JsonPropertyName("currentFileChecksum")]
    public string? CurrentFileChecksum { get; init; }

    [JsonPropertyName("currentRecordIndex")]
    public long? CurrentRecordIndex { get; init; }

    [JsonPropertyName("lastSymbol")]
    public string? LastSymbol { get; init; }

    [JsonPropertyName("lastTimestampUtc")]
    public DateTime? LastTimestampUtc { get; init; }

    [JsonPropertyName("lastRecordHash")]
    public string? LastRecordHash { get; init; }

    [JsonPropertyName("capturedAtUtc")]
    public DateTime CapturedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class EtlFileManifest
{
    [JsonPropertyName("fileName")]
    public required string FileName { get; init; }

    [JsonPropertyName("relativePath")]
    public required string RelativePath { get; init; }

    [JsonPropertyName("checksumSha256")]
    public required string ChecksumSha256 { get; init; }

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; init; }

    [JsonPropertyName("stagedAtUtc")]
    public DateTime StagedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class PartnerRecordEnvelope
{
    [JsonPropertyName("partnerSchemaId")]
    public required string PartnerSchemaId { get; init; }

    [JsonPropertyName("sourceFileName")]
    public required string SourceFileName { get; init; }

    [JsonPropertyName("sourceFileChecksum")]
    public required string SourceFileChecksum { get; init; }

    [JsonPropertyName("recordIndex")]
    public required long RecordIndex { get; init; }

    [JsonPropertyName("fields")]
    public required IReadOnlyDictionary<string, string?> Fields { get; init; }

    [JsonPropertyName("rawLine")]
    public string? RawLine { get; init; }
}

public sealed class EtlRejectRecord
{
    [JsonPropertyName("sourceFileName")]
    public required string SourceFileName { get; init; }

    [JsonPropertyName("recordIndex")]
    public long RecordIndex { get; init; }

    [JsonPropertyName("rejectCode")]
    public required string RejectCode { get; init; }

    [JsonPropertyName("rejectMessage")]
    public required string RejectMessage { get; init; }

    [JsonPropertyName("rawLine")]
    public string? RawLine { get; init; }

    [JsonPropertyName("capturedAtUtc")]
    public DateTime CapturedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class EtlAuditEvent
{
    [JsonPropertyName("stage")]
    public required string Stage { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("capturedAtUtc")]
    public DateTime CapturedAtUtc { get; init; } = DateTime.UtcNow;
}
