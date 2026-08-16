using Meridian.Contracts.Integrity;
using Meridian.Domain.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

public sealed record StatementRunCreateRequest(
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
    public string? CanonicalSourcePath { get; init; }

    public string CanonicalArtifactHash { get; init; } = string.Empty;

    public StatementAccountingScope? AccountingScope { get; init; }

    public string DuplicateKey => AccountingScope is null
        ? StatementDuplicateKey.Create(
            FundAccountId,
            StatementPeriodStart,
            StatementPeriodEnd,
            SourceFileHash,
            CanonicalArtifactHash)
        : StatementDuplicateKey.Create(
            FundAccountId,
            StatementPeriodStart,
            StatementPeriodEnd,
            SourceFileHash,
            CanonicalArtifactHash,
            AccountingScope);

    public static async Task<StatementRunCreateRequest> FromFileAsync(
        string broker,
        string sourceInstitution,
        string fundAccountId,
        string externalAccountId,
        DateOnly statementPeriodStart,
        DateOnly statementPeriodEnd,
        string sourcePath,
        string mappingProfileId,
        string toleranceProfileId,
        string importedBy,
        string? originalFileName = null,
        CancellationToken ct = default)
    {
        ValidateRequired(nameof(broker), broker);
        ValidateRequired(nameof(sourceInstitution), sourceInstitution);
        ValidateRequired(nameof(fundAccountId), fundAccountId);
        ValidateRequired(nameof(externalAccountId), externalAccountId);
        ValidateRequired(nameof(sourcePath), sourcePath);
        ValidateRequired(nameof(mappingProfileId), mappingProfileId);
        ValidateRequired(nameof(toleranceProfileId), toleranceProfileId);
        ValidateRequired(nameof(importedBy), importedBy);

        if (statementPeriodEnd < statementPeriodStart)
            throw new ArgumentException("Statement period end must be on or after statement period start.", nameof(statementPeriodEnd));

        await using var stream = File.OpenRead(sourcePath);
        // Same SourceFileHash family as BrokerStatementInfrastructure.CaptureFileAsync and
        // StatementRunWorkflowService.ComputeFileHashAsync — all three producers must emit the
        // canonical encoding so the value is consistent wherever it is recorded (#2691).
        var sourceFileHash = await Sha256Digest.ComputeAsync(stream, ct).ConfigureAwait(false);

        return new StatementRunCreateRequest(
            broker.Trim(),
            sourceInstitution.Trim(),
            fundAccountId.Trim(),
            externalAccountId.Trim(),
            statementPeriodStart,
            statementPeriodEnd,
            sourcePath,
            string.IsNullOrWhiteSpace(originalFileName) ? Path.GetFileName(sourcePath) : originalFileName.Trim(),
            mappingProfileId.Trim(),
            toleranceProfileId.Trim(),
            importedBy.Trim(),
            sourceFileHash);
    }

    public BrokerStatementImportRequest ToBrokerStatementImportRequest()
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
            CanonicalArtifactHash = CanonicalArtifactHash,
            AccountingScope = AccountingScope
        };

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
            CanonicalArtifactHash = CanonicalArtifactHash,
            AccountingScope = AccountingScope
        };

    private static void ValidateRequired(string parameterName, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{parameterName} is required.", parameterName);
    }
}
