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
    public async Task SaveTenantAdministrationProfileAsync(CancellationToken ct = default)
    {
        if (_activeFundProfile is null)
        {
            TenantAdministrationProfileStatusText = "Select a fund-linked context before saving tenant administration setup controls.";
            StatusText = TenantAdministrationProfileStatusText;
            return;
        }

        if (_tenantAdministrationProfileStore is null)
        {
            TenantAdministrationProfileStatusText = "Tenant administration profile store is not registered for this desktop session.";
            StatusText = TenantAdministrationProfileStatusText;
            return;
        }

        var tenantId = ResolveTenantAdministrationTenantId();
        var companyId = ResolveTenantAdministrationCompanyId();
        var evidence = WithLedgerBookTenantAdministrationEvidence(
            NormalizeTenantAdministrationEvidence(TenantAdministrationEvidenceText),
            tenantId,
            companyId,
            _configuration?.LedgerBookId,
            LedgerBookAdministrationStudioConfigured,
            ImplementationSandboxConfigured);
        if (evidence.Count == 0)
        {
            TenantAdministrationProfileStatusText = "Retained setup evidence is required before saving tenant administration controls.";
            StatusText = TenantAdministrationProfileStatusText;
            NotifyTenantAdministrationSaveStateChanged();
            return;
        }

        var approvalQueueConfigurations = BuildApprovalQueueConfigurations();
        if (ApprovalQueueStudioConfigured && approvalQueueConfigurations.Count == 0)
        {
            TenantAdministrationProfileStatusText = "Complete approval queue id, display name, workflow kind, approval role/count, segregation policy, and evidence requirement before saving.";
            StatusText = TenantAdministrationProfileStatusText;
            return;
        }

        var dimensionMappingConfigurations = BuildDimensionMappingConfigurations();
        if (DimensionMappingStudioConfigured && dimensionMappingConfigurations.Count == 0)
        {
            TenantAdministrationProfileStatusText = "Complete dimension mapping id, display name, provider id, Meridian/provider dimensions, and evidence requirement before saving.";
            StatusText = TenantAdministrationProfileStatusText;
            return;
        }

        try
        {
            var correlationId = $"wpf-accounting-tenant-admin-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var profile = new AccountingTenantAdministrationProfileDto(
                tenantId,
                companyId,
                TenantScopeConfigured,
                AdminRoleProfileConfigured,
                ScopedAccessPoliciesConfigured,
                ReportingGroupsConfigured,
                AccountingAdminSurfaceConfigured,
                DateTimeOffset.UtcNow,
                DefaultLifecycleActor,
                evidence,
                correlationId,
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
                ApprovalQueueConfigurations: approvalQueueConfigurations,
                DimensionMappingConfigurations: dimensionMappingConfigurations);

            var saved = await _tenantAdministrationProfileStore.UpsertAsync(
                new AccountingTenantAdministrationProfileUpsertRequestDto(
                    profile,
                    DefaultLifecycleActor,
                    correlationId,
                    evidence),
                ct).ConfigureAwait(false);

            ApplyTenantAdministrationProfile(saved, "Tenant administration setup profile saved; production readiness refreshed from retained controls.");
            await RefreshProductionReadinessAsync(ct).ConfigureAwait(false);
            StatusText = TenantAdministrationProfileStatusText;
        }
        catch (Exception ex)
        {
            TenantAdministrationProfileStatusText = $"Tenant administration setup profile save failed: {ex.Message}";
            StatusText = TenantAdministrationProfileStatusText;
        }
    }

    public async Task RetainImplementationSandboxProofAsync(CancellationToken ct = default)
    {
        if (_activeFundProfile is null)
        {
            ImplementationSandboxProofStatusText = "Select a fund-linked context before retaining implementation sandbox proof.";
            StatusText = ImplementationSandboxProofStatusText;
            return;
        }

        if (_tenantAdministrationProfileStore is null)
        {
            ImplementationSandboxProofStatusText = "Tenant administration profile store is not registered for this desktop session.";
            StatusText = ImplementationSandboxProofStatusText;
            return;
        }

        if (_configuration?.LedgerBookId is not { } ledgerBookId)
        {
            ImplementationSandboxProofStatusText = "Select a ledger-book-scoped Accounting Configure workspace before retaining implementation sandbox proof.";
            StatusText = ImplementationSandboxProofStatusText;
            return;
        }

        var tenantId = ResolveTenantAdministrationTenantId();
        var companyId = ResolveTenantAdministrationCompanyId();
        var evidence = WithLedgerBookTenantAdministrationEvidence(
            NormalizeTenantAdministrationEvidence(TenantAdministrationEvidenceText),
            tenantId,
            companyId,
            ledgerBookId,
            LedgerBookAdministrationStudioConfigured,
            implementationSandboxConfigured: true);
        var approvalQueueConfigurations = BuildApprovalQueueConfigurations();
        if (ApprovalQueueStudioConfigured && approvalQueueConfigurations.Count == 0)
        {
            ImplementationSandboxProofStatusText = "Complete approval queue id, display name, workflow kind, approval role/count, segregation policy, and evidence requirement before retaining implementation sandbox proof.";
            StatusText = ImplementationSandboxProofStatusText;
            return;
        }

        var dimensionMappingConfigurations = BuildDimensionMappingConfigurations();
        if (DimensionMappingStudioConfigured && dimensionMappingConfigurations.Count == 0)
        {
            ImplementationSandboxProofStatusText = "Complete dimension mapping id, display name, provider id, Meridian/provider dimensions, and evidence requirement before retaining implementation sandbox proof.";
            StatusText = ImplementationSandboxProofStatusText;
            return;
        }

        try
        {
            var correlationId = $"wpf-accounting-sandbox-proof-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var profile = new AccountingTenantAdministrationProfileDto(
                tenantId,
                companyId,
                TenantScopeConfigured,
                AdminRoleProfileConfigured,
                ScopedAccessPoliciesConfigured,
                ReportingGroupsConfigured,
                AccountingAdminSurfaceConfigured,
                DateTimeOffset.UtcNow,
                DefaultLifecycleActor,
                evidence,
                correlationId,
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
                ImplementationSandboxConfigured: true,
                ApprovalQueueConfigurations: approvalQueueConfigurations,
                DimensionMappingConfigurations: dimensionMappingConfigurations);

            var saved = await _tenantAdministrationProfileStore.UpsertAsync(
                new AccountingTenantAdministrationProfileUpsertRequestDto(
                    profile,
                    DefaultLifecycleActor,
                    correlationId,
                    evidence),
                ct).ConfigureAwait(false);

            ApplyTenantAdministrationProfile(saved, "Tenant administration setup profile saved; production readiness refreshed from retained controls.");
            ImplementationSandboxProofStatusText = "Implementation sandbox proof retained; production readiness refreshed from fixture and ledger-book validation evidence.";
            await RefreshProductionReadinessAsync(ct).ConfigureAwait(false);
            StatusText = ImplementationSandboxProofStatusText;
        }
        catch (Exception ex)
        {
            ImplementationSandboxProofStatusText = $"Implementation sandbox proof save failed: {ex.Message}";
            StatusText = ImplementationSandboxProofStatusText;
        }
    }

    private async Task LoadTenantAdministrationProfileAsync(CancellationToken ct)
    {
        if (_activeFundProfile is null)
        {
            TenantAdministrationProfileStatusText = "Locked until a fund context is selected.";
            TenantAdministrationProfileScopeText = "Tenant and company scope require a fund context.";
            TenantAdministrationProfileUpdatedText = "No retained tenant administration setup profile loaded.";
            ImplementationSandboxProofStatusText = "Implementation sandbox proof is locked until a fund context is selected.";
            TenantAdministrationControlRows.Clear();
            NotifyTenantAdministrationSandboxStateChanged();
            return;
        }

        var tenantId = ResolveTenantAdministrationTenantId();
        var companyId = ResolveTenantAdministrationCompanyId();
        TenantAdministrationProfileScopeText = $"Tenant {tenantId}; company {companyId}.";

        if (_tenantAdministrationProfileStore is null)
        {
            TenantAdministrationProfileStatusText = "Tenant administration profile store is not registered for this desktop session.";
            TenantAdministrationProfileUpdatedText = "Retained tenant setup controls are unavailable.";
            ImplementationSandboxProofStatusText = "Implementation sandbox proof is unavailable for this desktop session.";
            ApplyTenantAdministrationControlRows();
            NotifyTenantAdministrationSaveStateChanged();
            NotifyTenantAdministrationSandboxStateChanged();
            return;
        }

        try
        {
            var profile = await _tenantAdministrationProfileStore.GetAsync(tenantId, companyId, ct).ConfigureAwait(false);
            if (profile is null)
            {
                TenantAdministrationProfileStatusText = "No retained tenant administration setup profile exists for the active fund context.";
                TenantAdministrationProfileUpdatedText = "Save setup controls with retained evidence before production certification.";
                TenantAdministrationEvidenceText = $"evidence://tenant-admin/{tenantId}/{companyId}/setup";
                ImplementationSandboxProofStatusText = "Implementation sandbox proof has not been retained.";
                ApplyTenantAdministrationControlRows();
                NotifyTenantAdministrationSaveStateChanged();
                NotifyTenantAdministrationSandboxStateChanged();
                return;
            }

            ApplyTenantAdministrationProfile(profile, "Tenant administration setup profile loaded from the shared store.");
        }
        catch (Exception ex)
        {
            TenantAdministrationProfileStatusText = $"Tenant administration setup profile could not load: {ex.Message}";
            TenantAdministrationProfileUpdatedText = "Retained tenant setup controls are unavailable.";
            ImplementationSandboxProofStatusText = "Implementation sandbox proof could not load.";
            ApplyTenantAdministrationControlRows();
            NotifyTenantAdministrationSaveStateChanged();
            NotifyTenantAdministrationSandboxStateChanged();
        }
    }

    private void ApplyTenantAdministrationProfile(
        AccountingTenantAdministrationProfileDto profile,
        string statusText)
    {
        TenantScopeConfigured = profile.TenantScopeConfigured;
        AdminRoleProfileConfigured = profile.AdminRoleProfileConfigured;
        ScopedAccessPoliciesConfigured = profile.ScopedAccessPoliciesConfigured;
        ReportingGroupsConfigured = profile.ReportingGroupsConfigured;
        AccountingAdminSurfaceConfigured = profile.AccountingAdminSurfaceConfigured;
        WpfAccountingAdminSurfaceConfigured = profile.WpfAccountingAdminSurfaceConfigured;
        ChartAdministrationStudioConfigured = profile.ChartAdministrationStudioConfigured;
        RuleTestPromotionStudioConfigured = profile.RuleTestPromotionStudioConfigured;
        CloseSetupStudioConfigured = profile.CloseSetupStudioConfigured;
        ProviderMappingStudioConfigured = profile.ProviderMappingStudioConfigured;
        TenantCompanyReportGroupSetupStudioConfigured = profile.TenantCompanyReportGroupSetupStudioConfigured;
        AuditReviewToolingConfigured = profile.AuditReviewToolingConfigured;
        BulkImportExportSafeguardsConfigured = profile.BulkImportExportSafeguardsConfigured;
        PerformanceValidationConfigured = profile.PerformanceValidationConfigured;
        DisasterRecoveryRunbookConfigured = profile.DisasterRecoveryRunbookConfigured;
        LedgerBookAdministrationStudioConfigured = profile.LedgerBookAdministrationStudioConfigured;
        PostingRuleAuthoringStudioConfigured = profile.PostingRuleAuthoringStudioConfigured;
        ApprovalQueueStudioConfigured = profile.ApprovalQueueStudioConfigured;
        ApplyApprovalQueueConfiguration(profile.ApprovalQueueConfigurations.FirstOrDefault());
        DimensionMappingStudioConfigured = profile.DimensionMappingStudioConfigured;
        ApplyDimensionMappingConfiguration(profile.DimensionMappingConfigurations.FirstOrDefault());
        ImplementationSandboxConfigured = profile.ImplementationSandboxConfigured;
        TenantAdministrationEvidenceText = string.Join(Environment.NewLine, profile.EvidenceReferences);
        TenantAdministrationProfileScopeText = $"Tenant {profile.TenantId}; company {profile.CompanyId}.";
        TenantAdministrationProfileUpdatedText =
            $"Last retained by {profile.UpdatedBy} at {UtcFormat.FormatUtcTimestamp(profile.UpdatedAtUtc)}.";
        TenantAdministrationProfileStatusText = statusText;
        ImplementationSandboxProofStatusText = ImplementationSandboxConfigured
            ? "Implementation sandbox proof retained with fixture and ledger-book validation evidence."
            : "Implementation sandbox proof has not been retained.";
        ApplyTenantAdministrationControlRows();
        NotifyTenantAdministrationSaveStateChanged();
        NotifyTenantAdministrationSandboxStateChanged();
    }

    private void NotifyTenantAdministrationSaveStateChanged()
    {
        RaisePropertyChanged(nameof(CanSaveTenantAdministrationProfile));
        SaveTenantAdministrationProfileCommand.NotifyCanExecuteChanged();
    }

    private void NotifyTenantAdministrationSandboxStateChanged()
    {
        RaisePropertyChanged(nameof(CanRetainImplementationSandboxProof));
        RetainImplementationSandboxProofCommand.NotifyCanExecuteChanged();
    }

    private IReadOnlyList<AccountingApprovalQueueConfigurationDto> BuildApprovalQueueConfigurations()
    {
        if (string.IsNullOrWhiteSpace(ApprovalQueueId) ||
            string.IsNullOrWhiteSpace(ApprovalQueueDisplayName) ||
            string.IsNullOrWhiteSpace(ApprovalQueueWorkflowKind) ||
            string.IsNullOrWhiteSpace(ApprovalQueueRequiredApprovalRole) ||
            ApprovalQueueRequiredApprovalCount <= 0 ||
            string.IsNullOrWhiteSpace(ApprovalQueueSegregationPolicy) ||
            string.IsNullOrWhiteSpace(ApprovalQueueEvidenceRequirement))
        {
            return [];
        }

        return
        [
            new AccountingApprovalQueueConfigurationDto(
                ApprovalQueueId.Trim(),
                ApprovalQueueDisplayName.Trim(),
                ApprovalQueueWorkflowKind.Trim(),
                ApprovalQueueRequiredApprovalRole.Trim(),
                ApprovalQueueRequiredApprovalCount,
                ApprovalQueueSegregationPolicy.Trim(),
                ApprovalQueueEvidenceRequirement.Trim())
        ];
    }

    private void ApplyApprovalQueueConfiguration(AccountingApprovalQueueConfigurationDto? configuration)
    {
        configuration ??= new AccountingApprovalQueueConfigurationDto(
            "accounting-configuration-approval",
            "Accounting configuration approval",
            "ConfigurationPromotion",
            "Controller",
            2,
            "Preparer cannot approve own configuration change.",
            "approval-queue;configuration-approval;segregation-review");

        ApprovalQueueId = configuration.QueueId;
        ApprovalQueueDisplayName = configuration.DisplayName;
        ApprovalQueueWorkflowKind = configuration.WorkflowKind;
        ApprovalQueueRequiredApprovalRole = configuration.RequiredApprovalRole;
        ApprovalQueueRequiredApprovalCount = Math.Max(1, configuration.RequiredApprovalCount);
        ApprovalQueueSegregationPolicy = configuration.SegregationPolicy;
        ApprovalQueueEvidenceRequirement = configuration.EvidenceRequirement;
    }

    private IReadOnlyList<AccountingDimensionMappingConfigurationDto> BuildDimensionMappingConfigurations()
    {
        if (string.IsNullOrWhiteSpace(DimensionMappingId) ||
            string.IsNullOrWhiteSpace(DimensionMappingDisplayName) ||
            string.IsNullOrWhiteSpace(DimensionMappingProviderId) ||
            string.IsNullOrWhiteSpace(DimensionMappingMeridianDimensionsText) ||
            string.IsNullOrWhiteSpace(DimensionMappingProviderDimensionsText) ||
            string.IsNullOrWhiteSpace(DimensionMappingEvidenceRequirement))
        {
            return [];
        }

        var providerId = DimensionMappingProviderId.Trim();
        return
        [
            new AccountingDimensionMappingConfigurationDto(
                DimensionMappingId.Trim(),
                DimensionMappingDisplayName.Trim(),
                providerId,
                ParseLedgerDimensionSet(DimensionMappingMeridianDimensionsText, BuildDefaultMeridianExternalGlDimensions(providerId, DimensionMappingId.Trim())),
                ParseLedgerDimensionSet(DimensionMappingProviderDimensionsText, BuildDefaultProviderExternalGlDimensions()),
                DimensionMappingEvidenceRequirement.Trim())
        ];
    }

    private void ApplyDimensionMappingConfiguration(AccountingDimensionMappingConfigurationDto? configuration)
    {
        configuration ??= new AccountingDimensionMappingConfigurationDto(
            "canonical-provider-dimension-map",
            "Canonical provider dimension map",
            "quickbooks-fixture",
            BuildDefaultMeridianExternalGlDimensions("quickbooks-fixture", "canonical-provider-dimension-map"),
            BuildDefaultProviderExternalGlDimensions(),
            "dimension-mapping;external-dimension-mapping;gl-dimension-mapping");

        DimensionMappingId = configuration.MappingId;
        DimensionMappingDisplayName = configuration.DisplayName;
        DimensionMappingProviderId = configuration.ProviderId;
        DimensionMappingMeridianDimensionsText = FormatExternalGlLedgerDimensionSet(configuration.MeridianDimensions);
        DimensionMappingProviderDimensionsText = FormatExternalGlLedgerDimensionSet(configuration.ProviderDimensions);
        DimensionMappingEvidenceRequirement = configuration.EvidenceRequirement;
    }

    private void ApplyTenantAdministrationControlRows()
    {
        TenantAdministrationControlRows.ReplaceWith(
        [
            new AccountingWorkbenchRow(
                "Tenant config",
                TenantScopeConfigured ? "Configured" : "Missing",
                "Tenant scope, accounting configuration, and company setup are retained.",
                ResolveTenantAdministrationTenantId(),
                "tenant-config"),
            new AccountingWorkbenchRow(
                "Admin roles",
                AdminRoleProfileConfigured ? "Configured" : "Missing",
                "Accounting admin role profiles are configured for governed setup.",
                DefaultLifecycleActor,
                "admin-roles"),
            new AccountingWorkbenchRow(
                "Scoped access",
                ScopedAccessPoliciesConfigured ? "Configured" : "Missing",
                "Accounting permissions are scoped to tenant, company, fund, or book boundaries.",
                ResolveTenantAdministrationCompanyId(),
                "scoped-access"),
            new AccountingWorkbenchRow(
                "Reporting groups",
                ReportingGroupsConfigured ? "Configured" : "Missing",
                "Reporting group principals are configured for close, report, and export review.",
                "close/report/export",
                "reporting-groups"),
            new AccountingWorkbenchRow(
                "Operator surface",
                AccountingAdminSurfaceConfigured ? "Configured" : "Missing",
                "Accounting setup controls are exposed through the governed operator surface.",
                "WPF Accounting Configure",
                "operator-surface"),
            new AccountingWorkbenchRow(
                "WPF admin",
                WpfAccountingAdminSurfaceConfigured ? "Configured" : "Missing",
                "Desktop accounting setup controls are exposed through the governed WPF administration surface.",
                "WPF Accounting Configure",
                "wpf-admin-studio"),
            new AccountingWorkbenchRow(
                "Chart admin",
                ChartAdministrationStudioConfigured ? "Configured" : "Missing",
                "Chart, ledger-book chart, account template, and activation controls are retained.",
                "chart setup",
                "chart-administration-studio"),
            new AccountingWorkbenchRow(
                "Rule tests",
                RuleTestPromotionStudioConfigured ? "Configured" : "Missing",
                "Rules Studio tests, dry-run previews, generated postings, and promotion queues are retained.",
                "rules studio",
                "rule-test-promotion-studio"),
            new AccountingWorkbenchRow(
                "Close setup",
                CloseSetupStudioConfigured ? "Configured" : "Missing",
                "Close checklist, dependencies, sign-offs, materiality, late adjustments, and period locks are retained.",
                "close setup",
                "close-setup-studio"),
            new AccountingWorkbenchRow(
                "Provider maps",
                ProviderMappingStudioConfigured ? "Configured" : "Missing",
                "QuickBooks, Xero, NetSuite, dimension mapping, import evidence, and guarded export setup are retained.",
                "provider mapping",
                "provider-mapping-studio"),
            new AccountingWorkbenchRow(
                "Tenant/report setup",
                TenantCompanyReportGroupSetupStudioConfigured ? "Configured" : "Missing",
                "Tenant, company, scoped access, admin role, and report group setup controls are retained.",
                "tenant/company/report",
                "tenant-company-report-group-studio"),
            new AccountingWorkbenchRow(
                "Audit review",
                AuditReviewToolingConfigured ? "Configured" : "Missing",
                "Audit, evidence, transition, and exception review tooling are retained for operations.",
                "audit/evidence review",
                "audit-review-tooling"),
            new AccountingWorkbenchRow(
                "Bulk safeguards",
                BulkImportExportSafeguardsConfigured ? "Configured" : "Missing",
                "Bulk import, guarded export, reconciliation, preview, approval, and rollback safeguards are retained.",
                "import/export",
                "bulk-import-export-safeguards"),
            new AccountingWorkbenchRow(
                "Performance proof",
                PerformanceValidationConfigured ? "Configured" : "Missing",
                "Accounting configuration, ledger queries, close, report, and export performance validation is retained.",
                "load/capacity",
                "performance-validation"),
            new AccountingWorkbenchRow(
                "Recovery runbook",
                DisasterRecoveryRunbookConfigured ? "Configured" : "Missing",
                "Disaster recovery, restore, replay, escalation, and operating runbook evidence is retained.",
                "DR/runbook",
                "disaster-recovery-runbook"),
            new AccountingWorkbenchRow(
                "Book admin",
                LedgerBookAdministrationStudioConfigured ? "Configured" : "Missing",
                "Ledger-book creation, selection, policy, activation, and period setup are retained.",
                "ledger books",
                "ledger-book-administration-studio"),
            new AccountingWorkbenchRow(
                "Rule authoring",
                PostingRuleAuthoringStudioConfigured ? "Configured" : "Missing",
                "Event predicates, scopes, thresholds, formulas, allocations, and posting previews are retained.",
                "posting rules",
                "posting-rule-authoring-studio"),
            new AccountingWorkbenchRow(
                "Approvals",
                ApprovalQueueStudioConfigured ? "Configured" : "Missing",
                "Rule promotion, journal, configuration, and segregation approval queues are retained.",
                "approvals",
                "approval-queue-studio"),
            new AccountingWorkbenchRow(
                "Dimension maps",
                DimensionMappingStudioConfigured ? "Configured" : "Missing",
                $"Canonical and external GL dimension mapping setup is retained for {DimensionMappingProviderId}.",
                string.IsNullOrWhiteSpace(DimensionMappingId) ? "dimensions" : DimensionMappingId,
                "dimension-mapping-studio"),
            new AccountingWorkbenchRow(
                "Sandbox proof",
                ImplementationSandboxConfigured ? "Configured" : "Missing",
                "Implementation fixture, migration rehearsal, import, rule, close, and report validation is retained.",
                "implementation",
                "implementation-sandbox")
        ]);
    }

    private static IReadOnlyList<string> NormalizeTenantAdministrationEvidence(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value
                .Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private void ApplyExternalGlMappingProfileDraft(ExternalGlMappingProfileDto profile)
    {
        ExternalGlMappingProviderId = profile.ProviderId;
        ExternalGlMappingProfileId = profile.ProfileId;
        ExternalGlMappingDisplayName = profile.DisplayName;
        ExternalGlMappingAccountMappingsText = string.Join(
            Environment.NewLine,
            profile.AccountMappings
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => $"{pair.Key}={pair.Value}"));
        var dimensionMapping = profile.DimensionMappings.FirstOrDefault();
        ExternalGlMappingMeridianDimensionsText = FormatExternalGlLedgerDimensionSet(
            dimensionMapping?.MeridianDimensions ?? BuildDefaultMeridianExternalGlDimensions(profile.ProviderId, profile.ProfileId));
        ExternalGlMappingExternalDimensionsText = FormatExternalGlLedgerDimensionSet(
            dimensionMapping?.ExternalDimensions ?? BuildDefaultProviderExternalGlDimensions());
        ExternalGlMappingEvidenceText = $"approval:external-gl-mapping:{profile.ProfileId}";
        ExternalGlMappingProfileCertified = profile.CertificationState == AccountingCertificationStateDto.Certified;
    }

    private static bool HasTenantAdministrationLedgerBookEvidence(
        IReadOnlyList<string> evidence,
        string ledgerBookId,
        string marker)
        => evidence.Any(reference =>
            reference.Contains(ledgerBookId, StringComparison.OrdinalIgnoreCase) &&
            reference.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> BuildImplementationSandboxEvidence(
        string tenantId,
        string companyId,
        string ledgerBookId)
        =>
        [
            $"evidence://tenant-admin/{tenantId}/{companyId}/implementation-sandbox/ledgerBookId={ledgerBookId}",
            $"evidence://tenant-admin/{tenantId}/{companyId}/sandbox-validation/ledgerBookId={ledgerBookId}",
            $"evidence://tenant-admin/{tenantId}/{companyId}/fixture-validation/ledgerBookId={ledgerBookId}",
            $"evidence://tenant-admin/{tenantId}/{companyId}/implementation-fixture/ledgerBookId={ledgerBookId}"
        ];
}
