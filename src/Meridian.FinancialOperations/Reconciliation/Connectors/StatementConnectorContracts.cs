using Meridian.Execution.Sdk;

namespace Meridian.FinancialOperations.Reconciliation.Connectors;

/// <summary>
/// Describes a statement connector so operators can discover what formats can be imported
/// and whether the connector can fetch statements remotely in addition to accepting files.
/// </summary>
public sealed record StatementConnectorDescriptor(
    string ConnectorId,
    string DisplayName,
    IReadOnlyList<string> FileExtensions,
    bool SupportsFileImport,
    bool SupportsRemoteFetch,
    bool RequiresMappingProfile,
    string? DefaultProfileId);

/// <summary>
/// A statement source handed to a connector: either an operator upload or the retained
/// document produced by a remote fetch.
/// </summary>
public sealed record StatementSourceDocument(
    string FileName,
    ReadOnlyMemory<byte> Content,
    string? MappingProfileId = null,
    string? ExternalAccountId = null);

public enum StatementMappingConfidence
{
    Exact,
    Alias,
    Fuzzy,
    Unmapped
}

/// <summary>How a detected source column was mapped to a canonical statement field.</summary>
public sealed record StatementColumnMapping(
    string SourceColumn,
    StatementCanonicalField? CanonicalField,
    StatementMappingConfidence Confidence,
    decimal Score,
    string Rationale);

/// <summary>
/// A parse/mapping diagnostic. Severity is "Error", "Warning", or "Info"; only Error
/// severities block a commit.
/// </summary>
public sealed record StatementParseIssue(
    string Code,
    string Severity,
    int? RowNumber,
    string? Field,
    string Message)
{
    public const string ErrorSeverity = "Error";
    public const string WarningSeverity = "Warning";
    public const string InfoSeverity = "Info";

    public static StatementParseIssue Error(string code, string message, int? rowNumber = null, string? field = null) =>
        new(code, ErrorSeverity, rowNumber, field, message);

    public static StatementParseIssue Warning(string code, string message, int? rowNumber = null, string? field = null) =>
        new(code, WarningSeverity, rowNumber, field, message);

    public static StatementParseIssue Info(string code, string message, int? rowNumber = null, string? field = null) =>
        new(code, InfoSeverity, rowNumber, field, message);
}

/// <summary>
/// The kind of statement data a canonical record carries. Mirrors the Domain
/// <c>StatementRowKind</c> taxonomy so mixed statements (positions + transactions +
/// cash + fees + dividends) classify identically across every connector.
/// </summary>
public enum StatementRecordKind
{
    Position,
    CashBalance,
    Transaction,
    Fee,
    Dividend
}

/// <summary>
/// One canonical statement row as produced by a connector; rendered deterministically to
/// the canonical CSV artifact on commit.
/// </summary>
public sealed record StatementCanonicalRecord(
    StatementRecordKind Kind,
    string Account,
    string Symbol,
    decimal Quantity,
    decimal Price,
    decimal CashAmount,
    string ActivityType,
    DateOnly TradeDate,
    DateOnly? SettlementDate = null,
    string? Currency = null,
    decimal? FeesCommission = null,
    string? ExternalTransactionId = null,
    string? ActivityCategory = null,
    string? ActivitySubtype = null,
    string? ProviderActivityCode = null,
    string? RelatedTransactionId = null,
    string? OrderId = null,
    string? Description = null);

/// <summary>
/// Structural fingerprint of a parsed source used for format-drift detection: when a
/// custodian silently changes its export layout, the column set diff surfaces before
/// rows are mapped incorrectly.
/// </summary>
public sealed record StatementFormatFingerprint(
    string Sha256,
    IReadOnlyList<string> NormalizedColumns,
    string Delimiter);

public sealed record StatementParseResult(
    string ConnectorId,
    string? ProfileId,
    IReadOnlyList<string> DetectedColumns,
    IReadOnlyList<StatementColumnMapping> ColumnMappings,
    IReadOnlyList<StatementCanonicalRecord> Records,
    IReadOnlyList<StatementParseIssue> Issues,
    StatementFormatFingerprint Fingerprint,
    IReadOnlyList<BrokerageAccountSnapshotDto>? AccountSnapshots = null,
    IReadOnlyList<BrokerageActivityEventDto>? ActivityEvents = null,
    IReadOnlyList<BrokerageActivityCursorDto>? ActivityCursors = null,
    IReadOnlyList<BrokerageTaxLotSnapshotDto>? TaxLots = null,
    IReadOnlyList<BrokerageBorrowPositionSnapshotDto>? BorrowPositions = null)
{
    public bool HasErrors => Issues.Any(static issue =>
        string.Equals(issue.Severity, StatementParseIssue.ErrorSeverity, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Additive canonical evidence retained beside the reconciliation CSV. The CSV remains the
/// stable row-ingestion contract; this sidecar preserves account, cursor, options, borrow,
/// and tax-lot detail without flattening missing values into zeroes.
/// </summary>
public sealed record StatementCanonicalEvidenceArtifact(
    string ConnectorId,
    string? ProfileId,
    DateTimeOffset RetainedAtUtc,
    StatementFormatFingerprint Fingerprint,
    IReadOnlyList<StatementCanonicalRecord> Records,
    IReadOnlyList<BrokerageAccountSnapshotDto> AccountSnapshots,
    IReadOnlyList<BrokerageActivityEventDto> ActivityEvents,
    IReadOnlyList<BrokerageActivityCursorDto> ActivityCursors,
    IReadOnlyList<BrokerageTaxLotSnapshotDto> TaxLots,
    IReadOnlyList<BrokerageBorrowPositionSnapshotDto> BorrowPositions);

public interface IStatementConnector
{
    StatementConnectorDescriptor Descriptor { get; }

    /// <summary>Extension and content sniffing; used for connector auto-detection.</summary>
    bool CanHandle(StatementSourceDocument document);

    Task<StatementParseResult> ParseAsync(StatementSourceDocument document, CancellationToken ct = default);
}

/// <summary>Which remote datasets a fetch-capable connector should pull.</summary>
[Flags]
public enum StatementFetchDatasets
{
    Activity = 1,
    Positions = 2,
    All = Activity | Positions
}

public sealed record StatementFetchRequest(
    string ConnectorId,
    string ExternalAccountId,
    DateTimeOffset? Since = null,
    string? MappingProfileId = null,
    StatementFetchDatasets Datasets = StatementFetchDatasets.All);

/// <summary>
/// A connector that can pull statements from a remote source. Fetch yields a retained
/// document that <see cref="IStatementConnector.ParseAsync"/> then consumes, keeping a
/// single parse path and raw evidence for every import regardless of origin.
/// </summary>
public interface IFetchingStatementConnector : IStatementConnector
{
    Task<StatementSourceDocument> FetchAsync(StatementFetchRequest request, CancellationToken ct = default);
}

/// <summary>
/// Maps canonical activity types (position, cash, cashbalance, trade, fee, dividend, div)
/// to record kinds with the same semantics as the downstream reconciliation row classifier,
/// so every connector buckets mixed-kind statements identically.
/// </summary>
public static class StatementRecordKindResolver
{
    public static StatementRecordKind Resolve(string canonicalActivityType)
    {
        if (canonicalActivityType.Equals("position", StringComparison.OrdinalIgnoreCase))
        {
            return StatementRecordKind.Position;
        }

        if (canonicalActivityType.Equals("cash", StringComparison.OrdinalIgnoreCase)
            || canonicalActivityType.Equals("cashbalance", StringComparison.OrdinalIgnoreCase))
        {
            return StatementRecordKind.CashBalance;
        }

        if (canonicalActivityType.Equals("fee", StringComparison.OrdinalIgnoreCase))
        {
            return StatementRecordKind.Fee;
        }

        if (canonicalActivityType.Equals("dividend", StringComparison.OrdinalIgnoreCase)
            || canonicalActivityType.Equals("div", StringComparison.OrdinalIgnoreCase))
        {
            return StatementRecordKind.Dividend;
        }

        return StatementRecordKind.Transaction;
    }
}
