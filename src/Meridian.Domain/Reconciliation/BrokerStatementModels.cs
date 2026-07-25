using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Meridian.Domain.Reconciliation;

public sealed record StatementRunRequest(
    string Broker,
    string SourceInstitution,
    string FundAccountId,
    string ExternalAccountId,
    DateOnly StatementPeriodStart,
    DateOnly StatementPeriodEnd,
    string SourcePath,
    string OriginalFileName,
    string MappingProfileId,
    string ToleranceProfileId,
    string ImportedBy,
    string SourceFileHash)
{
    /// <summary>
    /// Optional normalized artifact consumed by the reconciliation parser. When omitted, the
    /// retained source file is parsed directly.
    /// </summary>
    public string? CanonicalSourcePath { get; init; }

    /// <summary>
    /// Optional SHA-256 assertion for <see cref="CanonicalSourcePath"/>. The importer always
    /// recomputes this value from the captured parse bytes before accepting it.
    /// </summary>
    public string CanonicalArtifactHash { get; init; } = string.Empty;

    public string EffectiveParsePath =>
        string.IsNullOrWhiteSpace(CanonicalSourcePath) ? SourcePath : CanonicalSourcePath;

    public string DuplicateKey => StatementDuplicateKey.Create(
        FundAccountId,
        StatementPeriodStart,
        StatementPeriodEnd,
        SourceFileHash,
        CanonicalArtifactHash);
}

public sealed record BrokerStatementImportRequest(
    string Broker,
    string SourceInstitution,
    string FundAccountId,
    string ExternalAccountId,
    DateOnly StatementPeriodStart,
    DateOnly StatementPeriodEnd,
    string SourcePath,
    string OriginalFileName,
    string MappingProfileId,
    string ToleranceProfileId,
    string ImportedBy,
    string SourceFileHash)
{
    public BrokerStatementImportRequest(string broker, string sourcePath, DateOnly statementDate)
        : this(
            broker,
            broker,
            string.Empty,
            string.Empty,
            statementDate,
            statementDate,
            sourcePath,
            Path.GetFileName(sourcePath),
            string.Empty,
            string.Empty,
            "system",
            string.Empty)
    {
    }

    public DateOnly StatementDate => StatementPeriodEnd;

    public string DuplicateKey => StatementDuplicateKey.Create(
        FundAccountId,
        StatementPeriodStart,
        StatementPeriodEnd,
        SourceFileHash,
        CanonicalArtifactHash);

    /// <summary>
    /// Optional normalized artifact consumed by the format-specific parser. The retained raw
    /// source remains <see cref="SourcePath"/>.
    /// </summary>
    public string? CanonicalSourcePath { get; init; }

    /// <summary>
    /// Optional SHA-256 assertion for the canonical parse artifact. It is never trusted without
    /// recomputing the hash from the same immutable bytes that are parsed.
    /// </summary>
    public string CanonicalArtifactHash { get; init; } = string.Empty;

    public string EffectiveParsePath =>
        string.IsNullOrWhiteSpace(CanonicalSourcePath) ? SourcePath : CanonicalSourcePath;

    public string EffectiveParseHash =>
        string.IsNullOrWhiteSpace(CanonicalArtifactHash) ? SourceFileHash : CanonicalArtifactHash;

    public BrokerStatementImportRequest WithSourceFileHash(string sourceFileHash)
        => this with { SourceFileHash = sourceFileHash };

    public StatementRunRequest ToStatementRunRequest()
        => new(
            Broker,
            SourceInstitution,
            FundAccountId,
            ExternalAccountId,
            StatementPeriodStart,
            StatementPeriodEnd,
            SourcePath,
            OriginalFileName,
            MappingProfileId,
            ToleranceProfileId,
            ImportedBy,
            SourceFileHash)
        {
            CanonicalSourcePath = CanonicalSourcePath,
            CanonicalArtifactHash = CanonicalArtifactHash
        };
}

public static class StatementDuplicateKey
{
    public static string Create(
        string fundAccountId,
        DateOnly statementPeriodStart,
        DateOnly statementPeriodEnd,
        string sourceFileHash,
        string? canonicalArtifactHash = null)
    {
        var sourceHash = Normalize(sourceFileHash);
        var canonicalHash = Normalize(canonicalArtifactHash ?? string.Empty);
        var materialParts = new List<string>
        {
            Normalize(fundAccountId),
            statementPeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            statementPeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            sourceHash
        };
        if (canonicalHash.Length > 0 && !string.Equals(sourceHash, canonicalHash, StringComparison.Ordinal))
        {
            materialParts.Add(canonicalHash);
        }

        var material = string.Join('|', materialParts);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Returns the current raw-plus-canonical identity followed by the canonical-only identity used
    /// by statement connector imports before raw source hashes were retained separately. Callers
    /// use both candidates when checking existing runs, but only the first candidate for new runs.
    /// </summary>
    public static IReadOnlyList<string> CreateCompatibleKeys(
        string fundAccountId,
        DateOnly statementPeriodStart,
        DateOnly statementPeriodEnd,
        string sourceFileHash,
        string? canonicalArtifactHash = null)
    {
        var current = Create(
            fundAccountId,
            statementPeriodStart,
            statementPeriodEnd,
            sourceFileHash,
            canonicalArtifactHash);
        var canonicalHash = Normalize(canonicalArtifactHash ?? string.Empty);
        if (canonicalHash.Length == 0 ||
            string.Equals(canonicalHash, Normalize(sourceFileHash), StringComparison.Ordinal))
        {
            return [current];
        }

        var canonicalOnly = Create(
            fundAccountId,
            statementPeriodStart,
            statementPeriodEnd,
            canonicalHash);
        return string.Equals(current, canonicalOnly, StringComparison.Ordinal)
            ? [current]
            : [current, canonicalOnly];
    }

    private static string Normalize(string value)
        => value.Trim().ToUpperInvariant();
}

public sealed class StatementAlreadyImportedException : InvalidOperationException
{
    public StatementAlreadyImportedException(string existingImportId)
        : base("Statement already imported (fund account, statement period, and retained artifact hashes match).")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(existingImportId);
        ExistingImportId = existingImportId;
    }

    public string ExistingImportId { get; }
}

public sealed record BrokerStatementValidationResult(bool IsValid, IReadOnlyList<string> Errors, int RowCount);

public sealed record BrokerStatementImportResult(CanonicalStatementImport Import, IReadOnlyList<CanonicalStatementRow> Rows);

public sealed record MatchOutcome(
    string RowChecksum,
    string OutcomeType,
    string LinkedEntityId,
    decimal Confidence,
    string Rationale)
{
    public string ToleranceProfileId { get; init; } = string.Empty;
    public int ToleranceProfileVersion { get; init; }
    public string? ToleranceRuleId { get; init; }
}

public interface IBrokerStatementService
{
    Task<BrokerStatementValidationResult> ValidateAsync(BrokerStatementImportRequest request, CancellationToken ct = default);
    Task<BrokerStatementImportResult> ImportAsync(BrokerStatementImportRequest request, CancellationToken ct = default);
}

public interface IReconciliationCaseService
{
    Task<IReadOnlyList<ReconciliationCase>> CreateOpenCasesAsync(string importId, IReadOnlyList<MatchOutcome> outcomes, CancellationToken ct = default);
    Task<ReconciliationCase> UpdateStatusAsync(string caseId, string toStatus, string note, CancellationToken ct = default);
    Task<ReconciliationCase> AssignAsync(string caseId, string assignee, string note, CancellationToken ct = default);
    Task<ReconciliationCase> AddCommentAsync(string caseId, string subject, string body, string actor, string? parentCommentId = null, CancellationToken ct = default);
    Task<IReadOnlyList<ReconciliationCase>> ListOpenCasesAsync(CancellationToken ct = default);
}
