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
        LedgerDimensionSetDto? dimensions = null,
        string? tenantId = null,
        string? companyId = null,
        CancellationToken ct = default);

    Task<AccountingReportPackageBundleDto?> CertifyPackageAsync(
        CertifyAccountingReportPackageRequestDto request,
        CancellationToken ct = default);

    Task<ReportExportArtifactManifestDto?> GetExportArtifactManifestAsync(
        string packageId,
        string artifactId,
        string? tenantId = null,
        string? companyId = null,
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
        var tenantId = NormalizeOptional(request.TenantId);
        var companyId = NormalizeOptional(request.CompanyId);
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
            validationIssues.AddRange(BuildCloseBackedReportEvidenceScopeIssues(
                closePlan,
                periodId,
                ledgerBookId,
                evidenceLinks));
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
                AccountingConfigurationValidationSeverityDto.Critical,
                "No retained report evidence links were supplied for the package.",
                periodId,
                "Attach close package, ledger, reconciliation, and report-render evidence before certification."));
        }

        var packageDimensions = BuildReportPackageDimensions(request, fundProfileId, ledgerBookId);
        var dimensionScope = BuildReportDimensionScope(packageDimensions, fundProfileId, ledgerBookId);
        validationIssues.AddRange(BuildReportDimensionScopeIssues(request, fundProfileId, ledgerBookId, packageDimensions));
        validationIssues.AddRange(BuildReportCertificationEvidenceIssues(periodId, ledgerBookId, evidenceLinks));
        validationIssues.AddRange(BuildRestatementCertificationIssues(
            request,
            fundProfileId,
            periodId,
            ledgerBookId,
            packageDimensions,
            evidenceLinks,
            ReadPackages()));

        var hasCritical = validationIssues.Any(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        var state = hasCritical ? AccountingCertificationStateDto.Draft : AccountingCertificationStateDto.ReadyForReview;
        var packageId = BuildPackageId(fundProfileId, periodId, ledgerBookId, request.Dimensions is null ? null : packageDimensions, tenantId, companyId);
        ThrowIfCertifiedPackageWouldBeReplaced(packageId, tenantId, companyId, ReadPackages());
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
            periodId,
            currency,
            endingCapital,
            packageDimensions,
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
            lineProvenance,
            packageDimensions);
        var investorStatement = new InvestorCapitalStatementDto(
            $"investor-capital-statement-{Sanitize(fundProfileId)}-{Sanitize(periodId)}-{Sanitize(request.CapitalAccountId ?? "aggregate")}",
            fundProfileId,
            ledgerBookId,
            string.IsNullOrWhiteSpace(request.CapitalAccountId) ? "capital-account:aggregate" : request.CapitalAccountId.Trim(),
            string.IsNullOrWhiteSpace(request.InvestorId) ? null : request.InvestorId.Trim(),
            periodId,
            packageDimensions,
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
            packageDimensions,
            request.RealizedGainLoss,
            currency,
            state,
            evidenceLinks);
        var navPackage = new NavPackageDto(
            $"nav-package-{Sanitize(fundProfileId)}-{Sanitize(periodId)}",
            fundProfileId,
            ledgerBookId,
            periodId,
            packageDimensions,
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
            restatement,
            dimensionScope);
        var closeReadinessItems = BuildCloseReadinessItems(
            closePlan,
            financialStatements,
            certification,
            validationIssues,
            exportArtifacts,
            restatement);

        var bundle = new AccountingReportPackageBundleDto(
            financialStatements,
            [investorStatement],
            realizedGainLoss,
            navPackage,
            certification,
            validationIssues,
            exportArtifacts,
            request.CloseWorkflowId,
            tenantId,
            companyId,
            closeReadinessItems,
            dimensionScope);
        await SavePackageAsync(bundle, ct).ConfigureAwait(false);
        return bundle;
    }

    public Task<IReadOnlyList<AccountingReportPackageBundleDto>> ListPackagesAsync(
        string? fundProfileId = null,
        string? periodId = null,
        Guid? ledgerBookId = null,
        LedgerDimensionSetDto? dimensions = null,
        string? tenantId = null,
        string? companyId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedFundProfileId = string.IsNullOrWhiteSpace(fundProfileId) ? null : fundProfileId.Trim();
        var normalizedPeriodId = string.IsNullOrWhiteSpace(periodId) ? null : periodId.Trim();
        var normalizedDimensions = NormalizeDimensionSet(dimensions);
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        var rows = ReadPackages()
            .Where(package => MatchesTenantScope(package, normalizedTenantId, normalizedCompanyId))
            .Where(package => normalizedFundProfileId is null || string.Equals(package.FinancialStatements.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase))
            .Where(package => normalizedPeriodId is null || string.Equals(package.FinancialStatements.PeriodId, normalizedPeriodId, StringComparison.OrdinalIgnoreCase))
            .Where(package => !ledgerBookId.HasValue || package.FinancialStatements.LedgerBookId == ledgerBookId.Value)
            .Where(package => MatchesDimensionScope(package.FinancialStatements.Dimensions, normalizedDimensions))
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
        var tenantId = NormalizeOptional(request.TenantId);
        var companyId = NormalizeOptional(request.CompanyId);
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
                MatchesTenantScope(package, tenantId, companyId) &&
                (string.Equals(package.FinancialStatements.PackageId, packageId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(package.Certification.CertificationId, packageId, StringComparison.OrdinalIgnoreCase)));
            if (index < 0)
            {
                return null;
            }

            var package = rows[index];
            if (!HasReportCertificationEvidenceWithProvenance(package, evidenceLinks))
            {
                throw new ArgumentException("Accounting report package certification evidence must reference the retained package, certification id, ledger book, exact package period, and explicit dimension scope when applicable in the same artifact.");
            }

            if (!HasRestatementCertificationEvidenceWithPriorPackage(package, evidenceLinks))
            {
                throw new ArgumentException("Restatement report package certification evidence must reference the exact prior package being restated in the same retained approval artifact.");
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

            if (string.Equals(package.Certification.Actor, actor, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Accounting report package '{package.FinancialStatements.PackageId}' must be certified by an actor independent from preparer '{package.Certification.Actor}'.");
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
                    .ToArray(),
                CloseReadinessItems = CertifyCloseReadinessItems(package.CloseReadinessItems, evidenceLinks)
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
        string? tenantId = null,
        string? companyId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedPackageId = RequireText(packageId, nameof(packageId));
        var normalizedArtifactId = RequireText(artifactId, nameof(artifactId));
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        var package = ReadPackages().FirstOrDefault(row =>
            MatchesTenantScope(row, normalizedTenantId, normalizedCompanyId) &&
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
            return
            [
                new AccountingConfigurationValidationIssueDto(
                    "CloseWorkflowRequired",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Accounting report package '{package.FinancialStatements.PackageId}' is not linked to a retained close workflow.",
                    package.FinancialStatements.PackageId,
                    "Build the report package from a close workflow with ledger-book scope, period lock, sign-offs, reconciliation, and report evidence before certification.")
            ];
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
        issues.AddRange(BuildCloseBackedReportEvidenceScopeIssues(
            closePlan,
            package.FinancialStatements.PeriodId,
            package.FinancialStatements.LedgerBookId,
            package.FinancialStatements.EvidenceLinks));
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

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> BuildCloseBackedReportEvidenceScopeIssues(
        ClosePeriodPlanDto closePlan,
        string periodId,
        Guid? packageLedgerBookId,
        IReadOnlyList<string> evidenceLinks)
    {
        if (!packageLedgerBookId.HasValue)
        {
            return [];
        }

        var scopedRequirements = new (string Code, string Label, string[] Tokens)[]
        {
            ("ReportLedgerEvidenceScopeMissing", "ledger/trial-balance", ["ledger", "trial-balance"]),
            ("ReportReconciliationEvidenceScopeMissing", "reconciliation tie-out", ["reconciliation", "tie-out"]),
            ("ReportRenderEvidenceScopeMissing", "rendered report artifact", ["report-render", "rendered-report", "report-package"]),
            ("ReportNavEvidenceScopeMissing", "NAV support", ["nav", "shadow-nav"])
        };
        var issues = new List<AccountingConfigurationValidationIssueDto>();
        foreach (var requirement in scopedRequirements)
        {
            var matchingEvidence = EvidenceMatching(evidenceLinks, requirement.Tokens);
            if (matchingEvidence.Count == 0 ||
                matchingEvidence.Any(link => EvidenceReferencesLedgerBook(link, packageLedgerBookId.Value)))
            {
                continue;
            }

            issues.Add(new AccountingConfigurationValidationIssueDto(
                requirement.Code,
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Close-backed report package for period '{periodId}' has retained {requirement.Label} evidence, but no link names ledger book '{packageLedgerBookId.Value:D}'.",
                closePlan.ClosePlanId,
                "Attach ledger-book-scoped close, ledger, reconciliation, rendered report, and NAV evidence before report certification."));
        }

        return issues;
    }

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> BuildRestatementCertificationIssues(
        AccountingReportPackageRequestDto request,
        string fundProfileId,
        string periodId,
        Guid? ledgerBookId,
        LedgerDimensionSetDto packageDimensions,
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
            var requestTenantId = NormalizeOptional(request.TenantId);
            var requestCompanyId = NormalizeOptional(request.CompanyId);
            var priorPackage = retainedPackages.FirstOrDefault(package =>
                MatchesTenantScope(package, requestTenantId, requestCompanyId) &&
                (string.Equals(package.FinancialStatements.PackageId, priorPackageId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(package.Certification.CertificationId, priorPackageId, StringComparison.OrdinalIgnoreCase)));
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

                if (ledgerBookId.HasValue &&
                    priorPackage.FinancialStatements.LedgerBookId != ledgerBookId.Value)
                {
                    issues.Add(new AccountingConfigurationValidationIssueDto(
                        "RestatementPriorPackageLedgerBookMismatch",
                        AccountingConfigurationValidationSeverityDto.Critical,
                        $"Restatement package for ledger book '{ledgerBookId.Value:D}' references prior package '{priorPackageId}' for ledger book '{priorPackage.FinancialStatements.LedgerBookId?.ToString("D") ?? "unscoped"}'.",
                        priorPackageId,
                        "Select a prior certified report package for the same ledger book before restatement certification."));
                }

                if (!MatchesExactDimensionScope(priorPackage.FinancialStatements.Dimensions, packageDimensions))
                {
                    issues.Add(new AccountingConfigurationValidationIssueDto(
                        "RestatementPriorPackageDimensionScopeMismatch",
                        AccountingConfigurationValidationSeverityDto.Critical,
                        $"Restatement package for period '{periodId}' references prior package '{priorPackageId}' with a different retained dimension scope.",
                        priorPackageId,
                        "Select a prior certified report package with the same fund, ledger book, investor, capital-account, entity, strategy, cost-center, counterparty, instrument, tax-lot, and external-GL dimension scope before restatement certification."));
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

                if (!HasRestatementPriorPackageEvidence(evidenceLinks, priorPackage))
                {
                    issues.Add(new AccountingConfigurationValidationIssueDto(
                        "RestatementPriorPackageEvidenceMissing",
                        AccountingConfigurationValidationSeverityDto.Critical,
                        $"Restatement package for period '{periodId}' references prior package '{priorPackageId}' without retained evidence naming the exact prior package or certification.",
                        priorPackageId,
                        "Attach restatement lineage evidence that names the exact prior package or certification id before certification."));
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

    private static bool HasRestatementPriorPackageEvidence(
        IReadOnlyList<string> evidenceLinks,
        AccountingReportPackageBundleDto priorPackage)
        => evidenceLinks.Any(link =>
            (link.Contains("restatement", StringComparison.OrdinalIgnoreCase) ||
             link.Contains("prior-package", StringComparison.OrdinalIgnoreCase)) &&
            (EvidenceReferencesReportIdentifier(link, priorPackage.FinancialStatements.PackageId) ||
             EvidenceReferencesReportIdentifier(link, priorPackage.Certification.CertificationId)));

    private static IReadOnlyList<AccountingCloseReadinessItemDto> BuildCloseReadinessItems(
        ClosePeriodPlanDto? closePlan,
        FinancialStatementPackageDto financialStatements,
        ReportCertificationDto certification,
        IReadOnlyList<AccountingConfigurationValidationIssueDto> validationIssues,
        IReadOnlyList<ReportExportArtifactDto> exportArtifacts,
        RestatementWorkflowDto? restatement)
    {
        var criticalIssues = validationIssues
            .Where(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical)
            .ToArray();
        var restatementEvidenceLinks = restatement?.EvidenceLinks ?? [];
        var evidenceLinks = NormalizeEvidenceLinks([
            .. financialStatements.EvidenceLinks,
            .. certification.EvidenceLinks,
            .. exportArtifacts.SelectMany(static artifact => artifact.EvidenceLinks),
            .. restatementEvidenceLinks
        ]);
        var rows = new List<AccountingCloseReadinessItemDto>();

        if (closePlan is null)
        {
            rows.Add(BuildCloseReadinessItem(
                "close-workflow-linkage",
                "CloseWorkflow",
                "Close workflow linkage",
                AccountingReadinessStateDto.NeedsAttention,
                "The package is retained without a close workflow snapshot.",
                "Build the report package from a close workflow before final certification.",
                financialStatements,
                evidenceLinks,
                IssuesFor(criticalIssues, "ClosePlanMissing", "CloseWorkflowRequired")));
        }
        else
        {
            var openTasks = closePlan.Tasks
                .Where(static task => task.Status != CloseTaskStatusDto.SignedOff)
                .ToArray();
            var signOffGaps = closePlan.Tasks
                .Where(static task => task.SignOffs.All(static signOff => signOff.ApprovalState != ManualJournalEntryStatusDto.Approved))
                .ToArray();
            var checklistIssues = IssuesFor(criticalIssues, "CloseChecklistIncomplete", "CloseSignOffMissing");
            rows.Add(BuildCloseReadinessItem(
                "close-checklist-signoffs",
                "CloseChecklist",
                "Close checklist and sign-off matrix",
                ResolveReadinessState(checklistIssues, openTasks.Length + signOffGaps.Length, certification.State),
                openTasks.Length == 0 && signOffGaps.Length == 0
                    ? "Every close checklist task has retained approved sign-off evidence."
                    : $"{openTasks.Length:N0} task(s) remain open and {signOffGaps.Length:N0} task(s) lack approved sign-off evidence.",
                "Complete dependencies and retain required role-scoped sign-off evidence before report certification.",
                financialStatements,
                closePlan.Tasks.SelectMany(static task => task.EvidenceLinks).Concat(evidenceLinks),
                checklistIssues));

            var periodLockIssues = IssuesFor(criticalIssues, "PeriodNotLocked");
            rows.Add(BuildCloseReadinessItem(
                "period-lock",
                "PeriodLock",
                "Period lock",
                closePlan.IsPeriodLocked
                    ? ResolveReadinessState(periodLockIssues, 0, certification.State)
                    : AccountingReadinessStateDto.Blocked,
                closePlan.IsPeriodLocked
                    ? "The accounting period is locked after close."
                    : "The accounting period remains open for late adjustments.",
                "Lock the period after close approvals before final report certification.",
                financialStatements,
                closePlan.Tasks.SelectMany(static task => task.EvidenceLinks).Concat(evidenceLinks),
                periodLockIssues));

            var pendingLateAdjustments = closePlan.LateAdjustments
                .Where(adjustment =>
                    RequiresMaterialLateAdjustmentApproval(adjustment, closePlan.MaterialityPolicy) &&
                    IsLateAdjustmentDecisionPending(adjustment))
                .ToArray();
            var lateAdjustmentIssues = IssuesFor(criticalIssues, "LateAdjustmentApprovalPending");
            rows.Add(BuildCloseReadinessItem(
                "late-adjustment-review",
                "LateAdjustments",
                "Late adjustment review",
                ResolveReadinessState(lateAdjustmentIssues, pendingLateAdjustments.Length, certification.State),
                pendingLateAdjustments.Length == 0
                    ? "No material late adjustments are pending approval."
                    : $"{pendingLateAdjustments.Length:N0} material late adjustment(s) still require approval or rejection.",
                "Approve or reject material late adjustments with retained evidence before certification.",
                financialStatements,
                closePlan.LateAdjustments.SelectMany(static adjustment => adjustment.EvidenceLinks).Concat(evidenceLinks),
                lateAdjustmentIssues));
        }

        var dimensionIssues = IssuesFor(
            criticalIssues,
            "ReportPackageFundDimensionMismatch",
            "ReportPackageLedgerBookDimensionMismatch",
            "ReportPackageInvestorDimensionMismatch",
            "ReportPackageCapitalAccountDimensionMismatch",
            "ReportPackageDimensionScopeMismatch");
        rows.Add(BuildCloseReadinessItem(
            "report-dimension-scope",
            "ReportDimensions",
            "Report dimension scope",
            ResolveReadinessState(dimensionIssues, 0, certification.State),
            dimensionIssues.Count == 0
                ? "Report package dimensions align with the selected fund, ledger book, investor, capital account, and explicit scope."
                : $"{dimensionIssues.Count:N0} report dimension scope blocker(s) require remediation.",
            "Resolve fund, ledger-book, investor, capital-account, and explicit dimension-scope mismatches before report certification.",
            financialStatements,
            evidenceLinks,
            dimensionIssues));

        rows.Add(BuildCloseReadinessItem(
            "report-evidence-package",
            "ReportEvidence",
            "Report evidence package",
            ResolveReadinessState(
                IssuesFor(
                    criticalIssues,
                    "ReportEvidenceMissing",
                    "ReportLedgerEvidenceMissing",
                    "ReportReconciliationEvidenceMissing",
                    "ReportRenderEvidenceMissing",
                    "ReportNavEvidenceMissing",
                    "ReportLedgerEvidenceMissingLedgerBookScope",
                    "ReportReconciliationEvidenceMissingLedgerBookScope",
                    "ReportRenderEvidenceMissingLedgerBookScope",
                    "ReportNavEvidenceMissingLedgerBookScope",
                    "ReportLedgerEvidenceScopeMissing",
                    "ReportReconciliationEvidenceScopeMissing",
                    "ReportRenderEvidenceScopeMissing",
                    "ReportNavEvidenceScopeMissing"),
                evidenceLinks.Count == 0 ? 1 : 0,
                certification.State),
            evidenceLinks.Count > 0
                ? $"{evidenceLinks.Count:N0} retained evidence link(s) support the package."
                : "No retained report evidence links are attached.",
            "Attach ledger, reconciliation, rendered report, NAV, certification, and package evidence before certification.",
            financialStatements,
            evidenceLinks,
            IssuesFor(
                criticalIssues,
                "ReportEvidenceMissing",
                "ReportLedgerEvidenceMissing",
                "ReportReconciliationEvidenceMissing",
                "ReportRenderEvidenceMissing",
                "ReportNavEvidenceMissing",
                "ReportLedgerEvidenceMissingLedgerBookScope",
                "ReportReconciliationEvidenceMissingLedgerBookScope",
                "ReportRenderEvidenceMissingLedgerBookScope",
                "ReportNavEvidenceMissingLedgerBookScope",
                "ReportLedgerEvidenceScopeMissing",
                "ReportReconciliationEvidenceScopeMissing",
                "ReportRenderEvidenceScopeMissing",
                "ReportNavEvidenceScopeMissing")));

        var uncertifiedExports = exportArtifacts
            .Count(static artifact => artifact.CertificationState != AccountingCertificationStateDto.Certified);
        var exportArtifactIssues = BuildReportExportArtifactIssues(financialStatements, exportArtifacts);
        rows.Add(BuildCloseReadinessItem(
            "report-export-certification",
            "ReportExports",
            "Report export certification",
            ResolveReadinessState(exportArtifactIssues, uncertifiedExports, certification.State),
            exportArtifactIssues.Count > 0
                ? $"{exportArtifactIssues.Count:N0} retained export artifact blocker(s) require remediation."
                : exportArtifacts.Count > 0
                ? $"{exportArtifacts.Count - uncertifiedExports:N0}/{exportArtifacts.Count:N0} retained export artifact(s) are certified."
                : "No retained report export artifacts are attached.",
            "Certify the report package so every retained export artifact carries certified state, content hash, ledger-book scope, dimension scope, and retained evidence.",
            financialStatements,
            exportArtifacts.SelectMany(static artifact => artifact.EvidenceLinks).Concat(evidenceLinks),
            exportArtifactIssues));

        if (restatement is not null)
        {
            rows.Add(BuildCloseReadinessItem(
                "restatement-workflow",
                "Restatement",
                "Restatement workflow",
                restatement.ApprovalState == ManualJournalEntryStatusDto.Approved
                    ? AccountingReadinessStateDto.Certified
                    : AccountingReadinessStateDto.NeedsAttention,
                $"Restatement '{restatement.RestatementId}' references prior package '{restatement.PriorPackageId}' with approval state '{restatement.ApprovalState}'.",
                "Retain prior-package lineage and approve the restatement during report certification.",
                financialStatements,
                restatement.EvidenceLinks.Concat(evidenceLinks),
                IssuesFor(
                    criticalIssues,
                    "RestatementPriorPackageMissing",
                    "RestatementPriorPackageNotRetained",
                    "RestatementPriorPackageNotCertified",
                    "RestatementEvidenceMissing")));
        }

        return rows;
    }

    private static IReadOnlyList<AccountingCloseReadinessItemDto> CertifyCloseReadinessItems(
        IReadOnlyList<AccountingCloseReadinessItemDto> items,
        IReadOnlyList<string> evidenceLinks)
        => items
            .Select(item => item.BlockingIssueCount > 0 || item.State == AccountingReadinessStateDto.Blocked
                ? item
                : item with
                {
                    State = AccountingReadinessStateDto.Certified,
                    Summary = $"{item.Label} is certified with retained package approval evidence.",
                    EvidenceLinks = MergeEvidenceLinks(item.EvidenceLinks, evidenceLinks)
                })
            .ToArray();

    private static AccountingCloseReadinessItemDto BuildCloseReadinessItem(
        string itemId,
        string category,
        string label,
        AccountingReadinessStateDto state,
        string summary,
        string requiredAction,
        FinancialStatementPackageDto financialStatements,
        IEnumerable<string?> evidenceLinks,
        IReadOnlyList<AccountingConfigurationValidationIssueDto> blockingIssues)
        => new(
            itemId,
            category,
            label,
            state,
            summary,
            requiredAction,
            blockingIssues.Count,
            NormalizeEvidenceLinks(evidenceLinks),
            blockingIssues,
            financialStatements.LedgerBookId,
            financialStatements.Dimensions);

    private static AccountingReadinessStateDto ResolveReadinessState(
        IReadOnlyList<AccountingConfigurationValidationIssueDto> blockingIssues,
        int openItemCount,
        AccountingCertificationStateDto certificationState)
    {
        if (blockingIssues.Count > 0)
        {
            return AccountingReadinessStateDto.Blocked;
        }

        if (certificationState == AccountingCertificationStateDto.Certified)
        {
            return AccountingReadinessStateDto.Certified;
        }

        return openItemCount > 0
            ? AccountingReadinessStateDto.NeedsAttention
            : AccountingReadinessStateDto.ReadyForReview;
    }

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> IssuesFor(
        IEnumerable<AccountingConfigurationValidationIssueDto> issues,
        params string[] codes)
        => issues
            .Where(issue => codes.Any(code => string.Equals(issue.Code, code, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> BuildReportExportArtifactIssues(
        FinancialStatementPackageDto financialStatements,
        IReadOnlyList<ReportExportArtifactDto> exportArtifacts)
    {
        if (exportArtifacts.Count == 0)
        {
            return
            [
                new AccountingConfigurationValidationIssueDto(
                    "ReportExportArtifactMissing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Accounting report package '{financialStatements.PackageId}' has no retained export artifacts.",
                    financialStatements.PackageId,
                    "Retain report export artifacts with ledger-book scope, dimension scope, evidence, and content hashes before certification.")
            ];
        }

        var issues = new List<AccountingConfigurationValidationIssueDto>();
        foreach (var artifact in exportArtifacts)
        {
            if (string.IsNullOrWhiteSpace(artifact.ContentHash))
            {
                issues.Add(new AccountingConfigurationValidationIssueDto(
                    "ReportExportArtifactHashMissing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Report export artifact '{artifact.ArtifactId}' is missing a retained content hash.",
                    artifact.ArtifactId,
                    "Regenerate the export artifact and retain its deterministic content hash before certification."));
            }

            if (artifact.EvidenceLinks.Count == 0)
            {
                issues.Add(new AccountingConfigurationValidationIssueDto(
                    "ReportExportArtifactEvidenceMissing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Report export artifact '{artifact.ArtifactId}' has no retained evidence links.",
                    artifact.ArtifactId,
                    "Attach retained ledger, reconciliation, rendered-report, NAV, or certification evidence to every export artifact."));
            }

            if (artifact.LedgerBookId != financialStatements.LedgerBookId)
            {
                issues.Add(new AccountingConfigurationValidationIssueDto(
                    "ReportExportArtifactLedgerBookMismatch",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Report export artifact '{artifact.ArtifactId}' targets ledger book '{artifact.LedgerBookId?.ToString("D") ?? "unscoped"}' but package '{financialStatements.PackageId}' targets ledger book '{financialStatements.LedgerBookId?.ToString("D") ?? "unscoped"}'.",
                    artifact.ArtifactId,
                    "Regenerate the export artifact from the same ledger book as the retained report package."));
            }

            var artifactDimensionScope = artifact.DimensionScope;
            if (!MatchesExactDimensionScope(artifact.Dimensions, financialStatements.Dimensions) ||
                artifactDimensionScope is null ||
                !string.Equals(
                    artifactDimensionScope.ScopeHash,
                    BuildDimensionScopeHash(financialStatements.Dimensions),
                    StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new AccountingConfigurationValidationIssueDto(
                    "ReportExportArtifactDimensionScopeMismatch",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Report export artifact '{artifact.ArtifactId}' does not retain the same dimensional scope as package '{financialStatements.PackageId}'.",
                    artifact.ArtifactId,
                    "Regenerate the export artifact from the same fund, ledger-book, investor, capital-account, and explicit dimension scope as the retained report package."));
            }
        }

        return issues
            .OrderBy(static issue => issue.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static issue => issue.TargetId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildPackageId(
        string fundProfileId,
        string periodId,
        Guid? ledgerBookId,
        LedgerDimensionSetDto? explicitDimensions,
        string? tenantId,
        string? companyId)
    {
        var baseId = ledgerBookId.HasValue
            ? $"accounting-report-package-{Sanitize(fundProfileId)}-{Sanitize(periodId)}-book-{ledgerBookId.Value:N}"
            : $"accounting-report-package-{Sanitize(fundProfileId)}-{Sanitize(periodId)}";
        var tenantScope = BuildTenantPackageScope(tenantId, companyId);
        if (!string.IsNullOrWhiteSpace(tenantScope))
        {
            baseId = $"{baseId}-{tenantScope}";
        }

        if (explicitDimensions is null || !HasExplicitDimensionScope(explicitDimensions, fundProfileId, ledgerBookId))
        {
            return baseId;
        }

        return $"{baseId}-scope-{BuildDimensionScopeHash(explicitDimensions)}";
    }

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> BuildReportCertificationEvidenceIssues(
        string periodId,
        Guid? ledgerBookId,
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
            var matchingEvidence = evidenceLinks
                .Where(link => requirement.Tokens.Any(token =>
                    link.Contains(token, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (matchingEvidence.Length == 0)
            {
                issues.Add(new AccountingConfigurationValidationIssueDto(
                    requirement.Code,
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Accounting report package for period '{periodId}' is missing retained {requirement.Label} evidence.",
                    periodId,
                    "Attach ledger, reconciliation, rendered report, and NAV support evidence before report certification."));
                continue;
            }

            if (ledgerBookId is null || matchingEvidence.Any(link => EvidenceReferencesLedgerBook(link, ledgerBookId.Value)))
            {
                continue;
            }

            issues.Add(new AccountingConfigurationValidationIssueDto(
                $"{requirement.Code}LedgerBookScope",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Accounting report package for period '{periodId}' has retained {requirement.Label} evidence, but no link names ledger book '{ledgerBookId.Value:D}'.",
                periodId,
                "Attach ledger-book-scoped ledger, reconciliation, rendered report, and NAV support evidence before report certification."));
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
            EvidenceReferencesReportIdentifier(link, package.FinancialStatements.PackageId) &&
            EvidenceReferencesReportIdentifier(link, package.Certification.CertificationId) &&
            EvidenceReferencesReportIdentifier(link, package.FinancialStatements.PeriodId) &&
            EvidenceReferencesReportLedgerBook(package, link) &&
            EvidenceReferencesReportTenantCompanyScope(package, link) &&
            EvidenceReferencesReportDimensionScope(package, link));

    private static bool HasRestatementCertificationEvidenceWithPriorPackage(
        AccountingReportPackageBundleDto package,
        IReadOnlyList<string> evidenceLinks)
    {
        var restatement = package.NavPackage.Restatement ?? package.FinancialStatements.Restatement;
        if (restatement is null)
        {
            return true;
        }

        return evidenceLinks.Any(link =>
            HasReportCertificationEvidence([link]) &&
            EvidenceReferencesReportIdentifier(link, package.FinancialStatements.PackageId) &&
            EvidenceReferencesReportIdentifier(link, package.Certification.CertificationId) &&
            EvidenceReferencesReportIdentifier(link, package.FinancialStatements.PeriodId) &&
            EvidenceReferencesReportLedgerBook(package, link) &&
            EvidenceReferencesReportTenantCompanyScope(package, link) &&
            EvidenceReferencesReportDimensionScope(package, link) &&
            EvidenceReferencesReportIdentifier(link, restatement.PriorPackageId));
    }

    private static bool EvidenceReferencesReportIdentifier(string evidenceLink, string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        var searchIndex = 0;
        while (searchIndex < evidenceLink.Length)
        {
            var identifierIndex = evidenceLink.IndexOf(identifier, searchIndex, StringComparison.OrdinalIgnoreCase);
            if (identifierIndex < 0)
            {
                return false;
            }

            if (IsEvidenceTokenBoundary(evidenceLink, identifierIndex - 1) &&
                IsEvidenceTokenBoundary(evidenceLink, identifierIndex + identifier.Length))
            {
                return true;
            }

            searchIndex = identifierIndex + identifier.Length;
        }

        return false;
    }

    private static bool EvidenceReferencesReportTenantCompanyScope(
        AccountingReportPackageBundleDto package,
        string evidenceLink)
        => ReferencesOptionalScope(evidenceLink, "tenant", "tenantId", package.TenantId) &&
           ReferencesOptionalScope(evidenceLink, "company", "companyId", package.CompanyId);

    private static bool ReferencesOptionalScope(
        string evidenceLink,
        string pathToken,
        string queryToken,
        string? scopeValue)
    {
        var normalized = NormalizeOptional(scopeValue);
        if (normalized is null)
        {
            return true;
        }

        return ReferencesScopedValue(evidenceLink, $"{pathToken}:", normalized) ||
               ReferencesScopedValue(evidenceLink, $"{pathToken}/", normalized) ||
               ReferencesScopedValue(evidenceLink, $"{queryToken}=", normalized) ||
               ReferencesScopedValue(evidenceLink, $"{queryToken}:", normalized) ||
               ReferencesScopedValue(evidenceLink, $"{queryToken}/", normalized);
    }

    private static bool EvidenceReferencesReportLedgerBook(
        AccountingReportPackageBundleDto package,
        string evidenceLink)
        => TryGetReportLedgerBookId(package, out var ledgerBookId) is false ||
           EvidenceReferencesLedgerBook(evidenceLink, ledgerBookId);

    private static bool EvidenceReferencesReportDimensionScope(
        AccountingReportPackageBundleDto package,
        string evidenceLink)
    {
        var dimensions = package.FinancialStatements.Dimensions;
        if (!HasExplicitDimensionScope(
                dimensions,
                package.FinancialStatements.FundProfileId,
                TryGetReportLedgerBookId(package, out var ledgerBookId) ? ledgerBookId : null))
        {
            return true;
        }

        return ReferencesScopedValue(evidenceLink, "dimension-scope:", BuildDimensionScopeHash(dimensions));
    }

    private static bool TryGetReportLedgerBookId(AccountingReportPackageBundleDto package, out Guid ledgerBookId)
    {
        if (package.FinancialStatements.LedgerBookId is Guid retainedLedgerBookId)
        {
            ledgerBookId = retainedLedgerBookId;
            return true;
        }

        if (Guid.TryParse(package.FinancialStatements.Dimensions.BookId, out var dimensionLedgerBookId))
        {
            ledgerBookId = dimensionLedgerBookId;
            return true;
        }

        ledgerBookId = default;
        return false;
    }

    private static bool EvidenceReferencesLedgerBook(string evidenceLink, Guid ledgerBookId)
    {
        var ledgerBookIdText = ledgerBookId.ToString("D");
        var ledgerBookIdCompact = ledgerBookId.ToString("N");
        return ReferencesScopedValue(evidenceLink, "book:", ledgerBookIdText) ||
               ReferencesScopedValue(evidenceLink, "ledger-book:", ledgerBookIdText) ||
               ReferencesScopedValue(evidenceLink, "ledger-book/", ledgerBookIdText) ||
               ReferencesScopedValue(evidenceLink, "book:", ledgerBookIdCompact) ||
               ReferencesScopedValue(evidenceLink, "ledger-book:", ledgerBookIdCompact) ||
               ReferencesScopedValue(evidenceLink, "ledger-book/", ledgerBookIdCompact);
    }

    private static bool ReferencesScopedValue(string reference, string prefix, string value)
    {
        var searchIndex = 0;
        while (searchIndex < reference.Length)
        {
            var prefixIndex = reference.IndexOf(prefix, searchIndex, StringComparison.OrdinalIgnoreCase);
            if (prefixIndex < 0)
            {
                return false;
            }

            var valueIndex = prefixIndex + prefix.Length;
            if (reference.Length >= valueIndex + value.Length &&
                string.Compare(reference, valueIndex, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0 &&
                IsEvidenceTokenBoundary(reference, valueIndex + value.Length))
            {
                return true;
            }

            searchIndex = valueIndex;
        }

        return false;
    }

    private static bool IsEvidenceTokenBoundary(string reference, int index)
        => index >= reference.Length ||
           reference[index] is '/' or ':' or '?' or '&' or '#' or ';' or ',' or ')' or ']' or '}' or ' ' or '\t' or '\r' or '\n';

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
            ThrowIfCertifiedPackageWouldBeReplaced(bundle.FinancialStatements.PackageId, bundle.TenantId, bundle.CompanyId, rows);
            var updatedRows = rows
                .Where(package => !MatchesPackageIdentity(package, bundle.FinancialStatements.PackageId, bundle.TenantId, bundle.CompanyId))
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
        string? tenantId,
        string? companyId,
        IReadOnlyList<AccountingReportPackageBundleDto> retainedPackages)
    {
        var certifiedPackage = retainedPackages.FirstOrDefault(package =>
            MatchesPackageIdentity(package, packageId, tenantId, companyId) &&
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
        RestatementWorkflowDto? restatement,
        ReportDimensionScopeDto dimensionScope)
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
                financialStatements.PackageId,
                financialStatements.LedgerBookId,
                financialStatements.Dimensions,
                dimensionScope),
            BuildReportExportArtifact(
                packageId,
                "financial-statements-workbook",
                "Financial statement workbook",
                "xlsx",
                financialStatements.PeriodId,
                certificationState,
                generatedAtUtc,
                evidenceLinks,
                financialStatements.PackageId,
                financialStatements.LedgerBookId,
                financialStatements.Dimensions,
                dimensionScope),
            BuildReportExportArtifact(
                packageId,
                "realized-gain-loss",
                "Realized gain/loss report",
                "csv",
                realizedGainLoss.PeriodId,
                certificationState,
                generatedAtUtc,
                realizedGainLoss.EvidenceLinks,
                realizedGainLoss.ReportId,
                realizedGainLoss.LedgerBookId,
                realizedGainLoss.Dimensions,
                dimensionScope),
            BuildReportExportArtifact(
                packageId,
                "nav-package",
                "NAV package",
                "pdf",
                navPackage.PeriodId,
                certificationState,
                generatedAtUtc,
                navPackage.EvidenceLinks,
                navPackage.PackageId,
                navPackage.LedgerBookId,
                navPackage.Dimensions,
                dimensionScope),
            BuildReportExportArtifact(
                packageId,
                "report-line-provenance",
                "Report-line provenance manifest",
                "json",
                financialStatements.PeriodId,
                certificationState,
                generatedAtUtc,
                financialStatements.LineProvenance.SelectMany(static row => row.EvidenceLinks).ToArray(),
                $"{financialStatements.PackageId}:line-provenance",
                financialStatements.LedgerBookId,
                financialStatements.Dimensions,
                dimensionScope)
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
            statement.StatementId,
            statement.LedgerBookId,
            statement.Dimensions,
            dimensionScope)));

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
                restatement.RestatementId,
                financialStatements.LedgerBookId,
                financialStatements.Dimensions,
                dimensionScope));
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
        string? sourceStatementId,
        Guid? ledgerBookId,
        LedgerDimensionSetDto dimensions,
        ReportDimensionScopeDto dimensionScope)
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
            ledgerBookId,
            dimensions,
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
            sourceStatementId,
            ledgerBookId,
            dimensions,
            dimensionScope);
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
            artifact.LedgerBookId,
            artifact.Dimensions,
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
        Guid? ledgerBookId,
        LedgerDimensionSetDto dimensions,
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
            ledgerBookId?.ToString("D") ?? string.Empty,
            dimensions.FundId ?? string.Empty,
            dimensions.EntityId ?? string.Empty,
            dimensions.InvestorId ?? string.Empty,
            dimensions.CapitalAccountId ?? string.Empty,
            dimensions.InstrumentId?.ToString("D") ?? string.Empty,
            dimensions.CostCenterId ?? string.Empty,
            dimensions.CounterpartyId ?? string.Empty,
            string.Join(",", dimensions.ExternalGlDimensions
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => $"{pair.Key}={pair.Value}")),
            certificationState,
            generatedAtUtc.ToString("O"),
            string.Join(",", evidenceLinks.Order(StringComparer.OrdinalIgnoreCase)));
        if (dimensions.PositionId.HasValue)
        {
            payload += $"|positionId={dimensions.PositionId.Value:D}";
        }

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
                tenantId = package.TenantId,
                companyId = package.CompanyId,
                fundProfileId = package.FinancialStatements.FundProfileId,
                ledgerBookId = artifact.LedgerBookId,
                dimensions = artifact.Dimensions,
                periodId = package.FinancialStatements.PeriodId,
                artifactId = artifact.ArtifactId,
                artifactKind = artifact.ArtifactKind,
                artifact.Format,
                artifact.CertificationState,
                artifact.ContentHash,
                artifact.SourceStatementId,
                artifact.DimensionScope,
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
            artifact.SourceStatementId,
            artifact.LedgerBookId,
            artifact.Dimensions,
            artifact.DimensionScope);
    }

    private static ReportDimensionScopeDto BuildReportDimensionScope(
        LedgerDimensionSetDto dimensions,
        string fundProfileId,
        Guid? ledgerBookId)
    {
        var hasExplicitScope = HasExplicitDimensionScope(dimensions, fundProfileId, ledgerBookId);
        var scopeHash = BuildDimensionScopeHash(dimensions);
        return new ReportDimensionScopeDto(
            ledgerBookId,
            dimensions,
            hasExplicitScope,
            scopeHash,
            $"dimension-scope:{scopeHash}",
            BuildScopedDimensionKeys(dimensions));
    }

    private static LedgerDimensionSetDto BuildReportPackageDimensions(
        AccountingReportPackageRequestDto request,
        string fundProfileId,
        Guid? ledgerBookId)
    {
        var dimensions = NormalizeDimensionSet(request.Dimensions);
        return new LedgerDimensionSetDto(
            FundId: fundProfileId,
            EntityId: dimensions?.EntityId,
            SleeveId: dimensions?.SleeveId,
            StrategyId: dimensions?.StrategyId,
            InvestorId: NormalizeOptional(request.InvestorId) ?? dimensions?.InvestorId,
            CapitalAccountId: NormalizeOptional(request.CapitalAccountId) ?? dimensions?.CapitalAccountId,
            InstrumentId: dimensions?.InstrumentId,
            TaxLotId: dimensions?.TaxLotId,
            CostCenterId: dimensions?.CostCenterId,
            CounterpartyId: dimensions?.CounterpartyId,
            ExternalGlDimensions: dimensions?.ExternalGlDimensions ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            OrganizationId: dimensions?.OrganizationId,
            PortfolioId: dimensions?.PortfolioId,
            BookId: ledgerBookId?.ToString("D") ?? dimensions?.BookId,
            AccountId: dimensions?.AccountId,
            CustomerId: dimensions?.CustomerId,
            VendorId: dimensions?.VendorId,
            ProjectId: dimensions?.ProjectId)
        {
            PositionId = dimensions?.PositionId
        };
    }

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> BuildReportDimensionScopeIssues(
        AccountingReportPackageRequestDto request,
        string fundProfileId,
        Guid? ledgerBookId,
        LedgerDimensionSetDto packageDimensions)
    {
        if (request.Dimensions is null)
        {
            return [];
        }

        var issues = new List<AccountingConfigurationValidationIssueDto>();
        AddDimensionConflict(
            issues,
            "ReportPackageFundDimensionMismatch",
            "fund",
            request.Dimensions.FundId,
            fundProfileId,
            request.PeriodId);
        AddDimensionConflict(
            issues,
            "ReportPackageLedgerBookDimensionMismatch",
            "ledger book",
            request.Dimensions.BookId,
            ledgerBookId?.ToString("D"),
            request.PeriodId);
        AddDimensionConflict(
            issues,
            "ReportPackageInvestorDimensionMismatch",
            "investor",
            request.Dimensions.InvestorId,
            NormalizeOptional(request.InvestorId),
            request.PeriodId);
        AddDimensionConflict(
            issues,
            "ReportPackageCapitalAccountDimensionMismatch",
            "capital account",
            request.Dimensions.CapitalAccountId,
            NormalizeOptional(request.CapitalAccountId),
            request.PeriodId);

        if (!MatchesDimensionScope(packageDimensions, NormalizeDimensionSet(request.Dimensions)))
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "ReportPackageDimensionScopeMismatch",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Accounting report package for period '{request.PeriodId}' could not retain the requested dimensional scope.",
                request.PeriodId,
                "Align request identifiers and dimension filters before certifying the report package."));
        }

        return issues;
    }

    private static IReadOnlyList<ReportLineProvenanceDto> BuildReportLineProvenance(
        AccountingReportPackageRequestDto request,
        string periodId,
        string currency,
        decimal endingCapital,
        LedgerDimensionSetDto dimensions,
        RestatementWorkflowDto? restatement,
        IReadOnlyList<string> evidenceLinks)
    {
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

    private static void AddDimensionConflict(
        List<AccountingConfigurationValidationIssueDto> issues,
        string code,
        string label,
        string? requestedValue,
        string? authoritativeValue,
        string periodId)
    {
        var requested = NormalizeOptional(requestedValue);
        var authoritative = NormalizeOptional(authoritativeValue);
        if (requested is null || authoritative is null ||
            string.Equals(requested, authoritative, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        issues.Add(new AccountingConfigurationValidationIssueDto(
            code,
            AccountingConfigurationValidationSeverityDto.Critical,
            $"Accounting report package dimension scope requested {label} '{requested}', but the package context resolves to '{authoritative}'.",
            periodId,
            "Use one consistent fund, ledger book, investor, capital account, and dimension scope before report certification."));
    }

    private static bool MatchesDimensionScope(
        LedgerDimensionSetDto? dimensions,
        LedgerDimensionSetDto? filter)
    {
        if (filter is null)
        {
            return true;
        }

        var normalized = NormalizeDimensionSet(dimensions);
        if (normalized is null)
        {
            return false;
        }

        return MatchesDimensionValue(normalized.FundId, filter.FundId)
               && MatchesDimensionValue(normalized.EntityId, filter.EntityId)
               && MatchesDimensionValue(normalized.SleeveId, filter.SleeveId)
               && MatchesDimensionValue(normalized.StrategyId, filter.StrategyId)
               && MatchesDimensionValue(normalized.InvestorId, filter.InvestorId)
               && MatchesDimensionValue(normalized.CapitalAccountId, filter.CapitalAccountId)
               && MatchesDimensionGuid(normalized.InstrumentId, filter.InstrumentId)
               && MatchesDimensionGuid(normalized.PositionId, filter.PositionId)
               && MatchesDimensionValue(normalized.TaxLotId, filter.TaxLotId)
               && MatchesDimensionValue(normalized.CostCenterId, filter.CostCenterId)
               && MatchesDimensionValue(normalized.CounterpartyId, filter.CounterpartyId)
               && MatchesDimensionValue(normalized.OrganizationId, filter.OrganizationId)
               && MatchesDimensionValue(normalized.PortfolioId, filter.PortfolioId)
               && MatchesDimensionValue(normalized.BookId, filter.BookId)
               && MatchesDimensionValue(normalized.AccountId, filter.AccountId)
               && MatchesDimensionValue(normalized.CustomerId, filter.CustomerId)
               && MatchesDimensionValue(normalized.VendorId, filter.VendorId)
               && MatchesDimensionValue(normalized.ProjectId, filter.ProjectId)
               && MatchesExternalGlDimensions(normalized.ExternalGlDimensions, filter.ExternalGlDimensions);
    }

    private static bool MatchesExactDimensionScope(
        LedgerDimensionSetDto? left,
        LedgerDimensionSetDto? right)
        => MatchesDimensionScope(left, right) && MatchesDimensionScope(right, left);

    private static bool MatchesDimensionValue(string? actual, string? expected)
    {
        var normalizedExpected = NormalizeOptional(expected);
        return normalizedExpected is null ||
               string.Equals(NormalizeOptional(actual), normalizedExpected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesDimensionGuid(Guid? actual, Guid? expected)
        => !expected.HasValue || actual == expected;

    private static bool MatchesExternalGlDimensions(
        IReadOnlyDictionary<string, string> actual,
        IReadOnlyDictionary<string, string> expected)
        => expected.All(pair =>
            actual.TryGetValue(pair.Key, out var actualValue) &&
            string.Equals(NormalizeOptional(actualValue), NormalizeOptional(pair.Value), StringComparison.OrdinalIgnoreCase));

    private static LedgerDimensionSetDto? NormalizeDimensionSet(LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
        {
            return null;
        }

        var externalGlDimensions = dimensions.ExternalGlDimensions
            .Where(pair => NormalizeOptional(pair.Key) is not null && NormalizeOptional(pair.Value) is not null)
            .ToDictionary(
                pair => NormalizeOptional(pair.Key)!,
                pair => NormalizeOptional(pair.Value)!,
                StringComparer.OrdinalIgnoreCase);

        var normalized = new LedgerDimensionSetDto(
            FundId: NormalizeOptional(dimensions.FundId),
            EntityId: NormalizeOptional(dimensions.EntityId),
            SleeveId: NormalizeOptional(dimensions.SleeveId),
            StrategyId: NormalizeOptional(dimensions.StrategyId),
            InvestorId: NormalizeOptional(dimensions.InvestorId),
            CapitalAccountId: NormalizeOptional(dimensions.CapitalAccountId),
            InstrumentId: dimensions.InstrumentId,
            TaxLotId: NormalizeOptional(dimensions.TaxLotId),
            CostCenterId: NormalizeOptional(dimensions.CostCenterId),
            CounterpartyId: NormalizeOptional(dimensions.CounterpartyId),
            ExternalGlDimensions: externalGlDimensions,
            OrganizationId: NormalizeOptional(dimensions.OrganizationId),
            PortfolioId: NormalizeOptional(dimensions.PortfolioId),
            BookId: NormalizeOptional(dimensions.BookId),
            AccountId: NormalizeOptional(dimensions.AccountId),
            CustomerId: NormalizeOptional(dimensions.CustomerId),
            VendorId: NormalizeOptional(dimensions.VendorId),
            ProjectId: NormalizeOptional(dimensions.ProjectId))
        {
            PositionId = dimensions.PositionId
        };
        return HasAnyDimensionScope(normalized) ? normalized : null;
    }

    private static bool HasExplicitDimensionScope(
        LedgerDimensionSetDto dimensions,
        string fundProfileId,
        Guid? ledgerBookId)
    {
        var filter = new LedgerDimensionSetDto(
            FundId: fundProfileId,
            BookId: ledgerBookId?.ToString("D"));
        return !MatchesDimensionScope(filter, dimensions) || !MatchesDimensionScope(dimensions, filter);
    }

    private static bool HasAnyDimensionScope(LedgerDimensionSetDto dimensions)
        => dimensions.FundId is not null
           || dimensions.EntityId is not null
           || dimensions.SleeveId is not null
           || dimensions.StrategyId is not null
           || dimensions.InvestorId is not null
           || dimensions.CapitalAccountId is not null
           || dimensions.InstrumentId.HasValue
           || dimensions.PositionId.HasValue
           || dimensions.TaxLotId is not null
           || dimensions.CostCenterId is not null
           || dimensions.CounterpartyId is not null
           || dimensions.OrganizationId is not null
           || dimensions.PortfolioId is not null
           || dimensions.BookId is not null
           || dimensions.AccountId is not null
           || dimensions.CustomerId is not null
           || dimensions.VendorId is not null
           || dimensions.ProjectId is not null
           || dimensions.ExternalGlDimensions.Count > 0;

    private static IReadOnlyList<string> BuildScopedDimensionKeys(LedgerDimensionSetDto dimensions)
    {
        var keys = new List<string>();
        AddDimensionKey(keys, "fundId", dimensions.FundId);
        AddDimensionKey(keys, "entityId", dimensions.EntityId);
        AddDimensionKey(keys, "sleeveId", dimensions.SleeveId);
        AddDimensionKey(keys, "strategyId", dimensions.StrategyId);
        AddDimensionKey(keys, "investorId", dimensions.InvestorId);
        AddDimensionKey(keys, "capitalAccountId", dimensions.CapitalAccountId);
        if (dimensions.InstrumentId.HasValue)
        {
            keys.Add("instrumentId");
        }

        if (dimensions.PositionId.HasValue)
        {
            keys.Add("positionId");
        }

        AddDimensionKey(keys, "taxLotId", dimensions.TaxLotId);
        AddDimensionKey(keys, "costCenterId", dimensions.CostCenterId);
        AddDimensionKey(keys, "counterpartyId", dimensions.CounterpartyId);
        AddDimensionKey(keys, "organizationId", dimensions.OrganizationId);
        AddDimensionKey(keys, "portfolioId", dimensions.PortfolioId);
        AddDimensionKey(keys, "bookId", dimensions.BookId);
        AddDimensionKey(keys, "accountId", dimensions.AccountId);
        AddDimensionKey(keys, "customerId", dimensions.CustomerId);
        AddDimensionKey(keys, "vendorId", dimensions.VendorId);
        AddDimensionKey(keys, "projectId", dimensions.ProjectId);
        keys.AddRange(dimensions.ExternalGlDimensions
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .Select(static pair => $"externalGl.{pair.Key.Trim()}")
            .Order(StringComparer.OrdinalIgnoreCase));
        return keys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddDimensionKey(List<string> keys, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            keys.Add(key);
        }
    }

    private static string BuildDimensionScopeHash(LedgerDimensionSetDto dimensions)
    {
        var payload = string.Join(
            "|",
            NormalizeOptional(dimensions.FundId) ?? string.Empty,
            NormalizeOptional(dimensions.EntityId) ?? string.Empty,
            NormalizeOptional(dimensions.SleeveId) ?? string.Empty,
            NormalizeOptional(dimensions.StrategyId) ?? string.Empty,
            NormalizeOptional(dimensions.InvestorId) ?? string.Empty,
            NormalizeOptional(dimensions.CapitalAccountId) ?? string.Empty,
            dimensions.InstrumentId?.ToString("D") ?? string.Empty,
            NormalizeOptional(dimensions.TaxLotId) ?? string.Empty,
            NormalizeOptional(dimensions.CostCenterId) ?? string.Empty,
            NormalizeOptional(dimensions.CounterpartyId) ?? string.Empty,
            NormalizeOptional(dimensions.OrganizationId) ?? string.Empty,
            NormalizeOptional(dimensions.PortfolioId) ?? string.Empty,
            NormalizeOptional(dimensions.BookId) ?? string.Empty,
            NormalizeOptional(dimensions.AccountId) ?? string.Empty,
            NormalizeOptional(dimensions.CustomerId) ?? string.Empty,
            NormalizeOptional(dimensions.VendorId) ?? string.Empty,
            NormalizeOptional(dimensions.ProjectId) ?? string.Empty,
            string.Join(";", dimensions.ExternalGlDimensions
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => $"{NormalizeOptional(pair.Key)}={NormalizeOptional(pair.Value)}")));
        if (dimensions.PositionId.HasValue)
        {
            payload += $"|positionId={dimensions.PositionId.Value:D}";
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..12].ToLowerInvariant();
    }

    private static bool MatchesTenantScope(
        AccountingReportPackageBundleDto package,
        string? tenantId,
        string? companyId)
    {
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        return (normalizedTenantId is null ||
                string.Equals(NormalizeOptional(package.TenantId), normalizedTenantId, StringComparison.OrdinalIgnoreCase))
               && (normalizedCompanyId is null ||
                   string.Equals(NormalizeOptional(package.CompanyId), normalizedCompanyId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesPackageIdentity(
        AccountingReportPackageBundleDto package,
        string packageId,
        string? tenantId,
        string? companyId)
        => MatchesTenantScope(package, NormalizeOptional(tenantId), NormalizeOptional(companyId)) &&
           string.Equals(package.FinancialStatements.PackageId, packageId, StringComparison.OrdinalIgnoreCase);

    private static string BuildTenantPackageScope(string? tenantId, string? companyId)
    {
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        if (normalizedTenantId is null && normalizedCompanyId is null)
        {
            return string.Empty;
        }

        return $"tenant-{Sanitize(normalizedTenantId ?? "default")}-company-{Sanitize(normalizedCompanyId ?? "default")}";
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string RequireText(string? value, string label)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{label} is required.")
            : value.Trim();

    private static string Sanitize(string value)
        => string.Concat(value.Select(static ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-'));

    private sealed record AccountingReportPackageSnapshot(
        IReadOnlyList<AccountingReportPackageBundleDto> Packages);
}
