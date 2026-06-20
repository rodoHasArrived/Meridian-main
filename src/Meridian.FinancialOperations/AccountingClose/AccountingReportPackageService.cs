using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Storage;
using Meridian.Storage.Archival;

namespace Meridian.FinancialOperations.AccountingClose;

public interface IAccountingReportPackageService
{
    Task<AccountingReportPackageBundleDto> BuildPackageAsync(
        AccountingReportPackageRequestDto request,
        CancellationToken ct = default);

    Task<IReadOnlyList<AccountingReportPackageBundleDto>> ListPackagesAsync(
        string? fundProfileId = null,
        string? periodId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default);

    Task<AccountingReportPackageBundleDto?> CertifyPackageAsync(
        CertifyAccountingReportPackageRequestDto request,
        CancellationToken ct = default);

    Task<ReportExportArtifactManifestDto?> GetExportArtifactManifestAsync(
        string packageId,
        string artifactId,
        CancellationToken ct = default);
}

public sealed class AccountingReportPackageService : IAccountingReportPackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IAccountingCloseManagementService? _closeManagementService;
    private readonly object _readGate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly string? _persistencePath;
    private IReadOnlyList<AccountingReportPackageBundleDto> _inMemoryPackages = [];

    public AccountingReportPackageService(IAccountingCloseManagementService? closeManagementService = null)
    {
        _closeManagementService = closeManagementService;
    }

    public AccountingReportPackageService(
        IAccountingCloseManagementService? closeManagementService,
        StorageOptions storageOptions)
        : this(closeManagementService)
    {
        ArgumentNullException.ThrowIfNull(storageOptions);
        _persistencePath = Path.Combine(storageOptions.RootPath, "accounting", "accounting-report-packages.json");
    }

    public async Task<AccountingReportPackageBundleDto> BuildPackageAsync(
        AccountingReportPackageRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = RequireText(request.Actor, "Actor");
        var fundProfileId = RequireText(request.FundProfileId, "FundProfileId");
        var periodId = RequireText(request.PeriodId, "PeriodId");
        var currency = string.IsNullOrWhiteSpace(request.Currency) ? "USD" : request.Currency.Trim().ToUpperInvariant();
        var evidenceLinks = NormalizeEvidenceLinks(request.EvidenceLinks);
        var validationIssues = new List<AccountingConfigurationValidationIssueDto>();
        var closePlan = request.CloseWorkflowId.HasValue && _closeManagementService is not null
            ? await _closeManagementService.GetPeriodPlanAsync(request.CloseWorkflowId.Value, ct).ConfigureAwait(false)
            : null;
        var ledgerBookId = request.LedgerBookId ?? closePlan?.LedgerBookId;

        if (!ledgerBookId.HasValue)
        {
            validationIssues.Add(new AccountingConfigurationValidationIssueDto(
                "ReportPackageLedgerBookMissing",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Accounting report package for period '{periodId}' is missing ledger-book scope.",
                periodId,
                "Select a ledger book or build from a close workflow that carries ledger-book scope before report certification."));
        }

        if (request.CloseWorkflowId.HasValue && closePlan is null)
        {
            validationIssues.Add(new AccountingConfigurationValidationIssueDto(
                "ClosePlanMissing",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Close workflow '{request.CloseWorkflowId.Value:D}' was not found.",
                request.CloseWorkflowId.Value.ToString("D"),
                "Create or select a close workflow before certifying the accounting report package."));
        }

        if (closePlan is not null)
        {
            validationIssues.AddRange(closePlan.ValidationIssues);
            validationIssues.AddRange(BuildCloseCertificationIssues(closePlan));
            validationIssues.AddRange(BuildCloseLedgerBookConsistencyIssues(
                closePlan,
                ledgerBookId,
                $"report package for period '{periodId}'"));
            if (!closePlan.IsPeriodLocked)
            {
                validationIssues.Add(new AccountingConfigurationValidationIssueDto(
                    "PeriodNotLocked",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    "The close period is not locked; close-backed report package certification is blocked.",
                    closePlan.ClosePlanId,
                    "Lock the period after close approvals before final report certification."));
            }
        }

        if (evidenceLinks.Count == 0)
        {
            validationIssues.Add(new AccountingConfigurationValidationIssueDto(
                "ReportEvidenceMissing",
                AccountingConfigurationValidationSeverityDto.Warning,
                "No retained report evidence links were supplied for the package.",
                periodId,
                "Attach close package, ledger, reconciliation, and report-render evidence before certification."));
        }

        validationIssues.AddRange(BuildReportCertificationEvidenceIssues(periodId, evidenceLinks));
        validationIssues.AddRange(BuildRestatementCertificationIssues(request, fundProfileId, periodId, evidenceLinks, ReadPackages()));

        var hasCritical = validationIssues.Any(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        var state = hasCritical ? AccountingCertificationStateDto.Draft : AccountingCertificationStateDto.ReadyForReview;
        var packageId = BuildPackageId(fundProfileId, periodId, ledgerBookId);
        ThrowIfCertifiedPackageWouldBeReplaced(packageId, ReadPackages());
        var certification = new ReportCertificationDto(
            $"report-certification-{Sanitize(packageId)}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            state,
            actor,
            DateTimeOffset.UtcNow,
            hasCritical
                ? "Accounting report package has blocking validation issues and cannot be certified."
                : "Accounting report package is assembled and ready for human certification review.",
            evidenceLinks);
        var restatement = BuildRestatement(request, actor, evidenceLinks);
        var endingCapital = request.BeginningCapital + request.Contributions - request.Distributions + request.RealizedGainLoss;
        var lineProvenance = BuildReportLineProvenance(
            request,
            fundProfileId,
            periodId,
            currency,
            endingCapital,
            ledgerBookId,
            restatement,
            evidenceLinks);

        var financialStatements = new FinancialStatementPackageDto(
            packageId,
            fundProfileId,
            ledgerBookId,
            periodId,
            state,
            ["balance-sheet", "income-statement", "trial-balance", "statement-of-changes-in-capital"],
            evidenceLinks,
            certification,
            restatement,
            lineProvenance);
        var investorStatement = new InvestorCapitalStatementDto(
            $"investor-capital-statement-{Sanitize(fundProfileId)}-{Sanitize(periodId)}-{Sanitize(request.CapitalAccountId ?? "aggregate")}",
            fundProfileId,
            string.IsNullOrWhiteSpace(request.CapitalAccountId) ? "capital-account:aggregate" : request.CapitalAccountId.Trim(),
            string.IsNullOrWhiteSpace(request.InvestorId) ? null : request.InvestorId.Trim(),
            periodId,
            request.BeginningCapital,
            request.Contributions,
            request.Distributions,
            request.RealizedGainLoss,
            endingCapital,
            currency,
            state,
            evidenceLinks);
        var realizedGainLoss = new RealizedGainLossReportDto(
            $"realized-gain-loss-{Sanitize(fundProfileId)}-{Sanitize(periodId)}",
            fundProfileId,
            ledgerBookId,
            periodId,
            new LedgerDimensionSetDto(
                FundId: fundProfileId,
                InvestorId: request.InvestorId,
                CapitalAccountId: request.CapitalAccountId,
                BookId: ledgerBookId?.ToString("D")),
            request.RealizedGainLoss,
            currency,
            state,
            evidenceLinks);
        var navPackage = new NavPackageDto(
            $"nav-package-{Sanitize(fundProfileId)}-{Sanitize(periodId)}",
            fundProfileId,
            ledgerBookId,
            periodId,
            request.Nav,
            currency,
            state,
            evidenceLinks,
            certification,
            restatement);
        var exportArtifacts = BuildReportExportArtifacts(
            financialStatements,
            [investorStatement],
            realizedGainLoss,
            navPackage,
            state,
            certification.RecordedAtUtc,
            evidenceLinks,
            restatement);

        var bundle = new AccountingReportPackageBundleDto(
            financialStatements,
            [investorStatement],
            realizedGainLoss,
            navPackage,
            certification,
            validationIssues,
            exportArtifacts,
            request.CloseWorkflowId);
        await SavePackageAsync(bundle, ct).ConfigureAwait(false);
        return bundle;
    }

    public Task<IReadOnlyList<AccountingReportPackageBundleDto>> ListPackagesAsync(
        string? fundProfileId = null,
        string? periodId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedFundProfileId = string.IsNullOrWhiteSpace(fundProfileId) ? null : fundProfileId.Trim();
        var normalizedPeriodId = string.IsNullOrWhiteSpace(periodId) ? null : periodId.Trim();
        var rows = ReadPackages()
            .Where(package => normalizedFundProfileId is null || string.Equals(package.FinancialStatements.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase))
            .Where(package => normalizedPeriodId is null || string.Equals(package.FinancialStatements.PeriodId, normalizedPeriodId, StringComparison.OrdinalIgnoreCase))
            .Where(package => !ledgerBookId.HasValue || package.FinancialStatements.LedgerBookId == ledgerBookId.Value)
            .OrderByDescending(static package => package.Certification.RecordedAtUtc)
            .ToArray();

        return Task.FromResult<IReadOnlyList<AccountingReportPackageBundleDto>>(rows);
    }

    public async Task<AccountingReportPackageBundleDto?> CertifyPackageAsync(
        CertifyAccountingReportPackageRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, "certify accounting report packages");
        var packageId = RequireText(request.PackageId, "PackageId");
        var actor = RequireText(request.Actor, "Actor");
        var notes = RequireText(request.Notes, "Notes");
        var evidenceLinks = NormalizeEvidenceLinks(request.EvidenceLinks);
        if (evidenceLinks.Count == 0)
        {
            throw new ArgumentException("At least one certification evidence link is required.");
        }

        if (!HasReportCertificationEvidence(evidenceLinks))
        {
            throw new ArgumentException("Accounting report package certification requires retained approval, certification, sign-off, or review evidence.");
        }

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var rows = ReadPackages().ToArray();
            var index = Array.FindIndex(rows, package =>
                string.Equals(package.FinancialStatements.PackageId, packageId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(package.Certification.CertificationId, packageId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return null;
            }

            var package = rows[index];
            if (!HasReportCertificationEvidenceWithProvenance(package, evidenceLinks))
            {
                throw new ArgumentException("Accounting report package certification evidence must reference the retained package, certification id, and exact package period in the same artifact.");
            }

            var currentCloseIssues = await BuildCurrentCloseCertificationIssuesAsync(package, ct).ConfigureAwait(false);
            if (currentCloseIssues.Any(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical))
            {
                var issueCodes = string.Join(", ", currentCloseIssues
                    .Where(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical)
                    .Select(static issue => issue.Code)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static code => code, StringComparer.OrdinalIgnoreCase));
                throw new InvalidOperationException(
                    $"Accounting report package '{package.FinancialStatements.PackageId}' has current close-plan blockers and cannot be certified: {issueCodes}.");
            }

            if (package.Certification.State == AccountingCertificationStateDto.Certified)
            {
                throw new InvalidOperationException($"Accounting report package '{package.FinancialStatements.PackageId}' is already certified.");
            }

            if (package.Certification.State != AccountingCertificationStateDto.ReadyForReview)
            {
                throw new InvalidOperationException(
                    $"Accounting report package '{package.FinancialStatements.PackageId}' must be ready for review before certification.");
            }

            if (package.ValidationIssues.Any(static issue =>
                    issue.Severity == AccountingConfigurationValidationSeverityDto.Critical))
            {
                throw new InvalidOperationException(
                    $"Accounting report package '{package.FinancialStatements.PackageId}' has critical validation issues and cannot be certified.");
            }

            var certifiedAtUtc = DateTimeOffset.UtcNow;
            var certificationEvidenceLinks = MergeEvidenceLinks(package.Certification.EvidenceLinks, evidenceLinks);
            var certification = package.Certification with
            {
                State = AccountingCertificationStateDto.Certified,
                Actor = actor,
                RecordedAtUtc = certifiedAtUtc,
                Summary = notes,
                EvidenceLinks = certificationEvidenceLinks
            };
            var certifiedRestatement = CertifyRestatementWorkflow(
                package.NavPackage.Restatement,
                actor,
                certifiedAtUtc,
                evidenceLinks);
            var certified = package with
            {
                FinancialStatements = package.FinancialStatements with
                {
                    CertificationState = AccountingCertificationStateDto.Certified,
                    EvidenceLinks = MergeEvidenceLinks(package.FinancialStatements.EvidenceLinks, evidenceLinks),
                    Certification = certification,
                    Restatement = certifiedRestatement
                },
                InvestorCapitalStatements = package.InvestorCapitalStatements
                    .Select(statement => statement with
                    {
                        CertificationState = AccountingCertificationStateDto.Certified,
                        EvidenceLinks = MergeEvidenceLinks(statement.EvidenceLinks, evidenceLinks)
                    })
                    .ToArray(),
                RealizedGainLoss = package.RealizedGainLoss with
                {
                    CertificationState = AccountingCertificationStateDto.Certified,
                    EvidenceLinks = MergeEvidenceLinks(package.RealizedGainLoss.EvidenceLinks, evidenceLinks)
                },
                NavPackage = package.NavPackage with
                {
                    CertificationState = AccountingCertificationStateDto.Certified,
                    EvidenceLinks = MergeEvidenceLinks(package.NavPackage.EvidenceLinks, evidenceLinks),
                    Certification = certification,
                    Restatement = certifiedRestatement
                },
                Certification = certification,
                ExportArtifacts = package.ExportArtifacts
                    .Select(artifact => CertifyExportArtifact(
                        package.FinancialStatements.PackageId,
                        package.FinancialStatements.PeriodId,
                        artifact,
                        certifiedAtUtc,
                        evidenceLinks))
                    .ToArray()
            };

            rows[index] = certified;
            await SavePackagesAsync(rows, ct).ConfigureAwait(false);
            return certified;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static RestatementWorkflowDto? CertifyRestatementWorkflow(
        RestatementWorkflowDto? restatement,
        string actor,
        DateTimeOffset certifiedAtUtc,
        IReadOnlyList<string> evidenceLinks)
        => restatement is null
            ? null
            : restatement with
            {
                ApprovalState = ManualJournalEntryStatusDto.Approved,
                RequestedBy = string.IsNullOrWhiteSpace(restatement.RequestedBy)
                    ? actor
                    : restatement.RequestedBy,
                RequestedAtUtc = restatement.RequestedAtUtc == default
                    ? certifiedAtUtc
                    : restatement.RequestedAtUtc,
                EvidenceLinks = MergeEvidenceLinks(restatement.EvidenceLinks, evidenceLinks)
            };

    public Task<ReportExportArtifactManifestDto?> GetExportArtifactManifestAsync(
        string packageId,
        string artifactId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedPackageId = RequireText(packageId, nameof(packageId));
        var normalizedArtifactId = RequireText(artifactId, nameof(artifactId));
        var package = ReadPackages().FirstOrDefault(row =>
            string.Equals(row.FinancialStatements.PackageId, normalizedPackageId, StringComparison.OrdinalIgnoreCase));
        var artifact = package?.ExportArtifacts.FirstOrDefault(row =>
            string.Equals(row.ArtifactId, normalizedArtifactId, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(package is null || artifact is null
            ? null
            : BuildExportArtifactManifest(package, artifact));
    }

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> BuildCloseCertificationIssues(
        ClosePeriodPlanDto closePlan)
    {
        var issues = new List<AccountingConfigurationValidationIssueDto>();
        var incompleteTasks = closePlan.Tasks
            .Where(static task => task.Status != CloseTaskStatusDto.SignedOff)
            .ToArray();
        if (incompleteTasks.Length > 0)
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "CloseChecklistIncomplete",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Close plan '{closePlan.ClosePlanId}' has {incompleteTasks.Length:N0} checklist task(s) that are not signed off.",
                closePlan.ClosePlanId,
                "Complete and sign off every close checklist dependency before report certification."));
        }

        foreach (var task in closePlan.Tasks.Where(static task =>
                     task.SignOffs.All(static signOff => signOff.ApprovalState != ManualJournalEntryStatusDto.Approved)))
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "CloseSignOffMissing",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Close task '{task.DisplayName}' has no approved sign-off retained.",
                task.TaskId,
                "Retain an approved sign-off with evidence before report certification."));
        }

        foreach (var adjustment in closePlan.LateAdjustments.Where(adjustment =>
                     RequiresMaterialLateAdjustmentApproval(adjustment, closePlan.MaterialityPolicy) &&
                     IsLateAdjustmentDecisionPending(adjustment)))
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "LateAdjustmentApprovalPending",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Material late adjustment '{adjustment.RequestId}' is not approved.",
                adjustment.RequestId,
                "Approve or reject material late adjustments before report certification."));
        }

        return issues;
    }

    private async Task<IReadOnlyList<AccountingConfigurationValidationIssueDto>> BuildCurrentCloseCertificationIssuesAsync(
        AccountingReportPackageBundleDto package,
        CancellationToken ct)
    {
        if (!package.CloseWorkflowId.HasValue)
        {
            return [];
        }

        if (_closeManagementService is null)
        {
            return
            [
                new AccountingConfigurationValidationIssueDto(
                    "ClosePlanServiceMissing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Close-backed report package '{package.FinancialStatements.PackageId}' cannot refresh close-plan state because close management is not registered.",
                    package.FinancialStatements.PackageId,
                    "Register close management before certifying close-backed report packages.")
            ];
        }

        var closePlan = await _closeManagementService.GetPeriodPlanAsync(package.CloseWorkflowId.Value, ct).ConfigureAwait(false);
        if (closePlan is null)
        {
            return
            [
                new AccountingConfigurationValidationIssueDto(
                    "ClosePlanMissing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Close workflow '{package.CloseWorkflowId.Value:D}' was not found.",
                    package.CloseWorkflowId.Value.ToString("D"),
                    "Create or select a close workflow before certifying the accounting report package.")
            ];
        }

        var issues = new List<AccountingConfigurationValidationIssueDto>();
        issues.AddRange(BuildCloseLedgerBookConsistencyIssues(
            closePlan,
            package.FinancialStatements.LedgerBookId,
            $"retained report package '{package.FinancialStatements.PackageId}'"));
        issues.AddRange(closePlan.ValidationIssues);
        issues.AddRange(BuildCloseCertificationIssues(closePlan));
        if (!closePlan.IsPeriodLocked)
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "PeriodNotLocked",
                AccountingConfigurationValidationSeverityDto.Critical,
                "The close period is not locked; close-backed report package certification is blocked.",
                closePlan.ClosePlanId,
                "Lock the period after close approvals before final report certification."));
        }

        return issues;
    }

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> BuildCloseLedgerBookConsistencyIssues(
        ClosePeriodPlanDto closePlan,
        Guid? packageLedgerBookId,
        string packageLabel)
    {
        if (!closePlan.LedgerBookId.HasValue)
        {
            return [];
        }

        if (!packageLedgerBookId.HasValue)
        {
            return
            [
                new AccountingConfigurationValidationIssueDto(
                    "ReportPackageLedgerBookMissing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Close-backed {packageLabel} does not carry the close plan ledger book '{closePlan.LedgerBookId.Value:D}'.",
                    closePlan.ClosePlanId,
                    "Rebuild the report package with the close plan ledger book before certification.")
            ];
        }

        if (packageLedgerBookId.Value == closePlan.LedgerBookId.Value)
        {
            return [];
        }

        return
        [
            new AccountingConfigurationValidationIssueDto(
                "ReportPackageLedgerBookMismatch",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Close-backed {packageLabel} targets ledger book '{packageLedgerBookId.Value:D}', but close plan '{closePlan.ClosePlanId}' targets ledger book '{closePlan.LedgerBookId.Value:D}'.",
                closePlan.ClosePlanId,
                "Use the same ledger book for close workflow, report package assembly, certification, and export.")
        ];
    }

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> BuildRestatementCertificationIssues(
        AccountingReportPackageRequestDto request,
        string fundProfileId,
        string periodId,
        IReadOnlyList<string> evidenceLinks,
        IReadOnlyList<AccountingReportPackageBundleDto> retainedPackages)
    {
        if (string.IsNullOrWhiteSpace(request.RestatementReasonCode))
        {
            return [];
        }

        var issues = new List<AccountingConfigurationValidationIssueDto>();
        if (string.IsNullOrWhiteSpace(request.PriorPackageId))
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "RestatementPriorPackageMissing",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Restatement package for period '{periodId}' does not identify the prior certified package.",
                periodId,
                "Select the prior certified report package before restatement certification."));
        }
        else
        {
            var priorPackageId = request.PriorPackageId.Trim();
            var priorPackage = retainedPackages.FirstOrDefault(package =>
                string.Equals(package.FinancialStatements.PackageId, priorPackageId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(package.Certification.CertificationId, priorPackageId, StringComparison.OrdinalIgnoreCase));
            if (priorPackage is null)
            {
                issues.Add(new AccountingConfigurationValidationIssueDto(
                    "RestatementPriorPackageNotRetained",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Restatement package for period '{periodId}' references prior package '{priorPackageId}' that is not retained.",
                    priorPackageId,
                    "Retain the prior certified report package before restatement certification."));
            }
            else
            {
                if (!string.Equals(priorPackage.FinancialStatements.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new AccountingConfigurationValidationIssueDto(
                        "RestatementPriorPackageFundMismatch",
                        AccountingConfigurationValidationSeverityDto.Critical,
                        $"Restatement package for fund '{fundProfileId}' references prior package '{priorPackageId}' for fund '{priorPackage.FinancialStatements.FundProfileId}'.",
                        priorPackageId,
                        "Select a prior certified report package for the same fund before restatement certification."));
                }

                if (priorPackage.Certification.State != AccountingCertificationStateDto.Certified)
                {
                    issues.Add(new AccountingConfigurationValidationIssueDto(
                        "RestatementPriorPackageNotCertified",
                        AccountingConfigurationValidationSeverityDto.Critical,
                        $"Restatement package for period '{periodId}' references prior package '{priorPackageId}' that is not certified.",
                        priorPackageId,
                        "Certify the prior report package before using it as restatement lineage."));
                }
            }
        }

        if (!evidenceLinks.Any(static link =>
                link.Contains("restatement", StringComparison.OrdinalIgnoreCase) ||
                link.Contains("prior-package", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "RestatementEvidenceMissing",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Restatement package for period '{periodId}' has no retained restatement evidence link.",
                periodId,
                "Attach restatement support, prior-package lineage, and approval evidence before certification."));
        }

        return issues;
    }

    private static string BuildPackageId(
        string fundProfileId,
        string periodId,
        Guid? ledgerBookId)
        => ledgerBookId.HasValue
            ? $"accounting-report-package-{Sanitize(fundProfileId)}-{Sanitize(periodId)}-book-{ledgerBookId.Value:N}"
            : $"accounting-report-package-{Sanitize(fundProfileId)}-{Sanitize(periodId)}";

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> BuildReportCertificationEvidenceIssues(
        string periodId,
        IReadOnlyList<string> evidenceLinks)
    {
        var requiredEvidence = new (string Code, string Label, string[] Tokens)[]
        {
            ("ReportLedgerEvidenceMissing", "ledger/trial-balance", ["ledger", "trial-balance"]),
            ("ReportReconciliationEvidenceMissing", "reconciliation", ["reconciliation", "tie-out"]),
            ("ReportRenderEvidenceMissing", "rendered report artifact", ["report-render", "rendered-report", "report-package"]),
            ("ReportNavEvidenceMissing", "NAV support", ["nav", "shadow-nav"])
        };
        var issues = new List<AccountingConfigurationValidationIssueDto>();
        foreach (var requirement in requiredEvidence)
        {
            if (evidenceLinks.Any(link => requirement.Tokens.Any(token =>
                    link.Contains(token, StringComparison.OrdinalIgnoreCase))))
            {
                continue;
            }

            issues.Add(new AccountingConfigurationValidationIssueDto(
                requirement.Code,
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Accounting report package for period '{periodId}' is missing retained {requirement.Label} evidence.",
                periodId,
                "Attach ledger, reconciliation, rendered report, and NAV support evidence before report certification."));
        }

        return issues;
    }

    private static bool HasReportCertificationEvidence(IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(static link =>
            link.Contains("certification", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("sign-off", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("review", StringComparison.OrdinalIgnoreCase));

    private static bool HasReportCertificationEvidenceWithProvenance(
        AccountingReportPackageBundleDto package,
        IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(link =>
            HasReportCertificationEvidence([link]) &&
            link.Contains(package.FinancialStatements.PackageId, StringComparison.OrdinalIgnoreCase) &&
            link.Contains(package.Certification.CertificationId, StringComparison.OrdinalIgnoreCase) &&
            link.Contains(package.FinancialStatements.PeriodId, StringComparison.OrdinalIgnoreCase));

    private static void EnsureHumanOrigin(OperationsActionOriginDto actionOrigin, string action)
    {
        if (actionOrigin != OperationsActionOriginDto.HumanOperator)
        {
            throw new InvalidOperationException($"Reviewed automation cannot {action}; a human operator must perform this accounting report action.");
        }
    }

    private async Task SavePackageAsync(AccountingReportPackageBundleDto bundle, CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var rows = ReadPackages();
            ThrowIfCertifiedPackageWouldBeReplaced(bundle.FinancialStatements.PackageId, rows);
            var updatedRows = rows
                .Where(package => !string.Equals(package.FinancialStatements.PackageId, bundle.FinancialStatements.PackageId, StringComparison.OrdinalIgnoreCase))
                .Append(bundle)
                .ToArray();
            await SavePackagesAsync(updatedRows, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static void ThrowIfCertifiedPackageWouldBeReplaced(
        string packageId,
        IReadOnlyList<AccountingReportPackageBundleDto> retainedPackages)
    {
        var certifiedPackage = retainedPackages.FirstOrDefault(package =>
            string.Equals(package.FinancialStatements.PackageId, packageId, StringComparison.OrdinalIgnoreCase) &&
            package.Certification.State == AccountingCertificationStateDto.Certified);
        if (certifiedPackage is not null)
        {
            throw new InvalidOperationException(
                $"Accounting report package '{packageId}' is certified and immutable; create a restatement package instead of replacing retained certification evidence.");
        }
    }

    private IReadOnlyList<AccountingReportPackageBundleDto> ReadPackages()
    {
        if (string.IsNullOrWhiteSpace(_persistencePath) || !File.Exists(_persistencePath))
        {
            lock (_readGate)
            {
                return _inMemoryPackages;
            }
        }

        lock (_readGate)
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<AccountingReportPackageSnapshot>(
                    File.ReadAllText(_persistencePath),
                    JsonOptions);
                return snapshot?.Packages ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }

    private async Task SavePackagesAsync(
        IReadOnlyList<AccountingReportPackageBundleDto> rows,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_persistencePath))
        {
            lock (_readGate)
            {
                _inMemoryPackages = rows.ToArray();
            }
            return;
        }

        var snapshot = new AccountingReportPackageSnapshot(
            rows
                .OrderBy(static package => package.FinancialStatements.FundProfileId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static package => package.FinancialStatements.PeriodId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static package => package.FinancialStatements.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await AtomicFileWriter.WriteAsync(_persistencePath, json, ct).ConfigureAwait(false);
    }

    private static RestatementWorkflowDto? BuildRestatement(
        AccountingReportPackageRequestDto request,
        string actor,
        IReadOnlyList<string> evidenceLinks)
    {
        if (string.IsNullOrWhiteSpace(request.RestatementReasonCode))
        {
            return null;
        }

        return new RestatementWorkflowDto(
            $"restatement-{Sanitize(request.FundProfileId)}-{Sanitize(request.PeriodId)}-{Guid.NewGuid():N}",
            string.IsNullOrWhiteSpace(request.PriorPackageId) ? "prior-package:unresolved" : request.PriorPackageId.Trim(),
            request.RestatementReasonCode.Trim(),
            ManualJournalEntryStatusDto.Submitted,
            actor,
            DateTimeOffset.UtcNow,
            evidenceLinks);
    }

    private static IReadOnlyList<ReportExportArtifactDto> BuildReportExportArtifacts(
        FinancialStatementPackageDto financialStatements,
        IReadOnlyList<InvestorCapitalStatementDto> investorCapitalStatements,
        RealizedGainLossReportDto realizedGainLoss,
        NavPackageDto navPackage,
        AccountingCertificationStateDto certificationState,
        DateTimeOffset generatedAtUtc,
        IReadOnlyList<string> evidenceLinks,
        RestatementWorkflowDto? restatement)
    {
        var packageId = financialStatements.PackageId;
        var rows = new List<ReportExportArtifactDto>
        {
            BuildReportExportArtifact(
                packageId,
                "financial-statements",
                "Financial statement package",
                "pdf",
                financialStatements.PeriodId,
                certificationState,
                generatedAtUtc,
                evidenceLinks,
                financialStatements.PackageId),
            BuildReportExportArtifact(
                packageId,
                "financial-statements-workbook",
                "Financial statement workbook",
                "xlsx",
                financialStatements.PeriodId,
                certificationState,
                generatedAtUtc,
                evidenceLinks,
                financialStatements.PackageId),
            BuildReportExportArtifact(
                packageId,
                "realized-gain-loss",
                "Realized gain/loss report",
                "csv",
                realizedGainLoss.PeriodId,
                certificationState,
                generatedAtUtc,
                realizedGainLoss.EvidenceLinks,
                realizedGainLoss.ReportId),
            BuildReportExportArtifact(
                packageId,
                "nav-package",
                "NAV package",
                "pdf",
                navPackage.PeriodId,
                certificationState,
                generatedAtUtc,
                navPackage.EvidenceLinks,
                navPackage.PackageId),
            BuildReportExportArtifact(
                packageId,
                "report-line-provenance",
                "Report-line provenance manifest",
                "json",
                financialStatements.PeriodId,
                certificationState,
                generatedAtUtc,
                financialStatements.LineProvenance.SelectMany(static row => row.EvidenceLinks).ToArray(),
                $"{financialStatements.PackageId}:line-provenance")
        };

        rows.AddRange(investorCapitalStatements.Select(statement => BuildReportExportArtifact(
            packageId,
            "investor-capital-statement",
            $"Investor capital statement {statement.CapitalAccountId}",
            "pdf",
            statement.PeriodId,
            certificationState,
            generatedAtUtc,
            statement.EvidenceLinks,
            statement.StatementId)));

        if (restatement is not null)
        {
            rows.Add(BuildReportExportArtifact(
                packageId,
                "restatement-workflow",
                "Restatement workflow manifest",
                "json",
                financialStatements.PeriodId,
                certificationState,
                generatedAtUtc,
                restatement.EvidenceLinks,
                restatement.RestatementId));
        }

        return rows
            .OrderBy(static row => row.ArtifactKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.ArtifactId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ReportExportArtifactDto BuildReportExportArtifact(
        string packageId,
        string artifactKind,
        string displayName,
        string format,
        string periodId,
        AccountingCertificationStateDto certificationState,
        DateTimeOffset generatedAtUtc,
        IReadOnlyList<string> evidenceLinks,
        string? sourceStatementId)
    {
        var artifactId = $"report-export-{Sanitize(packageId)}-{Sanitize(artifactKind)}-{Sanitize(sourceStatementId ?? "aggregate")}";
        var normalizedEvidence = NormalizeEvidenceLinks(evidenceLinks);
        var route = $"/api/ledger/reports/accounting-packages/{Uri.EscapeDataString(packageId)}/exports/{Uri.EscapeDataString(artifactId)}";
        var contentHash = ComputeArtifactHash(
            packageId,
            artifactKind,
            displayName,
            format,
            periodId,
            sourceStatementId ?? string.Empty,
            certificationState,
            generatedAtUtc,
            normalizedEvidence);
        return new ReportExportArtifactDto(
            artifactId,
            artifactKind,
            displayName,
            format,
            route,
            certificationState,
            generatedAtUtc,
            contentHash,
            normalizedEvidence,
            sourceStatementId);
    }

    private static ReportExportArtifactDto CertifyExportArtifact(
        string packageId,
        string periodId,
        ReportExportArtifactDto artifact,
        DateTimeOffset certifiedAtUtc,
        IReadOnlyList<string> evidenceLinks)
    {
        var mergedEvidenceLinks = MergeEvidenceLinks(artifact.EvidenceLinks, evidenceLinks);
        var contentHash = ComputeArtifactHash(
            packageId,
            artifact.ArtifactKind,
            artifact.DisplayName,
            artifact.Format,
            periodId,
            artifact.SourceStatementId ?? string.Empty,
            AccountingCertificationStateDto.Certified,
            certifiedAtUtc,
            mergedEvidenceLinks);
        return artifact with
        {
            CertificationState = AccountingCertificationStateDto.Certified,
            GeneratedAtUtc = certifiedAtUtc,
            ContentHash = contentHash,
            EvidenceLinks = mergedEvidenceLinks
        };
    }

    private static string ComputeArtifactHash(
        string packageId,
        string artifactKind,
        string displayName,
        string format,
        string periodId,
        string sourceStatementId,
        AccountingCertificationStateDto certificationState,
        DateTimeOffset generatedAtUtc,
        IReadOnlyList<string> evidenceLinks)
    {
        var payload = string.Join(
            "|",
            packageId,
            artifactKind,
            displayName,
            format,
            periodId,
            sourceStatementId,
            certificationState,
            generatedAtUtc.ToString("O"),
            string.Join(",", evidenceLinks.Order(StringComparer.OrdinalIgnoreCase)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static ReportExportArtifactManifestDto BuildExportArtifactManifest(
        AccountingReportPackageBundleDto package,
        ReportExportArtifactDto artifact)
    {
        var payload = JsonSerializer.Serialize(
            new
            {
                packageId = package.FinancialStatements.PackageId,
                fundProfileId = package.FinancialStatements.FundProfileId,
                periodId = package.FinancialStatements.PeriodId,
                artifactId = artifact.ArtifactId,
                artifactKind = artifact.ArtifactKind,
                artifact.Format,
                artifact.CertificationState,
                artifact.ContentHash,
                artifact.SourceStatementId,
                certificationId = package.Certification.CertificationId,
                package.Certification.Actor,
                package.Certification.RecordedAtUtc,
                evidenceLinks = artifact.EvidenceLinks
            },
            JsonOptions);
        return new ReportExportArtifactManifestDto(
            package.FinancialStatements.PackageId,
            artifact.ArtifactId,
            artifact.ArtifactKind,
            artifact.DisplayName,
            artifact.Format,
            artifact.Route,
            artifact.CertificationState,
            artifact.GeneratedAtUtc,
            artifact.ContentHash,
            "application/json",
            $"{Sanitize(artifact.DisplayName)}-{Sanitize(package.FinancialStatements.PeriodId)}.json",
            ExternalPostingAllowed: false,
            payload,
            artifact.EvidenceLinks,
            artifact.SourceStatementId);
    }

    private static IReadOnlyList<ReportLineProvenanceDto> BuildReportLineProvenance(
        AccountingReportPackageRequestDto request,
        string fundProfileId,
        string periodId,
        string currency,
        decimal endingCapital,
        Guid? ledgerBookId,
        RestatementWorkflowDto? restatement,
        IReadOnlyList<string> evidenceLinks)
    {
        var dimensions = new LedgerDimensionSetDto(
            FundId: fundProfileId,
            InvestorId: string.IsNullOrWhiteSpace(request.InvestorId) ? null : request.InvestorId.Trim(),
            CapitalAccountId: string.IsNullOrWhiteSpace(request.CapitalAccountId) ? null : request.CapitalAccountId.Trim(),
            BookId: ledgerBookId?.ToString("D"));
        var ledgerEvidence = EvidenceMatching(evidenceLinks, "ledger", "trial-balance");
        var reconciliationEvidence = EvidenceMatching(evidenceLinks, "reconciliation", "tie-out");
        var renderedReportEvidence = EvidenceMatching(evidenceLinks, "report-render", "rendered-report", "report-package");
        var navEvidence = EvidenceMatching(evidenceLinks, "nav", "shadow-nav");
        var restatementEvidence = EvidenceMatching(evidenceLinks, "restatement", "prior-package");
        var commonFinancialStatementEvidence = MergeEvidenceLinks(ledgerEvidence.Concat(reconciliationEvidence), renderedReportEvidence);
        var rows = new List<ReportLineProvenanceDto>
        {
            new(
                "balance-sheet",
                $"balance-sheet:{Sanitize(periodId)}:net-assets",
                "Net assets",
                "LedgerTrialBalance",
                request.Nav,
                currency,
                dimensions,
                MergeEvidenceLinks(commonFinancialStatementEvidence, navEvidence)),
            new(
                "income-statement",
                $"income-statement:{Sanitize(periodId)}:realized-gain-loss",
                "Realized gain/loss",
                "LedgerAndReconciliation",
                request.RealizedGainLoss,
                currency,
                dimensions,
                commonFinancialStatementEvidence),
            new(
                "statement-of-changes-in-capital",
                $"statement-of-changes-in-capital:{Sanitize(periodId)}:ending-capital",
                "Ending capital",
                "CapitalActivity",
                endingCapital,
                currency,
                dimensions,
                commonFinancialStatementEvidence),
            new(
                "investor-capital-statement",
                $"investor-capital-statement:{Sanitize(periodId)}:{Sanitize(request.CapitalAccountId ?? "aggregate")}:ending-capital",
                "Investor ending capital",
                "CapitalActivity",
                endingCapital,
                currency,
                dimensions,
                commonFinancialStatementEvidence),
            new(
                "nav-package",
                $"nav-package:{Sanitize(periodId)}:nav",
                "NAV",
                "NavSupport",
                request.Nav,
                currency,
                dimensions,
                MergeEvidenceLinks(navEvidence, renderedReportEvidence))
        };

        if (restatement is not null)
        {
            rows.Add(new ReportLineProvenanceDto(
                "restatement-workflow",
                $"restatement-workflow:{Sanitize(periodId)}:{Sanitize(restatement.ReasonCode)}",
                "Restatement lineage",
                "RestatementLineage",
                0m,
                currency,
                dimensions,
                MergeEvidenceLinks(restatement.EvidenceLinks, restatementEvidence)));
        }

        return rows;
    }

    private static IReadOnlyList<string> EvidenceMatching(
        IEnumerable<string> evidenceLinks,
        params string[] tokens)
        => evidenceLinks
            .Where(link => tokens.Any(token => link.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

    private static IReadOnlyList<string> NormalizeEvidenceLinks(IEnumerable<string?> evidenceLinks)
        => evidenceLinks
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> MergeEvidenceLinks(
        IEnumerable<string?> existing,
        IEnumerable<string?> incoming)
        => NormalizeEvidenceLinks(existing.Concat(incoming));

    private static bool RequiresMaterialLateAdjustmentApproval(
        LateAdjustmentRequestDto adjustment,
        MaterialityPolicyDto policy)
        => policy.RequiresLateAdjustmentApproval &&
           Math.Abs(adjustment.Amount) >= policy.AmountThreshold;

    private static bool IsLateAdjustmentDecisionPending(LateAdjustmentRequestDto adjustment)
        => adjustment.ApprovalState is not ManualJournalEntryStatusDto.Approved
            and not ManualJournalEntryStatusDto.Rejected;

    private static string RequireText(string? value, string label)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{label} is required.")
            : value.Trim();

    private static string Sanitize(string value)
        => string.Concat(value.Select(static ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-'));

    private sealed record AccountingReportPackageSnapshot(
        IReadOnlyList<AccountingReportPackageBundleDto> Packages);
}
