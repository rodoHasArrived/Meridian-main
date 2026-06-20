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
        var components = new List<AccountingProductionReadinessComponentDto>();
        var ledgerRollout = await BuildLedgerBookComponentAsync(request, fundProfileId, components, ct).ConfigureAwait(false);
        var rulesSummary = await BuildRulesStudioComponentAsync(request, fundProfileId, components, ct).ConfigureAwait(false);
        BuildJournalLifecycleComponent(components);
        BuildCloseReportingComponent(components);
        var externalGlCounts = await BuildExternalGlComponentAsync(request, fundProfileId, components, ct).ConfigureAwait(false);
        BuildMigrationRolloutComponent(request, components);
        BuildTenantAdministrationComponent(components);

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
            externalGlCounts.LivePostingEnabled);
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

    private static void BuildTenantAdministrationComponent(ICollection<AccountingProductionReadinessComponentDto> components)
    {
        var issues = new[]
        {
            Issue("tenant-admin.operator-surface-required", AccountingProductionReadinessAreaDto.TenantAdministration, AccountingConfigurationValidationSeverityDto.Warning, "Production rollout still needs a full tenant/company/report-group setup operator surface over these shared controls.", "Bind browser and WPF admin setup screens to this shared readiness contract instead of local setup heuristics.")
        };
        components.Add(Component(
            AccountingProductionReadinessAreaDto.TenantAdministration,
            "Tenant administration",
            AccountingProductionReadinessStatusDto.ReviewRequired,
            70,
            "Shared setup-readiness contract is available; full admin UX remains a rollout item.",
            issues));
    }

    private static void BuildMigrationRolloutComponent(
        AccountingProductionReadinessRequestDto request,
        ICollection<AccountingProductionReadinessComponentDto> components)
    {
        var issues = new List<AccountingProductionReadinessIssueDto>();
        if (!request.LedgerBookMigrationCertified)
        {
            issues.Add(Issue(
                "migration.ledger-book-scope-not-certified",
                AccountingProductionReadinessAreaDto.MigrationRollout,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Ledger-book migration scope has not been certified for production rollout.",
                "Certify ledger-book scoping and historical fund-level compatibility paths before production cutover.",
                request.MigrationEvidenceLinks));
        }

        if (!request.HistoricalJournalBackfillCertified)
        {
            issues.Add(Issue(
                "migration.historical-journal-backfill-not-certified",
                AccountingProductionReadinessAreaDto.MigrationRollout,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Historical journal backfill has not been certified.",
                "Run and retain historical journal backfill evidence before certifying ledger-book-native accounting.",
                request.MigrationEvidenceLinks));
        }

        if (!request.DimensionalBackfillCertified)
        {
            issues.Add(Issue(
                "migration.dimensional-backfill-not-certified",
                AccountingProductionReadinessAreaDto.MigrationRollout,
                AccountingConfigurationValidationSeverityDto.Critical,
                "Dimensional backfill has not been certified across retained journal lines and report inputs.",
                "Backfill and verify fund, entity, sleeve, strategy, investor, capital account, instrument, tax lot, cost center, counterparty, and external-GL dimensions before production reporting certification.",
                request.MigrationEvidenceLinks));
        }

        if (!request.AccountingConfigurationPromotionCertified)
        {
            issues.Add(Issue(
                "migration.configuration-promotion-not-certified",
                AccountingProductionReadinessAreaDto.MigrationRollout,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Accounting configuration promotion evidence has not been certified.",
                "Retain promotion evidence for chart, rule, policy, and approval-state migration before rollout.",
                request.MigrationEvidenceLinks));
        }

        if (!request.CloseReportingEvidenceMigrationCertified)
        {
            issues.Add(Issue(
                "migration.close-reporting-evidence-not-certified",
                AccountingProductionReadinessAreaDto.MigrationRollout,
                AccountingConfigurationValidationSeverityDto.Warning,
                "Close and reporting evidence migration has not been certified.",
                "Retain close checklist, report package, certification, and restatement evidence migration proof before production close.",
                request.MigrationEvidenceLinks));
        }

        var certifiedCount = new[]
        {
            request.LedgerBookMigrationCertified,
            request.HistoricalJournalBackfillCertified,
            request.DimensionalBackfillCertified,
            request.AccountingConfigurationPromotionCertified,
            request.CloseReportingEvidenceMigrationCertified
        }.Count(static certified => certified);

        components.Add(Component(
            AccountingProductionReadinessAreaDto.MigrationRollout,
            "Migration rollout",
            ResolveIssueStatus(issues),
            ScoreFromIssues(issues, hasPositiveEvidence: request.MigrationEvidenceLinks.Count > 0),
            $"{certifiedCount}/5 migration control(s) certified; {request.MigrationEvidenceLinks.Count} retained evidence link(s).",
            issues,
            route: UiApiRoutes.AccountingSystemProductionReadiness,
            evidenceReferences: request.MigrationEvidenceLinks));
    }

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

    private static string NormalizeFundProfileId(string? value)
        => string.IsNullOrWhiteSpace(value) ? DefaultFundProfileId : value.Trim();

    private sealed record ExternalGlCounts(
        int ProviderCount,
        int CertifiedMappingProfileCount,
        bool LivePostingEnabled);
}
