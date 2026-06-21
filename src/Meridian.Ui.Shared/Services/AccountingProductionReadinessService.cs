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
        var effectiveRequest = MergeTenantAdministrationProfile(
            request with { FundProfileId = fundProfileId, MigrationRunArtifacts = migrationRunArtifacts },
            tenantAdministrationProfile);
        var components = new List<AccountingProductionReadinessComponentDto>();
        var ledgerRollout = await BuildLedgerBookComponentAsync(effectiveRequest, fundProfileId, components, ct).ConfigureAwait(false);
        var rulesSummary = await BuildRulesStudioComponentAsync(effectiveRequest, fundProfileId, components, ct).ConfigureAwait(false);
        BuildJournalLifecycleComponent(components);
        BuildCloseReportingComponent(components);
        var externalGlCounts = await BuildExternalGlComponentAsync(effectiveRequest, fundProfileId, components, ct).ConfigureAwait(false);
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
            ledgerRollout,
            rulesSummary,
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
            foreach (var artifact in await store.ListAsync(fundProfileId, request.LedgerBookId, ct).ConfigureAwait(false))
            {
                artifacts[MigrationArtifactKey(artifact)] = artifact;
            }
        }

        foreach (var artifact in request.MigrationRunArtifacts)
        {
            artifacts[MigrationArtifactKey(artifact)] = artifact with
            {
                FundProfileId = NormalizeFundProfileId(artifact.FundProfileId ?? fundProfileId)
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
            TenantAdministrationEvidenceLinks = evidence
        };
    }

    private async Task<LedgerBookRolloutAssessmentDto?> BuildLedgerBookComponentAsync(
        AccountingProductionReadinessRequestDto request,
        string fundProfileId,
        ICollection<AccountingProductionReadinessComponentDto> components,
        CancellationToken ct)
    {
        var service = _services.GetService<ILedgerBookService>();
        if (service is null)
        {
            components.Add(Component(
                AccountingProductionReadinessAreaDto.LedgerBooks,
                "Ledger books",
                AccountingProductionReadinessStatusDto.Unavailable,
                0,
                "No ledger-book service is registered for production readiness assessment.",
                [Issue("ledger-books.service-missing", AccountingProductionReadinessAreaDto.LedgerBooks, AccountingConfigurationValidationSeverityDto.Critical, "Ledger-book service is not registered.", "Register ILedgerBookService before production accounting rollout.")],
                UiApiRoutes.LedgerBooks));
            return null;
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
            .ToArray();
        var status = issues.Any(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical)
            ? AccountingProductionReadinessStatusDto.Blocked
            : issues.Length > 0
                ? AccountingProductionReadinessStatusDto.ReviewRequired
                : AccountingProductionReadinessStatusDto.Ready;

        components.Add(Component(
            AccountingProductionReadinessAreaDto.LedgerBooks,
            "Ledger books",
            status,
            status == AccountingProductionReadinessStatusDto.Ready ? 100 : status == AccountingProductionReadinessStatusDto.ReviewRequired ? 70 : 20,
            $"{rollout.BookCount} ledger book(s), {rollout.OpenPeriodCount} open period(s), {rollout.CriticalIssueCount} critical issue(s).",
            issues,
            UiApiRoutes.LedgerBookRolloutAssessment));

        return rollout;
    }

    private async Task<AccountingRulesStudioSummaryDto?> BuildRulesStudioComponentAsync(
        AccountingProductionReadinessRequestDto request,
        string fundProfileId,
        ICollection<AccountingProductionReadinessComponentDto> components,
        CancellationToken ct)
    {
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
            components.Add(Component(
                AccountingProductionReadinessAreaDto.DimensionalAccounting,
                "Dimensional accounting",
                AccountingProductionReadinessStatusDto.Unavailable,
                0,
                "Dimensional coverage cannot be assessed without accounting configuration.",
                [Issue("dimensions.configuration-missing", AccountingProductionReadinessAreaDto.DimensionalAccounting, AccountingConfigurationValidationSeverityDto.Critical, "Accounting configuration is unavailable.", "Register accounting configuration and rules before certifying dimensional accounting coverage.")]));
            return null;
        }

        var workspace = await service.GetWorkspaceAsync(fundProfileId, request.LedgerBookId, ct).ConfigureAwait(false);
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

        var dimensionalIssues = BuildDimensionalIssues(workspace);
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
            activeGeneratedPostingRuleCount == 0 ? AccountingProductionReadinessStatusDto.ReviewRequired : AccountingProductionReadinessStatusDto.Ready,
            activeGeneratedPostingRuleCount == 0 ? 65 : 100,
            $"{activeGeneratedPostingRuleCount} active generated-posting rule(s) are configured for governed draft candidates.",
            rulesIssues.Where(static issue => issue.Area == AccountingProductionReadinessAreaDto.PostingRules).ToArray(),
            UiApiRoutes.LedgerAccountingConfigurationPostingRuleCandidates));
        components.Add(Component(
            AccountingProductionReadinessAreaDto.DimensionalAccounting,
            "Dimensional accounting",
            ResolveIssueStatus(dimensionalIssues),
            ScoreFromIssues(dimensionalIssues, hasPositiveEvidence: dimensionalIssues.Count == 0),
            "Rules and generated postings were inspected for canonical LedgerDimensionSet coverage.",
            dimensionalIssues,
            UiApiRoutes.LedgerAccountingConfiguration));

        return summary;
    }

    private void BuildJournalLifecycleComponent(ICollection<AccountingProductionReadinessComponentDto> components)
    {
        var hasWorkbench = _services.GetService<IManualJournalEntryWorkbenchService>() is not null;
        var hasLifecycle = _services.GetService<IManualJournalEntryLifecycleService>() is not null;
        var issues = new List<AccountingProductionReadinessIssueDto>();
        if (!hasWorkbench || !hasLifecycle)
        {
            issues.Add(Issue("journal-lifecycle.service-missing", AccountingProductionReadinessAreaDto.JournalLifecycle, AccountingConfigurationValidationSeverityDto.Critical, "Manual journal workbench or lifecycle service is not registered.", "Register the governed manual journal lifecycle service before production rollout."));
        }

        components.Add(Component(
            AccountingProductionReadinessAreaDto.JournalLifecycle,
            "Journal lifecycle",
            ResolveIssueStatus(issues),
            hasWorkbench && hasLifecycle ? 100 : 0,
            hasWorkbench && hasLifecycle
                ? "Governed manual journal workbench and lifecycle transition service are registered."
                : "Journal lifecycle services are not fully registered.",
            issues,
            UiApiRoutes.LedgerManualJournalEntryWorkbench));
    }

    private void BuildCloseReportingComponent(ICollection<AccountingProductionReadinessComponentDto> components)
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

        components.Add(Component(
            AccountingProductionReadinessAreaDto.CloseReporting,
            "Close and reporting",
            ResolveIssueStatus(issues),
            hasClose && hasReports ? 100 : 0,
            hasClose && hasReports
                ? "Close management and accounting report package services are registered."
                : "Close/reporting production services are incomplete.",
            issues,
            UiApiRoutes.LedgerReportsAccountingPackage));
    }

    private async Task<ExternalGlCounts> BuildExternalGlComponentAsync(
        AccountingProductionReadinessRequestDto request,
        string fundProfileId,
        ICollection<AccountingProductionReadinessComponentDto> components,
        CancellationToken ct)
    {
        var service = _services.GetService<AccountingSystemIntegrationService>();
        if (service is null)
        {
            components.Add(Component(
                AccountingProductionReadinessAreaDto.ExternalGl,
                "External GL",
                AccountingProductionReadinessStatusDto.Unavailable,
                0,
                "No accounting-system integration service is registered.",
                [Issue("external-gl.service-missing", AccountingProductionReadinessAreaDto.ExternalGl, AccountingConfigurationValidationSeverityDto.Critical, "Accounting-system integration service is not registered.", "Register guarded external-GL import/mapping services before production rollout.")],
                UiApiRoutes.AccountingSystemProviders));
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

        if (certifiedMappings == 0)
        {
            issues.Add(Issue("external-gl.certified-mapping-missing", AccountingProductionReadinessAreaDto.ExternalGl, AccountingConfigurationValidationSeverityDto.Critical, "No certified external-GL mapping profile exists for this scope.", "Certify account and dimension mappings before guarded export review."));
        }

        issues.Add(Issue("external-gl.live-posting-disabled", AccountingProductionReadinessAreaDto.ExternalGl, AccountingConfigurationValidationSeverityDto.Info, "Live external GL posting remains disabled by product policy.", "Use import, reconciliation, and guarded export artifacts until a separately approved live-posting adapter exists."));

        components.Add(Component(
            AccountingProductionReadinessAreaDto.ExternalGl,
            "External GL",
            ResolveIssueStatus(issues),
            certifiedMappings > 0 ? 85 : 45,
            $"{providers.Count} provider row(s), {mappings.Count} mapping profile(s), {certifiedMappings} certified profile(s); live posting disabled.",
            issues,
            UiApiRoutes.AccountingSystemMappingProfiles));

        return new ExternalGlCounts(providers.Count, certifiedMappings, livePostingEnabled);
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
            evidenceReferences);
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
                "Attach canonical fund and ledger-book dimensions to the certified dimensional backfill artifact before production reporting certification.",
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

        return request.LedgerBookId is null ||
            artifact.LedgerBookId == request.LedgerBookId;
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

    private static string MigrationArtifactKey(AccountingMigrationRunArtifactDto artifact)
        => $"{NormalizeFundProfileId(artifact.FundProfileId)}|{artifact.LedgerBookId?.ToString("D") ?? "all"}|{artifact.RunId}";

    private static string NormalizeFundProfileId(string? value)
        => string.IsNullOrWhiteSpace(value) ? DefaultFundProfileId : value.Trim();

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ExternalGlCounts(
        int ProviderCount,
        int CertifiedMappingProfileCount,
        bool LivePostingEnabled);
}
