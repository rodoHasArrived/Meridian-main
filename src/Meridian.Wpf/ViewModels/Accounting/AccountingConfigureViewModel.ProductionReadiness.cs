using System;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Services.Services.Accounting;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Contracts.Integrity;
using UtcFormat = Meridian.Ui.Services.FormatHelpers;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed partial class AccountingConfigureViewModel
{
    public async Task SaveProductionCertificationProfileAsync(CancellationToken ct = default)
    {
        if (_activeFundProfile is null)
        {
            ProductionCertificationProfileStatusText = "Select a fund-linked context before saving production certification controls.";
            StatusText = ProductionCertificationProfileStatusText;
            return;
        }

        if (_productionCertificationCommandService is null)
        {
            ProductionCertificationProfileStatusText = "Production certification authority is not registered for this desktop session.";
            StatusText = ProductionCertificationProfileStatusText;
            return;
        }

        var retainedEvidence = NormalizeTenantAdministrationEvidence(ProductionCertificationEvidenceText);
        if (retainedEvidence.Count == 0)
        {
            ProductionCertificationProfileStatusText = "Retained evidence is required before saving production certification controls.";
            StatusText = ProductionCertificationProfileStatusText;
            NotifyProductionCertificationSaveStateChanged();
            return;
        }

        try
        {
            var correlationId = $"wpf-accounting-production-certification-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var actor = ResolveActor();
            var profile = new AccountingProductionCertificationProfileDto(
                _activeFundProfile.FundProfileId,
                _configuration?.LedgerBookId,
                PostingRulesLedgerBookNativeCertified,
                JournalLifecycleLedgerBookNativeCertified,
                CloseReportingLedgerBookNativeCertified,
                ExternalGlLedgerBookNativeCertified,
                PeriodReportDimensionQueriesCertified,
                CrossPeriodReportDimensionQueriesCertified,
                JournalQueryDimensionFiltersCertified,
                ExternalExportDimensionMappingCertified,
                default,
                actor,
                retainedEvidence,
                correlationId,
                ResolveTenantAdministrationTenantId(),
                ResolveTenantAdministrationCompanyId(),
                ReconciliationLedgerBookNativeCertified,
                DirectLendingLedgerBookNativeCertified,
                StrategyLedgerReadLedgerBookNativeCertified,
                LedgerLineDimensionsPersistedCertified,
                TrialBalanceDimensionFiltersCertified,
                ReportPackageDimensionProvenanceCertified,
                ClosePlanConfigurationLedgerBookNativeCertified);

            var saved = await _productionCertificationCommandService.CertifyAsync(
                new AccountingProductionCertificationProfileUpsertRequestDto(
                    profile,
                    actor,
                    correlationId,
                    retainedEvidence),
                actor,
                ct).ConfigureAwait(false);

            ApplyProductionCertificationProfile(saved, "Production certification profile saved; readiness refreshed from retained book and dimension controls.");
            await RefreshProductionReadinessAsync(ct).ConfigureAwait(false);
            StatusText = ProductionCertificationProfileStatusText;
        }
        catch (Exception ex)
        {
            ProductionCertificationProfileStatusText = $"Production certification profile save failed: {ex.Message}";
            StatusText = ProductionCertificationProfileStatusText;
        }
    }

    private async Task LoadProductionCertificationProfileAsync(CancellationToken ct)
    {
        if (_activeFundProfile is null)
        {
            ProductionCertificationProfileStatusText = "Locked until a fund context is selected.";
            ProductionCertificationProfileScopeText = "Fund and ledger-book scope require a fund context.";
            ProductionCertificationProfileUpdatedText = "No retained production certification profile loaded.";
            return;
        }

        var tenantId = ResolveTenantAdministrationTenantId();
        var companyId = ResolveTenantAdministrationCompanyId();
        var ledgerBookId = _configuration?.LedgerBookId;
        ProductionCertificationProfileScopeText = ledgerBookId.HasValue
            ? $"Tenant {tenantId}; company {companyId}; fund {_activeFundProfile.FundProfileId}; ledger book {ledgerBookId:D}."
            : $"Tenant {tenantId}; company {companyId}; fund {_activeFundProfile.FundProfileId}; fund-level scope.";

        if (_productionCertificationProfileStore is null)
        {
            ProductionCertificationProfileStatusText = "Production certification profile store is not registered for this desktop session.";
            ProductionCertificationProfileUpdatedText = "Retained production certification controls are unavailable.";
            NotifyProductionCertificationSaveStateChanged();
            return;
        }

        try
        {
            var profile = await _productionCertificationProfileStore.GetAsync(tenantId, companyId, _activeFundProfile.FundProfileId, ledgerBookId, ct).ConfigureAwait(false);
            if (profile is null)
            {
                ProductionCertificationProfileStatusText = "No retained production certification profile exists for the active accounting scope.";
                ProductionCertificationProfileUpdatedText = "Save book-native and dimensional controls with retained evidence before production certification.";
                ProductionCertificationEvidenceText = ledgerBookId.HasValue
                    ? $"evidence://ledger-book/{ledgerBookId:D}/production-certification"
                    : $"evidence://fund/{_activeFundProfile.FundProfileId}/production-certification";
                NotifyProductionCertificationSaveStateChanged();
                return;
            }

            ApplyProductionCertificationProfile(profile, "Production certification profile loaded from the shared store.");
        }
        catch (Exception ex)
        {
            ProductionCertificationProfileStatusText = $"Production certification profile could not load: {ex.Message}";
            ProductionCertificationProfileUpdatedText = "Retained production certification controls are unavailable.";
            NotifyProductionCertificationSaveStateChanged();
        }
    }

    private void ApplyProductionCertificationProfile(
        AccountingProductionCertificationProfileDto profile,
        string statusText)
    {
        PostingRulesLedgerBookNativeCertified = profile.PostingRulesLedgerBookNativeCertified;
        JournalLifecycleLedgerBookNativeCertified = profile.JournalLifecycleLedgerBookNativeCertified;
        CloseReportingLedgerBookNativeCertified = profile.CloseReportingLedgerBookNativeCertified;
        ClosePlanConfigurationLedgerBookNativeCertified = profile.ClosePlanConfigurationLedgerBookNativeCertified;
        ExternalGlLedgerBookNativeCertified = profile.ExternalGlLedgerBookNativeCertified;
        ReconciliationLedgerBookNativeCertified = profile.ReconciliationLedgerBookNativeCertified;
        DirectLendingLedgerBookNativeCertified = profile.DirectLendingLedgerBookNativeCertified;
        StrategyLedgerReadLedgerBookNativeCertified = profile.StrategyLedgerReadLedgerBookNativeCertified;
        PeriodReportDimensionQueriesCertified = profile.PeriodReportDimensionQueriesCertified;
        CrossPeriodReportDimensionQueriesCertified = profile.CrossPeriodReportDimensionQueriesCertified;
        JournalQueryDimensionFiltersCertified = profile.JournalQueryDimensionFiltersCertified;
        ExternalExportDimensionMappingCertified = profile.ExternalExportDimensionMappingCertified;
        LedgerLineDimensionsPersistedCertified = profile.LedgerLineDimensionsPersistedCertified;
        TrialBalanceDimensionFiltersCertified = profile.TrialBalanceDimensionFiltersCertified;
        ReportPackageDimensionProvenanceCertified = profile.ReportPackageDimensionProvenanceCertified;
        ProductionCertificationEvidenceText = string.Join(Environment.NewLine, profile.EvidenceReferences);
        ProductionCertificationProfileScopeText = profile.LedgerBookId.HasValue
            ? $"Tenant {AccountingWorkbenchPresentation.FormatReadinessScope(profile.TenantId)}; company {AccountingWorkbenchPresentation.FormatReadinessScope(profile.CompanyId)}; fund {profile.FundProfileId}; ledger book {profile.LedgerBookId:D}."
            : $"Tenant {AccountingWorkbenchPresentation.FormatReadinessScope(profile.TenantId)}; company {AccountingWorkbenchPresentation.FormatReadinessScope(profile.CompanyId)}; fund {profile.FundProfileId}; fund-level scope.";
        ProductionCertificationProfileUpdatedText =
            $"Last retained by {profile.UpdatedBy} at {UtcFormat.FormatUtcTimestamp(profile.UpdatedAtUtc)}.";
        ProductionCertificationProfileStatusText = statusText;
        NotifyProductionCertificationSaveStateChanged();
    }

    private void NotifyProductionCertificationSaveStateChanged()
    {
        RaisePropertyChanged(nameof(CanSaveProductionCertificationProfile));
        SaveProductionCertificationProfileCommand.NotifyCanExecuteChanged();
    }

    private async Task RefreshProductionReadinessAsync(CancellationToken ct)
    {
        if (_activeFundProfile is null)
        {
            ProductionReadinessStatusText = "Locked";
            ProductionReadinessDetailText = "Select a fund-linked context before assessing accounting production readiness.";
            ProductionReadinessScoreText = "No score";
            ProductionReadinessLedgerBookText = "Locked until a fund context is selected.";
            ProductionReadinessExternalGlText = "Locked until a fund context is selected.";
            ProductionReadinessDimensionalReportingText = "Locked until a fund context is selected.";
            ProductionReadinessTenantAdminText = "Locked until a fund context is selected.";
            ProductionReadinessComponentRows.Clear();
            ProductionReadinessIssueRows.Clear();
            ProductionReadinessWorkflowRows.Clear();
            ProductionReadinessDimensionalControlRows.Clear();
            ProductionReadinessGapRows.Clear();
            ProductionReadinessMigrationPlanRows.Clear();
            ProductionReadinessMigrationArtifactRows.Clear();
            ProductionReadinessMigrationWorkerPlanRows.Clear();
            return;
        }

        if (_accountingProductionReadinessService is null)
        {
            ProductionReadinessStatusText = "Unavailable";
            ProductionReadinessDetailText = "Accounting production-readiness service is not registered for this desktop session.";
            ProductionReadinessScoreText = "No score";
            ProductionReadinessLedgerBookText = "Ledger-book readiness cannot be assessed.";
            ProductionReadinessExternalGlText = "External GL readiness cannot be assessed.";
            ProductionReadinessDimensionalReportingText = "Dimensional reporting readiness cannot be assessed.";
            ProductionReadinessTenantAdminText = "Tenant administration readiness cannot be assessed.";
            ProductionReadinessComponentRows.Clear();
            ProductionReadinessWorkflowRows.Clear();
            ProductionReadinessDimensionalControlRows.Clear();
            ProductionReadinessGapRows.Clear();
            ProductionReadinessMigrationPlanRows.Clear();
            ProductionReadinessMigrationArtifactRows.Clear();
            ProductionReadinessMigrationWorkerPlanRows.Clear();
            ProductionReadinessIssueRows.ReplaceWith(
            [
                new AccountingWorkbenchRow(
                    "production-readiness.service-missing",
                    AccountingConfigurationValidationSeverityDto.Critical.ToString(),
                    ProductionReadinessDetailText,
                    "Register AccountingProductionReadinessService in the Accounting feature module.")
            ]);
            return;
        }

        try
        {
            var request = BuildProductionReadinessRequest();
            var readiness = await _accountingProductionReadinessService.AssessAsync(request, ct).ConfigureAwait(false);
            var workerPlans = await LoadMigrationWorkerPlansAsync(readiness, ct).ConfigureAwait(false);
            ApplyProductionReadiness(readiness, workerPlans);
        }
        catch (Exception ex)
        {
            ProductionReadinessStatusText = "Unavailable";
            ProductionReadinessDetailText = $"Accounting production readiness could not be assessed: {ex.Message}";
            ProductionReadinessScoreText = "No score";
            ProductionReadinessLedgerBookText = "Ledger-book readiness could not be assessed.";
            ProductionReadinessExternalGlText = "External GL readiness could not be assessed.";
            ProductionReadinessDimensionalReportingText = "Dimensional reporting readiness could not be assessed.";
            ProductionReadinessTenantAdminText = "Tenant administration readiness could not be assessed.";
            ProductionReadinessComponentRows.Clear();
            ProductionReadinessWorkflowRows.Clear();
            ProductionReadinessDimensionalControlRows.Clear();
            ProductionReadinessGapRows.Clear();
            ProductionReadinessMigrationPlanRows.Clear();
            ProductionReadinessMigrationArtifactRows.Clear();
            ProductionReadinessMigrationWorkerPlanRows.Clear();
            ProductionReadinessIssueRows.ReplaceWith(
            [
                new AccountingWorkbenchRow(
                    "production-readiness.assessment-failed",
                    AccountingConfigurationValidationSeverityDto.Critical.ToString(),
                    ProductionReadinessDetailText,
                    "Resolve shared accounting service registration or configuration errors before rollout.")
            ]);
        }
    }

    private AccountingProductionReadinessRequestDto BuildProductionReadinessRequest()
    {
        var workspace = _configuration;
        var setupCandidate = workspace?.LedgerBookSetupCandidate;
        var requiredScopes = setupCandidate is null
            ? Array.Empty<LedgerBookRequiredScopeDto>()
            :
            [
                new LedgerBookRequiredScopeDto(
                    setupCandidate.FundStructureNodeId,
                    setupCandidate.FundStructureNodeKind,
                    setupCandidate.AccountingBasis,
                    setupCandidate.DisplayName)
            ];

        return new AccountingProductionReadinessRequestDto(
            _activeFundProfile?.FundProfileId,
            workspace?.LedgerBookId,
            setupCandidate?.AccountingBasis ?? (workspace is null ? null : ResolveActiveLedgerBook(workspace)?.AccountingBasis),
            ProviderId: null,
            TenantId: ResolveTenantAdministrationTenantId(),
            CompanyId: ResolveTenantAdministrationCompanyId(),
            TenantScopeConfigured: TenantScopeConfigured,
            AdminRoleProfileConfigured: AdminRoleProfileConfigured,
            ScopedAccessPoliciesConfigured: ScopedAccessPoliciesConfigured,
            ReportingGroupsConfigured: ReportingGroupsConfigured,
            AccountingAdminSurfaceConfigured: AccountingAdminSurfaceConfigured,
            WpfAccountingAdminSurfaceConfigured: WpfAccountingAdminSurfaceConfigured,
            ChartAdministrationStudioConfigured: ChartAdministrationStudioConfigured,
            RuleTestPromotionStudioConfigured: RuleTestPromotionStudioConfigured,
            CloseSetupStudioConfigured: CloseSetupStudioConfigured,
            ProviderMappingStudioConfigured: ProviderMappingStudioConfigured,
            TenantCompanyReportGroupSetupStudioConfigured: TenantCompanyReportGroupSetupStudioConfigured,
            AuditReviewToolingConfigured: AuditReviewToolingConfigured,
            BulkImportExportSafeguardsConfigured: BulkImportExportSafeguardsConfigured,
            PerformanceValidationConfigured: PerformanceValidationConfigured,
            DisasterRecoveryRunbookConfigured: DisasterRecoveryRunbookConfigured,
            LedgerBookAdministrationStudioConfigured: LedgerBookAdministrationStudioConfigured,
            PostingRuleAuthoringStudioConfigured: PostingRuleAuthoringStudioConfigured,
            ApprovalQueueStudioConfigured: ApprovalQueueStudioConfigured,
            DimensionMappingStudioConfigured: DimensionMappingStudioConfigured,
            ImplementationSandboxConfigured: ImplementationSandboxConfigured,
            TenantAdministrationEvidenceLinks: NormalizeTenantAdministrationEvidence(TenantAdministrationEvidenceText),
            PostingRulesLedgerBookNativeCertified: PostingRulesLedgerBookNativeCertified,
            JournalLifecycleLedgerBookNativeCertified: JournalLifecycleLedgerBookNativeCertified,
            CloseReportingLedgerBookNativeCertified: CloseReportingLedgerBookNativeCertified,
            ClosePlanConfigurationLedgerBookNativeCertified: ClosePlanConfigurationLedgerBookNativeCertified,
            ExternalGlLedgerBookNativeCertified: ExternalGlLedgerBookNativeCertified,
            ReconciliationLedgerBookNativeCertified: ReconciliationLedgerBookNativeCertified,
            DirectLendingLedgerBookNativeCertified: DirectLendingLedgerBookNativeCertified,
            StrategyLedgerReadLedgerBookNativeCertified: StrategyLedgerReadLedgerBookNativeCertified,
            LedgerBookWorkflowEvidenceLinks: NormalizeTenantAdministrationEvidence(ProductionCertificationEvidenceText),
            PeriodReportDimensionQueriesCertified: PeriodReportDimensionQueriesCertified,
            CrossPeriodReportDimensionQueriesCertified: CrossPeriodReportDimensionQueriesCertified,
            JournalQueryDimensionFiltersCertified: JournalQueryDimensionFiltersCertified,
            ExternalExportDimensionMappingCertified: ExternalExportDimensionMappingCertified,
            LedgerLineDimensionsPersistedCertified: LedgerLineDimensionsPersistedCertified,
            TrialBalanceDimensionFiltersCertified: TrialBalanceDimensionFiltersCertified,
            ReportPackageDimensionProvenanceCertified: ReportPackageDimensionProvenanceCertified,
            DimensionalReportingEvidenceLinks: NormalizeTenantAdministrationEvidence(ProductionCertificationEvidenceText),
            RequiredLedgerBookScopes: requiredScopes);
    }

    private async Task<IReadOnlyList<AccountingMigrationRunWorkerPlanDto>> LoadMigrationWorkerPlansAsync(
        AccountingProductionReadinessDto readiness,
        CancellationToken ct)
    {
        if (_migrationRunWorkerPlanStore is null)
        {
            return [];
        }

        var fundProfileId = _activeFundProfile?.FundProfileId;
        var ledgerBookId = _configuration?.LedgerBookId;
        return await _migrationRunWorkerPlanStore.ListAsync(
                fundProfileId,
                ledgerBookId,
                kind: null,
                ct: ct,
                tenantId: ResolveTenantAdministrationTenantId(),
                companyId: ResolveTenantAdministrationCompanyId())
            .ConfigureAwait(false);
    }

    private void ApplyProductionReadiness(
        AccountingProductionReadinessDto readiness,
        IReadOnlyList<AccountingMigrationRunWorkerPlanDto> migrationWorkerPlans)
    {
        ProductionReadinessStatusText = $"{readiness.Status} | {readiness.Score}/100";
        ProductionReadinessScoreText = $"{readiness.Score}/100";
        ProductionReadinessDetailText =
            $"{readiness.Components.Count} component(s), {readiness.CriticalIssueCount} critical issue(s), {readiness.WarningIssueCount} warning issue(s); generated {UtcFormat.FormatUtcTimestamp(readiness.GeneratedAtUtc)}.";
        ProductionReadinessLedgerBookText = readiness.LedgerBookRollout is null
            ? "Ledger-book rollout assessment is unavailable."
            : $"{readiness.LedgerBookRollout.BookCount} book(s), {readiness.LedgerBookRollout.OpenPeriodCount} open period(s), {readiness.LedgerBookRollout.CriticalIssueCount} critical rollout issue(s); {readiness.LedgerBookWorkflows?.CompletedControlCount ?? 0}/{readiness.LedgerBookWorkflows?.RequiredControlCount ?? 10} workflow control(s).";
        ProductionReadinessExternalGlText =
            $"{readiness.ExternalGlProviderCount} provider(s), {readiness.CertifiedExternalGlMappingProfileCount} certified mapping profile(s); live posting {(readiness.ExternalGlLivePostingEnabled ? "available" : "disabled")}.";
        ProductionReadinessDimensionalReportingText = readiness.DimensionalReporting is null
            ? "Dimensional reporting readiness is unavailable."
            : $"{readiness.DimensionalReporting.CompletedControlCount}/{readiness.DimensionalReporting.RequiredControlCount} ledger/query/report/export dimension control(s); ledger book {AccountingWorkbenchPresentation.FormatReadinessScope(readiness.DimensionalReporting.LedgerBookId?.ToString("D"))}; {readiness.DimensionalReporting.EvidenceReferences.Count} retained evidence link(s).";
        ProductionReadinessTenantAdminText = readiness.TenantAdministration is null
            ? "Tenant administration readiness is unavailable."
            : $"{readiness.TenantAdministration.CompletedControlCount}/{readiness.TenantAdministration.RequiredControlCount} tenant admin control(s); tenant {AccountingWorkbenchPresentation.FormatReadinessScope(readiness.TenantAdministration.TenantId)}; company {AccountingWorkbenchPresentation.FormatReadinessScope(readiness.TenantAdministration.CompanyId)}; {readiness.TenantAdministration.EvidenceReferences.Count} retained evidence link(s).";

        ProductionReadinessComponentRows.ReplaceWith(readiness.Components.Select(component =>
            new AccountingWorkbenchRow(
                component.Label,
                $"{component.Status} | {component.Score}/100",
                component.Summary,
                component.EvidenceReferences.Count > 0
                    ? string.Join("; ", component.EvidenceReferences)
                    : component.Route ?? string.Empty,
                component.Area.ToString())));

        ProductionReadinessIssueRows.ReplaceWith(readiness.Issues
            .Where(static issue => issue.Severity is AccountingConfigurationValidationSeverityDto.Critical or AccountingConfigurationValidationSeverityDto.Warning)
            .Take(12)
            .Select(issue => new AccountingWorkbenchRow(
                issue.Code,
                issue.Severity.ToString(),
                issue.Message,
                issue.SuggestedAction,
                issue.EvidenceReferences.Count > 0
                    ? string.Join("; ", issue.EvidenceReferences)
                    : issue.Area.ToString())));
        ProductionReadinessWorkflowRows.ReplaceWith(AccountingWorkbenchPresentation.BuildLedgerBookWorkflowRows(readiness.LedgerBookWorkflows));
        ProductionReadinessDimensionalControlRows.ReplaceWith(AccountingWorkbenchPresentation.BuildDimensionalReportingRows(readiness.DimensionalReporting));
        ProductionReadinessGapRows.ReplaceWith(AccountingWorkbenchPresentation.BuildProductionGapRows(readiness.ProductionGaps));
        ProductionReadinessMigrationPlanRows.ReplaceWith(readiness.MigrationRolloutPlan.Select(item =>
            new AccountingWorkbenchRow(
                item.Label,
                $"{item.Status} | {(item.Certified ? "Certified" : "Not certified")}",
                $"{item.ScopeLabel}; latest run {(string.IsNullOrWhiteSpace(item.LatestRunId) ? "missing" : item.LatestRunId)}{(item.LatestRunStatus.HasValue ? $" ({item.LatestRunStatus.Value})" : string.Empty)}; {item.MigratedRecordCount:N0} record(s), {item.IssueCount:N0} issue(s).",
                item.BlockingIssueCodes.Count > 0
                    ? $"{item.RequiredAction} | {string.Join(", ", item.BlockingIssueCodes)}"
                    : item.RequiredAction,
                item.Code)));
        ProductionReadinessMigrationArtifactRows.ReplaceWith(readiness.MigrationRunArtifacts
            .OrderByDescending(static artifact => artifact.StartedAtUtc)
            .ThenBy(static artifact => artifact.RunId, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(artifact => new AccountingWorkbenchRow(
                AccountingWorkbenchPresentation.FormatMigrationArtifactKind(artifact.Kind),
                artifact.Status.ToString(),
                $"{AccountingWorkbenchPresentation.FormatMigrationArtifactScope(artifact)}; {artifact.MigratedRecordCount:N0} record(s), {artifact.IssueCount:N0} issue(s); started {UtcFormat.FormatUtcTimestamp(artifact.StartedAtUtc)}{(artifact.CompletedAtUtc.HasValue ? $", completed {UtcFormat.FormatUtcTimestamp(artifact.CompletedAtUtc.Value)}" : string.Empty)}.",
                artifact.EvidenceReferences.Count > 0
                    ? string.Join("; ", artifact.EvidenceReferences)
                    : AccountingWorkbenchPresentation.FormatLedgerDimensionSet(artifact.Dimensions),
                artifact.RunId)));
        ProductionReadinessMigrationWorkerPlanRows.ReplaceWith(migrationWorkerPlans
            .OrderBy(static plan => plan.Kind)
            .ThenBy(static plan => plan.PlanId, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(plan => new AccountingWorkbenchRow(
                AccountingWorkbenchPresentation.FormatMigrationArtifactKind(plan.Kind),
                plan.SourceRecordCount == plan.MigratedRecordCount ? "Reconciled" : "Mismatch",
                $"{AccountingWorkbenchPresentation.FormatMigrationWorkerPlanScope(plan)}; {plan.SourceRecordCount:N0} source record(s), {plan.MigratedRecordCount:N0} migrated record(s).",
                plan.EvidenceReferences.Count > 0
                    ? string.Join("; ", plan.EvidenceReferences)
                    : AccountingWorkbenchPresentation.FormatLedgerDimensionSet(plan.Dimensions),
                plan.PlanId)));
    }
}
