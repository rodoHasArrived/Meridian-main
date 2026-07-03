using System.Text.Json.Serialization;

namespace Meridian.Contracts.Workstation;

/// <summary>
/// Describes an available custodian/broker statement connector so workstation clients can
/// offer file import and remote fetch entry points.
/// </summary>
public sealed record StatementConnectorDescriptorDto(
    string ConnectorId,
    string DisplayName,
    IReadOnlyList<string> FileExtensions,
    bool SupportsFileImport,
    bool SupportsRemoteFetch,
    bool RequiresMappingProfile,
    string? DefaultProfileId);

/// <summary>Declarative statement mapping profile shared with workstation clients.</summary>
public sealed record StatementMappingProfileDto(
    int SchemaVersion,
    string ProfileId,
    string DisplayName,
    string Format,
    StatementMappingProfileCsvOptionsDto? Csv,
    string? Culture,
    IReadOnlyList<string>? DateFormats,
    IReadOnlyList<StatementMappingProfileFieldDto> Fields,
    IReadOnlyList<StatementMappingProfileActivityCodeDto> ActivityCodes,
    string? LastAcceptedFingerprint,
    bool IsBuiltIn,
    string? Notes);

public sealed record StatementMappingProfileCsvOptionsDto(
    string Delimiter,
    string Quote,
    bool HasHeader);

public sealed record StatementMappingProfileFieldDto(
    string CanonicalField,
    string SourceColumn,
    IReadOnlyList<string>? Aliases,
    bool Required);

public sealed record StatementMappingProfileActivityCodeDto(
    string SourceCode,
    string CanonicalActivityType);

/// <summary>Confidence tier for one detected source column in the import preview.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StatementColumnConfidenceDto>))]
public enum StatementColumnConfidenceDto : byte
{
    Exact = 0,
    Alias = 1,
    Fuzzy = 2,
    Unmapped = 3
}

public sealed record StatementColumnMappingDto(
    string SourceColumn,
    string? CanonicalField,
    StatementColumnConfidenceDto Confidence,
    decimal Score,
    string Rationale);

public sealed record StatementImportIssueDto(
    string Code,
    string Severity,
    int? RowNumber,
    string? Field,
    string Message);

/// <summary>One canonical record sample shown in the import preview, grouped by kind.</summary>
public sealed record StatementRecordPreviewDto(
    string Kind,
    string Account,
    string Symbol,
    decimal Quantity,
    decimal Price,
    decimal CashAmount,
    string ActivityType,
    string TradeDate,
    string? SettlementDate,
    string? Currency,
    decimal? FeesCommission,
    string? ExternalTransactionId);

/// <summary>
/// Per-kind record breakdown so operators importing a mixed statement see exactly what
/// lands in each lane (positions, transactions, cash balances, fees, dividends).
/// </summary>
public sealed record StatementKindSummaryDto(
    string Kind,
    int RecordCount,
    IReadOnlyList<StatementRecordPreviewDto> SampleRecords);

/// <summary>A catalog profile ranked against the detected columns of an uploaded statement.</summary>
public sealed record StatementProfileSuggestionDto(
    string ProfileId,
    string DisplayName,
    decimal Score);

public sealed record StatementImportPreviewDto(
    string ConnectorId,
    string ConnectorDisplayName,
    string? ProfileId,
    string FileName,
    long FileSizeBytes,
    IReadOnlyList<string> DetectedColumns,
    IReadOnlyList<StatementColumnMappingDto> ColumnMappings,
    int RecordCount,
    IReadOnlyList<StatementKindSummaryDto> KindSummaries,
    IReadOnlyList<StatementImportIssueDto> Issues,
    IReadOnlyList<StatementProfileSuggestionDto> ProfileSuggestions,
    string Status,
    string NextAction);

public sealed record StatementImportCommitResultDto(
    string RunId,
    bool Duplicate,
    int RecordCount,
    IReadOnlyList<StatementKindSummaryDto> KindSummaries,
    int BreakCount,
    int CaseCount,
    string RetainedSourcePath,
    string RetainedCanonicalPath,
    string Status,
    string NextAction)
{
    public EvidenceVaultIdentityDto? EvidenceVaultIdentity { get; init; }
    public string? EvidenceWorkbenchRoute { get; init; }
    public string? ReconciliationRoute { get; init; }
    public IReadOnlyList<string> NextActions { get; init; } = [];
}

/// <summary>A persisted scheduled-fetch configuration for a fetch-capable connector.</summary>
public sealed record StatementFetchScheduleDto(
    string ScheduleId,
    string ConnectorId,
    string ExternalAccountId,
    string FundAccountId,
    string SourceInstitution,
    string? MappingProfileId,
    string ToleranceProfileId,
    int CadenceHours,
    bool Enabled,
    DateTimeOffset? LastRunAtUtc,
    string? LastRunStatus,
    DateTimeOffset? NextDueAtUtc);

public sealed record StatementFetchScheduleUpsertRequestDto(
    string? ScheduleId,
    string ConnectorId,
    string ExternalAccountId,
    string FundAccountId,
    string SourceInstitution,
    string? MappingProfileId,
    string? ToleranceProfileId,
    int CadenceHours,
    bool Enabled);
