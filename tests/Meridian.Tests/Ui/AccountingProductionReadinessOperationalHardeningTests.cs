using FluentAssertions;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class AccountingProductionReadinessOperationalHardeningTests
{
    private static readonly Guid LedgerBookId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public async Task ProductionReadinessService_RequiresLedgerBookScopedOperationalHardeningEvidence()
    {
        var services = new ServiceCollection();
        services.AddSingleton<AccountingProductionReadinessService>();
        await using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<AccountingProductionReadinessService>();

        var genericEvidence = await service.AssessAsync(BuildRequest([
            "evidence://tenant-admin/tenant-alpha/company-alpha/tenant-admin/full",
            "approval:tenant-admin:tenant-alpha:company-alpha"
        ]));

        var requiredBookScopedIssueCodes = new[]
        {
            "tenant-admin.tenant-scope-book-evidence-missing",
            "tenant-admin.role-profile-book-evidence-missing",
            "tenant-admin.scoped-access-book-evidence-missing",
            "tenant-admin.operator-surface-book-evidence-missing",
            "tenant-admin.browser-admin-studio-book-evidence-missing",
            "tenant-admin.wpf-admin-studio-book-evidence-missing",
            "tenant-admin.reporting-groups-book-evidence-missing",
            "tenant-admin.chart-administration-studio-book-evidence-missing",
            "tenant-admin.rule-test-promotion-studio-book-evidence-missing",
            "tenant-admin.close-setup-studio-book-evidence-missing",
            "tenant-admin.provider-mapping-studio-book-evidence-missing",
            "tenant-admin.tenant-company-report-group-studio-book-evidence-missing",
            "tenant-admin.audit-review-tooling-book-evidence-missing",
            "tenant-admin.bulk-import-export-safeguards-book-evidence-missing",
            "tenant-admin.performance-validation-book-evidence-missing",
            "tenant-admin.disaster-recovery-runbook-book-evidence-missing",
            "tenant-admin.ledger-book-administration-studio-book-evidence-missing",
            "tenant-admin.posting-rule-authoring-studio-book-evidence-missing",
            "tenant-admin.approval-queue-studio-book-evidence-missing",
            "tenant-admin.dimension-mapping-studio-book-evidence-missing",
            "tenant-admin.implementation-sandbox-book-evidence-missing"
        };
        genericEvidence.TenantAdministration.Should().NotBeNull();
        genericEvidence.TenantAdministration!.CompletedControlCount.Should().Be(23);
        genericEvidence.Issues
            .Where(issue => requiredBookScopedIssueCodes.Contains(issue.Code, StringComparer.OrdinalIgnoreCase))
            .Should()
            .HaveCount(requiredBookScopedIssueCodes.Length)
            .And.OnlyContain(issue =>
                issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration &&
                issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
                issue.EvidenceReferences.Contains("evidence://tenant-admin/tenant-alpha/company-alpha/tenant-admin/full"));
        genericEvidence.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.TenantAdministration &&
            component.Status == AccountingProductionReadinessStatusDto.Blocked);

        var scopedEvidence = await service.AssessAsync(BuildRequest([
            $"evidence://tenant-admin/tenant-alpha/company-alpha/operational-hardening/ledgerBookId={LedgerBookId:D}/tenant-admin/full",
            "approval:tenant-admin:tenant-alpha:company-alpha"
        ]));

        scopedEvidence.TenantAdministration.Should().NotBeNull();
        scopedEvidence.TenantAdministration!.CompletedControlCount.Should().Be(23);
        scopedEvidence.Issues.Should().NotContain(issue =>
            requiredBookScopedIssueCodes.Contains(issue.Code, StringComparer.OrdinalIgnoreCase));
    }

    private static AccountingProductionReadinessRequestDto BuildRequest(IReadOnlyList<string> evidenceLinks)
        => new(
            FundProfileId: "default-fund",
            LedgerBookId: LedgerBookId,
            TenantId: "tenant-alpha",
            CompanyId: "company-alpha",
            TenantScopeConfigured: true,
            AdminRoleProfileConfigured: true,
            ScopedAccessPoliciesConfigured: true,
            ReportingGroupsConfigured: true,
            AccountingAdminSurfaceConfigured: true,
            BrowserAccountingAdminSurfaceConfigured: true,
            WpfAccountingAdminSurfaceConfigured: true,
            ChartAdministrationStudioConfigured: true,
            RuleTestPromotionStudioConfigured: true,
            CloseSetupStudioConfigured: true,
            ProviderMappingStudioConfigured: true,
            TenantCompanyReportGroupSetupStudioConfigured: true,
            AuditReviewToolingConfigured: true,
            BulkImportExportSafeguardsConfigured: true,
            PerformanceValidationConfigured: true,
            DisasterRecoveryRunbookConfigured: true,
            LedgerBookAdministrationStudioConfigured: true,
            PostingRuleAuthoringStudioConfigured: true,
            ApprovalQueueStudioConfigured: true,
            DimensionMappingStudioConfigured: true,
            ImplementationSandboxConfigured: true,
            TenantAdministrationEvidenceLinks: evidenceLinks);
}
