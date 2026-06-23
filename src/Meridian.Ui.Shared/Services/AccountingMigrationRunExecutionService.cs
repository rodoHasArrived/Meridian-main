using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public sealed class AccountingMigrationRunExecutionService
{
    private const string DefaultFundProfileId = "default-fund";

    private readonly IAccountingMigrationRunArtifactStore _store;

    public AccountingMigrationRunExecutionService(IAccountingMigrationRunArtifactStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<AccountingMigrationRunExecutionResultDto> ExecuteAsync(
        AccountingMigrationRunExecutionRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin);

        var actor = RequireText(request.Actor, "Migration run actor");
        var fundProfileId = NormalizeFundProfileId(request.FundProfileId);
        var tenantId = NormalizeOptional(request.TenantId);
        var companyId = NormalizeOptional(request.CompanyId);
        var startedAt = DateTimeOffset.UtcNow;
        var runId = NormalizeOptional(request.RunId)
            ?? BuildRunId(request.Kind, fundProfileId, request.LedgerBookId, startedAt);
        var issues = BuildIssues(request, fundProfileId, tenantId, companyId);
        var completedAt = DateTimeOffset.UtcNow;
        var status = issues.Any(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical)
            ? AccountingMigrationRunStatusDto.Failed
            : request.CertifyOnSuccess
                ? AccountingMigrationRunStatusDto.Certified
                : AccountingMigrationRunStatusDto.Completed;
        var evidenceReferences = BuildEvidenceReferences(
            request,
            runId,
            fundProfileId,
            request.LedgerBookId,
            tenantId,
            companyId,
            status);
        var artifact = new AccountingMigrationRunArtifactDto(
            runId,
            request.Kind,
            status,
            startedAt,
            completedAt,
            actor,
            MigratedRecordCount: EstimateMigratedRecordCount(request.Kind, request.Dimensions, status),
            IssueCount: issues.Count,
            EvidenceReferences: evidenceReferences,
            FundProfileId: fundProfileId,
            LedgerBookId: request.LedgerBookId,
            Summary: BuildSummary(request.Kind, status, issues.Count, request.CertifyOnSuccess),
            Dimensions: NormalizeDimensions(request.Dimensions, fundProfileId, request.LedgerBookId),
            TenantId: tenantId,
            CompanyId: companyId);

        var retained = await _store
            .UpsertAsync(
                new AccountingMigrationRunArtifactUpsertRequestDto(
                    artifact,
                    actor,
                    request.CorrelationId,
                    EvidenceLinks: []),
                ct)
            .ConfigureAwait(false);

        return new AccountingMigrationRunExecutionResultDto(
            retained,
            retained.Status,
            retained.Status == AccountingMigrationRunStatusDto.Certified,
            issues,
            retained.EvidenceReferences);
    }

    private static IReadOnlyList<AccountingMigrationRunExecutionIssueDto> BuildIssues(
        AccountingMigrationRunExecutionRequestDto request,
        string fundProfileId,
        string? tenantId,
        string? companyId)
    {
        var issues = new List<AccountingMigrationRunExecutionIssueDto>();
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            issues.Add(Issue(
                "migration-run.tenant-scope-missing",
                "Migration runs require tenant scope.",
                "Run the migration from an authenticated tenant-scoped workstation session."));
        }

        if (string.IsNullOrWhiteSpace(companyId))
        {
            issues.Add(Issue(
                "migration-run.company-scope-missing",
                "Migration runs require company scope.",
                "Run the migration from an authenticated company-scoped workstation session."));
        }

        if (string.IsNullOrWhiteSpace(fundProfileId))
        {
            issues.Add(Issue(
                "migration-run.fund-scope-missing",
                "Migration runs require fund profile scope.",
                "Select the fund profile whose accounting data is being migrated."));
        }

        if (!request.LedgerBookId.HasValue)
        {
            issues.Add(Issue(
                "migration-run.ledger-book-scope-missing",
                "Migration runs require ledger-book scope.",
                "Select the target ledger book before executing migration or backfill work."));
        }

        if (request.CertifyOnSuccess && request.EvidenceLinks.Count == 0)
        {
            issues.Add(Issue(
                "migration-run.certification-evidence-missing",
                "Certified migration runs require retained evidence.",
                "Attach retained migration evidence before requesting certification."));
        }

        if (request.Kind == AccountingMigrationRunKindDto.DimensionalBackfill)
        {
            AddDimensionalBackfillIssues(request, fundProfileId, issues);
        }

        return issues;
    }

    private static void AddDimensionalBackfillIssues(
        AccountingMigrationRunExecutionRequestDto request,
        string fundProfileId,
        List<AccountingMigrationRunExecutionIssueDto> issues)
    {
        if (request.Dimensions is null)
        {
            issues.Add(Issue(
                "migration-run.dimensions-missing",
                "Dimensional backfill runs require a retained dimension coverage envelope.",
                "Provide fund, book, and canonical ledger dimensions before running dimensional backfill."));
            return;
        }

        var ledgerBookId = request.LedgerBookId?.ToString("D");
        if (!string.Equals(request.Dimensions.FundId, fundProfileId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.Dimensions.BookId, ledgerBookId, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Issue(
                "migration-run.dimension-scope-mismatch",
                "Dimensional backfill scope must match the selected fund profile and ledger book.",
                "Use dimensions for the same fund and ledger book as the migration request."));
        }
    }

    private static AccountingMigrationRunExecutionIssueDto Issue(
        string code,
        string message,
        string suggestedAction)
        => new(
            code,
            AccountingConfigurationValidationSeverityDto.Critical,
            message,
            suggestedAction);

    private static IReadOnlyList<string> BuildEvidenceReferences(
        AccountingMigrationRunExecutionRequestDto request,
        string runId,
        string fundProfileId,
        Guid? ledgerBookId,
        string? tenantId,
        string? companyId,
        AccountingMigrationRunStatusDto status)
    {
        var evidence = request.EvidenceLinks
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .ToList();

        if (!string.IsNullOrWhiteSpace(tenantId) &&
            !string.IsNullOrWhiteSpace(companyId) &&
            ledgerBookId.HasValue)
        {
            evidence.Add(
                $"evidence://migration/tenant/{tenantId}/company/{companyId}/fund/{fundProfileId}/ledger-book/{ledgerBookId:D}/{MigrationKindCode(request.Kind)}/{status.ToString().ToLowerInvariant()}/run/{runId}");
        }

        if (!string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            evidence.Add($"correlation:{request.CorrelationId.Trim()}");
        }

        return evidence
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static LedgerDimensionSetDto? NormalizeDimensions(
        LedgerDimensionSetDto? dimensions,
        string fundProfileId,
        Guid? ledgerBookId)
        => dimensions is null
            ? null
            : dimensions with
            {
                FundId = NormalizeOptional(dimensions.FundId) ?? fundProfileId,
                BookId = NormalizeOptional(dimensions.BookId) ?? ledgerBookId?.ToString("D")
            };

    private static int EstimateMigratedRecordCount(
        AccountingMigrationRunKindDto kind,
        LedgerDimensionSetDto? dimensions,
        AccountingMigrationRunStatusDto status)
    {
        if (status == AccountingMigrationRunStatusDto.Failed)
        {
            return 0;
        }

        return kind switch
        {
            AccountingMigrationRunKindDto.DimensionalBackfill => dimensions is null ? 0 : 1,
            AccountingMigrationRunKindDto.HistoricalJournalBackfill => 1,
            AccountingMigrationRunKindDto.LedgerBookScope => 1,
            AccountingMigrationRunKindDto.AccountingConfigurationPromotion => 1,
            AccountingMigrationRunKindDto.CloseReportingEvidence => 1,
            _ => 0
        };
    }

    private static string BuildRunId(
        AccountingMigrationRunKindDto kind,
        string fundProfileId,
        Guid? ledgerBookId,
        DateTimeOffset timestamp)
        => $"migration-run-{MigrationKindCode(kind)}-{Sanitize(fundProfileId)}-{ledgerBookId?.ToString("N") ?? "unscoped"}-{timestamp:yyyyMMddHHmmss}";

    private static string BuildSummary(
        AccountingMigrationRunKindDto kind,
        AccountingMigrationRunStatusDto status,
        int issueCount,
        bool certifyOnSuccess)
    {
        if (status == AccountingMigrationRunStatusDto.Failed)
        {
            return $"{MigrationKindLabel(kind)} migration run failed with {issueCount} blocking issue(s).";
        }

        if (status == AccountingMigrationRunStatusDto.Certified)
        {
            return $"{MigrationKindLabel(kind)} migration run completed and retained certification evidence.";
        }

        return certifyOnSuccess
            ? $"{MigrationKindLabel(kind)} migration run completed, but certification was not retained."
            : $"{MigrationKindLabel(kind)} migration run completed as a retained rollout artifact.";
    }

    private static string MigrationKindCode(AccountingMigrationRunKindDto kind)
        => kind switch
        {
            AccountingMigrationRunKindDto.LedgerBookScope => "ledger-book-scope",
            AccountingMigrationRunKindDto.HistoricalJournalBackfill => "historical-journal-backfill",
            AccountingMigrationRunKindDto.DimensionalBackfill => "dimensional-backfill",
            AccountingMigrationRunKindDto.AccountingConfigurationPromotion => "configuration-promotion",
            AccountingMigrationRunKindDto.CloseReportingEvidence => "close-reporting-evidence",
            _ => kind.ToString().ToLowerInvariant()
        };

    private static string MigrationKindLabel(AccountingMigrationRunKindDto kind)
        => kind switch
        {
            AccountingMigrationRunKindDto.LedgerBookScope => "Ledger-book scope",
            AccountingMigrationRunKindDto.HistoricalJournalBackfill => "Historical journal backfill",
            AccountingMigrationRunKindDto.DimensionalBackfill => "Dimensional backfill",
            AccountingMigrationRunKindDto.AccountingConfigurationPromotion => "Accounting configuration promotion",
            AccountingMigrationRunKindDto.CloseReportingEvidence => "Close and reporting evidence",
            _ => kind.ToString()
        };

    private static void EnsureHumanOrigin(OperationsActionOriginDto actionOrigin)
    {
        if (actionOrigin != OperationsActionOriginDto.HumanOperator)
        {
            throw new ArgumentException("Only a human operator can execute accounting migration runs.", nameof(actionOrigin));
        }
    }

    private static string RequireText(string? value, string label)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{label} is required.", nameof(value))
            : value.Trim();

    private static string NormalizeFundProfileId(string? value)
        => string.IsNullOrWhiteSpace(value) ? DefaultFundProfileId : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Sanitize(string value)
    {
        var chars = value
            .Select(static ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-')
            .ToArray();
        return new string(chars).Trim('-');
    }
}
