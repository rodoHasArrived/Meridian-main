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
    public string DuplicateKey => StatementDuplicateKey.Create(FundAccountId, StatementPeriodStart, StatementPeriodEnd, SourceFileHash);
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

    public string DuplicateKey => StatementDuplicateKey.Create(FundAccountId, StatementPeriodStart, StatementPeriodEnd, SourceFileHash);

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
            SourceFileHash);
}

public static class StatementDuplicateKey
{
    public static string Create(
        string fundAccountId,
        DateOnly statementPeriodStart,
        DateOnly statementPeriodEnd,
        string sourceFileHash)
    {
        var material = string.Join(
            '|',
            Normalize(fundAccountId),
            statementPeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            statementPeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Normalize(sourceFileHash));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(bytes);
    }

    private static string Normalize(string value)
        => value.Trim().ToUpperInvariant();
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
