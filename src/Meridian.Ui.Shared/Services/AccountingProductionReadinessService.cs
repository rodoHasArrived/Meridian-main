using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.FinancialOperations.AccountingSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Services;

public sealed class AccountingProductionReadinessService
{
    private const string DefaultFundProfileId = "default-fund";

    private readonly IServiceProvider _services;

    public AccountingProductionReadinessService(IServiceProvider services)
    {
        _services = services;
    }

    public async Task<AccountingProductionReadinessDto> AssessAsync(
        AccountingProductionReadinessRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var fundProfileId = NormalizeFundProfileId(request.FundProfileId);
        var migrationRunArtifacts = await LoadMigrationRunArtifactsAsync(request, fundProfileId, ct).ConfigureAwait(false);
        var tenantAdministrationProfile = await LoadTenantAdministrationProfileAsync(request, ct).ConfigureAwait(false);
        var productionCertificationProfile = await LoadProductionCertificationProfileAsync(request, fundProfileId, ct).ConfigureAwait(false);
        var profileMergedRequest = MergeProductionCertificationProfile(
            request with { FundProfileId = fundProfileId, MigrationRunArtifacts = migrationRunArtifacts },
            productionCertificationProfile);
        var effectiveRequest = MergeTenantAdministrationProfile(
            profileMergedRequest,
            tenantAdministrationProfile);
        var components = new List<AccountingProductionReadinessComponentDto>();
        var ledgerBookResult = await BuildLedgerBookComponentAsync(effectiveRequest, fundProfileId, components, ct).ConfigureAwait(false);
        var rulesStudioResult = await BuildRulesStudioComponentAsync(effectiveRequest, fundProfileId, ledgerBookResult.WorkflowReadiness, components, ct).ConfigureAwait(false);
        BuildJournalLifecycleComponent(components, ledgerBookResult.WorkflowReadiness);
        BuildCloseReportingComponent(components, ledgerBookResult.WorkflowReadiness, rulesStudioResult.DimensionalReportingReadiness);
        var externalGlCounts = await BuildExternalGlComponentAsync(effectiveRequest, fundProfileId, ledgerBookResult.WorkflowReadiness, components, ct).ConfigureAwait(false);
        BuildMigrationRolloutComponent(effectiveRequest, components);
        var tenantAdministration = BuildTenantAdministrationComponent(effectiveRequest, components);

        var issues = components
            .SelectMany(static component => component.Issues)
            .OrderByDescending(static issue => issue.Severity)
            .ThenBy(static issue => issue.Area)
            .ThenBy(static issue => issue.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var status = ResolveStatus(components);
        var score = components.Count == 0 ? 0 : (int)Math.Round(components.Average(static component => component.Score));

        return new AccountingProductionReadinessDto(
            DateTimeOffset.UtcNow,
            fundProfileId,
            request.LedgerBookId,
            status,
            score,
            components,
            issues,
            ledgerBookResult.Rollout,
            rulesStudioResult.Summary,
            ledgerBookResult.WorkflowReadiness,
            rulesStudioResult.DimensionalReportingReadiness,
            externalGlCounts.ProviderCount,
            externalGlCounts.CertifiedMappingProfileCount,
            externalGlCounts.LivePostingEnabled,
            migrationRunArtifacts,
            tenantAdministration);
    }

    private async Task<IReadOnlyList<AccountingMigrationRunArtifactDto>> LoadMigrationRunArtifactsAsync(
        AccountingProductionReadinessRequestDto request,
        string fundProfileId,
        CancellationToken ct)
    {
        var artifacts = new Dictionary<string, AccountingMigrationRunArtifactDto>(StringComparer.OrdinalIgnoreCase);
        var store = _services.GetService<IAccountingMigrationRunArtifactStore>();
        if (store is not null)
        {
            foreach (var artifact in await store
                         .ListAsync(fundProfileId, request.LedgerBookId, ct, request.TenantId, request.CompanyId)
                         .ConfigureAwait(false))
            {
                artifacts[MigrationArtifactKey(artifact)] = artifact;
            }
        }

        foreach (var artifact in request.MigrationRunArtifacts)
        {
            artifacts[MigrationArtifactKey(artifact)] = artifact with
            {
                FundProfileId = NormalizeFundProfileId(artifact.FundProfileId ?? fundProfileId),
                TenantId = TrimOrNull(artifact.TenantId) ?? TrimOrNull(request.TenantId),
                CompanyId = TrimOrNull(artifact.CompanyId) ?? TrimOrNull(request.CompanyId)
            };
        }

        return artifacts.Values
            .OrderByDescending(static item => item.StartedAtUtc)
            .ThenBy(static item => item.RunId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<AccountingTenantAdministrationProfileDto?> LoadTenantAdministrationProfileAsync(
        AccountingProductionReadinessRequestDto request,
        CancellationToken ct)
    {
        var store = _services.GetService<IAccountingTenantAdministrationProfileStore>();
        if (store is null)
        {
            return null;
        }

        return await store.GetAsync(request.TenantId, request.CompanyId, ct).ConfigureAwait(false);
    }

    private async Task<AccountingProductionCertificationProfileDto?> LoadProductionCertificationProfileAsync(
        AccountingProductionReadinessRequestDto request,
        string fundProfileId,
        CancellationToken ct)
    {
        var store = _services.GetService<IAccountingProductionCertificationProfileStore>();
        if (store is null)
        {
            return null;
        }

        return await store.GetAsync(request.TenantId, request.CompanyId, fundProfileId, request.LedgerBookId, ct).ConfigureAwait(false);
    }

    private static AccountingProductionReadinessRequestDto MergeProductionCertificationProfile(
        AccountingProductionReadinessRequestDto request,
        AccountingProductionCertificationProfileDto? profile)
    {
        if (profile is null)
        {
            return request;
        }

        var workflowEvidence = request.LedgerBookWorkflowEvidenceLinks
            .Concat(profile.EvidenceReferences)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var dimensionalEvidence = request.DimensionalReportingEvidenceLinks
            .Concat(profile.EvidenceReferences)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return request with
        {
            PostingRulesLedgerBookNativeCertified = request.PostingRulesLedgerBookNativeCertified || profile.PostingRulesLedgerBookNativeCertified,
            JournalLifecycleLedgerBookNativeCertified = request.JournalLifecycleLedgerBookNativeCertified || profile.JournalLifecycleLedgerBookNativeCertified,
            CloseReportingLedgerBookNativeCertified = request.CloseReportingLedgerBookNativeCertified || profile.CloseReportingLedgerBookNativeCertified,
            ExternalGlLedgerBookNativeCertified = request.ExternalGlLedgerBookNativeCertified || profile.ExternalGlLedgerBookNativeCertified,
            ReconciliationLedgerBookNativeCertified = request.ReconciliationLedgerBookNativeCertified || profile.ReconciliationLedgerBookNativeCertified,
            DirectLendingLedgerBookNativeCertified = request.DirectLendingLedgerBookNativeCertified || profile.DirectLendingLedgerBookNativeCertified,
            StrategyLedgerReadLedgerBookNativeCertified = request.StrategyLedgerReadLedgerBookNativeCertified || profile.StrategyLedgerReadLedgerBookNativeCertified,
            LedgerBookWorkflowEvidenceLinks = workflowEvidence,
            PeriodReportDimensionQueriesCertified = request.PeriodReportDimensionQueriesCertified || profile.PeriodReportDimensionQueriesCertified,
            CrossPeriodReportDimensionQueriesCertified = request.CrossPeriodReportDimensionQueriesCertified || profile.CrossPeriodReportDimensionQueriesCertified,
            JournalQueryDimensionFiltersCertified = request.JournalQueryDimensionFiltersCertified || profile.JournalQueryDimensionFiltersCertified,
            ExternalExportDimensionMappingCertified = request.ExternalExportDimensionMappingCertified || profile.ExternalExportDimensionMappingCertified,
            LedgerLineDimensionsPersistedCertified = request.LedgerLineDimensionsPersistedCertified || profile.LedgerLineDimensionsPersistedCertified,
            TrialBalanceDimensionFiltersCertified = request.TrialBalanceDimensionFiltersCertified || profile.TrialBalanceDimensionFiltersCertified,
            ReportPackageDimensionProvenanceCertified = request.ReportPackageDimensionProvenanceCertified || profile.ReportPackageDimensionProvenanceCertified,
            DimensionalReportingEvidenceLinks = dimensionalEvidence
        };
    }

    private static AccountingProductionReadinessRequestDto MergeTenantAdministrationProfile(
        AccountingProductionReadinessRequestDto request,
        AccountingTenantAdministrationProfileDto? profile)
    {
        if (profile is null)
        {
            return request;
        }

        var evidence = request.TenantAdministrationEvidenceLinks
            .Concat(profile.EvidenceReferences)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return request with
        {
            TenantId = profile.TenantId,
            CompanyId = profile.CompanyId,
            TenantScopeConfigured = profile.TenantScopeConfigured,
            AdminRoleProfileConfigured = profile.AdminRoleProfileConfigured,
            ScopedAccessPoliciesConfigured = profile.ScopedAccessPoliciesConfigured,
            ReportingGroupsConfigured = profile.ReportingGroupsConfigured,
            AccountingAdminSurfaceConfigured = profile.AccountingAdminSurfaceConfigured,
            BrowserAccountingAdminSurfaceConfigured = profile.BrowserAccountingAdminSurfaceConfigured,
            WpfAccountingAdminSurfaceConfigured = profile.WpfAccountingAdminSurfaceConfigured,
            ChartAdministrationStudioConfigured = profile.ChartAdministrationStudioConfigured,
            RuleTestPromotionStudioConfigured = profile.RuleTestPromotionStudioConfigured,
            CloseSetupStudioConfigured = profile.CloseSetupStudioConfigured,
            ProviderMappingStudioConfigured = profile.ProviderMappingStudioConfigured,
            TenantCompanyReportGroupSetupStudioConfigured = profile.TenantCompanyReportGroupSetupStudioConfigured,
            AuditReviewToolingConfigured = profile.AuditReviewToolingConfigured,
            BulkImportExportSafeguardsConfigured = profile.BulkImportExportSafeguardsConfigured,
            PerformanceValidationConfigured = profile.PerformanceValidationConfigured,
            DisasterRecoveryRunbookConfigured = profile.DisasterRecoveryRunbookConfigured,
            LedgerBookAdministrationStudioConfigured = profile.LedgerBookAdministrationStudioConfigured,
            PostingRuleAuthoringStudioConfigured = profile.PostingRuleAuthoringStudioConfigured,
            ApprovalQueueStudioConfigured = profile.ApprovalQueueStudioConfigured,
            DimensionMappingStudioConfigured = profile.DimensionMappingStudioConfigured,
            ImplementationSandboxConfigured = profile.ImplementationSandboxConfigured,
            TenantAdministrationEvidenceLinks = evidence
        };
    }

    private async Task<LedgerBookComponentResult> BuildLedgerBookComponentAsync(
        AccountingProductionReadinessRequestDto request,
        string fundProfileId,
        ICollection<AccountingProductionReadinessComponentDto> components,
        CancellationToken ct)
    {
        var workflowEvidence = NormalizeEvidenceReferences(request.LedgerBookWorkflowEvidenceLinks);
        var workflowReadiness = new AccountingLedgerBookWorkflowReadinessDto(
            request.LedgerBookId,
            request.PostingRulesLedgerBookNativeCertified,
            request.JournalLifecycleLedgerBookNativeCertified,
            request.CloseReportingLedgerBookNativeCertified,
            request.ExternalGlLedgerBookNativeCertified,
            request.ReconciliationLedgerBookNativeCertified,
            request.DirectLendingLedgerBookNativeCertified,
            request.StrategyLedgerReadLedgerBookNativeCertified,
            workflowEvidence);
        var workflowIssues = BuildLedgerBookWorkflowIssues(workflowReadiness);
        var service = _services.GetService<ILedgerBookService>();
        if (service is null)
        {
            var serviceIssues = new List<AccountingProductionReadinessIssueDto>
            {
                Issue("ledger-books.service-missing", AccountingProductionReadinessAreaDto.LedgerBooks, AccountingConfigurationValidationSeverityDto.Critical, "Ledger-book service is not registered.", "Register ILedgerBookService before production accounting rollout.")
            };
            serviceIssues.AddRange(workflowIssues);
            components.Add(Component(
                AccountingProductionReadinessAreaDto.LedgerBooks,
                "Ledger books",
                AccountingProductionReadinessStatusDto.Unavailable,
                0,
                "No ledger-book service is registered for production readiness assessment.",
                serviceIssues,
                UiApiRoutes.LedgerBooks,
                workflowReadiness.EvidenceReferences));
            return new LedgerBookComponentResult(null, workflowReadiness);
        }

        var rollout = await service.AssessRolloutAsync(
            new LedgerBookRolloutAssessmentRequest(
                fundProfileId,
                request.LedgerBookId,
                FundStructureNodeKind: null,
                request.AccountingBasis,
                request.RequiredLedgerBookScopes),
            ct).ConfigureAwait(false);
        var issues = rollout.Issues
            .Select(issue => Issue(
                $"ledger-books.{issue.Code}",
                AccountingProductionReadinessAreaDto.LedgerBooks,
                MapSeverity(issue.Severity),
                issue.Message,
                issue.Severity == LedgerBookRolloutIssueSeverityDto.Critical
                    ? "Create or migrate the missing ledger-book scope before production cutover."
                    : "Review ledger-book close and policy posture before production certification.",
                issue.LedgerBookId.HasValue ? [$"ledger-book:{issue.LedgerBookId.Value:D}"] : []))
            .Concat(workflowIssues)
            .ToArray();
        var status = ResolveIssueStatus(issues);

        components.Add(Component(
            AccountingProductionReadinessAreaDto.LedgerBooks,
            "Ledger books",
            status,
            status == AccountingProductionReadinessStatusDto.Ready ? 100 : status == AccountingProductionReadinessStatusDto.ReviewRequired ? 70 : 20,
            $"{rollout.BookCount} ledger book(s), {rollout.OpenPeriodCount} open period(s), {rollout.CriticalIssueCount} critical issue(s); {workflowReadiness.CompletedControlCount}/{workflowReadiness.RequiredControlCount} ledger-book-native workflow control(s) certified across posting, lifecycle, close/reporting, external GL, reconciliation, direct lending, and strategy-ledger reads; {workflowReadiness.EvidenceReferences.Count} retained workflow evidence link(s).",
            issues,
            UiApiRoutes.LedgerBookRolloutAssessment,
            workflowReadiness.EvidenceReferences));

        return new LedgerBookComponentResult(rollout, workflowReadiness);
    }

    private static IReadOnlyList<AccountingProductionReadinessIssueDto> BuildLedgerBookWorkflowIssues(
        AccountingLedgerBookWorkflowReadinessDto readiness)
    {
        var issues = new List<AccountingProductionReadinessIssueDto>();
        if (!readiness.HasLedgerBookScope)
        {
            issues.Add(Issue(
                "ledger-books.workflow-ledger-book-scope-missing",
                AccountingProductionReadinessAreaDto.LedgerBooks,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Accounting workflow certification is not scoped to a Meridian ledger book.",
                "Select the target ledger book before certifying posting rules, journal lifecycle, close/reporting, external-GL, reconciliation, direct-lending, and strategy-ledger workflows as ledger-book-native."));
        }

        if (!readiness.HasRetainedEvidence)
        {
            issues.Add(Issue(
                "ledger-books.workflow-evidence-missing",
                AccountingProductionReadinessAreaDto.LedgerBooks,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Ledger-book-native workflow certification has no retained evidence links.",
                "Attach retained evidence identifying the selected ledger book and the certified posting-rule, journal-lifecycle, close/reporting, external-GL, reconciliation, direct-lending, and strategy-ledger workflow checks before production rollout."));
        }
        else if (readiness.HasLedgerBookScope && !readiness.HasLedgerBookScopedEvidence)
        {
            issues.Add(Issue(
                "ledger-books.workflow-evidence-scope-mismatch",
                AccountingProductionReadinessAreaDto.LedgerBooks,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Ledger-book-native workflow certification evidence does not identify the selected ledger book.",
                "Retain workflow certification evidence that names the selected ledgerBookId before production rollout.",
                readiness.EvidenceReferences));
        }

        if (!readiness.PostingRulesLedgerBookNativeCertified)
        {
            issues.Add(Issue(
                "ledger-books.posting-rules-not-certified",
                AccountingProductionReadinessAreaDto.LedgerBooks,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Posting rule execution has not been certified as ledger-book-native.",
                "Prove source-event predicates, generated posting candidates, evidence, and draft handoff all preserve the selected ledger book before production rollout."));
        }
        else if (!readiness.HasPostingRulesLedgerBookNativeEvidence)
        {
            issues.Add(Issue(
                "ledger-books.posting-rules-evidence-missing",
                AccountingProductionReadinessAreaDto.LedgerBooks,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Posting rule execution certification lacks retained posting-rule workflow evidence for the selected ledger book.",
                "Attach evidence naming the selected ledger book and posting-rule, Rules Studio, or posting-candidate workflow before production rollout.",
                readiness.EvidenceReferences));
        }

        if (!readiness.JournalLifecycleLedgerBookNativeCertified)
        {
            issues.Add(Issue(
                "ledger-books.journal-lifecycle-not-certified",
                AccountingProductionReadinessAreaDto.LedgerBooks,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Journal entry lifecycle has not been certified as ledger-book-native.",
                "Prove draft, validation, submission, approval, posting, correction, close-lock, and audit transitions cannot cross ledger-book scope."));
        }
        else if (!readiness.HasJournalLifecycleLedgerBookNativeEvidence)
        {
            issues.Add(Issue(
                "ledger-books.journal-lifecycle-evidence-missing",
                AccountingProductionReadinessAreaDto.LedgerBooks,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Journal entry lifecycle certification lacks retained lifecycle workflow evidence for the selected ledger book.",
                "Attach evidence naming the selected ledger book and journal-entry lifecycle workflow before production rollout.",
                readiness.EvidenceReferences));
        }

        if (!readiness.CloseReportingLedgerBookNativeCertified)
        {
            issues.Add(Issue(
                "ledger-books.close-reporting-not-certified",
                AccountingProductionReadinessAreaDto.LedgerBooks,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Close and reporting workflows have not been certified as ledger-book-native.",
                "Prove close checklists, period locks, report packages, certification, and restatement workflows consume only the selected ledger book and its dimensions."));
        }
        else if (!readiness.HasCloseReportingLedgerBookNativeEvidence)
        {
            issues.Add(Issue(
                "ledger-books.close-reporting-evidence-missing",
                AccountingProductionReadinessAreaDto.LedgerBooks,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Close/reporting certification lacks retained close or report workflow evidence for the selected ledger book.",
                "Attach evidence naming the selected ledger book and close-management, report-package, or restatement workflow before production rollout.",
                readiness.EvidenceReferences));
        }

        if (!readiness.ExternalGlLedgerBookNativeCertified)
        {
            issues.Add(Issue(
                "ledger-books.external-gl-not-certified",
                AccountingProductionReadinessAreaDto.LedgerBooks,
                AccountingConfigurationValidationSeverityDto.Critical,
                "External GL import, reconciliation, mapping, and guarded export workflows have not been certified as ledger-book-native.",
                "Prove provider imports, reconciliation snapshots, mapping profiles, and export packages are bound to the selected Meridian ledger book."));
        }
        else if (!readiness.HasExternalGlLedgerBookNativeEvidence)
        {
            issues.Add(Issue(
                "ledger-books.external-gl-evidence-missing",
                AccountingProductionReadinessAreaDto.LedgerBooks,
                AccountingConfigurationValidationSeverityDto.Critical,
                "External GL certification lacks retained external-GL workflow evidence for the selected ledger book.",
                "Attach evidence naming the selected ledger book and external-GL import, reconciliation, mapping, or guarded-export workflow before production rollout.",
                readiness.EvidenceReferences));
        }

        if (!readiness.ReconciliationLedgerBookNativeCertified)
        {
            issues.Add(Issue(
                "ledger-books.reconciliation-not-certified",
                AccountingProductionReadinessAreaDto.LedgerBooks,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Reconciliation workflows have not been certified as ledger-book-native.",
                "Prove break queues, statement reconciliation, casework, and retained reconciliation snapshots are bound to the selected ledger book before production rollout."));
        }
        else if (!readiness.HasReconciliationLedgerBookNativeEvidence)
        {
            issues.Add(Issue(
                "ledger-books.reconciliation-evidence-missing",
                AccountingProductionReadinessAreaDto.LedgerBooks,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Reconciliation certification lacks retained reconciliation workflow evidence for the selected ledger book.",
                "Attach evidence naming the selected ledger book and reconciliation, break-queue, statement-reconciliation, or reconciliation-case workflow before production rollout.",
                readiness.EvidenceReferences));
        }

        if (!readiness.DirectLendingLedgerBookNativeCertified)
        {
            issues.Add(Issue(
                "ledger-books.direct-lending-not-certified",
                AccountingProductionReadinessAreaDto.LedgerBooks,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Direct-lending accounting projections have not been certified as ledger-book-native.",
                "Prove loan-account, borrower, accrual, and direct-lending projection workflows retain the selected ledger book before production rollout."));
        }
        else if (!readiness.HasDirectLendingLedgerBookNativeEvidence)
        {
            issues.Add(Issue(
                "ledger-books.direct-lending-evidence-missing",
                AccountingProductionReadinessAreaDto.LedgerBooks,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Direct-lending certification lacks retained direct-lending workflow evidence for the selected ledger book.",
                "Attach evidence naming the selected ledger book and direct-lending, loan-account, borrower, or direct-lending-projection workflow before production rollout.",
                readiness.EvidenceReferences));
        }

        if (!readiness.StrategyLedgerReadLedgerBookNativeCertified)
        {
            issues.Add(Issue(
                "ledger-books.strategy-ledger-reads-not-certified",
                AccountingProductionReadinessAreaDto.LedgerBooks,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Strategy ledger reads have not been certified as ledger-book-native.",
                "Prove strategy-run trial-balance, journal, and drill-through reads preserve the selected ledger book and fail closed on cross-book data before production rollout."));
        }
        else if (!readiness.HasStrategyLedgerReadLedgerBookNativeEvidence)
        {
            issues.Add(Issue(
                "ledger-books.strategy-ledger-reads-evidence-missing",
                AccountingProductionReadinessAreaDto.LedgerBooks,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Strategy ledger-read certification lacks retained strategy-ledger evidence for the selected ledger book.",
                "Attach evidence naming the selected ledger book and strategy-ledger, strategy-run, run-ledger, or strategy-ledger-read workflow before production rollout.",
                readiness.EvidenceReferences));
        }

        return issues;
    }

    private async Task<RulesStudioComponentResult> BuildRulesStudioComponentAsync(
        AccountingProductionReadinessRequestDto request,
        string fundProfileId,
        AccountingLedgerBookWorkflowReadinessDto workflowReadiness,
        ICollection<AccountingProductionReadinessComponentDto> components,
        CancellationToken ct)
    {
        var dimensionalReportingReadiness = BuildDimensionalReportingReadiness(request);
        var service = _services.GetService<IAccountingConfigurationService>();
        if (service is null)
        {
            components.Add(Component(
                AccountingProductionReadinessAreaDto.RulesStudio,
                "Rules Studio",
                AccountingProductionReadinessStatusDto.Unavailable,
                0,
                "No accounting configuration service is registered.",
                [Issue("rules-studio.service-missing", AccountingProductionReadinessAreaDto.RulesStudio, AccountingConfigurationValidationSeverityDto.Critical, "Accounting configuration service is not registered.", "Register IAccountingConfigurationService before accounting configuration rollout.")],
                UiApiRoutes.LedgerAccountingConfiguration));
            var unavailablePostingRuleIssues = new List<AccountingProductionReadinessIssueDto>
            {
                Issue("posting-rules.configuration-missing", AccountingProductionReadinessAreaDto.PostingRules, AccountingConfigurationValidationSeverityDto.Critical, "Posting-rule execution cannot be assessed without accounting configuration.", "Register accounting configuration, Rules Studio, and posting-rule candidate services before production rollout.")
            };
            AddPostingRulesWorkflowIssues(workflowReadiness, unavailablePostingRuleIssues);
            components.Add(Component(
                AccountingProductionReadinessAreaDto.PostingRules,
                "Posting rule execution",
                AccountingProductionReadinessStatusDto.Unavailable,
                0,
                "Posting-rule execution cannot be assessed without accounting configuration.",
                unavailablePostingRuleIssues,
                UiApiRoutes.LedgerAccountingConfigurationPostingRuleCandidates,
                workflowReadiness.EvidenceReferences));
            components.Add(Component(
                AccountingProductionReadinessAreaDto.DimensionalAccounting,
                "Dimensional accounting",
                AccountingProductionReadinessStatusDto.Unavailable,
                0,
                "Dimensional coverage cannot be assessed without accounting configuration.",
                [Issue("dimensions.configuration-missing", AccountingProductionReadinessAreaDto.DimensionalAccounting, AccountingConfigurationValidationSeverityDto.Critical, "Accounting configuration is unavailable.", "Register accounting configuration and rules before certifying dimensional accounting coverage.")],
                evidenceReferences: dimensionalReportingReadiness.EvidenceReferences));
            return new RulesStudioComponentResult(null, dimensionalReportingReadiness);
        }

        var workspace = await service.GetWorkspaceAsync(fundProfileId, request.LedgerBookId, ct, request.TenantId, request.CompanyId).ConfigureAwait(false);
        var summary = workspace.RulesStudio?.Summary;
        var rulesIssues = new List<AccountingProductionReadinessIssueDto>();
        rulesIssues.AddRange(workspace.ValidationIssues.Select(issue => Issue(
            $"configuration.{issue.Code}",
            AccountingProductionReadinessAreaDto.RulesStudio,
            issue.Severity,
            issue.Message,
            issue.SuggestedAction ?? "Resolve the accounting configuration validation issue before activation.")));
        if (summary is null || summary.TotalRules == 0)
        {
            rulesIssues.Add(Issue("rules-studio.no-rules", AccountingProductionReadinessAreaDto.RulesStudio, AccountingConfigurationValidationSeverityDto.Critical, "No posting rules are configured.", "Configure scoped posting rules and saved tests before production rollout."));
        }
        else
        {
            if (summary.RulesMissingCurrentVersionRegressionTests > 0)
            {
                rulesIssues.Add(Issue("rules-studio.regression-tests-missing", AccountingProductionReadinessAreaDto.RulesStudio, AccountingConfigurationValidationSeverityDto.Warning, $"{summary.RulesMissingCurrentVersionRegressionTests} rule(s) are missing current-version regression tests.", "Add saved dry-run regression tests for each active production rule version."));
            }

            if (summary.PendingPromotionApprovalRules > 0)
            {
                rulesIssues.Add(Issue("rules-studio.promotion-pending", AccountingProductionReadinessAreaDto.RulesStudio, AccountingConfigurationValidationSeverityDto.Warning, $"{summary.PendingPromotionApprovalRules} rule promotion approval(s) are pending.", "Complete human promotion approval before activating production rules."));
            }
        }

        var activeGeneratedPostingRuleCount = workspace.PostingRules.Count(static rule =>
            !rule.IsArchived && rule.GeneratedPostings.Count > 0);
        if (activeGeneratedPostingRuleCount == 0)
        {
            rulesIssues.Add(Issue("posting-rules.generated-postings-missing", AccountingProductionReadinessAreaDto.PostingRules, AccountingConfigurationValidationSeverityDto.Warning, "No active posting rule generates multi-line postings.", "Convert template-only mappings into governed generated posting rules for production source events."));
        }

        var postingRuleIssues = rulesIssues
            .Where(static issue => issue.Area == AccountingProductionReadinessAreaDto.PostingRules)
            .ToList();
        AddPostingRulesWorkflowIssues(workflowReadiness, postingRuleIssues);

        var dimensionalIssues = BuildDimensionalIssues(workspace);
        dimensionalIssues.AddRange(BuildDimensionalReportingIssues(dimensionalReportingReadiness));
        components.Add(Component(
            AccountingProductionReadinessAreaDto.RulesStudio,
            "Rules Studio",
            ResolveIssueStatus(rulesIssues),
            ScoreFromIssues(rulesIssues, summary?.TotalRules > 0),
            summary is null
                ? "Rules Studio summary is unavailable."
                : $"{summary.TotalRules} rule(s), {summary.GeneratedPostingRules} generated-posting rule(s), {summary.SavedTestCaseCount} saved test case(s), {summary.PendingPromotionApprovalRules} pending promotion approval(s).",
            rulesIssues,
            UiApiRoutes.LedgerAccountingConfiguration));
        components.Add(Component(
            AccountingProductionReadinessAreaDto.PostingRules,
            "Posting rule execution",
            ResolveIssueStatus(postingRuleIssues),
            ScoreFromIssues(postingRuleIssues, hasPositiveEvidence: activeGeneratedPostingRuleCount > 0 && workflowReadiness.HasPostingRulesLedgerBookNativeEvidence),
            $"{activeGeneratedPostingRuleCount} active generated-posting rule(s) are configured for governed draft candidates; posting-rule workflow {(workflowReadiness.PostingRulesLedgerBookNativeCertified ? "certified" : "not certified")}.",
            postingRuleIssues,
            UiApiRoutes.LedgerAccountingConfigurationPostingRuleCandidates,
            workflowReadiness.EvidenceReferences));
        components.Add(Component(
            AccountingProductionReadinessAreaDto.DimensionalAccounting,
            "Dimensional accounting",
            ResolveIssueStatus(dimensionalIssues),
            ScoreFromIssues(dimensionalIssues, hasPositiveEvidence: dimensionalReportingReadiness.HasRetainedEvidence),
            $"Rules/generated postings plus {dimensionalReportingReadiness.CompletedControlCount}/{dimensionalReportingReadiness.RequiredControlCount} ledger/query/report/export dimension control(s) were inspected for canonical LedgerDimensionSet coverage.",
            dimensionalIssues,
            UiApiRoutes.LedgerAccountingConfiguration,
            dimensionalReportingReadiness.EvidenceReferences));

        return new RulesStudioComponentResult(summary, dimensionalReportingReadiness);
    }

    private static void AddPostingRulesWorkflowIssues(
        AccountingLedgerBookWorkflowReadinessDto readiness,
        ICollection<AccountingProductionReadinessIssueDto> issues)
    {
        if (!readiness.HasLedgerBookScope)
        {
            issues.Add(Issue(
                "posting-rules.ledger-book-workflow-scope-missing",
                AccountingProductionReadinessAreaDto.PostingRules,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Posting-rule execution readiness is not scoped to a Meridian ledger book.",
                "Select the target ledger book before certifying source-event predicates, generated posting candidates, and governed journal-draft handoff."));
            return;
        }

        if (!readiness.PostingRulesLedgerBookNativeCertified)
        {
            issues.Add(Issue(
                "posting-rules.ledger-book-native-not-certified",
                AccountingProductionReadinessAreaDto.PostingRules,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Posting-rule execution has not been certified as ledger-book-native.",
                "Prove source-event predicates, generated posting candidates, evidence, and draft handoff all preserve the selected ledger book."));
        }
        else if (!readiness.HasPostingRulesLedgerBookNativeEvidence)
        {
            issues.Add(Issue(
                "posting-rules.ledger-book-native-evidence-missing",
                AccountingProductionReadinessAreaDto.PostingRules,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Posting-rule execution certification lacks retained workflow evidence for the selected ledger book.",
                "Attach retained evidence naming the selected ledger book and posting-rule, Rules Studio, or posting-candidate workflow.",
                readiness.EvidenceReferences));
        }
    }

    private static AccountingDimensionalReportingReadinessDto BuildDimensionalReportingReadiness(
        AccountingProductionReadinessRequestDto request)
        => new(
            request.LedgerBookId,
            request.PeriodReportDimensionQueriesCertified,
            request.CrossPeriodReportDimensionQueriesCertified,
            request.JournalQueryDimensionFiltersCertified,
            request.ExternalExportDimensionMappingCertified,
            request.LedgerLineDimensionsPersistedCertified,
            request.TrialBalanceDimensionFiltersCertified,
            request.ReportPackageDimensionProvenanceCertified,
            NormalizeEvidenceReferences(request.DimensionalReportingEvidenceLinks));

    private static IReadOnlyList<AccountingProductionReadinessIssueDto> BuildDimensionalReportingIssues(
        AccountingDimensionalReportingReadinessDto readiness)
    {
        var issues = new List<AccountingProductionReadinessIssueDto>();
        if (!readiness.HasLedgerBookScope)
        {
            issues.Add(Issue(
                "dimensions.reporting-ledger-book-scope-missing",
                AccountingProductionReadinessAreaDto.DimensionalAccounting,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Dimensional reporting certification is not scoped to a Meridian ledger book.",
                "Select the target ledger book before certifying ledger-line persistence, trial-balance filters, period reports, cross-period reports, journal filters, report packages, and export dimension mappings."));
        }

        if (!readiness.HasRetainedEvidence)
        {
            issues.Add(Issue(
                "dimensions.reporting-evidence-missing",
                AccountingProductionReadinessAreaDto.DimensionalAccounting,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Dimensional reporting certification has no retained evidence links.",
                "Attach retained evidence identifying the selected ledger book and the certified ledger-line, report/query, provenance, and export dimension checks before production rollout."));
        }
        else if (readiness.HasLedgerBookScope && !readiness.HasLedgerBookScopedEvidence)
        {
            issues.Add(Issue(
                "dimensions.reporting-evidence-scope-mismatch",
                AccountingProductionReadinessAreaDto.DimensionalAccounting,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Dimensional reporting certification evidence does not identify the selected ledger book.",
                "Retain dimensional reporting evidence that names the selected ledgerBookId before production rollout.",
                readiness.EvidenceReferences));
        }

        if (!readiness.PeriodReportDimensionQueriesCertified)
        {
            issues.Add(Issue(
                "dimensions.period-reports-not-certified",
                AccountingProductionReadinessAreaDto.DimensionalAccounting,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Period report dimension queries have not been certified.",
                "Prove period trial balance, financial statements, NAV, and investor package filters retain canonical dimensions for the selected ledger book."));
        }
        else if (!readiness.HasPeriodReportDimensionQueryEvidence)
        {
            issues.Add(Issue(
                "dimensions.period-reports-evidence-missing",
                AccountingProductionReadinessAreaDto.DimensionalAccounting,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Period report dimension certification lacks retained period-report evidence for the selected ledger book.",
                "Attach retained evidence naming the selected ledger book and period report, trial-balance, financial-statement, NAV, or investor-package dimension checks.",
                readiness.EvidenceReferences));
        }

        if (!readiness.CrossPeriodReportDimensionQueriesCertified)
        {
            issues.Add(Issue(
                "dimensions.cross-period-reports-not-certified",
                AccountingProductionReadinessAreaDto.DimensionalAccounting,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Cross-period report dimension queries have not been certified.",
                "Prove comparative and roll-forward report filters retain canonical dimensions across accounting periods for the selected ledger book."));
        }
        else if (!readiness.HasCrossPeriodReportDimensionQueryEvidence)
        {
            issues.Add(Issue(
                "dimensions.cross-period-reports-evidence-missing",
                AccountingProductionReadinessAreaDto.DimensionalAccounting,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Cross-period report dimension certification lacks retained cross-period evidence for the selected ledger book.",
                "Attach retained evidence naming the selected ledger book and cross-period, comparative, or roll-forward report dimension checks.",
                readiness.EvidenceReferences));
        }

        if (!readiness.JournalQueryDimensionFiltersCertified)
        {
            issues.Add(Issue(
                "dimensions.journal-query-filters-not-certified",
                AccountingProductionReadinessAreaDto.DimensionalAccounting,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Journal query dimension filters have not been certified.",
                "Prove ledger journal drill-through, audit, and evidence queries filter by canonical dimensions instead of account-name or route heuristics."));
        }
        else if (!readiness.HasJournalQueryDimensionFilterEvidence)
        {
            issues.Add(Issue(
                "dimensions.journal-query-filters-evidence-missing",
                AccountingProductionReadinessAreaDto.DimensionalAccounting,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Journal query dimension certification lacks retained journal-filter evidence for the selected ledger book.",
                "Attach retained evidence naming the selected ledger book and journal query, journal filter, or ledger journal dimension checks.",
                readiness.EvidenceReferences));
        }

        if (!readiness.ExternalExportDimensionMappingCertified)
        {
            issues.Add(Issue(
                "dimensions.external-export-mapping-not-certified",
                AccountingProductionReadinessAreaDto.DimensionalAccounting,
                AccountingConfigurationValidationSeverityDto.Critical,
                "External export dimension mapping has not been certified.",
                "Prove guarded export packages preserve fund, entity, ledger-book, and external-GL dimension mappings before production export review."));
        }
        else if (!readiness.HasExternalExportDimensionMappingEvidence)
        {
            issues.Add(Issue(
                "dimensions.external-export-mapping-evidence-missing",
                AccountingProductionReadinessAreaDto.DimensionalAccounting,
                AccountingConfigurationValidationSeverityDto.Critical,
                "External export dimension mapping certification lacks retained export-mapping evidence for the selected ledger book.",
                "Attach retained evidence naming the selected ledger book and external export, external-GL mapping, or GL export dimension checks.",
                readiness.EvidenceReferences));
        }

        if (!readiness.LedgerLineDimensionsPersistedCertified)
        {
            issues.Add(Issue(
                "dimensions.ledger-line-persistence-not-certified",
                AccountingProductionReadinessAreaDto.DimensionalAccounting,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Posted ledger-line dimension persistence has not been certified.",
                "Prove posted journal lines retain canonical LedgerDimensionSet scope before production reporting or export certification."));
        }
        else if (!readiness.HasLedgerLineDimensionPersistenceEvidence)
        {
            issues.Add(Issue(
                "dimensions.ledger-line-persistence-evidence-missing",
                AccountingProductionReadinessAreaDto.DimensionalAccounting,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Ledger-line dimension persistence certification lacks retained ledger-line evidence for the selected ledger book.",
                "Attach retained evidence naming the selected ledger book and ledger-line, line-dimension, posted-ledger-line, or journal-line-dimension checks.",
                readiness.EvidenceReferences));
        }

        if (!readiness.TrialBalanceDimensionFiltersCertified)
        {
            issues.Add(Issue(
                "dimensions.trial-balance-filters-not-certified",
                AccountingProductionReadinessAreaDto.DimensionalAccounting,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Trial-balance dimension filters have not been certified.",
                "Prove period and aggregate trial-balance routes filter by canonical fund, entity, book, instrument, cost-center, counterparty, and external-GL dimensions."));
        }
        else if (!readiness.HasTrialBalanceDimensionFilterEvidence)
        {
            issues.Add(Issue(
                "dimensions.trial-balance-filters-evidence-missing",
                AccountingProductionReadinessAreaDto.DimensionalAccounting,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Trial-balance dimension filter certification lacks retained trial-balance evidence for the selected ledger book.",
                "Attach retained evidence naming the selected ledger book and trial-balance-filter, trial-balance-dimension, or ledger-report-filter checks.",
                readiness.EvidenceReferences));
        }

        if (!readiness.ReportPackageDimensionProvenanceCertified)
        {
            issues.Add(Issue(
                "dimensions.report-package-provenance-not-certified",
                AccountingProductionReadinessAreaDto.DimensionalAccounting,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Report-package dimensional provenance has not been certified.",
                "Prove report packages, NAV packages, and report-line provenance retain canonical dimensions from posted ledger lines through certification and export."));
        }
        else if (!readiness.HasReportPackageDimensionProvenanceEvidence)
        {
            issues.Add(Issue(
                "dimensions.report-package-provenance-evidence-missing",
                AccountingProductionReadinessAreaDto.DimensionalAccounting,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Report-package dimensional provenance certification lacks retained report-package evidence for the selected ledger book.",
                "Attach retained evidence naming the selected ledger book and report-package-provenance, report-line-provenance, package-dimension, or NAV-package checks.",
                readiness.EvidenceReferences));
        }

        return issues;
    }

    private void BuildJournalLifecycleComponent(
        ICollection<AccountingProductionReadinessComponentDto> components,
        AccountingLedgerBookWorkflowReadinessDto workflowReadiness)
    {
        var hasWorkbench = _services.GetService<IManualJournalEntryWorkbenchService>() is not null;
        var hasLifecycle = _services.GetService<IManualJournalEntryLifecycleService>() is not null;
        var issues = new List<AccountingProductionReadinessIssueDto>();
        if (!hasWorkbench || !hasLifecycle)
        {
            issues.Add(Issue("journal-lifecycle.service-missing", AccountingProductionReadinessAreaDto.JournalLifecycle, AccountingConfigurationValidationSeverityDto.Critical, "Manual journal workbench or lifecycle service is not registered.", "Register the governed manual journal lifecycle service before production rollout."));
        }

        AddJournalLifecycleWorkflowIssues(workflowReadiness, issues);

        components.Add(Component(
            AccountingProductionReadinessAreaDto.JournalLifecycle,
            "Journal lifecycle",
            ResolveIssueStatus(issues),
            ScoreFromIssues(issues, hasPositiveEvidence: hasWorkbench && hasLifecycle && workflowReadiness.HasJournalLifecycleLedgerBookNativeEvidence),
            hasWorkbench && hasLifecycle && workflowReadiness.JournalLifecycleLedgerBookNativeCertified && workflowReadiness.HasJournalLifecycleLedgerBookNativeEvidence
                ? "Governed manual journal services are registered and certified with retained ledger-book-native workflow evidence."
                : "Journal lifecycle services, ledger-book-native certification, or retained workflow evidence are incomplete.",
            issues,
            UiApiRoutes.LedgerManualJournalEntryWorkbench,
            workflowReadiness.EvidenceReferences));
    }

    private static void AddJournalLifecycleWorkflowIssues(
        AccountingLedgerBookWorkflowReadinessDto readiness,
        ICollection<AccountingProductionReadinessIssueDto> issues)
    {
        if (!readiness.HasLedgerBookScope)
        {
            issues.Add(Issue(
                "journal-lifecycle.ledger-book-scope-missing",
                AccountingProductionReadinessAreaDto.JournalLifecycle,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Journal lifecycle production readiness is not scoped to a Meridian ledger book.",
                "Select the target ledger book before certifying journal draft, approval, posting, correction, and close-lock transitions."));
            return;
        }

        if (!readiness.JournalLifecycleLedgerBookNativeCertified)
        {
            issues.Add(Issue(
                "journal-lifecycle.ledger-book-native-not-certified",
                AccountingProductionReadinessAreaDto.JournalLifecycle,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Journal lifecycle has not been certified as ledger-book-native.",
                "Prove draft, validation, submission, approval, posting, correction, close-lock, and audit transitions remain inside the selected ledger book."));
        }
        else if (!readiness.HasJournalLifecycleLedgerBookNativeEvidence)
        {
            issues.Add(Issue(
                "journal-lifecycle.ledger-book-native-evidence-missing",
                AccountingProductionReadinessAreaDto.JournalLifecycle,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Journal lifecycle certification lacks retained workflow evidence for the selected ledger book.",
                "Attach retained evidence naming the selected ledger book and journal-entry lifecycle workflow.",
                readiness.EvidenceReferences));
        }
    }

    private void BuildCloseReportingComponent(
        ICollection<AccountingProductionReadinessComponentDto> components,
        AccountingLedgerBookWorkflowReadinessDto workflowReadiness,
        AccountingDimensionalReportingReadinessDto dimensionalReportingReadiness)
    {
        var hasClose = _services.GetService<IAccountingCloseManagementService>() is not null;
        var hasReports = _services.GetService<IAccountingReportPackageService>() is not null;
        var issues = new List<AccountingProductionReadinessIssueDto>();
        if (!hasClose)
        {
            issues.Add(Issue("close-management.service-missing", AccountingProductionReadinessAreaDto.CloseReporting, AccountingConfigurationValidationSeverityDto.Critical, "Accounting close management service is not registered.", "Register close management before production close rollout."));
        }

        if (!hasReports)
        {
            issues.Add(Issue("reporting.service-missing", AccountingProductionReadinessAreaDto.CloseReporting, AccountingConfigurationValidationSeverityDto.Critical, "Accounting report package service is not registered.", "Register accounting report package certification before production reporting rollout."));
        }

        AddCloseReportingWorkflowIssues(workflowReadiness, issues);
        AddCloseReportingDimensionalIssues(dimensionalReportingReadiness, issues);

        components.Add(Component(
            AccountingProductionReadinessAreaDto.CloseReporting,
            "Close and reporting",
            ResolveIssueStatus(issues),
            ScoreFromIssues(issues, hasPositiveEvidence: hasClose && hasReports && workflowReadiness.HasCloseReportingLedgerBookNativeEvidence && dimensionalReportingReadiness.CompletedControlCount == dimensionalReportingReadiness.RequiredControlCount),
            hasClose && hasReports &&
            workflowReadiness.CloseReportingLedgerBookNativeCertified &&
            workflowReadiness.HasCloseReportingLedgerBookNativeEvidence &&
            dimensionalReportingReadiness.CompletedControlCount == dimensionalReportingReadiness.RequiredControlCount
                ? "Close management and accounting report package services are registered and certified with retained ledger-book-native workflow and dimensional reporting evidence."
                : "Close/reporting services, ledger-book-native certification, retained workflow evidence, or dimensional reporting evidence are incomplete.",
            issues,
            UiApiRoutes.LedgerReportsAccountingPackage,
            workflowReadiness.EvidenceReferences.Concat(dimensionalReportingReadiness.EvidenceReferences).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
    }

    private static void AddCloseReportingWorkflowIssues(
        AccountingLedgerBookWorkflowReadinessDto readiness,
        ICollection<AccountingProductionReadinessIssueDto> issues)
    {
        if (!readiness.HasLedgerBookScope)
        {
            issues.Add(Issue(
                "close-reporting.ledger-book-scope-missing",
                AccountingProductionReadinessAreaDto.CloseReporting,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Close/reporting production readiness is not scoped to a Meridian ledger book.",
                "Select the target ledger book before certifying close plans, period locks, reporting packages, certifications, and restatements."));
            return;
        }

        if (!readiness.CloseReportingLedgerBookNativeCertified)
        {
            issues.Add(Issue(
                "close-reporting.ledger-book-native-not-certified",
                AccountingProductionReadinessAreaDto.CloseReporting,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Close and reporting workflows have not been certified as ledger-book-native.",
                "Prove close checklists, period locks, report packages, certification, and restatement workflows consume only the selected ledger book and its dimensions."));
        }
        else if (!readiness.HasCloseReportingLedgerBookNativeEvidence)
        {
            issues.Add(Issue(
                "close-reporting.ledger-book-native-evidence-missing",
                AccountingProductionReadinessAreaDto.CloseReporting,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Close/reporting certification lacks retained workflow evidence for the selected ledger book.",
                "Attach retained evidence naming the selected ledger book and close-management, report-package, or restatement workflow.",
                readiness.EvidenceReferences));
        }
    }

    private static void AddCloseReportingDimensionalIssues(
        AccountingDimensionalReportingReadinessDto readiness,
        ICollection<AccountingProductionReadinessIssueDto> issues)
    {
        if (!readiness.HasLedgerBookScope)
        {
            issues.Add(Issue(
                "close-reporting.dimension-ledger-book-scope-missing",
                AccountingProductionReadinessAreaDto.CloseReporting,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Close/reporting dimensional readiness is not scoped to a Meridian ledger book.",
                "Select the target ledger book before certifying period reports, cross-period reports, journal filters, and export dimension mappings."));
            return;
        }

        if (readiness.CompletedControlCount < readiness.RequiredControlCount)
        {
            issues.Add(Issue(
                "close-reporting.dimension-controls-incomplete",
                AccountingProductionReadinessAreaDto.CloseReporting,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Close/reporting dimensional report, query, or export controls are incomplete for the selected ledger book.",
                "Certify period reports, cross-period reports, journal dimension filters, and external-export dimension mappings with retained ledger-book-scoped evidence before production close/reporting rollout.",
                readiness.EvidenceReferences));
        }
    }

    private async Task<ExternalGlCounts> BuildExternalGlComponentAsync(
        AccountingProductionReadinessRequestDto request,
        string fundProfileId,
        AccountingLedgerBookWorkflowReadinessDto workflowReadiness,
        ICollection<AccountingProductionReadinessComponentDto> components,
        CancellationToken ct)
    {
        var service = _services.GetService<AccountingSystemIntegrationService>();
        if (service is null)
        {
            var serviceIssues = new List<AccountingProductionReadinessIssueDto>
            {
                Issue("external-gl.service-missing", AccountingProductionReadinessAreaDto.ExternalGl, AccountingConfigurationValidationSeverityDto.Critical, "Accounting-system integration service is not registered.", "Register guarded external-GL import/mapping services before production rollout.")
            };
            AddExternalGlWorkflowIssues(workflowReadiness, serviceIssues);

            components.Add(Component(
                AccountingProductionReadinessAreaDto.ExternalGl,
                "External GL",
                AccountingProductionReadinessStatusDto.Unavailable,
                0,
                "No accounting-system integration service is registered.",
                serviceIssues,
                UiApiRoutes.AccountingSystemProviders,
                workflowReadiness.EvidenceReferences));
            return new ExternalGlCounts(0, 0, false);
        }

        var providers = await service.ListProvidersAsync(ct).ConfigureAwait(false);
        var mappings = await service.ListMappingProfilesAsync(request.ProviderId, fundProfileId, request.LedgerBookId, ct).ConfigureAwait(false);
        var certifiedMappings = mappings.Count(static profile => profile.CertificationState == AccountingCertificationStateDto.Certified);
        var livePostingEnabled = providers.Any(static provider =>
            provider.State == AccountingSystemProviderStateDto.Available && provider.SupportsPosting);
        var issues = new List<AccountingProductionReadinessIssueDto>();
        if (providers.Count == 0)
        {
            issues.Add(Issue("external-gl.providers-missing", AccountingProductionReadinessAreaDto.ExternalGl, AccountingConfigurationValidationSeverityDto.Warning, "No external-GL providers are available.", "Register import-first provider fixtures or credentialed read-only adapters."));
        }

        if (!request.LedgerBookId.HasValue)
        {
            issues.Add(Issue("external-gl.ledger-book-scope-missing", AccountingProductionReadinessAreaDto.ExternalGl, AccountingConfigurationValidationSeverityDto.Critical, "External GL production readiness is not scoped to a Meridian ledger book.", "Select the target ledger book so import evidence, reconciliation, mapping profiles, and guarded export packages are certified against the same book."));
        }

        if (certifiedMappings == 0)
        {
            issues.Add(Issue("external-gl.certified-mapping-missing", AccountingProductionReadinessAreaDto.ExternalGl, AccountingConfigurationValidationSeverityDto.Critical, "No certified external-GL mapping profile exists for this scope.", "Certify account and dimension mappings before guarded export review."));
        }

        if (livePostingEnabled)
        {
            var postingProviderEvidence = providers
                .Where(static provider => provider.State == AccountingSystemProviderStateDto.Available && provider.SupportsPosting)
                .Select(static provider => $"external-gl-provider:{provider.ProviderId}")
                .ToArray();
            issues.Add(Issue(
                "external-gl.live-posting-enabled",
                AccountingProductionReadinessAreaDto.ExternalGl,
                AccountingConfigurationValidationSeverityDto.Critical,
                "An available external GL provider reports live posting support.",
                "Disable live posting and keep the provider in import, reconciliation, and guarded-export mode until a separately approved posting adapter and release gate exists.",
                postingProviderEvidence));
        }
        else
        {
            issues.Add(Issue("external-gl.live-posting-disabled", AccountingProductionReadinessAreaDto.ExternalGl, AccountingConfigurationValidationSeverityDto.Info, "Live external GL posting remains disabled by product policy.", "Use import, reconciliation, and guarded export artifacts until a separately approved live-posting adapter exists."));
        }

        AddExternalGlWorkflowIssues(workflowReadiness, issues);

        components.Add(Component(
            AccountingProductionReadinessAreaDto.ExternalGl,
            "External GL",
            ResolveIssueStatus(issues),
            ScoreFromIssues(issues, hasPositiveEvidence: certifiedMappings > 0 || workflowReadiness.HasExternalGlLedgerBookNativeEvidence),
            $"{providers.Count} provider row(s), {mappings.Count} mapping profile(s), {certifiedMappings} certified profile(s); ledger book {(request.LedgerBookId.HasValue ? request.LedgerBookId.Value.ToString("D") : "missing")}; external-GL workflow {(workflowReadiness.ExternalGlLedgerBookNativeCertified ? "certified" : "not certified")}; live posting {(livePostingEnabled ? "enabled" : "disabled")}.",
            issues,
            UiApiRoutes.AccountingSystemMappingProfiles,
            workflowReadiness.EvidenceReferences));

        return new ExternalGlCounts(providers.Count, certifiedMappings, livePostingEnabled);
    }

    private static void AddExternalGlWorkflowIssues(
        AccountingLedgerBookWorkflowReadinessDto readiness,
        ICollection<AccountingProductionReadinessIssueDto> issues)
    {
        if (!readiness.HasLedgerBookScope)
        {
            issues.Add(Issue(
                "external-gl.ledger-book-workflow-scope-missing",
                AccountingProductionReadinessAreaDto.ExternalGl,
                AccountingConfigurationValidationSeverityDto.Critical,
                "External GL workflow readiness is not scoped to a Meridian ledger book.",
                "Select the target ledger book before certifying import, reconciliation, mapping, and guarded export workflows."));
            return;
        }

        if (!readiness.ExternalGlLedgerBookNativeCertified)
        {
            issues.Add(Issue(
                "external-gl.ledger-book-native-not-certified",
                AccountingProductionReadinessAreaDto.ExternalGl,
                AccountingConfigurationValidationSeverityDto.Critical,
                "External GL workflows have not been certified as ledger-book-native.",
                "Prove provider imports, reconciliation snapshots, mapping profiles, and guarded export packages are bound to the selected Meridian ledger book."));
        }
        else if (!readiness.HasExternalGlLedgerBookNativeEvidence)
        {
            issues.Add(Issue(
                "external-gl.ledger-book-native-evidence-missing",
                AccountingProductionReadinessAreaDto.ExternalGl,
                AccountingConfigurationValidationSeverityDto.Critical,
                "External GL certification lacks retained external-GL workflow evidence for the selected ledger book.",
                "Attach retained evidence naming the selected ledger book and external-GL import, reconciliation, mapping, or guarded-export workflow.",
                readiness.EvidenceReferences));
        }
    }

    private static AccountingTenantAdministrationReadinessDto BuildTenantAdministrationComponent(
        AccountingProductionReadinessRequestDto request,
        ICollection<AccountingProductionReadinessComponentDto> components)
    {
        var evidenceReferences = request.TenantAdministrationEvidenceLinks
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var readiness = new AccountingTenantAdministrationReadinessDto(
            TrimOrNull(request.TenantId),
            TrimOrNull(request.CompanyId),
            request.TenantScopeConfigured,
            request.AdminRoleProfileConfigured,
            request.ScopedAccessPoliciesConfigured,
            request.ReportingGroupsConfigured,
            request.AccountingAdminSurfaceConfigured,
            request.BrowserAccountingAdminSurfaceConfigured,
            request.WpfAccountingAdminSurfaceConfigured,
            request.ChartAdministrationStudioConfigured,
            request.RuleTestPromotionStudioConfigured,
            request.CloseSetupStudioConfigured,
            request.ProviderMappingStudioConfigured,
            request.TenantCompanyReportGroupSetupStudioConfigured,
            evidenceReferences,
            AuditReviewToolingConfigured: request.AuditReviewToolingConfigured,
            BulkImportExportSafeguardsConfigured: request.BulkImportExportSafeguardsConfigured,
            PerformanceValidationConfigured: request.PerformanceValidationConfigured,
            DisasterRecoveryRunbookConfigured: request.DisasterRecoveryRunbookConfigured,
            LedgerBookAdministrationStudioConfigured: request.LedgerBookAdministrationStudioConfigured,
            PostingRuleAuthoringStudioConfigured: request.PostingRuleAuthoringStudioConfigured,
            ApprovalQueueStudioConfigured: request.ApprovalQueueStudioConfigured,
            DimensionMappingStudioConfigured: request.DimensionMappingStudioConfigured,
            ImplementationSandboxConfigured: request.ImplementationSandboxConfigured);
        var issues = new List<AccountingProductionReadinessIssueDto>();
        if (!readiness.HasTenantScope)
        {
            issues.Add(Issue(
                "tenant-admin.tenant-scope-missing",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Accounting production readiness is not scoped to a tenant.",
                "Resolve tenant scope from the authenticated workstation session or provide the target tenant id before rollout certification.",
                evidenceReferences));
        }

        if (!readiness.HasCompanyScope)
        {
            issues.Add(Issue(
                "tenant-admin.company-scope-missing",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Accounting production readiness is not scoped to a company.",
                "Bind the rollout to a company principal before enabling production accounting workflows.",
                evidenceReferences));
        }

        if (!readiness.TenantScopeConfigured)
        {
            issues.Add(Issue(
                "tenant-admin.tenant-scope-not-certified",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Tenant-scoped accounting configuration has not been certified.",
                "Certify tenant-scoped ledger, provider, evidence, and workstation storage setup before production rollout.",
                evidenceReferences));
        }
        else if (!readiness.HasTenantScopeEvidence)
        {
            issues.Add(Issue(
                "tenant-admin.tenant-scope-evidence-missing",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Tenant-scoped accounting configuration certification lacks retained tenant-scope evidence.",
                "Attach tenant administration evidence for tenant scope, tenant storage, tenant ledger, or provider setup before production rollout.",
                evidenceReferences));
        }

        if (!readiness.AdminRoleProfileConfigured)
        {
            issues.Add(Issue(
                "tenant-admin.role-profile-not-certified",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Accounting administrator role profile setup has not been certified.",
                "Configure and retain approval evidence for accounting administrator role profiles before production rollout.",
                evidenceReferences));
        }
        else if (!readiness.HasAdminRoleProfileEvidence)
        {
            issues.Add(Issue(
                "tenant-admin.role-profile-evidence-missing",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Accounting administrator role profile certification lacks retained role-profile evidence.",
                "Attach tenant administration evidence for admin role or accounting role-profile setup before production rollout.",
                evidenceReferences));
        }

        if (!readiness.ScopedAccessPoliciesConfigured)
        {
            issues.Add(Issue(
                "tenant-admin.scoped-access-not-certified",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Scoped access policies for accounting workflows have not been certified.",
                "Configure fund, entity, account, and report-package scoped access before production rollout.",
                evidenceReferences));
        }
        else if (!readiness.HasScopedAccessPolicyEvidence)
        {
            issues.Add(Issue(
                "tenant-admin.scoped-access-evidence-missing",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Scoped access policy certification lacks retained access-policy evidence.",
                "Attach tenant administration evidence for scoped access policies, accounting entitlements, or report-package access before production rollout.",
                evidenceReferences));
        }

        if (!readiness.ReportingGroupsConfigured)
        {
            issues.Add(Issue(
                "tenant-admin.reporting-groups-not-certified",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Reporting delivery groups have not been certified for accounting outputs.",
                "Retain reporting group and entitlement setup evidence before investor, board, tax, or compliance delivery.",
                evidenceReferences));
        }
        else if (!readiness.HasReportingGroupEvidence)
        {
            issues.Add(Issue(
                "tenant-admin.reporting-groups-evidence-missing",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Reporting delivery group certification lacks retained reporting-group evidence.",
                "Attach tenant administration evidence for reporting groups, delivery groups, or report-group entitlements before production reporting delivery.",
                evidenceReferences));
        }

        if (!readiness.AccountingAdminSurfaceConfigured)
        {
            issues.Add(Issue(
                "tenant-admin.operator-surface-required",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Production rollout still needs a full tenant/company/report-group setup operator surface over these shared controls.",
                "Bind browser and WPF admin setup screens to this shared readiness contract instead of local setup heuristics.",
                evidenceReferences));
        }
        else if (!readiness.HasAccountingAdminSurfaceEvidence)
        {
            issues.Add(Issue(
                "tenant-admin.operator-surface-evidence-missing",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Accounting administration operator-surface certification lacks retained setup-surface evidence.",
                "Attach tenant administration evidence for the aggregate accounting admin, operator, or setup surface before production rollout.",
                evidenceReferences));
        }

        if (!readiness.BrowserAccountingAdminSurfaceConfigured)
        {
            issues.Add(Issue(
                "tenant-admin.browser-admin-studio-required",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Browser accounting administration studio coverage has not been certified.",
                "Bind the browser accounting configuration, Rules Studio, migration, tenant setup, and production-readiness controls to this shared contract before production rollout.",
                evidenceReferences));
        }
        else if (!readiness.HasBrowserAccountingAdminSurfaceEvidence)
        {
            issues.Add(Issue(
                "tenant-admin.browser-admin-studio-evidence-missing",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Browser accounting administration studio certification lacks retained browser setup evidence.",
                "Attach tenant administration evidence for browser accounting admin-studio or browser setup coverage before production rollout.",
                evidenceReferences));
        }

        if (!readiness.WpfAccountingAdminSurfaceConfigured)
        {
            issues.Add(Issue(
                "tenant-admin.wpf-admin-studio-required",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "WPF accounting administration studio coverage has not been certified.",
                "Bind the WPF accounting configuration, Rules Studio, migration, tenant setup, and production-readiness controls to this shared contract before production rollout.",
                evidenceReferences));
        }
        else if (!readiness.HasWpfAccountingAdminSurfaceEvidence)
        {
            issues.Add(Issue(
                "tenant-admin.wpf-admin-studio-evidence-missing",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "WPF accounting administration studio certification lacks retained WPF setup evidence.",
                "Attach tenant administration evidence for WPF accounting admin-studio or desktop setup coverage before production rollout.",
                evidenceReferences));
        }

        if (!readiness.ChartAdministrationStudioConfigured)
        {
            issues.Add(Issue(
                "tenant-admin.chart-administration-studio-required",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Enterprise chart administration coverage has not been certified.",
                "Bind chart-of-accounts, ledger-book chart, account template, and activation controls into the shared accounting configuration studio before production rollout.",
                evidenceReferences));
        }
        else if (!readiness.HasChartAdministrationStudioEvidence)
        {
            issues.Add(Issue(
                "tenant-admin.chart-administration-studio-evidence-missing",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Chart administration studio certification lacks retained chart setup evidence.",
                "Attach tenant administration evidence for chart administration, ledger-book chart, or chart-of-accounts setup.",
                evidenceReferences));
        }

        if (!readiness.RuleTestPromotionStudioConfigured)
        {
            issues.Add(Issue(
                "tenant-admin.rule-test-promotion-studio-required",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Rules Studio test and promotion queue coverage has not been certified.",
                "Bind rule authoring, saved test cases, dry-run preview, generated postings, and promotion approvals into the shared enterprise accounting configuration studio.",
                evidenceReferences));
        }
        else if (!readiness.HasRuleTestPromotionStudioEvidence)
        {
            issues.Add(Issue(
                "tenant-admin.rule-test-promotion-studio-evidence-missing",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Rules Studio test and promotion queue certification lacks retained rule setup evidence.",
                "Attach tenant administration evidence for Rules Studio, rule tests, generated-posting preview, or promotion queues.",
                evidenceReferences));
        }

        if (!readiness.CloseSetupStudioConfigured)
        {
            issues.Add(Issue(
                "tenant-admin.close-setup-studio-required",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Close setup studio coverage has not been certified.",
                "Bind close checklist, dependency graph, sign-off matrix, materiality policy, late-adjustment, period-lock, and close-calendar setup into the shared configuration studio.",
                evidenceReferences));
        }
        else if (!readiness.HasCloseSetupStudioEvidence)
        {
            issues.Add(Issue(
                "tenant-admin.close-setup-studio-evidence-missing",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Close setup studio certification lacks retained close setup evidence.",
                "Attach tenant administration evidence for close setup, close checklist, close calendar, or materiality policy setup.",
                evidenceReferences));
        }

        if (!readiness.ProviderMappingStudioConfigured)
        {
            issues.Add(Issue(
                "tenant-admin.provider-mapping-studio-required",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Provider and external-GL mapping setup coverage has not been certified.",
                "Bind QuickBooks, Xero, NetSuite, dimension mapping, import evidence, reconciliation, and guarded export setup into the shared enterprise configuration studio.",
                evidenceReferences));
        }
        else if (!readiness.HasProviderMappingStudioEvidence)
        {
            issues.Add(Issue(
                "tenant-admin.provider-mapping-studio-evidence-missing",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Provider mapping studio certification lacks retained provider or external-GL setup evidence.",
                "Attach tenant administration evidence for provider mapping, external-GL mapping, or mapping-profile setup.",
                evidenceReferences));
        }

        if (!readiness.TenantCompanyReportGroupSetupStudioConfigured)
        {
            issues.Add(Issue(
                "tenant-admin.tenant-company-report-group-studio-required",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Tenant, company, and report-group setup studio coverage has not been certified.",
                "Bind tenant/company selection, scoped access, admin roles, reporting groups, and delivery group setup into the shared enterprise accounting configuration studio.",
                evidenceReferences));
        }
        else if (!readiness.HasTenantCompanyReportGroupSetupStudioEvidence)
        {
            issues.Add(Issue(
                "tenant-admin.tenant-company-report-group-studio-evidence-missing",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Tenant, company, and report-group setup studio certification lacks retained setup evidence.",
                "Attach tenant administration evidence for tenant/company setup, report-group setup, or company report-group controls.",
                evidenceReferences));
        }

        if (!readiness.AuditReviewToolingConfigured)
        {
            issues.Add(Issue(
                "tenant-admin.audit-review-tooling-required",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Audit review tooling coverage has not been certified.",
                "Bind audit review, evidence review, transition audit, and exception-review tooling into the shared accounting administration surface before production rollout.",
                evidenceReferences));
        }
        else if (!readiness.HasAuditReviewToolingEvidence)
        {
            issues.Add(Issue(
                "tenant-admin.audit-review-tooling-evidence-missing",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Audit review tooling certification lacks retained audit-review evidence.",
                "Attach tenant administration evidence for audit review, evidence review, audit workbench, or transition-audit tooling.",
                evidenceReferences));
        }

        if (!readiness.BulkImportExportSafeguardsConfigured)
        {
            issues.Add(Issue(
                "tenant-admin.bulk-import-export-safeguards-required",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Bulk import/export safeguards have not been certified.",
                "Bind bulk import, guarded export, reconciliation, preview, approval, and rollback safeguards into tenant administration before production rollout.",
                evidenceReferences));
        }
        else if (!readiness.HasBulkImportExportSafeguardsEvidence)
        {
            issues.Add(Issue(
                "tenant-admin.bulk-import-export-safeguards-evidence-missing",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Bulk import/export safeguard certification lacks retained safeguard evidence.",
                "Attach tenant administration evidence for bulk import/export, guarded export, import preview, rollback, or reconciliation safeguards.",
                evidenceReferences));
        }

        if (!readiness.PerformanceValidationConfigured)
        {
            issues.Add(Issue(
                "tenant-admin.performance-validation-required",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Accounting performance validation has not been certified.",
                "Retain performance, load, capacity, or query-latency validation for accounting configuration, ledger queries, close, reports, and exports before rollout.",
                evidenceReferences));
        }
        else if (!readiness.HasPerformanceValidationEvidence)
        {
            issues.Add(Issue(
                "tenant-admin.performance-validation-evidence-missing",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Performance validation certification lacks retained performance evidence.",
                "Attach tenant administration evidence for performance validation, load tests, capacity tests, or production query-latency validation.",
                evidenceReferences));
        }

        if (!readiness.DisasterRecoveryRunbookConfigured)
        {
            issues.Add(Issue(
                "tenant-admin.disaster-recovery-runbook-required",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Disaster recovery and operating runbooks have not been certified.",
                "Retain disaster-recovery, restore, replay, escalation, and accounting operating-runbook evidence before production rollout.",
                evidenceReferences));
        }
        else if (!readiness.HasDisasterRecoveryRunbookEvidence)
        {
            issues.Add(Issue(
                "tenant-admin.disaster-recovery-runbook-evidence-missing",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Disaster recovery runbook certification lacks retained runbook evidence.",
                "Attach tenant administration evidence for disaster recovery, operating runbooks, restore validation, replay validation, or escalation procedures.",
                evidenceReferences));
        }

        if (!readiness.LedgerBookAdministrationStudioConfigured)
        {
            issues.Add(Issue(
                "tenant-admin.ledger-book-administration-studio-required",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Ledger-book administration studio coverage has not been certified.",
                "Bind ledger-book creation, selection, basis/currency policy, scope, activation, and period setup into the shared enterprise configuration studio.",
                evidenceReferences));
        }
        else if (!readiness.HasLedgerBookAdministrationStudioEvidence)
        {
            issues.Add(Issue(
                "tenant-admin.ledger-book-administration-studio-evidence-missing",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Ledger-book administration studio certification lacks retained book-administration evidence.",
                "Attach tenant administration evidence for ledger-book administration, book setup, book activation, or ledger-book period setup.",
                evidenceReferences));
        }

        if (!readiness.PostingRuleAuthoringStudioConfigured)
        {
            issues.Add(Issue(
                "tenant-admin.posting-rule-authoring-studio-required",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Posting-rule authoring studio coverage has not been certified.",
                "Bind event predicates, scopes, thresholds, generated multi-line postings, formulas, allocations, and dry-run previews into the shared accounting configuration studio.",
                evidenceReferences));
        }
        else if (!readiness.HasPostingRuleAuthoringStudioEvidence)
        {
            issues.Add(Issue(
                "tenant-admin.posting-rule-authoring-studio-evidence-missing",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Posting-rule authoring studio certification lacks retained rule-authoring evidence.",
                "Attach tenant administration evidence for posting-rule authoring, rule setup, generated-posting setup, or dry-run authoring.",
                evidenceReferences));
        }

        if (!readiness.ApprovalQueueStudioConfigured)
        {
            issues.Add(Issue(
                "tenant-admin.approval-queue-studio-required",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Approval queue studio coverage has not been certified.",
                "Bind rule promotion approvals, journal approvals, configuration approvals, segregation checks, and retained approval evidence into the shared accounting configuration studio.",
                evidenceReferences));
        }
        else if (!readiness.HasApprovalQueueStudioEvidence)
        {
            issues.Add(Issue(
                "tenant-admin.approval-queue-studio-evidence-missing",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Approval queue studio certification lacks retained approval-queue evidence.",
                "Attach tenant administration evidence for approval queues, promotion approvals, journal approvals, or configuration approvals.",
                evidenceReferences));
        }

        if (!readiness.DimensionMappingStudioConfigured)
        {
            issues.Add(Issue(
                "tenant-admin.dimension-mapping-studio-required",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Dimension mapping studio coverage has not been certified.",
                "Bind fund, entity, sleeve, strategy, investor, capital account, instrument, tax lot, cost center, counterparty, and external-GL dimension mapping setup into the shared configuration studio.",
                evidenceReferences));
        }
        else if (!readiness.HasDimensionMappingStudioEvidence)
        {
            issues.Add(Issue(
                "tenant-admin.dimension-mapping-studio-evidence-missing",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Dimension mapping studio certification lacks retained dimension-mapping evidence.",
                "Attach tenant administration evidence for canonical dimension mapping, external dimension mapping, or GL dimension mapping.",
                evidenceReferences));
        }

        if (!readiness.ImplementationSandboxConfigured)
        {
            issues.Add(Issue(
                "tenant-admin.implementation-sandbox-required",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Implementation sandbox and fixture validation have not been certified.",
                "Retain sandbox, fixture, migration rehearsal, provider import, rule dry-run, and close/report package validation evidence before production rollout.",
                evidenceReferences));
        }
        else if (!readiness.HasImplementationSandboxEvidence)
        {
            issues.Add(Issue(
                "tenant-admin.implementation-sandbox-evidence-missing",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Implementation sandbox certification lacks retained sandbox validation evidence.",
                "Attach tenant administration evidence for implementation sandbox validation, fixture validation, migration rehearsal, or provider-import rehearsal.",
                evidenceReferences));
        }

        if (!readiness.HasRetainedEvidence)
        {
            issues.Add(Issue(
                "tenant-admin.evidence-missing",
                AccountingProductionReadinessAreaDto.TenantAdministration,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Tenant administration setup has no retained evidence links.",
                "Attach retained tenant, company, role-profile, scoped-access, and report-group setup evidence before production certification."));
        }

        components.Add(Component(
            AccountingProductionReadinessAreaDto.TenantAdministration,
            "Tenant administration",
            ResolveIssueStatus(issues),
            ScoreFromIssues(issues, hasPositiveEvidence: readiness.HasRetainedEvidence),
            $"{readiness.CompletedControlCount}/{readiness.RequiredControlCount} tenant administration control(s) complete; {evidenceReferences.Length} retained evidence link(s).",
            issues,
            route: UiApiRoutes.AccountingSystemProductionReadiness,
            evidenceReferences: evidenceReferences));
        return readiness;
    }

    private static void BuildMigrationRolloutComponent(
        AccountingProductionReadinessRequestDto request,
        ICollection<AccountingProductionReadinessComponentDto> components)
    {
        var issues = new List<AccountingProductionReadinessIssueDto>();
        AddMigrationRolloutScopeIssues(request, issues);
        AddMigrationControlIssues(
            request,
            issues,
            request.LedgerBookMigrationCertified,
            AccountingMigrationRunKindDto.LedgerBookScope,
            "migration.ledger-book-scope-not-certified",
            "Ledger-book migration scope has not been certified for production rollout.",
            "Certify ledger-book scoping and historical fund-level compatibility paths before production cutover.",
            AccountingConfigurationValidationSeverityDto.Critical);
        AddMigrationControlIssues(
            request,
            issues,
            request.HistoricalJournalBackfillCertified,
            AccountingMigrationRunKindDto.HistoricalJournalBackfill,
            "migration.historical-journal-backfill-not-certified",
            "Historical journal backfill has not been certified.",
            "Run and retain historical journal backfill evidence before certifying ledger-book-native accounting.",
            AccountingConfigurationValidationSeverityDto.Critical);
        AddMigrationControlIssues(
            request,
            issues,
            request.DimensionalBackfillCertified,
            AccountingMigrationRunKindDto.DimensionalBackfill,
            "migration.dimensional-backfill-not-certified",
            "Dimensional backfill has not been certified across retained journal lines and report inputs.",
            "Backfill and verify fund, entity, sleeve, strategy, investor, capital account, instrument, tax lot, cost center, counterparty, and external-GL dimensions before production reporting certification.",
            AccountingConfigurationValidationSeverityDto.Critical);
        AddMigrationControlIssues(
            request,
            issues,
            request.AccountingConfigurationPromotionCertified,
            AccountingMigrationRunKindDto.AccountingConfigurationPromotion,
            "migration.configuration-promotion-not-certified",
            "Accounting configuration promotion evidence has not been certified.",
            "Retain promotion evidence for chart, rule, policy, and approval-state migration before rollout.",
            AccountingConfigurationValidationSeverityDto.Warning);
        AddMigrationControlIssues(
            request,
            issues,
            request.CloseReportingEvidenceMigrationCertified,
            AccountingMigrationRunKindDto.CloseReportingEvidence,
            "migration.close-reporting-evidence-not-certified",
            "Close and reporting evidence migration has not been certified.",
            "Retain close checklist, report package, certification, and restatement evidence migration proof before production close.",
            AccountingConfigurationValidationSeverityDto.Warning);

        var certifiedCount = new[]
        {
            request.LedgerBookMigrationCertified,
            request.HistoricalJournalBackfillCertified,
            request.DimensionalBackfillCertified,
            request.AccountingConfigurationPromotionCertified,
            request.CloseReportingEvidenceMigrationCertified
        }.Count(static certified => certified);
        var evidenceReferences = request.MigrationRunArtifacts
            .SelectMany(static artifact => artifact.EvidenceReferences)
            .Concat(request.MigrationEvidenceLinks)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var certifiedArtifactCount = request.MigrationRunArtifacts.Count(static artifact =>
            artifact.Status == AccountingMigrationRunStatusDto.Certified);

        components.Add(Component(
            AccountingProductionReadinessAreaDto.MigrationRollout,
            "Migration rollout",
            ResolveIssueStatus(issues),
            ScoreFromIssues(issues, hasPositiveEvidence: evidenceReferences.Length > 0),
            $"{certifiedCount}/5 migration control(s) certified; {certifiedArtifactCount} retained certified migration run artifact(s); {evidenceReferences.Length} retained evidence link(s).",
            issues,
            route: UiApiRoutes.AccountingSystemProductionReadiness,
            evidenceReferences: evidenceReferences));
    }

    private static void AddMigrationRolloutScopeIssues(
        AccountingProductionReadinessRequestDto request,
        ICollection<AccountingProductionReadinessIssueDto> issues)
    {
        var evidenceReferences = request.MigrationRunArtifacts
            .SelectMany(static artifact => artifact.EvidenceReferences)
            .Concat(request.MigrationEvidenceLinks)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            issues.Add(Issue(
                "migration.tenant-scope-missing",
                AccountingProductionReadinessAreaDto.MigrationRollout,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Migration rollout is not scoped to a tenant.",
                "Select the target tenant before certifying ledger-book migration, historical backfill, dimensional backfill, configuration promotion, or close/reporting evidence migration.",
                evidenceReferences));
        }

        if (string.IsNullOrWhiteSpace(request.CompanyId))
        {
            issues.Add(Issue(
                "migration.company-scope-missing",
                AccountingProductionReadinessAreaDto.MigrationRollout,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Migration rollout is not scoped to a company.",
                "Select the target company before using retained migration run evidence for production accounting rollout.",
                evidenceReferences));
        }
    }

    private static void AddMigrationControlIssues(
        AccountingProductionReadinessRequestDto request,
        ICollection<AccountingProductionReadinessIssueDto> issues,
        bool certified,
        AccountingMigrationRunKindDto kind,
        string notCertifiedCode,
        string notCertifiedMessage,
        string suggestedAction,
        AccountingConfigurationValidationSeverityDto notCertifiedSeverity)
    {
        var artifacts = request.MigrationRunArtifacts
            .Where(artifact => artifact.Kind == kind)
            .ToArray();
        var scopedArtifacts = artifacts
            .Where(artifact => IsMigrationArtifactInScope(request, artifact))
            .ToArray();
        var evidenceReferences = artifacts
            .SelectMany(static artifact => artifact.EvidenceReferences)
            .Concat(request.MigrationEvidenceLinks)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var scopedEvidenceReferences = scopedArtifacts
            .SelectMany(static artifact => artifact.EvidenceReferences)
            .Concat(request.MigrationEvidenceLinks)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (artifacts.Any(artifact => !IsMigrationArtifactInScope(request, artifact)))
        {
            issues.Add(Issue(
                $"migration.{MigrationKindCode(kind)}-artifact-scope-mismatch",
                AccountingProductionReadinessAreaDto.MigrationRollout,
                AccountingConfigurationValidationSeverityDto.Critical,
                $"{MigrationKindLabel(kind)} has retained migration run artifacts outside the requested fund or ledger-book rollout scope.",
                "Attach a retained migration run artifact scoped to the requested fund and ledger book before production rollout certification.",
                evidenceReferences));
        }

        if (!certified)
        {
            issues.Add(Issue(
                notCertifiedCode,
                AccountingProductionReadinessAreaDto.MigrationRollout,
                notCertifiedSeverity,
                notCertifiedMessage,
                suggestedAction,
                evidenceReferences));
        }

        if (scopedArtifacts.Any(static artifact => artifact.Status == AccountingMigrationRunStatusDto.Failed))
        {
            issues.Add(Issue(
                $"migration.{MigrationKindCode(kind)}-run-failed",
                AccountingProductionReadinessAreaDto.MigrationRollout,
                AccountingConfigurationValidationSeverityDto.Critical,
                $"{MigrationKindLabel(kind)} has a retained failed migration run artifact.",
                "Resolve the failed migration run and retain a certified rerun before production rollout.",
                scopedEvidenceReferences));
        }

        if (kind == AccountingMigrationRunKindDto.DimensionalBackfill)
        {
            AddDimensionalBackfillScopeIssues(request, issues, scopedArtifacts, scopedEvidenceReferences);
        }

        if (certified && scopedArtifacts.All(static artifact => artifact.Status != AccountingMigrationRunStatusDto.Certified))
        {
            issues.Add(Issue(
                $"migration.{MigrationKindCode(kind)}-certified-run-missing",
                AccountingProductionReadinessAreaDto.MigrationRollout,
                AccountingConfigurationValidationSeverityDto.Critical,
                $"{MigrationKindLabel(kind)} is marked certified but has no retained certified migration run artifact.",
                "Attach the retained certified migration run artifact before production rollout certification.",
                evidenceReferences));
        }
    }

    private static void AddDimensionalBackfillScopeIssues(
        AccountingProductionReadinessRequestDto request,
        ICollection<AccountingProductionReadinessIssueDto> issues,
        IReadOnlyList<AccountingMigrationRunArtifactDto> scopedArtifacts,
        IReadOnlyList<string> scopedEvidenceReferences)
    {
        var certifiedArtifacts = scopedArtifacts
            .Where(static artifact => artifact.Status == AccountingMigrationRunStatusDto.Certified)
            .ToArray();
        if (certifiedArtifacts.Length == 0)
        {
            return;
        }

        if (certifiedArtifacts.Any(static artifact => artifact.Dimensions is null))
        {
            issues.Add(Issue(
                "migration.dimensional-backfill-dimensions-missing",
                AccountingProductionReadinessAreaDto.MigrationRollout,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Dimensional backfill has certified migration run artifacts without retained ledger dimensions.",
                "Attach canonical fund, ledger-book, entity, sleeve, strategy, investor, capital-account, instrument, tax-lot, cost-center, counterparty, and external-GL dimensions to the certified dimensional backfill artifact before production reporting certification.",
                scopedEvidenceReferences));
            return;
        }

        var requestedFundProfileId = NormalizeFundProfileId(request.FundProfileId);
        var requestedBookId = request.LedgerBookId?.ToString("D");
        var hasMismatchedDimensions = certifiedArtifacts.Any(artifact =>
            !string.Equals(artifact.Dimensions!.FundId, requestedFundProfileId, StringComparison.OrdinalIgnoreCase) ||
            (requestedBookId is not null &&
                !string.Equals(artifact.Dimensions.BookId, requestedBookId, StringComparison.OrdinalIgnoreCase)) ||
            (artifact.LedgerBookId.HasValue &&
                !string.Equals(artifact.Dimensions.BookId, artifact.LedgerBookId.Value.ToString("D"), StringComparison.OrdinalIgnoreCase)));
        if (!hasMismatchedDimensions)
        {
            var missingCanonicalDimensions = certifiedArtifacts
                .SelectMany(static artifact => MissingCanonicalProductionDimensions(artifact.Dimensions!))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static dimension => dimension, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (missingCanonicalDimensions.Length == 0)
            {
                return;
            }

            issues.Add(Issue(
                "migration.dimensional-backfill-canonical-coverage-missing",
                AccountingProductionReadinessAreaDto.MigrationRollout,
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Dimensional backfill certified artifacts are missing canonical production dimension coverage: {string.Join(", ", missingCanonicalDimensions)}.",
                "Re-run or re-certify the dimensional backfill with retained fund, ledger-book, entity, sleeve, strategy, investor, capital-account, instrument, tax-lot, cost-center, counterparty, and external-GL dimension coverage before production reporting certification.",
                scopedEvidenceReferences));
            return;
        }

        issues.Add(Issue(
            "migration.dimensional-backfill-dimensions-scope-mismatch",
            AccountingProductionReadinessAreaDto.MigrationRollout,
            AccountingConfigurationValidationSeverityDto.Critical,
            "Dimensional backfill has retained dimensions outside the requested fund or ledger-book rollout scope.",
            "Re-run or re-certify the dimensional backfill with canonical dimensions matching the requested fund and ledger book.",
            scopedEvidenceReferences));
    }

    private static IEnumerable<string> MissingCanonicalProductionDimensions(LedgerDimensionSetDto dimensions)
    {
        if (string.IsNullOrWhiteSpace(dimensions.FundId))
        {
            yield return "fund";
        }

        if (string.IsNullOrWhiteSpace(dimensions.BookId))
        {
            yield return "ledger book";
        }

        if (string.IsNullOrWhiteSpace(dimensions.EntityId))
        {
            yield return "entity";
        }

        if (string.IsNullOrWhiteSpace(dimensions.SleeveId))
        {
            yield return "sleeve";
        }

        if (string.IsNullOrWhiteSpace(dimensions.StrategyId))
        {
            yield return "strategy";
        }

        if (string.IsNullOrWhiteSpace(dimensions.InvestorId))
        {
            yield return "investor";
        }

        if (string.IsNullOrWhiteSpace(dimensions.CapitalAccountId))
        {
            yield return "capital account";
        }

        if (!dimensions.InstrumentId.HasValue)
        {
            yield return "instrument";
        }

        if (string.IsNullOrWhiteSpace(dimensions.TaxLotId))
        {
            yield return "tax lot";
        }

        if (string.IsNullOrWhiteSpace(dimensions.CostCenterId))
        {
            yield return "cost center";
        }

        if (string.IsNullOrWhiteSpace(dimensions.CounterpartyId))
        {
            yield return "counterparty";
        }

        if (dimensions.ExternalGlDimensions.Count == 0 ||
            dimensions.ExternalGlDimensions.Any(static pair =>
                string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)))
        {
            yield return "external GL";
        }
    }

    private static bool IsMigrationArtifactInScope(
        AccountingProductionReadinessRequestDto request,
        AccountingMigrationRunArtifactDto artifact)
    {
        var requestedFundProfileId = NormalizeFundProfileId(request.FundProfileId);
        var artifactFundProfileId = NormalizeFundProfileId(artifact.FundProfileId);
        if (!string.Equals(artifactFundProfileId, requestedFundProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (request.LedgerBookId is not null &&
            artifact.LedgerBookId != request.LedgerBookId)
        {
            return false;
        }

        var requestedTenantId = TrimOrNull(request.TenantId);
        var artifactTenantId = TrimOrNull(artifact.TenantId);
        if (requestedTenantId is not null &&
            !string.Equals(artifactTenantId, requestedTenantId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var requestedCompanyId = TrimOrNull(request.CompanyId);
        var artifactCompanyId = TrimOrNull(artifact.CompanyId);
        return requestedCompanyId is null ||
            string.Equals(artifactCompanyId, requestedCompanyId, StringComparison.OrdinalIgnoreCase);
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
            AccountingMigrationRunKindDto.LedgerBookScope => "Ledger-book migration scope",
            AccountingMigrationRunKindDto.HistoricalJournalBackfill => "Historical journal backfill",
            AccountingMigrationRunKindDto.DimensionalBackfill => "Dimensional backfill",
            AccountingMigrationRunKindDto.AccountingConfigurationPromotion => "Accounting configuration promotion",
            AccountingMigrationRunKindDto.CloseReportingEvidence => "Close and reporting evidence migration",
            _ => kind.ToString()
        };

    private static List<AccountingProductionReadinessIssueDto> BuildDimensionalIssues(AccountingConfigurationWorkspaceDto workspace)
    {
        var issues = new List<AccountingProductionReadinessIssueDto>();
        var activeRules = workspace.PostingRules.Where(static rule => !rule.IsArchived).ToArray();
        if (activeRules.Length == 0)
        {
            issues.Add(Issue("dimensions.no-active-rules", AccountingProductionReadinessAreaDto.DimensionalAccounting, AccountingConfigurationValidationSeverityDto.Critical, "No active rules are available to prove dimensional coverage.", "Configure active scoped rules before certifying dimensional accounting."));
            return issues;
        }

        var generatedLines = activeRules.SelectMany(static rule => rule.GeneratedPostings).ToArray();
        if (generatedLines.Length == 0)
        {
            issues.Add(Issue("dimensions.generated-lines-missing", AccountingProductionReadinessAreaDto.DimensionalAccounting, AccountingConfigurationValidationSeverityDto.Warning, "No generated posting lines exist to prove line-level dimensions.", "Add generated multi-line postings with LedgerDimensionSet scope."));
            return issues;
        }

        if (generatedLines.Any(static line => line.Dimensions is null))
        {
            issues.Add(Issue("dimensions.line-scope-missing", AccountingProductionReadinessAreaDto.DimensionalAccounting, AccountingConfigurationValidationSeverityDto.Warning, "Some generated posting lines do not carry LedgerDimensionSet scope.", "Add fund/entity/cost-center/counterparty/instrument or external-GL dimensions to generated posting lines where production reporting requires them."));
        }

        if (!generatedLines.Any(static line => line.Dimensions?.ExternalGlDimensions.Count > 0))
        {
            issues.Add(Issue("dimensions.external-gl-missing", AccountingProductionReadinessAreaDto.DimensionalAccounting, AccountingConfigurationValidationSeverityDto.Warning, "Generated posting lines do not include external-GL dimensions.", "Map generated postings to external GL dimensions before production export certification."));
        }

        return issues;
    }

    private static AccountingProductionReadinessComponentDto Component(
        AccountingProductionReadinessAreaDto area,
        string label,
        AccountingProductionReadinessStatusDto status,
        int score,
        string summary,
        IReadOnlyList<AccountingProductionReadinessIssueDto> issues,
        string? route = null,
        IReadOnlyList<string>? evidenceReferences = null)
    {
        IReadOnlyList<string> evidence = evidenceReferences is { Count: > 0 }
            ? evidenceReferences
            : route is null ? Array.Empty<string>() : [route];
        return new(area, label, status, Math.Clamp(score, 0, 100), summary, issues, evidence, route);
    }

    private static AccountingProductionReadinessIssueDto Issue(
        string code,
        AccountingProductionReadinessAreaDto area,
        AccountingConfigurationValidationSeverityDto severity,
        string message,
        string suggestedAction,
        IReadOnlyList<string>? evidenceReferences = null)
        => new(code, area, severity, message, suggestedAction, evidenceReferences);

    private static AccountingProductionReadinessStatusDto ResolveStatus(IEnumerable<AccountingProductionReadinessComponentDto> components)
    {
        var rows = components.ToArray();
        if (rows.Any(static component => component.Status == AccountingProductionReadinessStatusDto.Blocked))
        {
            return AccountingProductionReadinessStatusDto.Blocked;
        }

        if (rows.Any(static component => component.Status is AccountingProductionReadinessStatusDto.ReviewRequired or AccountingProductionReadinessStatusDto.Unavailable))
        {
            return AccountingProductionReadinessStatusDto.ReviewRequired;
        }

        return AccountingProductionReadinessStatusDto.Ready;
    }

    private static AccountingProductionReadinessStatusDto ResolveIssueStatus(IReadOnlyCollection<AccountingProductionReadinessIssueDto> issues)
    {
        if (issues.Any(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical))
        {
            return AccountingProductionReadinessStatusDto.Blocked;
        }

        return issues.Any(static issue => issue.Severity is AccountingConfigurationValidationSeverityDto.Warning or AccountingConfigurationValidationSeverityDto.Info)
            ? AccountingProductionReadinessStatusDto.ReviewRequired
            : AccountingProductionReadinessStatusDto.Ready;
    }

    private static int ScoreFromIssues(IReadOnlyCollection<AccountingProductionReadinessIssueDto> issues, bool hasPositiveEvidence)
    {
        if (issues.Any(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical))
        {
            return 25;
        }

        if (issues.Any(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Warning))
        {
            return hasPositiveEvidence ? 75 : 60;
        }

        return hasPositiveEvidence ? 100 : 50;
    }

    private static AccountingConfigurationValidationSeverityDto MapSeverity(LedgerBookRolloutIssueSeverityDto severity)
        => severity switch
        {
            LedgerBookRolloutIssueSeverityDto.Critical => AccountingConfigurationValidationSeverityDto.Critical,
            LedgerBookRolloutIssueSeverityDto.Warning => AccountingConfigurationValidationSeverityDto.Warning,
            _ => AccountingConfigurationValidationSeverityDto.Info
        };

    private static IReadOnlyList<string> NormalizeEvidenceReferences(IEnumerable<string>? evidenceReferences)
        => evidenceReferences is null
            ? Array.Empty<string>()
            : evidenceReferences
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private static string MigrationArtifactKey(AccountingMigrationRunArtifactDto artifact)
        => $"{TrimOrNull(artifact.TenantId) ?? "all"}|{TrimOrNull(artifact.CompanyId) ?? "all"}|{NormalizeFundProfileId(artifact.FundProfileId)}|{artifact.LedgerBookId?.ToString("D") ?? "all"}|{artifact.RunId}";

    private static string NormalizeFundProfileId(string? value)
        => string.IsNullOrWhiteSpace(value) ? DefaultFundProfileId : value.Trim();

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ExternalGlCounts(
        int ProviderCount,
        int CertifiedMappingProfileCount,
        bool LivePostingEnabled);

    private sealed record LedgerBookComponentResult(
        LedgerBookRolloutAssessmentDto? Rollout,
        AccountingLedgerBookWorkflowReadinessDto WorkflowReadiness);

    private sealed record RulesStudioComponentResult(
        AccountingRulesStudioSummaryDto? Summary,
        AccountingDimensionalReportingReadinessDto DimensionalReportingReadiness);
}
