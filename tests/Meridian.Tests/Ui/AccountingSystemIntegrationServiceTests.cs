using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.AccountingSystem.Fixtures;
using Meridian.DataIntegration.AccountingSystem.QuickBooks;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.Identity.Auth;
using Meridian.Identity;
using Meridian.Ledger;
using Meridian.ProviderSdk.AccountingSystem;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using IAccountingReportPackageService = Meridian.FinancialOperations.AccountingClose.IAccountingReportPackageService;

namespace Meridian.Tests.Ui;

public sealed class AccountingSystemIntegrationServiceTests
{
    private static readonly Guid ExternalGlLedgerBookId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public async Task ImportAsync_WithQuickBooksFixture_ReturnsReadOnlyExternalGlEvidence()
    {
        var service = CreateService();

        var detail = await service.ImportAsync(new AccountingSystemImportRequestDto("quickbooks-fixture"));

        detail.Summary.ProviderId.Should().Be("quickbooks-fixture");
        detail.Summary.ChartAccountCount.Should().BeGreaterThan(0);
        detail.Summary.JournalEntryCount.Should().BeGreaterThan(0);
        detail.Summary.TrialBalanceLineCount.Should().BeGreaterThan(0);
        detail.Summary.Warnings.Should().Contain(warning => warning.Contains("source of all ledger truth", StringComparison.OrdinalIgnoreCase));
        detail.Summary.Warnings.Should().Contain(warning => warning.Contains("posting/export is disabled", StringComparison.OrdinalIgnoreCase));
        detail.JournalEntries.Should().OnlyContain(entry => entry.TotalDebits == entry.TotalCredits);
    }

    [Fact]
    public async Task ProductionReadinessService_ReturnsConsolidatedFailClosedAccountingPosture()
    {
        var services = new ServiceCollection();
        var ledgerBookId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var missingGaapBookNodeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        services.AddSingleton<IAccountingSystemProvider, QuickBooksFixtureAccountingProvider>();
        services.AddSingleton<ILedgerJournalStore>(_ => CreateMatchedQuickBooksFixtureLedgerStore(ledgerBookId));
        services.AddSingleton<ILedgerBookService>(sp => new PostgresLedgerBookService(sp.GetRequiredService<ILedgerJournalStore>()));
        services.AddSingleton<AccountingSystemIntegrationService>();
        services.AddSingleton<IAccountingConfigurationStore, InMemoryAccountingConfigurationStore>();
        services.AddSingleton<IAccountingActionAuditStore, InMemoryAccountingActionAuditStore>();
        services.AddSingleton<IAccountingConfigurationService, AccountingConfigurationService>();
        services.AddSingleton<AccountingProductionReadinessService>();
        await using var provider = services.BuildServiceProvider();

        var readiness = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId,
                RequiredLedgerBookScopes:
                [
                    new LedgerBookRequiredScopeDto(
                        missingGaapBookNodeId,
                        FundStructureNodeKindDto.Fund,
                        AccountingBasisKindDto.Gaap,
                        "Default fund GAAP")
                ]));

        readiness.Status.Should().Be(AccountingProductionReadinessStatusDto.Blocked);
        readiness.LedgerBookRollout.Should().NotBeNull();
        readiness.LedgerBookRollout!.Issues.Should().Contain(issue => issue.Code == "LedgerBookRequiredScopeMissing");
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "ledger-books.LedgerBookRequiredScopeMissing" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "external-gl.certified-mapping-missing" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "migration.ledger-book-scope-not-certified" &&
            issue.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "migration.dimensional-backfill-not-certified" &&
            issue.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "migration.tenant-scope-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "migration.company-scope-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        readiness.LedgerBookWorkflows.Should().NotBeNull();
        readiness.LedgerBookWorkflows!.CompletedControlCount.Should().Be(1);
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "ledger-books.workflow-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.LedgerBooks &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "ledger-books.posting-rules-not-certified" &&
            issue.Area == AccountingProductionReadinessAreaDto.LedgerBooks &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "external-gl.live-posting-disabled" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Info);
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "tenant-admin.tenant-scope-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        readiness.TenantAdministration.Should().NotBeNull();
        readiness.TenantAdministration!.CompletedControlCount.Should().Be(0);
        readiness.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.RulesStudio &&
            component.Status == AccountingProductionReadinessStatusDto.Blocked);
        readiness.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.PostingRules &&
            component.Status == AccountingProductionReadinessStatusDto.Blocked &&
            component.Issues.Any(issue => issue.Code == "posting-rules.ledger-book-native-not-certified"));
        readiness.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.TenantAdministration &&
            component.Status == AccountingProductionReadinessStatusDto.Blocked &&
            component.Summary.Contains("tenant administration control", StringComparison.OrdinalIgnoreCase));
        readiness.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            component.Status == AccountingProductionReadinessStatusDto.Blocked &&
            component.Summary.Contains("migration control", StringComparison.OrdinalIgnoreCase));
        readiness.MigrationRolloutPlan.Should().HaveCount(5);
        readiness.MigrationRolloutPlan.Should().Contain(row =>
            row.Kind == AccountingMigrationRunKindDto.LedgerBookScope &&
            row.Sequence == 1 &&
            row.DependencyCodes.Count == 0 &&
            row.ActionRoute == $"{UiApiRoutes.AccountingSystemMigrationRunArtifacts}?fundProfileId=default-fund&ledgerBookId={ledgerBookId:D}");
        readiness.MigrationRolloutPlan.Should().Contain(row =>
            row.Kind == AccountingMigrationRunKindDto.DimensionalBackfill &&
            row.Sequence == 3 &&
            row.DependencyCodes.SequenceEqual(new[] { "ledger-book-scope", "historical-journal-backfill" }) &&
            row.BlockingIssueCodes.Contains("migration.dependency-ledger-book-scope-not-ready") &&
            row.BlockingIssueCodes.Contains("migration.dependency-historical-journal-backfill-not-ready"));
        readiness.ProductionGaps.Should().HaveCount(5);
        readiness.ProductionGaps.Should().Contain(gap =>
            gap.Code == "multi-ledger-native-workflows" &&
            gap.Status == AccountingProductionReadinessStatusDto.Blocked &&
            gap.Areas.Contains(AccountingProductionReadinessAreaDto.LedgerBooks) &&
            gap.Areas.Contains(AccountingProductionReadinessAreaDto.JournalLifecycle) &&
            gap.BlockingIssueCodes.Contains("ledger-books.workflow-evidence-missing") &&
            gap.Routes.Contains(UiApiRoutes.LedgerBookRolloutAssessment));
        readiness.ProductionGaps.Should().Contain(gap =>
            gap.Code == "enterprise-accounting-configuration-studio" &&
            gap.Status == AccountingProductionReadinessStatusDto.Blocked &&
            gap.BlockingIssueCodes.Contains("tenant-admin.browser-admin-studio-required") &&
            gap.BlockingIssueCodes.Contains("tenant-admin.wpf-admin-studio-required") &&
            gap.Routes.Contains(UiApiRoutes.LedgerAccountingConfiguration));
        readiness.ProductionGaps.Should().Contain(gap =>
            gap.Code == "external-gl-guarded-integration" &&
            gap.Status == AccountingProductionReadinessStatusDto.Blocked &&
            gap.BlockingIssueCodes.Contains("external-gl.certified-mapping-missing") &&
            gap.BlockingIssueCodes.Contains("external-gl.ledger-book-native-not-certified"));
        readiness.ProductionGaps.Should().Contain(gap =>
            gap.Code == "dimensional-ledger-reporting" &&
            gap.Status == AccountingProductionReadinessStatusDto.Blocked &&
            gap.BlockingIssueCodes.Contains("dimensions.reporting-evidence-missing"));
        readiness.ProductionGaps.Should().Contain(gap =>
            gap.Code == "production-controls-hardening" &&
            gap.Status == AccountingProductionReadinessStatusDto.Blocked &&
            gap.BlockingIssueCodes.Contains("migration.historical-journal-backfill-not-certified") &&
            gap.BlockingIssueCodes.Contains("tenant-admin.performance-validation-required"));
        readiness.MigrationRunArtifacts.Should().BeEmpty();
        readiness.ExternalGlProviderCount.Should().BeGreaterThan(0);
        readiness.CertifiedExternalGlMappingProfileCount.Should().Be(0);
        readiness.ExternalGlLivePostingEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task ProductionReadinessService_RequiresLedgerBookNativeWorkflowCertification()
    {
        var services = new ServiceCollection();
        var ledgerBookId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var configurationService = new AccountingConfigurationService(
            new InMemoryAccountingConfigurationStore(),
            new InMemoryAccountingActionAuditStore());
        await configurationService.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            "default-fund",
            new PostingRuleDto(
                "rule-alpha-interest",
                "Alpha interest",
                "InterestAccrual",
                TemplateId: "generated",
                RuleVersion: "v1",
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source", 0m),
                    new GeneratedPostingLineDto("credit", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source", 0m)
                ]),
            "controller",
            LedgerBookId: ledgerBookId));
        services.AddSingleton<ILedgerJournalStore>(_ => CreateMatchedQuickBooksFixtureLedgerStore(ledgerBookId));
        services.AddSingleton<ILedgerBookService>(sp => new PostgresLedgerBookService(sp.GetRequiredService<ILedgerJournalStore>()));
        services.AddSingleton<IAccountingConfigurationService>(configurationService);
        services.AddSingleton<AccountingProductionReadinessService>();
        await using var provider = services.BuildServiceProvider();

        var blocked = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId,
                PostingRulesLedgerBookNativeCertified: true));

        blocked.LedgerBookWorkflows.Should().NotBeNull();
        blocked.LedgerBookWorkflows!.LedgerBookId.Should().Be(ledgerBookId);
        blocked.LedgerBookWorkflows.CompletedControlCount.Should().Be(1);
        blocked.LedgerBookWorkflows.RequiredControlCount.Should().Be(9);
        blocked.Issues.Should().Contain(issue =>
            issue.Code == "ledger-books.journal-lifecycle-not-certified" &&
            issue.Area == AccountingProductionReadinessAreaDto.LedgerBooks &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        blocked.Issues.Should().Contain(issue =>
            issue.Code == "ledger-books.posting-rules-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.LedgerBooks &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        blocked.Issues.Should().Contain(issue =>
            issue.Code == "ledger-books.reconciliation-not-certified" &&
            issue.Area == AccountingProductionReadinessAreaDto.LedgerBooks &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        blocked.Issues.Should().Contain(issue =>
            issue.Code == "ledger-books.direct-lending-not-certified" &&
            issue.Area == AccountingProductionReadinessAreaDto.LedgerBooks &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        blocked.Issues.Should().Contain(issue =>
            issue.Code == "ledger-books.strategy-ledger-reads-not-certified" &&
            issue.Area == AccountingProductionReadinessAreaDto.LedgerBooks &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        blocked.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.LedgerBooks &&
            component.Status == AccountingProductionReadinessStatusDto.Blocked &&
            component.Summary.Contains("1/9 ledger-book-native workflow control", StringComparison.OrdinalIgnoreCase));
        blocked.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.PostingRules &&
            component.Status == AccountingProductionReadinessStatusDto.Blocked &&
            component.Issues.Any(issue => issue.Code == "posting-rules.ledger-book-native-evidence-missing"));
        blocked.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.JournalLifecycle &&
            component.Status == AccountingProductionReadinessStatusDto.Blocked &&
            component.Issues.Any(issue => issue.Code == "journal-lifecycle.ledger-book-native-not-certified"));
        blocked.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.CloseReporting &&
            component.Status == AccountingProductionReadinessStatusDto.Blocked &&
            component.Issues.Any(issue => issue.Code == "close-reporting.ledger-book-native-not-certified"));
        blocked.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.ExternalGl &&
            component.Status == AccountingProductionReadinessStatusDto.Unavailable &&
            component.Issues.Any(issue => issue.Code == "external-gl.ledger-book-native-not-certified"));

        var otherLedgerBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var mismatchedEvidence = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId,
                PostingRulesLedgerBookNativeCertified: true,
                JournalLifecycleLedgerBookNativeCertified: true,
                CloseReportingLedgerBookNativeCertified: true,
                ExternalGlLedgerBookNativeCertified: true,
                ReconciliationLedgerBookNativeCertified: true,
                DirectLendingLedgerBookNativeCertified: true,
                StrategyLedgerReadLedgerBookNativeCertified: true,
                LedgerBookWorkflowEvidenceLinks: [$"evidence://ledger-book/{otherLedgerBookId:D}/workflow-certification/full"]));

        mismatchedEvidence.Issues.Should().Contain(issue =>
            issue.Code == "ledger-books.workflow-evidence-scope-mismatch" &&
            issue.Area == AccountingProductionReadinessAreaDto.LedgerBooks &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.EvidenceReferences.Contains($"evidence://ledger-book/{otherLedgerBookId:D}/workflow-certification/full"));

        var incidentalGuidEvidence = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId,
                PostingRulesLedgerBookNativeCertified: true,
                JournalLifecycleLedgerBookNativeCertified: true,
                CloseReportingLedgerBookNativeCertified: true,
                ExternalGlLedgerBookNativeCertified: true,
                ReconciliationLedgerBookNativeCertified: true,
                DirectLendingLedgerBookNativeCertified: true,
                StrategyLedgerReadLedgerBookNativeCertified: true,
                LedgerBookWorkflowEvidenceLinks: [$"evidence://workflow-certification/full?selected={ledgerBookId:D}"]));

        incidentalGuidEvidence.LedgerBookWorkflows.Should().NotBeNull();
        incidentalGuidEvidence.LedgerBookWorkflows!.HasLedgerBookScopedEvidence.Should().BeFalse();
        incidentalGuidEvidence.LedgerBookWorkflows.CompletedControlCount.Should().Be(1);
        incidentalGuidEvidence.Issues.Should().Contain(issue =>
            issue.Code == "ledger-books.workflow-evidence-scope-mismatch" &&
            issue.Area == AccountingProductionReadinessAreaDto.LedgerBooks &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.EvidenceReferences.Contains($"evidence://workflow-certification/full?selected={ledgerBookId:D}"));
        incidentalGuidEvidence.Issues.Should().Contain(issue =>
            issue.Code == "ledger-books.posting-rules-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.LedgerBooks);

        var partialEvidence = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId,
                PostingRulesLedgerBookNativeCertified: true,
                JournalLifecycleLedgerBookNativeCertified: true,
                CloseReportingLedgerBookNativeCertified: true,
                ExternalGlLedgerBookNativeCertified: true,
                ReconciliationLedgerBookNativeCertified: true,
                DirectLendingLedgerBookNativeCertified: true,
                StrategyLedgerReadLedgerBookNativeCertified: true,
                LedgerBookWorkflowEvidenceLinks: [$"evidence://ledger-book/{ledgerBookId:D}/posting-rules/candidate-certification"]));

        partialEvidence.LedgerBookWorkflows.Should().NotBeNull();
        partialEvidence.LedgerBookWorkflows!.CompletedControlCount.Should().Be(3);
        partialEvidence.Issues.Should().NotContain(issue => issue.Code == "ledger-books.posting-rules-evidence-missing");
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "ledger-books.journal-lifecycle-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.LedgerBooks);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "ledger-books.close-reporting-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.LedgerBooks);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "ledger-books.external-gl-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.LedgerBooks);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "ledger-books.reconciliation-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.LedgerBooks);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "ledger-books.direct-lending-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.LedgerBooks);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "ledger-books.strategy-ledger-reads-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.LedgerBooks);
        partialEvidence.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.PostingRules &&
            component.EvidenceReferences.Contains($"evidence://ledger-book/{ledgerBookId:D}/posting-rules/candidate-certification") &&
            component.Issues.All(issue => issue.Code != "posting-rules.ledger-book-native-not-certified" &&
                                          issue.Code != "posting-rules.ledger-book-native-evidence-missing"));
        partialEvidence.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.JournalLifecycle &&
            component.Status == AccountingProductionReadinessStatusDto.Blocked &&
            component.EvidenceReferences.Contains($"evidence://ledger-book/{ledgerBookId:D}/posting-rules/candidate-certification") &&
            component.Issues.Any(issue => issue.Code == "journal-lifecycle.ledger-book-native-evidence-missing"));
        partialEvidence.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.CloseReporting &&
            component.Status == AccountingProductionReadinessStatusDto.Blocked &&
            component.EvidenceReferences.Contains($"evidence://ledger-book/{ledgerBookId:D}/posting-rules/candidate-certification") &&
            component.Issues.Any(issue => issue.Code == "close-reporting.ledger-book-native-evidence-missing"));
        partialEvidence.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.ExternalGl &&
            component.Status == AccountingProductionReadinessStatusDto.Unavailable &&
            component.EvidenceReferences.Contains($"evidence://ledger-book/{ledgerBookId:D}/posting-rules/candidate-certification") &&
            component.Issues.Any(issue => issue.Code == "external-gl.ledger-book-native-evidence-missing"));

        var certifiedEvidence = $"evidence://ledger-book/{ledgerBookId:D}/workflow-certification/full";
        var certified = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId,
                PostingRulesLedgerBookNativeCertified: true,
                JournalLifecycleLedgerBookNativeCertified: true,
                CloseReportingLedgerBookNativeCertified: true,
                ExternalGlLedgerBookNativeCertified: true,
                ReconciliationLedgerBookNativeCertified: true,
                DirectLendingLedgerBookNativeCertified: true,
                StrategyLedgerReadLedgerBookNativeCertified: true,
                LedgerBookWorkflowEvidenceLinks: [certifiedEvidence]));

        certified.LedgerBookWorkflows.Should().NotBeNull();
        certified.LedgerBookWorkflows!.CompletedControlCount.Should().Be(9);
        certified.LedgerBookWorkflows.EvidenceReferences.Should().Contain(certifiedEvidence);
        certified.Issues.Should().NotContain(issue =>
            issue.Area == AccountingProductionReadinessAreaDto.LedgerBooks &&
            (issue.Code.EndsWith("-not-certified", StringComparison.OrdinalIgnoreCase) ||
             issue.Code.EndsWith("-evidence-missing", StringComparison.OrdinalIgnoreCase) ||
             issue.Code == "ledger-books.workflow-evidence-missing" ||
             issue.Code == "ledger-books.workflow-evidence-scope-mismatch"));
        certified.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.LedgerBooks &&
            component.Summary.Contains("9/9 ledger-book-native workflow control", StringComparison.OrdinalIgnoreCase) &&
            component.EvidenceReferences.Contains(certifiedEvidence));
        certified.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.PostingRules &&
            component.EvidenceReferences.Contains(certifiedEvidence) &&
            component.Issues.All(issue => issue.Code != "posting-rules.ledger-book-native-not-certified" &&
                                          issue.Code != "posting-rules.ledger-book-native-evidence-missing"));
        certified.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.JournalLifecycle &&
            component.EvidenceReferences.Contains(certifiedEvidence) &&
            component.Issues.All(issue => issue.Code != "journal-lifecycle.ledger-book-native-not-certified" &&
                                          issue.Code != "journal-lifecycle.ledger-book-native-evidence-missing"));
        certified.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.CloseReporting &&
            component.EvidenceReferences.Contains(certifiedEvidence) &&
            component.Issues.All(issue => issue.Code != "close-reporting.ledger-book-native-not-certified" &&
                                          issue.Code != "close-reporting.ledger-book-native-evidence-missing"));
        certified.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.ExternalGl &&
            component.EvidenceReferences.Contains(certifiedEvidence) &&
            component.Issues.All(issue => issue.Code != "external-gl.ledger-book-native-not-certified" &&
                                          issue.Code != "external-gl.ledger-book-native-evidence-missing"));
    }

    [Fact]
    public async Task ProductionReadinessService_RequiresDimensionalReportQueryCertification()
    {
        var services = new ServiceCollection();
        var ledgerBookId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        services.AddSingleton<IAccountingConfigurationStore, InMemoryAccountingConfigurationStore>();
        services.AddSingleton<IAccountingActionAuditStore, InMemoryAccountingActionAuditStore>();
        services.AddSingleton<IAccountingConfigurationService, AccountingConfigurationService>();
        services.AddSingleton<AccountingProductionReadinessService>();
        await using var provider = services.BuildServiceProvider();

        var otherLedgerBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var blocked = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId,
                PeriodReportDimensionQueriesCertified: true,
                DimensionalReportingEvidenceLinks: [$"evidence://ledger-book/{otherLedgerBookId:D}/dimensions/report-query-certification"]));

        blocked.DimensionalReporting.Should().NotBeNull();
        blocked.DimensionalReporting!.LedgerBookId.Should().Be(ledgerBookId);
        blocked.DimensionalReporting.CompletedControlCount.Should().Be(1);
        blocked.DimensionalReporting.RequiredControlCount.Should().Be(10);
        blocked.Issues.Should().Contain(issue =>
            issue.Code == "dimensions.reporting-evidence-scope-mismatch" &&
            issue.Area == AccountingProductionReadinessAreaDto.DimensionalAccounting &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        blocked.Issues.Should().Contain(issue =>
            issue.Code == "dimensions.period-reports-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.DimensionalAccounting &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        blocked.Issues.Should().Contain(issue =>
            issue.Code == "dimensions.cross-period-reports-not-certified" &&
            issue.Area == AccountingProductionReadinessAreaDto.DimensionalAccounting &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        blocked.Issues.Should().Contain(issue =>
            issue.Code == "dimensions.ledger-line-persistence-not-certified" &&
            issue.Area == AccountingProductionReadinessAreaDto.DimensionalAccounting &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        blocked.Issues.Should().Contain(issue =>
            issue.Code == "dimensions.trial-balance-filters-not-certified" &&
            issue.Area == AccountingProductionReadinessAreaDto.DimensionalAccounting &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        blocked.Issues.Should().Contain(issue =>
            issue.Code == "dimensions.report-package-provenance-not-certified" &&
            issue.Area == AccountingProductionReadinessAreaDto.DimensionalAccounting &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        blocked.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.DimensionalAccounting &&
            component.Summary.Contains("1/10 ledger/query/report/export dimension control", StringComparison.OrdinalIgnoreCase));
        blocked.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.CloseReporting &&
            component.Issues.Any(issue => issue.Code == "close-reporting.dimension-controls-incomplete" &&
                                          issue.EvidenceReferences.Contains($"evidence://ledger-book/{otherLedgerBookId:D}/dimensions/report-query-certification")));

        var incidentalGuidEvidence = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId,
                PeriodReportDimensionQueriesCertified: true,
                CrossPeriodReportDimensionQueriesCertified: true,
                JournalQueryDimensionFiltersCertified: true,
                ExternalExportDimensionMappingCertified: true,
                LedgerLineDimensionsPersistedCertified: true,
                TrialBalanceDimensionFiltersCertified: true,
                ReportPackageDimensionProvenanceCertified: true,
                DimensionalReportingEvidenceLinks: [$"evidence://dimensions/report-query-certification/full?selected={ledgerBookId:D}"]));

        incidentalGuidEvidence.DimensionalReporting.Should().NotBeNull();
        incidentalGuidEvidence.DimensionalReporting!.HasLedgerBookScopedEvidence.Should().BeFalse();
        incidentalGuidEvidence.DimensionalReporting.CompletedControlCount.Should().Be(1);
        incidentalGuidEvidence.Issues.Should().Contain(issue =>
            issue.Code == "dimensions.reporting-evidence-scope-mismatch" &&
            issue.Area == AccountingProductionReadinessAreaDto.DimensionalAccounting &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.EvidenceReferences.Contains($"evidence://dimensions/report-query-certification/full?selected={ledgerBookId:D}"));
        incidentalGuidEvidence.Issues.Should().Contain(issue =>
            issue.Code == "dimensions.period-reports-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.DimensionalAccounting);

        var partialEvidence = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId,
                PeriodReportDimensionQueriesCertified: true,
                CrossPeriodReportDimensionQueriesCertified: true,
                JournalQueryDimensionFiltersCertified: true,
                ExternalExportDimensionMappingCertified: true,
                LedgerLineDimensionsPersistedCertified: true,
                TrialBalanceDimensionFiltersCertified: true,
                ReportPackageDimensionProvenanceCertified: true,
                DimensionalReportingEvidenceLinks: [$"evidence://ledger-book/{ledgerBookId:D}/dimensions/period-reports/trial-balance"]));

        partialEvidence.DimensionalReporting.Should().NotBeNull();
        partialEvidence.DimensionalReporting!.CompletedControlCount.Should().Be(3);
        partialEvidence.Issues.Should().NotContain(issue => issue.Code == "dimensions.period-reports-evidence-missing");
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "dimensions.reporting-dimension-scope-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.DimensionalAccounting);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "dimensions.cross-period-reports-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.DimensionalAccounting);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "dimensions.journal-query-filters-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.DimensionalAccounting);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "dimensions.external-export-mapping-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.DimensionalAccounting);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "dimensions.ledger-line-persistence-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.DimensionalAccounting);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "dimensions.trial-balance-filters-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.DimensionalAccounting);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "dimensions.report-package-provenance-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.DimensionalAccounting);
        partialEvidence.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.CloseReporting &&
            component.EvidenceReferences.Contains($"evidence://ledger-book/{ledgerBookId:D}/dimensions/period-reports/trial-balance") &&
            component.Issues.Any(issue => issue.Code == "close-reporting.dimension-controls-incomplete"));

        var certifiedEvidence = $"evidence://ledger-book/{ledgerBookId:D}/dimensions/report-query-certification/full/dimension-scope/canonical-production";
        var certified = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId,
                PeriodReportDimensionQueriesCertified: true,
                CrossPeriodReportDimensionQueriesCertified: true,
                JournalQueryDimensionFiltersCertified: true,
                ExternalExportDimensionMappingCertified: true,
                LedgerLineDimensionsPersistedCertified: true,
                TrialBalanceDimensionFiltersCertified: true,
                ReportPackageDimensionProvenanceCertified: true,
                DimensionalReportingEvidenceLinks: [certifiedEvidence]));

        certified.DimensionalReporting.Should().NotBeNull();
        certified.DimensionalReporting!.CompletedControlCount.Should().Be(10);
        certified.DimensionalReporting.EvidenceReferences.Should().Contain(certifiedEvidence);
        certified.Issues.Should().NotContain(issue =>
            issue.Area == AccountingProductionReadinessAreaDto.DimensionalAccounting &&
            (issue.Code.StartsWith("dimensions.reporting-", StringComparison.OrdinalIgnoreCase) ||
             issue.Code.EndsWith("-evidence-missing", StringComparison.OrdinalIgnoreCase) ||
             issue.Code == "dimensions.period-reports-not-certified" ||
             issue.Code == "dimensions.cross-period-reports-not-certified" ||
             issue.Code == "dimensions.journal-query-filters-not-certified" ||
             issue.Code == "dimensions.external-export-mapping-not-certified" ||
             issue.Code == "dimensions.ledger-line-persistence-not-certified" ||
             issue.Code == "dimensions.trial-balance-filters-not-certified" ||
             issue.Code == "dimensions.report-package-provenance-not-certified"));
        certified.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.DimensionalAccounting &&
            component.Summary.Contains("10/10 ledger/query/report/export dimension control", StringComparison.OrdinalIgnoreCase) &&
            component.EvidenceReferences.Contains(certifiedEvidence));
        certified.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.CloseReporting &&
            component.EvidenceReferences.Contains(certifiedEvidence) &&
            component.Issues.All(issue => issue.Code != "close-reporting.dimension-controls-incomplete"));
    }

    [Fact]
    public async Task ProductionReadinessService_UsesCertifiedReportPackageDimensionScopeAsProvenanceEvidence()
    {
        var services = new ServiceCollection();
        var ledgerBookId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var retainedPackage = BuildReportPackage(
            ledgerBookId,
            AccountingCertificationStateDto.Certified,
            "dimension-scope-alpha");
        var reportPackageService = new StubAccountingReportPackageService([retainedPackage]);
        services.AddSingleton<IAccountingConfigurationStore, InMemoryAccountingConfigurationStore>();
        services.AddSingleton<IAccountingActionAuditStore, InMemoryAccountingActionAuditStore>();
        services.AddSingleton<IAccountingConfigurationService, AccountingConfigurationService>();
        services.AddSingleton<IAccountingReportPackageService>(reportPackageService);
        services.AddSingleton<AccountingProductionReadinessService>();
        await using var provider = services.BuildServiceProvider();

        var readiness = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId,
                PeriodReportDimensionQueriesCertified: true,
                CrossPeriodReportDimensionQueriesCertified: true,
                JournalQueryDimensionFiltersCertified: true,
                ExternalExportDimensionMappingCertified: true,
                LedgerLineDimensionsPersistedCertified: true,
                TrialBalanceDimensionFiltersCertified: true,
                DimensionalReportingEvidenceLinks:
                [
                    $"evidence://ledger-book/{ledgerBookId:D}/dimensions/report-query-certification/full/dimension-scope/canonical-production"
                ]));

        readiness.DimensionalReporting.Should().NotBeNull();
        reportPackageService.ListCalls.Should().Be(1);
        readiness.DimensionalReporting!.ReportPackageDimensionProvenanceCertified.Should().BeTrue();
        readiness.DimensionalReporting.HasReportPackageDimensionProvenanceEvidence.Should().BeTrue();
        readiness.DimensionalReporting.CompletedControlCount.Should().Be(10);
        readiness.DimensionalReporting.EvidenceReferences.Should().Contain(reference =>
            reference.Contains($"ledger-book/{ledgerBookId:D}", StringComparison.OrdinalIgnoreCase) &&
            reference.Contains("report-package-provenance", StringComparison.OrdinalIgnoreCase) &&
            reference.Contains("dimension-scope/dimension-scope-alpha", StringComparison.OrdinalIgnoreCase));
        readiness.Issues.Should().NotContain(issue =>
            issue.Area == AccountingProductionReadinessAreaDto.DimensionalAccounting &&
            (issue.Code == "dimensions.report-package-provenance-not-certified" ||
             issue.Code == "dimensions.report-package-provenance-evidence-missing"));

        var draftPackageServices = new ServiceCollection();
        var draftReportPackageService = new StubAccountingReportPackageService(
            [BuildReportPackage(ledgerBookId, AccountingCertificationStateDto.ReadyForReview, "dimension-scope-draft")]);
        draftPackageServices.AddSingleton<IAccountingConfigurationStore, InMemoryAccountingConfigurationStore>();
        draftPackageServices.AddSingleton<IAccountingActionAuditStore, InMemoryAccountingActionAuditStore>();
        draftPackageServices.AddSingleton<IAccountingConfigurationService, AccountingConfigurationService>();
        draftPackageServices.AddSingleton<IAccountingReportPackageService>(draftReportPackageService);
        draftPackageServices.AddSingleton<AccountingProductionReadinessService>();
        await using var draftProvider = draftPackageServices.BuildServiceProvider();

        var draftReadiness = await draftProvider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId,
                PeriodReportDimensionQueriesCertified: true,
                CrossPeriodReportDimensionQueriesCertified: true,
                JournalQueryDimensionFiltersCertified: true,
                ExternalExportDimensionMappingCertified: true,
                LedgerLineDimensionsPersistedCertified: true,
                TrialBalanceDimensionFiltersCertified: true,
                DimensionalReportingEvidenceLinks:
                [
                    $"evidence://ledger-book/{ledgerBookId:D}/dimensions/report-query-certification/full/dimension-scope/canonical-production"
                ]));

        draftReadiness.DimensionalReporting.Should().NotBeNull();
        draftReportPackageService.ListCalls.Should().Be(1);
        draftReadiness.DimensionalReporting!.ReportPackageDimensionProvenanceCertified.Should().BeFalse();
        draftReadiness.Issues.Should().Contain(issue =>
            issue.Area == AccountingProductionReadinessAreaDto.DimensionalAccounting &&
            issue.Code == "dimensions.report-package-provenance-not-certified");
    }

    [Fact]
    public async Task ProductionReadinessService_RequiresTenantAdministrationControlsAndEvidence()
    {
        var services = new ServiceCollection();
        services.AddSingleton<AccountingProductionReadinessService>();
        await using var provider = services.BuildServiceProvider();

        var blocked = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(FundProfileId: "default-fund"));

        blocked.TenantAdministration.Should().NotBeNull();
        blocked.TenantAdministration!.HasTenantScope.Should().BeFalse();
        blocked.Issues.Should().Contain(issue =>
            issue.Code == "tenant-admin.evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);

        var partialEvidence = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
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
                TenantAdministrationEvidenceLinks:
                [
                    "evidence://tenant-admin/tenant-alpha/company-alpha/setup-certified",
                    "evidence://tenant-admin/tenant-alpha/company-alpha/admin-role/accounting-controller",
                    "approval:tenant-admin:tenant-alpha:company-alpha"
                ]));

        partialEvidence.TenantAdministration.Should().NotBeNull();
        partialEvidence.TenantAdministration!.CompletedControlCount.Should().Be(3);
        partialEvidence.TenantAdministration.HasTenantCompanyScopedEvidence.Should().BeTrue();
        partialEvidence.Issues.Should().NotContain(issue => issue.Code == "tenant-admin.role-profile-evidence-missing");
        partialEvidence.Issues.Should().NotContain(issue => issue.Code == "tenant-admin.evidence-scope-mismatch");
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "tenant-admin.tenant-scope-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "tenant-admin.scoped-access-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "tenant-admin.reporting-groups-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "tenant-admin.browser-admin-studio-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "tenant-admin.wpf-admin-studio-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "tenant-admin.chart-administration-studio-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "tenant-admin.rule-test-promotion-studio-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "tenant-admin.close-setup-studio-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "tenant-admin.provider-mapping-studio-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "tenant-admin.tenant-company-report-group-studio-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "tenant-admin.audit-review-tooling-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "tenant-admin.bulk-import-export-safeguards-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "tenant-admin.performance-validation-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "tenant-admin.disaster-recovery-runbook-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "tenant-admin.ledger-book-administration-studio-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "tenant-admin.posting-rule-authoring-studio-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "tenant-admin.approval-queue-studio-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "tenant-admin.dimension-mapping-studio-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration);
        partialEvidence.Issues.Should().Contain(issue =>
            issue.Code == "tenant-admin.implementation-sandbox-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration);

        var readyControls = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
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
                TenantAdministrationEvidenceLinks:
                [
                    "evidence://tenant-admin/tenant-alpha/company-alpha/tenant-admin/full",
                    "approval:tenant-admin:tenant-alpha:company-alpha"
                ]));

        readyControls.TenantAdministration.Should().NotBeNull();
        readyControls.TenantAdministration!.TenantId.Should().Be("tenant-alpha");
        readyControls.TenantAdministration.CompanyId.Should().Be("company-alpha");
        readyControls.TenantAdministration.CompletedControlCount.Should().Be(23);
        readyControls.TenantAdministration.HasRetainedEvidence.Should().BeTrue();
        readyControls.TenantAdministration.HasTenantCompanyScopedEvidence.Should().BeTrue();
        readyControls.Issues.Should().NotContain(issue =>
            issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration);
        readyControls.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.TenantAdministration &&
            component.Status == AccountingProductionReadinessStatusDto.Ready &&
            component.EvidenceReferences.Contains("evidence://tenant-admin/tenant-alpha/company-alpha/tenant-admin/full"));
    }

    [Fact]
    public async Task ProductionReadinessService_RequiresTenantAdministrationEvidenceForRequestedTenantAndCompany()
    {
        var services = new ServiceCollection();
        services.AddSingleton<AccountingProductionReadinessService>();
        await using var provider = services.BuildServiceProvider();

        var readiness = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
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
                TenantAdministrationEvidenceLinks:
                [
                    "evidence://tenant-admin/tenant-alpha/tenant-admin/full",
                    "evidence://tenant-admin/tenant-alpha/company-beta/tenant-admin/full",
                    "approval:tenant-admin:tenant-alpha"
                ]));

        readiness.TenantAdministration.Should().NotBeNull();
        readiness.TenantAdministration!.HasTenantCompanyScopedEvidence.Should().BeFalse();
        readiness.TenantAdministration.CompletedControlCount.Should().Be(2);
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "tenant-admin.evidence-scope-mismatch" &&
            issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.EvidenceReferences.Contains("evidence://tenant-admin/tenant-alpha/company-beta/tenant-admin/full"));
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "tenant-admin.role-profile-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration);
        readiness.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.TenantAdministration &&
            component.Status == AccountingProductionReadinessStatusDto.Blocked);
    }

    [Fact]
    public async Task ProductionReadinessService_BlocksExternalGlReadinessWithoutLedgerBookScope()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAccountingSystemProvider, QuickBooksFixtureAccountingProvider>();
        services.AddSingleton<AccountingSystemIntegrationService>();
        services.AddSingleton<AccountingProductionReadinessService>();
        await using var provider = services.BuildServiceProvider();

        var readiness = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(FundProfileId: "default-fund"));

        readiness.LedgerBookId.Should().BeNull();
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "external-gl.ledger-book-scope-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.ExternalGl &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "external-gl.ledger-book-workflow-scope-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.ExternalGl &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        readiness.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.ExternalGl &&
            component.Status == AccountingProductionReadinessStatusDto.Blocked &&
            component.Summary.Contains("ledger book missing", StringComparison.OrdinalIgnoreCase));
        readiness.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.PostingRules &&
            component.Status == AccountingProductionReadinessStatusDto.Unavailable &&
            component.Issues.Any(issue => issue.Code == "posting-rules.configuration-missing") &&
            component.Issues.Any(issue => issue.Code == "posting-rules.ledger-book-workflow-scope-missing"));
    }

    [Fact]
    public async Task ProductionReadinessService_BlocksExternalGlReadinessWhenProviderSupportsLivePosting()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAccountingSystemProvider>(_ => new PostingCapableAccountingSystemProvider());
        services.AddSingleton<AccountingSystemIntegrationService>();
        services.AddSingleton<AccountingProductionReadinessService>();
        await using var provider = services.BuildServiceProvider();

        var readiness = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookId: ExternalGlLedgerBookId));

        readiness.ExternalGlLivePostingEnabled.Should().BeTrue();
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "external-gl.live-posting-enabled" &&
            issue.Area == AccountingProductionReadinessAreaDto.ExternalGl &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.EvidenceReferences.Contains("external-gl-provider:posting-provider"));
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "external-gl.ledger-book-native-not-certified" &&
            issue.Area == AccountingProductionReadinessAreaDto.ExternalGl &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        readiness.Issues.Should().NotContain(issue => issue.Code == "external-gl.live-posting-disabled");
        readiness.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.ExternalGl &&
            component.Status == AccountingProductionReadinessStatusDto.Blocked &&
            component.Summary.Contains("live posting enabled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProductionReadinessService_LoadsRetainedTenantAdministrationProfileFromStore()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAccountingTenantAdministrationProfileStore, InMemoryAccountingTenantAdministrationProfileStore>();
        services.AddSingleton<AccountingProductionReadinessService>();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IAccountingTenantAdministrationProfileStore>();

        await store.UpsertAsync(new AccountingTenantAdministrationProfileUpsertRequestDto(
            new AccountingTenantAdministrationProfileDto(
                "tenant-alpha",
                "company-alpha",
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
                UpdatedAtUtc: DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                UpdatedBy: "controller",
                EvidenceReferences: ["evidence://tenant-admin/tenant-alpha/company-alpha/tenant-admin/full"]),
            "controller",
            CorrelationId: "tenant-admin-tenant-alpha",
            EvidenceLinks: ["approval:tenant-admin:tenant-alpha:company-alpha"]));

        var readiness = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                TenantId: "tenant-alpha",
                CompanyId: "company-alpha"));

        readiness.TenantAdministration.Should().NotBeNull();
        readiness.TenantAdministration!.CompletedControlCount.Should().Be(23);
        readiness.TenantAdministration.HasTenantCompanyScopedEvidence.Should().BeTrue();
        readiness.TenantAdministration.EvidenceReferences.Should().Contain("evidence://tenant-admin/tenant-alpha/company-alpha/tenant-admin/full");
        readiness.TenantAdministration.EvidenceReferences.Should().Contain("approval:tenant-admin:tenant-alpha:company-alpha");
        readiness.TenantAdministration.EvidenceReferences.Should().Contain("correlation:tenant-admin-tenant-alpha");
        readiness.Issues.Should().NotContain(issue => issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration);
        readiness.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.TenantAdministration &&
            component.Status == AccountingProductionReadinessStatusDto.Ready);
    }

    [Fact]
    public async Task ProductionReadinessService_LoadsRetainedProductionCertificationProfileFromStore()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAccountingProductionCertificationProfileStore, InMemoryAccountingProductionCertificationProfileStore>();
        services.AddSingleton<IAccountingConfigurationStore, InMemoryAccountingConfigurationStore>();
        services.AddSingleton<IAccountingActionAuditStore, InMemoryAccountingActionAuditStore>();
        services.AddSingleton<IAccountingConfigurationService, AccountingConfigurationService>();
        services.AddSingleton<AccountingProductionReadinessService>();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IAccountingProductionCertificationProfileStore>();

        await store.UpsertAsync(new AccountingProductionCertificationProfileUpsertRequestDto(
            new AccountingProductionCertificationProfileDto(
                "default-fund",
                ExternalGlLedgerBookId,
                PostingRulesLedgerBookNativeCertified: true,
                JournalLifecycleLedgerBookNativeCertified: true,
                CloseReportingLedgerBookNativeCertified: true,
                ExternalGlLedgerBookNativeCertified: true,
                PeriodReportDimensionQueriesCertified: true,
                CrossPeriodReportDimensionQueriesCertified: true,
                JournalQueryDimensionFiltersCertified: true,
                ExternalExportDimensionMappingCertified: true,
                UpdatedAtUtc: DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                UpdatedBy: "controller",
                EvidenceReferences:
                [
                    $"evidence://tenant/tenant-alpha/company/company-alpha/fund/default-fund/ledger-book/{ExternalGlLedgerBookId:D}/production-certification/full/dimension-scope/canonical-production"
                ],
                TenantId: "tenant-alpha",
                CompanyId: "company-alpha",
                ReconciliationLedgerBookNativeCertified: true,
                DirectLendingLedgerBookNativeCertified: true,
                StrategyLedgerReadLedgerBookNativeCertified: true,
                LedgerLineDimensionsPersistedCertified: true,
                TrialBalanceDimensionFiltersCertified: true,
                ReportPackageDimensionProvenanceCertified: true),
            "controller",
            CorrelationId: "production-certification-default-fund",
            EvidenceLinks: [$"approval:tenant:tenant-alpha:company:company-alpha:fund:default-fund:ledger-book:{ExternalGlLedgerBookId:D}:production-certification"]));

        var readiness = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookId: ExternalGlLedgerBookId,
                TenantId: "tenant-alpha",
                CompanyId: "company-alpha"));

        readiness.LedgerBookWorkflows.Should().NotBeNull();
        readiness.LedgerBookWorkflows!.CompletedControlCount.Should().Be(9);
        readiness.DimensionalReporting.Should().NotBeNull();
        readiness.DimensionalReporting!.CompletedControlCount.Should().Be(10);
        readiness.Issues.Should().NotContain(issue =>
            issue.Area == AccountingProductionReadinessAreaDto.LedgerBooks &&
            (issue.Code.Contains("workflow", StringComparison.OrdinalIgnoreCase) ||
             issue.Code.EndsWith("-not-certified", StringComparison.OrdinalIgnoreCase)));
        readiness.Issues.Should().NotContain(issue =>
            issue.Area == AccountingProductionReadinessAreaDto.DimensionalAccounting &&
            (issue.Code.StartsWith("dimensions.reporting-", StringComparison.OrdinalIgnoreCase) ||
             issue.Code.EndsWith("-not-certified", StringComparison.OrdinalIgnoreCase)));
        readiness.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.LedgerBooks &&
            component.EvidenceReferences.Contains($"approval:tenant:tenant-alpha:company:company-alpha:fund:default-fund:ledger-book:{ExternalGlLedgerBookId:D}:production-certification"));
        readiness.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.DimensionalAccounting &&
            component.EvidenceReferences.Contains($"approval:tenant:tenant-alpha:company:company-alpha:fund:default-fund:ledger-book:{ExternalGlLedgerBookId:D}:production-certification"));
    }

    [Fact]
    public async Task ProductionReadinessService_LoadsRulesStudioByTenantCompanyScope()
    {
        var configurationService = new AccountingConfigurationService(
            new InMemoryAccountingConfigurationStore(),
            new InMemoryAccountingActionAuditStore());
        var ledgerBookId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        await configurationService.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            "default-fund",
            new PostingRuleDto(
                "rule-alpha-interest",
                "Alpha interest",
                "InterestAccrual",
                TemplateId: "generated",
                RuleVersion: "v1",
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source", 0m),
                    new GeneratedPostingLineDto("credit", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source", 0m)
                ]),
            "controller",
            CompanyId: "company-alpha",
            LedgerBookId: ledgerBookId,
            TenantId: "tenant-alpha"));
        var services = new ServiceCollection();
        services.AddSingleton<IAccountingConfigurationService>(configurationService);
        services.AddSingleton<AccountingProductionReadinessService>();
        await using var provider = services.BuildServiceProvider();

        var alpha = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                TenantId: "tenant-alpha",
                CompanyId: "company-alpha",
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId));
        var beta = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                TenantId: "tenant-beta",
                CompanyId: "company-beta",
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId));

        alpha.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.RulesStudio &&
            component.Issues.All(issue => issue.Code != "rules-studio.no-rules"));
        beta.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.RulesStudio &&
            component.Issues.Any(issue => issue.Code == "rules-studio.no-rules"));
    }

    [Fact]
    public async Task ProductionReadinessService_DoesNotUseRetainedProductionCertificationProfileOutsideTenantCompanyScope()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAccountingProductionCertificationProfileStore, InMemoryAccountingProductionCertificationProfileStore>();
        services.AddSingleton<IAccountingConfigurationStore, InMemoryAccountingConfigurationStore>();
        services.AddSingleton<IAccountingActionAuditStore, InMemoryAccountingActionAuditStore>();
        services.AddSingleton<IAccountingConfigurationService, AccountingConfigurationService>();
        services.AddSingleton<AccountingProductionReadinessService>();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IAccountingProductionCertificationProfileStore>();

        await store.UpsertAsync(new AccountingProductionCertificationProfileUpsertRequestDto(
            new AccountingProductionCertificationProfileDto(
                "default-fund",
                ExternalGlLedgerBookId,
                PostingRulesLedgerBookNativeCertified: true,
                JournalLifecycleLedgerBookNativeCertified: true,
                CloseReportingLedgerBookNativeCertified: true,
                ExternalGlLedgerBookNativeCertified: true,
                PeriodReportDimensionQueriesCertified: true,
                CrossPeriodReportDimensionQueriesCertified: true,
                JournalQueryDimensionFiltersCertified: true,
                ExternalExportDimensionMappingCertified: true,
                UpdatedAtUtc: DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                UpdatedBy: "controller",
                EvidenceReferences: [$"evidence://ledger-book/{ExternalGlLedgerBookId:D}/production-certification/tenant-alpha"],
                TenantId: "tenant-alpha",
                CompanyId: "company-alpha",
                ReconciliationLedgerBookNativeCertified: true,
                DirectLendingLedgerBookNativeCertified: true,
                StrategyLedgerReadLedgerBookNativeCertified: true,
                LedgerLineDimensionsPersistedCertified: true,
                TrialBalanceDimensionFiltersCertified: true,
                ReportPackageDimensionProvenanceCertified: true),
            "controller",
            CorrelationId: "production-certification-tenant-alpha",
            EvidenceLinks: [$"approval:ledger-book:{ExternalGlLedgerBookId:D}:production-certification:tenant-alpha"]));

        var readiness = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookId: ExternalGlLedgerBookId,
                TenantId: "tenant-beta",
                CompanyId: "company-alpha"));

        readiness.LedgerBookWorkflows.Should().NotBeNull();
        readiness.LedgerBookWorkflows!.CompletedControlCount.Should().Be(1);
        readiness.DimensionalReporting.Should().NotBeNull();
        readiness.DimensionalReporting!.CompletedControlCount.Should().Be(1);
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "ledger-books.workflow-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.LedgerBooks &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "dimensions.reporting-evidence-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.DimensionalAccounting &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        readiness.Components.Should().NotContain(component =>
            component.EvidenceReferences.Any(reference => reference.Contains("tenant-alpha", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task ProductionReadinessService_RequiresRetainedMigrationRunArtifactsForCertifiedControls()
    {
        var services = new ServiceCollection();
        services.AddSingleton<AccountingProductionReadinessService>();
        await using var provider = services.BuildServiceProvider();

        var readiness = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookMigrationCertified: true,
                HistoricalJournalBackfillCertified: true,
                DimensionalBackfillCertified: true,
                AccountingConfigurationPromotionCertified: true,
                CloseReportingEvidenceMigrationCertified: true,
                MigrationEvidenceLinks: ["evidence://migration/control-certification/default-fund"],
                MigrationRunArtifacts:
                [
                    new AccountingMigrationRunArtifactDto(
                        "migration-run-ledger-book-scope-default-fund",
                        AccountingMigrationRunKindDto.LedgerBookScope,
                        AccountingMigrationRunStatusDto.Certified,
                        DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
                        CompletedAtUtc: DateTimeOffset.Parse("2026-02-01T00:05:00Z"),
                        Actor: "controller",
                        MigratedRecordCount: 24,
                        IssueCount: 0,
                        EvidenceReferences: ["evidence://migration/ledger-book-scope/default-fund"],
                        FundProfileId: "default-fund",
                        Summary: "Ledger-book scope migration completed and certified."),
                    new AccountingMigrationRunArtifactDto(
                        "migration-run-dimensional-backfill-default-fund",
                        AccountingMigrationRunKindDto.DimensionalBackfill,
                        AccountingMigrationRunStatusDto.Failed,
                        DateTimeOffset.Parse("2026-02-01T00:06:00Z"),
                        CompletedAtUtc: DateTimeOffset.Parse("2026-02-01T00:07:00Z"),
                        Actor: "controller",
                        MigratedRecordCount: 12,
                        IssueCount: 3,
                        EvidenceReferences: ["evidence://migration/dimensional-backfill/default-fund/failed"],
                        FundProfileId: "default-fund",
                        Summary: "Dimensional backfill failed retained validation.")
                ]));

        readiness.MigrationRunArtifacts.Should().HaveCount(2);
        readiness.MigrationRolloutPlan.Should().HaveCount(5);
        readiness.MigrationRolloutPlan.Should().Contain(row =>
            row.Kind == AccountingMigrationRunKindDto.LedgerBookScope &&
            row.Sequence == 1 &&
            row.DependencyCodes.Count == 0 &&
            row.ActionRoute == $"{UiApiRoutes.AccountingSystemMigrationRunArtifacts}?fundProfileId=default-fund" &&
            row.Status == AccountingProductionReadinessStatusDto.Ready &&
            row.LatestRunId == "migration-run-ledger-book-scope-default-fund" &&
            row.MigratedRecordCount == 24 &&
            row.EvidenceReferences.Contains("evidence://migration/ledger-book-scope/default-fund"));
        readiness.MigrationRolloutPlan.Should().Contain(row =>
            row.Kind == AccountingMigrationRunKindDto.HistoricalJournalBackfill &&
            row.Sequence == 2 &&
            row.DependencyCodes.SequenceEqual(new[] { "ledger-book-scope" }) &&
            row.Status == AccountingProductionReadinessStatusDto.Blocked &&
            row.BlockingIssueCodes.Contains("migration.historical-journal-backfill-certified-run-missing"));
        readiness.MigrationRolloutPlan.Should().Contain(row =>
            row.Kind == AccountingMigrationRunKindDto.DimensionalBackfill &&
            row.Sequence == 3 &&
            row.DependencyCodes.SequenceEqual(new[] { "ledger-book-scope", "historical-journal-backfill" }) &&
            row.Status == AccountingProductionReadinessStatusDto.Blocked &&
            row.LatestRunStatus == AccountingMigrationRunStatusDto.Failed &&
            row.BlockingIssueCodes.Contains("migration.dimensional-backfill-run-failed") &&
            row.BlockingIssueCodes.Contains("migration.dependency-historical-journal-backfill-not-ready"));
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "migration.historical-journal-backfill-certified-run-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "migration.dimensional-backfill-run-failed" &&
            issue.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.EvidenceReferences.Contains("evidence://migration/dimensional-backfill/default-fund/failed"));
        readiness.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            component.Status == AccountingProductionReadinessStatusDto.Blocked &&
            component.Summary.Contains("retained certified migration run artifact", StringComparison.OrdinalIgnoreCase) &&
            component.EvidenceReferences.Contains("evidence://migration/ledger-book-scope/default-fund"));
    }

    [Fact]
    public async Task ProductionReadinessService_LoadsRetainedMigrationArtifactsFromStore()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAccountingMigrationRunArtifactStore, InMemoryAccountingMigrationRunArtifactStore>();
        services.AddSingleton<AccountingProductionReadinessService>();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IAccountingMigrationRunArtifactStore>();
        var ledgerBookId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        await store.UpsertAsync(new AccountingMigrationRunArtifactUpsertRequestDto(
            new AccountingMigrationRunArtifactDto(
                "migration-run-ledger-book-scope-default-fund",
                AccountingMigrationRunKindDto.LedgerBookScope,
                AccountingMigrationRunStatusDto.Certified,
                DateTimeOffset.Parse("2026-02-02T00:00:00Z"),
                CompletedAtUtc: DateTimeOffset.Parse("2026-02-02T00:05:00Z"),
                MigratedRecordCount: 42,
                EvidenceReferences: ["evidence://migration/ledger-book-scope/default-fund/certified"],
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId,
                Summary: "Ledger-book migration scope certified."),
            "controller",
            CorrelationId: "migration-ledger-book-scope-default-fund"));

        var readiness = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId,
                LedgerBookMigrationCertified: true));

        readiness.MigrationRunArtifacts.Should().ContainSingle(artifact =>
            artifact.RunId == "migration-run-ledger-book-scope-default-fund" &&
            artifact.Actor == "controller" &&
            artifact.EvidenceReferences.Contains("evidence://migration/ledger-book-scope/default-fund/certified") &&
            artifact.EvidenceReferences.Contains("correlation:migration-ledger-book-scope-default-fund"));
        readiness.Issues.Should().NotContain(issue => issue.Code == "migration.ledger-book-scope-certified-run-missing");
        readiness.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            component.EvidenceReferences.Contains("evidence://migration/ledger-book-scope/default-fund/certified"));
    }

    [Fact]
    public async Task ProductionReadinessService_RejectsCertifiedMigrationRunArtifactsWithRetainedIssues()
    {
        var services = new ServiceCollection();
        services.AddSingleton<AccountingProductionReadinessService>();
        await using var provider = services.BuildServiceProvider();
        var ledgerBookId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var readiness = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId,
                HistoricalJournalBackfillCertified: true,
                MigrationRunArtifacts:
                [
                    new AccountingMigrationRunArtifactDto(
                        "migration-run-historical-journal-backfill-default-fund",
                        AccountingMigrationRunKindDto.HistoricalJournalBackfill,
                        AccountingMigrationRunStatusDto.Certified,
                        DateTimeOffset.Parse("2026-02-02T00:00:00Z"),
                        CompletedAtUtc: DateTimeOffset.Parse("2026-02-02T00:05:00Z"),
                        MigratedRecordCount: 24,
                        IssueCount: 2,
                        EvidenceReferences: ["evidence://migration/historical-journal-backfill/default-fund/certified-with-issues"],
                        FundProfileId: "default-fund",
                        LedgerBookId: ledgerBookId,
                        Summary: "Historical journal backfill was marked certified with unresolved issue rows.")
                ]));

        readiness.Issues.Should().Contain(issue =>
            issue.Code == "migration.historical-journal-backfill-certified-run-has-issues" &&
            issue.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.EvidenceReferences.Contains("evidence://migration/historical-journal-backfill/default-fund/certified-with-issues"));
        readiness.MigrationRolloutPlan.Should().Contain(row =>
            row.Kind == AccountingMigrationRunKindDto.HistoricalJournalBackfill &&
            row.Status == AccountingProductionReadinessStatusDto.Blocked &&
            row.BlockingIssueCodes.Contains("migration.historical-journal-backfill-certified-run-has-issues"));
        readiness.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            component.Status == AccountingProductionReadinessStatusDto.Blocked);
    }

    [Fact]
    public async Task ProductionReadinessService_DoesNotUseUnscopedMigrationArtifactsForBookScopedReadiness()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAccountingMigrationRunArtifactStore, InMemoryAccountingMigrationRunArtifactStore>();
        services.AddSingleton<AccountingProductionReadinessService>();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IAccountingMigrationRunArtifactStore>();
        var ledgerBookId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        await store.UpsertAsync(new AccountingMigrationRunArtifactUpsertRequestDto(
            new AccountingMigrationRunArtifactDto(
                "migration-run-ledger-book-scope-unscoped",
                AccountingMigrationRunKindDto.LedgerBookScope,
                AccountingMigrationRunStatusDto.Certified,
                DateTimeOffset.Parse("2026-02-02T00:00:00Z"),
                CompletedAtUtc: DateTimeOffset.Parse("2026-02-02T00:05:00Z"),
                MigratedRecordCount: 42,
                EvidenceReferences: ["evidence://migration/ledger-book-scope/default-fund/unscoped-certified"],
                FundProfileId: "default-fund",
                Summary: "Fund-level ledger-book migration was certified before book-specific rollout."),
            "controller",
            CorrelationId: "migration-ledger-book-scope-unscoped"));

        var readiness = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId,
                LedgerBookMigrationCertified: true));

        readiness.MigrationRunArtifacts.Should().BeEmpty();
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "migration.ledger-book-scope-certified-run-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        readiness.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            component.Status == AccountingProductionReadinessStatusDto.Blocked);
    }

    [Fact]
    public async Task ProductionReadinessService_RejectsMigrationArtifactsOutsideRequestedLedgerBookScope()
    {
        var services = new ServiceCollection();
        services.AddSingleton<AccountingProductionReadinessService>();
        await using var provider = services.BuildServiceProvider();
        var requestedLedgerBookId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var otherLedgerBookId = Guid.Parse("99999999-2222-3333-4444-555555555555");

        var readiness = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookId: requestedLedgerBookId,
                LedgerBookMigrationCertified: true,
                MigrationRunArtifacts:
                [
                    new AccountingMigrationRunArtifactDto(
                        "migration-run-ledger-book-scope-other-book",
                        AccountingMigrationRunKindDto.LedgerBookScope,
                        AccountingMigrationRunStatusDto.Certified,
                        DateTimeOffset.Parse("2026-02-02T00:00:00Z"),
                        CompletedAtUtc: DateTimeOffset.Parse("2026-02-02T00:05:00Z"),
                        MigratedRecordCount: 42,
                        EvidenceReferences: ["evidence://migration/ledger-book-scope/default-fund/other-book/certified"],
                        FundProfileId: "default-fund",
                        LedgerBookId: otherLedgerBookId,
                        Summary: "Ledger-book migration scope certified for another book.")
                ]));

        readiness.Issues.Should().Contain(issue =>
            issue.Code == "migration.ledger-book-scope-artifact-scope-mismatch" &&
            issue.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.EvidenceReferences.Contains("evidence://migration/ledger-book-scope/default-fund/other-book/certified"));
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "migration.ledger-book-scope-certified-run-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        readiness.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            component.Status == AccountingProductionReadinessStatusDto.Blocked);
    }

    [Fact]
    public async Task ProductionReadinessService_RejectsDimensionalBackfillArtifactWithMismatchedDimensions()
    {
        var services = new ServiceCollection();
        services.AddSingleton<AccountingProductionReadinessService>();
        await using var provider = services.BuildServiceProvider();
        var requestedLedgerBookId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var otherLedgerBookId = Guid.Parse("99999999-2222-3333-4444-555555555555");

        var readiness = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookId: requestedLedgerBookId,
                DimensionalBackfillCertified: true,
                MigrationRunArtifacts:
                [
                    new AccountingMigrationRunArtifactDto(
                        "migration-run-dimensional-backfill-default-fund",
                        AccountingMigrationRunKindDto.DimensionalBackfill,
                        AccountingMigrationRunStatusDto.Certified,
                        DateTimeOffset.Parse("2026-02-02T00:00:00Z"),
                        CompletedAtUtc: DateTimeOffset.Parse("2026-02-02T00:05:00Z"),
                        MigratedRecordCount: 42,
                        EvidenceReferences: ["evidence://migration/dimensional-backfill/default-fund/certified"],
                        FundProfileId: "default-fund",
                        LedgerBookId: requestedLedgerBookId,
                        Summary: "Dimensional backfill certified with stale book dimensions.",
                        Dimensions: new LedgerDimensionSetDto(
                            FundId: "default-fund",
                            BookId: otherLedgerBookId.ToString("D"),
                            EntityId: "entity-alpha",
                            CostCenterId: "ops",
                            CounterpartyId: "administrator",
                            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["department"] = "fund-accounting"
                            }))
                ]));

        readiness.Issues.Should().Contain(issue =>
            issue.Code == "migration.dimensional-backfill-dimensions-scope-mismatch" &&
            issue.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.EvidenceReferences.Contains("evidence://migration/dimensional-backfill/default-fund/certified"));
        readiness.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            component.Status == AccountingProductionReadinessStatusDto.Blocked);
    }

    [Fact]
    public async Task ProductionReadinessService_RejectsDimensionalBackfillArtifactWithoutCanonicalCoverage()
    {
        var services = new ServiceCollection();
        services.AddSingleton<AccountingProductionReadinessService>();
        await using var provider = services.BuildServiceProvider();
        var requestedLedgerBookId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var readiness = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookId: requestedLedgerBookId,
                DimensionalBackfillCertified: true,
                MigrationRunArtifacts:
                [
                    new AccountingMigrationRunArtifactDto(
                        "migration-run-dimensional-backfill-default-fund",
                        AccountingMigrationRunKindDto.DimensionalBackfill,
                        AccountingMigrationRunStatusDto.Certified,
                        DateTimeOffset.Parse("2026-02-02T00:00:00Z"),
                        CompletedAtUtc: DateTimeOffset.Parse("2026-02-02T00:05:00Z"),
                        MigratedRecordCount: 42,
                        EvidenceReferences: ["evidence://migration/dimensional-backfill/default-fund/certified"],
                        FundProfileId: "default-fund",
                        LedgerBookId: requestedLedgerBookId,
                        Summary: "Dimensional backfill certified with sparse retained dimensions.",
                        Dimensions: new LedgerDimensionSetDto(
                            FundId: "default-fund",
                            BookId: requestedLedgerBookId.ToString("D"),
                            EntityId: "entity-alpha",
                            CostCenterId: "ops",
                            CounterpartyId: "administrator",
                            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["department"] = "fund-accounting"
                            }))
                ]));

        readiness.Issues.Should().Contain(issue =>
            issue.Code == "migration.dimensional-backfill-canonical-coverage-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.Message.Contains("sleeve", StringComparison.OrdinalIgnoreCase) &&
            issue.Message.Contains("capital account", StringComparison.OrdinalIgnoreCase) &&
            issue.Message.Contains("tax lot", StringComparison.OrdinalIgnoreCase) &&
            issue.EvidenceReferences.Contains("evidence://migration/dimensional-backfill/default-fund/certified"));
        readiness.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            component.Status == AccountingProductionReadinessStatusDto.Blocked);
    }

    [Fact]
    public async Task ReconcileLatestAsync_WithoutMeridianLedger_ReturnsMissingMeridianBreaksAndDisabledPosting()
    {
        var service = CreateService();
        await service.ImportAsync(new AccountingSystemImportRequestDto("quickbooks-fixture"));

        var summary = await service.ReconcileLatestAsync("quickbooks-fixture");

        summary.PostingEnabled.Should().BeFalse();
        summary.PostingDisabledReason.Should().Contain("source of all ledger truth");
        summary.PostingDisabledReason.Should().Contain("disabled");
        summary.Rows.Should().NotBeEmpty();
        summary.Rows.Should().OnlyContain(row => row.Status == AccountingSystemReconciliationStatusDto.MissingMeridian);
        summary.BreakCount.Should().Be(summary.Rows.Count);
    }

    [Fact]
    public async Task ReconcileLatestAsync_WithMeridianLedger_ReturnsMatchedRowsAndRetainedEvidencePackages()
    {
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var journalEntryId = Guid.NewGuid();
        var cashLineId = Guid.NewGuid();
        var incomeLineId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var sourceJournalEntryId = Guid.NewGuid();
        var journal = new JournalEntry(
            journalEntryId,
            timestamp,
            "Capital contribution",
            [
                new LedgerEntry(
                    cashLineId,
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("Assets:Cash:Operating", LedgerAccountType.Asset),
                    250_000m,
                    0m,
                    "Capital contribution"),
                new LedgerEntry(
                    incomeLineId,
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("Income:Investment", LedgerAccountType.Revenue),
                    0m,
                    250_000m,
                    "Capital contribution")
            ]);
        var ledgerStore = new StaticLedgerJournalStore(
            new LedgerBookRecord(
                ledgerBookId,
                "default-fund",
                Guid.NewGuid(),
                FundStructureNodeKindDto.Fund,
                "Default fund primary book",
                "USD",
                timestamp,
                timestamp),
            new LedgerAccountingPeriod(
                periodId,
                ledgerBookId,
                2026,
                1,
                "2026-01",
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 31),
                "Open",
                timestamp,
                null,
                1),
            [
                new LedgerJournalEntryRecord(
                    journal,
                    Guid.NewGuid(),
                    periodId,
                    CommandId: null,
                    CorrelationId: null,
                    GlobalSequence: 1,
                    CreatedAt: timestamp,
                    SourceEventId: sourceEventId,
                    SourceJournalEntryId: sourceJournalEntryId)
            ]);
        var service = CreateService(
            ledgerStore,
            new QuickBooksOnlineAccountingProvider(FakeQuickBooksConnectionStore.Configured(), new FakeQuickBooksClient()));

        await service.ImportAsync(new AccountingSystemImportRequestDto(
            "quickbooks",
            "default-fund",
            ledgerBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31)));

        var summary = await service.ReconcileLatestAsync("quickbooks", "default-fund", ledgerBookId);

        summary.LedgerBookId.Should().Be(ledgerBookId);
        summary.Rows.Should().OnlyContain(row => row.Status == AccountingSystemReconciliationStatusDto.Matched);
        summary.BreakCount.Should().Be(0);
        summary.EvidenceReferences.Should().Contain("quickbooks:company:9130359087654321:trial-balance:qbo-1000");
        summary.EvidenceReferences.Should().Contain($"ledger-entry:{cashLineId:D}");
        summary.EvidencePackages.Should().ContainSingle(package =>
            package.PackageId == $"gl-meridian-ledger-evidence:{summary.ImportId}" &&
            package.Status == AccountingSystemEvidencePackageStatusDto.Ready &&
            package.EvidenceReferences.Contains($"ledger-journal-entry:{journalEntryId:D}") &&
            package.RequiredActions.Count == 0);
        summary.EvidencePackages.Should().ContainSingle(package =>
            package.PackageId == $"gl-reconciliation-tie-out:{summary.ImportId}" &&
            package.Status == AccountingSystemEvidencePackageStatusDto.Ready &&
            package.RequiredActions.Count == 0);

        var cashRow = summary.Rows.Single(row => row.AccountCode == "Assets:Cash:Operating");
        cashRow.ExternalEvidenceReferences.Should().Contain("quickbooks:company:9130359087654321:trial-balance:qbo-1000");
        cashRow.MeridianEvidenceReferences.Should().Contain($"ledger-entry:{cashLineId:D}");
        cashRow.MeridianEvidenceReferences.Should().Contain($"source-event:{sourceEventId:D}");
        cashRow.MeridianEvidenceReferences.Should().Contain($"source-journal-entry:{sourceJournalEntryId:D}");
        cashRow.EvidenceReferences.Should().Contain("quickbooks:company:9130359087654321:trial-balance:qbo-1000");
        cashRow.EvidenceReferences.Should().Contain($"ledger-entry:{cashLineId:D}");
    }

    [Fact]
    public async Task ReconcileLatestAsync_WithMixedGlBreaks_ReturnsCategorySpecificTieOutActions()
    {
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 1, 12, 0, 0, 0, TimeSpan.Zero);
        var journalEntryId = Guid.NewGuid();
        var cashLineId = Guid.NewGuid();
        var receivableLineId = Guid.NewGuid();
        var incomeLineId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var sourceJournalEntryId = Guid.NewGuid();
        var journal = new JournalEntry(
            journalEntryId,
            timestamp,
            "Mixed GL tie-out breaks",
            [
                new LedgerEntry(
                    cashLineId,
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("Assets:Cash:Operating", LedgerAccountType.Asset),
                    240_000m,
                    0m,
                    "Mixed GL tie-out breaks"),
                new LedgerEntry(
                    receivableLineId,
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("Assets:Receivable:Subscription", LedgerAccountType.Asset),
                    5_000m,
                    0m,
                    "Mixed GL tie-out breaks"),
                new LedgerEntry(
                    incomeLineId,
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("Income:Investment", LedgerAccountType.Revenue),
                    0m,
                    245_000m,
                    "Mixed GL tie-out breaks")
            ]);
        var ledgerStore = new StaticLedgerJournalStore(
            new LedgerBookRecord(
                ledgerBookId,
                "default-fund",
                Guid.NewGuid(),
                FundStructureNodeKindDto.Fund,
                "Default fund primary book",
                "USD",
                timestamp,
                timestamp),
            new LedgerAccountingPeriod(
                periodId,
                ledgerBookId,
                2026,
                1,
                "2026-01",
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 31),
                "Open",
                timestamp,
                null,
                1),
            [
                new LedgerJournalEntryRecord(
                    journal,
                    Guid.NewGuid(),
                    periodId,
                    CommandId: null,
                    CorrelationId: null,
                    GlobalSequence: 1,
                    CreatedAt: timestamp,
                    SourceEventId: sourceEventId,
                    SourceJournalEntryId: sourceJournalEntryId)
            ]);
        var service = CreateService(ledgerStore);

        await service.ImportAsync(new AccountingSystemImportRequestDto(
            "quickbooks-fixture",
            "default-fund",
            ledgerBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31)));

        var summary = await service.ReconcileLatestAsync("quickbooks-fixture", "default-fund", ledgerBookId);

        summary.BreakCount.Should().Be(5);
        summary.Rows.Should().ContainSingle(row =>
            row.AccountCode == "Assets:Receivable:Subscription" &&
            row.Status == AccountingSystemReconciliationStatusDto.MissingExternal &&
            row.MeridianEvidenceReferences.Contains($"ledger-entry:{receivableLineId:D}"));
        summary.Rows.Count(row => row.Status == AccountingSystemReconciliationStatusDto.MissingMeridian).Should().Be(2);
        summary.Rows.Count(row => row.Status == AccountingSystemReconciliationStatusDto.Variance).Should().Be(2);

        var tieOutPackage = summary.EvidencePackages.Should().ContainSingle(package =>
            package.PackageId == $"gl-reconciliation-tie-out:{summary.ImportId}").Subject;
        tieOutPackage.Status.Should().Be(AccountingSystemEvidencePackageStatusDto.ReviewRequired);
        tieOutPackage.RequiredActions.Should().Contain(action =>
            action.Contains("1 Meridian ledger account is absent", StringComparison.OrdinalIgnoreCase) &&
            action.Contains("assign to accounting operations", StringComparison.OrdinalIgnoreCase));
        tieOutPackage.RequiredActions.Should().Contain(action =>
            action.Contains("2 external GL rows are absent", StringComparison.OrdinalIgnoreCase) &&
            action.Contains("assign to ledger operations", StringComparison.OrdinalIgnoreCase));
        tieOutPackage.RequiredActions.Should().Contain(action =>
            action.Contains("2 GL variance rows require", StringComparison.OrdinalIgnoreCase) &&
            action.Contains("retained approval evidence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ListProvidersAsync_IncludesPlannedXeroAndNetSuiteImportMappingsWithPostingDisabled()
    {
        var service = CreateService();

        var providers = await service.ListProvidersAsync();

        providers.Should().Contain(row =>
            row.ProviderId == "xero-fixture" &&
            row.State == AccountingSystemProviderStateDto.Available &&
            row.SupportsChartOfAccounts &&
            row.SupportsJournalEntries &&
            row.SupportsTrialBalance &&
            !row.SupportsPosting);
        providers.Should().Contain(row =>
            row.ProviderId == "netsuite-fixture" &&
            row.State == AccountingSystemProviderStateDto.Available &&
            row.SupportsChartOfAccounts &&
            row.SupportsJournalEntries &&
            row.SupportsTrialBalance &&
            !row.SupportsPosting);
        providers.Should().Contain(row =>
            row.ProviderId == "xero" &&
            row.State == AccountingSystemProviderStateDto.Planned &&
            row.SupportsChartOfAccounts &&
            row.SupportsJournalEntries &&
            row.SupportsTrialBalance &&
            !row.SupportsPosting);
        providers.Should().Contain(row =>
            row.ProviderId == "netsuite" &&
            row.State == AccountingSystemProviderStateDto.Planned &&
            row.SupportsChartOfAccounts &&
            row.SupportsJournalEntries &&
            row.SupportsTrialBalance &&
            !row.SupportsPosting);
    }

    [Theory]
    [InlineData("xero-fixture", "Xero Fixture", "XeroAccount")]
    [InlineData("netsuite-fixture", "NetSuite Fixture", "NetSuiteAccount")]
    public async Task ImportAsync_WithXeroAndNetSuiteFixtures_ReturnsReadOnlyExternalGlEvidence(
        string providerId,
        string displayName,
        string expectedEvidenceKind)
    {
        var service = CreateService();
        var ledgerBookId = Guid.NewGuid();

        var preview = await service.ImportAsync(new AccountingSystemImportRequestDto(
            providerId,
            FundProfileId: "default-fund",
            LedgerBookId: ledgerBookId,
            PersistPreview: false));
        var persisted = await service.ImportAsync(new AccountingSystemImportRequestDto(
            providerId,
            FundProfileId: "default-fund",
            LedgerBookId: ledgerBookId));
        var providers = await service.ListProvidersAsync();

        preview.Summary.ProviderId.Should().Be(providerId);
        preview.Summary.ProviderDisplayName.Should().Be(displayName);
        preview.Summary.State.Should().Be(AccountingSystemImportStateDto.Previewed);
        persisted.Summary.State.Should().Be(AccountingSystemImportStateDto.Imported);
        persisted.Summary.LedgerBookId.Should().Be(ledgerBookId);
        persisted.ChartAccounts.Should().NotBeEmpty();
        persisted.JournalEntries.Should().OnlyContain(entry => entry.TotalDebits == entry.TotalCredits);
        persisted.TrialBalance.Should().NotBeEmpty();
        persisted.Summary.EvidenceReferences.Should().Contain($"{providerId}:trial-balance");
        persisted.Summary.Warnings.Should().Contain(warning =>
            warning.Contains("read-only", StringComparison.OrdinalIgnoreCase));

        providers.Should().Contain(row =>
            row.ProviderId == providerId &&
            row.State == AccountingSystemProviderStateDto.Available &&
            row.EvidenceKinds.Contains(expectedEvidenceKind) &&
            !row.SupportsPosting);
    }

    [Fact]
    public async Task MappingProfiles_CertifiedProfileFeedsGuardedExportPackageWithoutEnablingPosting()
    {
        var service = CreateService(CreateMatchedQuickBooksFixtureLedgerStore());
        var profile = CertifiedQuickBooksMappingProfile();

        var upserted = await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));
        var profiles = await service.ListMappingProfilesAsync("quickbooks-fixture", "default-fund");
        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: [ExportControlEvidence(ExternalGlLedgerBookId)]));

        upserted.ProviderId.Should().Be("quickbooks-fixture");
        upserted.CertificationState.Should().Be(AccountingCertificationStateDto.Certified);
        profiles.Should().ContainSingle(row => row.ProfileId == profile.ProfileId);
        package.PostingEnabled.Should().BeFalse();
        package.PostingDisabledReason.Should().Contain("Guarded export package only");
        package.Certification.Should().NotBeNull();
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.ReadyForReview);
        package.ReconciliationSafeguardState.Should().Be(ExternalGlExportReconciliationSafeguardStateDto.Ready);
        package.ReconciliationSafeguardIssueCodes.Should().BeEmpty();
        package.ValidationIssues.Should().ContainSingle(issue => issue.Code == "LiveExternalPostingDisabled" && issue.Severity == AccountingConfigurationValidationSeverityDto.Info);
        package.ValidationIssues.Should().NotContain(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        package.EvidenceLinks.Should().Contain("external-gl-mapping-profile:qbo-default-fund-certified");
        package.EvidenceLinks.Should().Contain(link => link.StartsWith("external-gl-reconciliation:", StringComparison.OrdinalIgnoreCase));
        package.GeneratedLines.Should().HaveCount(3);
        var cashExportLine = package.GeneratedLines.Should().Contain(line =>
            line.MeridianAccountCode == "Assets:Cash:Operating" &&
            line.ExternalAccountId == "qbo-1000" &&
            line.Debit == 248_750m).Subject;
        cashExportLine.ExternalDimensions.Should().NotBeNull();
        cashExportLine.ExternalDimensions!.ExternalGlDimensions["Class"].Should().Be("DefaultFund");
        package.GeneratedLines.Should().OnlyContain(line => line.EvidenceLinks.Any(link => link.StartsWith("ledger-entry:", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task MappingProfiles_RejectAssistantOriginCertificationAndExportPackageRetention()
    {
        var service = CreateService(CreateMatchedQuickBooksFixtureLedgerStore());
        var profile = CertifiedQuickBooksMappingProfile();

        var assistantMappingCertification = () => service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "assistant",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"],
            ActionOrigin: OperationsActionOriginDto.AssistantDraft));

        await assistantMappingCertification.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Reviewed automation cannot certify external GL mapping profiles*human operator*");

        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));

        var assistantExportPackageRetention = () => service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "assistant",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: [ExportControlEvidence(ExternalGlLedgerBookId)],
            ActionOrigin: OperationsActionOriginDto.AssistantDraft));

        await assistantExportPackageRetention.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Reviewed automation cannot retain external GL export review packages*human operator*");
    }

    [Fact]
    public async Task ExportPackages_IsolateManifestAndCertificationByTenantAndCompany()
    {
        var service = CreateService(CreateMatchedQuickBooksFixtureLedgerStore());
        var profile = CertifiedQuickBooksMappingProfile();

        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"],
            TenantId: "tenant-alpha",
            CompanyId: "company-alpha"));
        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: [ExportControlEvidence(ExternalGlLedgerBookId)],
            TenantId: "tenant-alpha",
            CompanyId: "company-alpha"));

        var alphaManifest = await service.GetExportPackageManifestAsync(
            package.ExportPackageId,
            tenantId: "tenant-alpha",
            companyId: "company-alpha");
        var betaManifest = await service.GetExportPackageManifestAsync(
            package.ExportPackageId,
            tenantId: "tenant-beta",
            companyId: "company-alpha");
        var betaCertification = await service.CertifyExportPackageAsync(new CertifyAccountingSystemExportPackageRequestDto(
            package.ExportPackageId,
            "controller",
            "Wrong tenant cannot certify the guarded export package.",
            [ExportCertificationEvidence(package)],
            TenantId: "tenant-beta",
            CompanyId: "company-alpha"));
        var certified = await service.CertifyExportPackageAsync(new CertifyAccountingSystemExportPackageRequestDto(
            package.ExportPackageId,
            "controller",
            "Tenant controller certified the guarded export package.",
            [ExportCertificationEvidence(package)],
            TenantId: "tenant-alpha",
            CompanyId: "company-alpha"));

        package.TenantId.Should().Be("tenant-alpha");
        package.CompanyId.Should().Be("company-alpha");
        package.ExportPackageId.Should().Contain("tenant-tenant-alpha-company-company-alpha");
        alphaManifest.Should().NotBeNull();
        alphaManifest!.TenantId.Should().Be("tenant-alpha");
        alphaManifest.CompanyId.Should().Be("company-alpha");
        betaManifest.Should().BeNull();
        betaCertification.Should().BeNull();
        certified.Should().NotBeNull();
        certified!.Certification!.State.Should().Be(AccountingCertificationStateDto.Certified);
        certified.TenantId.Should().Be("tenant-alpha");
        certified.CompanyId.Should().Be("company-alpha");
    }

    [Fact]
    public async Task ExportPackages_ListRetainedPackagesByProviderFundBookCertificationAndEnterpriseScope()
    {
        var service = CreateService(CreateMatchedQuickBooksFixtureLedgerStore());
        var profile = CertifiedQuickBooksMappingProfile();
        var otherBookId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");

        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"],
            TenantId: "tenant-alpha",
            CompanyId: "company-alpha"));
        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile with { ProfileId = "qbo-default-fund-certified-other-book" },
            "accounting-ops",
            FundProfileId: "default-fund",
            LedgerBookId: otherBookId,
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified-other-book"],
            TenantId: "tenant-alpha",
            CompanyId: "company-alpha"));

        var retained = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: [ExportControlEvidence(ExternalGlLedgerBookId)],
            TenantId: "tenant-alpha",
            CompanyId: "company-alpha"));
        var otherBook = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            LedgerBookId: otherBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: "qbo-default-fund-certified-other-book",
            RequireBalancedReconciliation: false,
            EvidenceLinks: [ExportControlEvidence(otherBookId)],
            TenantId: "tenant-alpha",
            CompanyId: "company-alpha"));

        var filtered = await service.ListExportPackagesAsync(
            providerId: "quickbooks-fixture",
            fundProfileId: "default-fund",
            ledgerBookId: ExternalGlLedgerBookId,
            certificationState: AccountingCertificationStateDto.ReadyForReview,
            tenantId: "tenant-alpha",
            companyId: "company-alpha");
        var wrongTenant = await service.ListExportPackagesAsync(
            ledgerBookId: ExternalGlLedgerBookId,
            tenantId: "tenant-beta",
            companyId: "company-alpha");
        var allAlpha = await service.ListExportPackagesAsync(
            providerId: "quickbooks-fixture",
            fundProfileId: "default-fund",
            tenantId: "tenant-alpha",
            companyId: "company-alpha");

        filtered.Should().ContainSingle(package =>
            package.ExportPackageId == retained.ExportPackageId &&
            package.LedgerBookId == ExternalGlLedgerBookId &&
            package.Certification!.State == AccountingCertificationStateDto.ReadyForReview);
        filtered.Should().NotContain(package => package.ExportPackageId == otherBook.ExportPackageId);
        wrongTenant.Should().BeEmpty();
        allAlpha.Should().Contain(package => package.ExportPackageId == retained.ExportPackageId);
        allAlpha.Should().Contain(package => package.ExportPackageId == otherBook.ExportPackageId);
    }

    [Fact]
    public async Task ExportPackages_StampLedgerBookScopeIntoRetainedPackageIdentity()
    {
        var primaryBookId = ExternalGlLedgerBookId;
        var gaapBookId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
        var service = CreateService();
        var profile = CertifiedQuickBooksMappingProfile();

        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            LedgerBookId: primaryBookId,
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));
        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile with { ProfileId = "qbo-default-fund-certified-gaap" },
            "accounting-ops",
            FundProfileId: "default-fund",
            LedgerBookId: gaapBookId,
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified-gaap"]));

        var primary = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            LedgerBookId: primaryBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: [ExportControlEvidence(primaryBookId)]));
        var gaap = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            LedgerBookId: gaapBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: "qbo-default-fund-certified-gaap",
            RequireBalancedReconciliation: false,
            EvidenceLinks: [ExportControlEvidence(gaapBookId)]));
        var primaryManifest = await service.GetExportPackageManifestAsync(primary.ExportPackageId);
        var gaapManifest = await service.GetExportPackageManifestAsync(gaap.ExportPackageId);

        primary.ExportPackageId.Should().Contain($"book-{primaryBookId:N}");
        gaap.ExportPackageId.Should().Contain($"book-{gaapBookId:N}");
        primary.ExportPackageId.Should().NotBe(gaap.ExportPackageId);
        primary.LedgerBookId.Should().Be(primaryBookId);
        gaap.LedgerBookId.Should().Be(gaapBookId);
        primaryManifest.Should().NotBeNull();
        gaapManifest.Should().NotBeNull();
        primaryManifest!.LedgerBookId.Should().Be(primaryBookId);
        gaapManifest!.LedgerBookId.Should().Be(gaapBookId);
        primaryManifest.Payload.Should().Contain(primaryBookId.ToString("D"));
        gaapManifest.Payload.Should().Contain(gaapBookId.ToString("D"));
        primaryManifest.ExternalPostingAllowed.Should().BeFalse();
        gaapManifest.ExternalPostingAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task CreateExportPackageAsync_RequiresExplicitLedgerBookScopeBeforeReview()
    {
        var service = CreateService(CreateMatchedQuickBooksFixtureLedgerStore());
        var profile = CertifiedQuickBooksMappingProfile();

        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));

        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: [ExportControlEvidence(ExternalGlLedgerBookId)]));

        package.LedgerBookId.Should().BeNull();
        package.PostingEnabled.Should().BeFalse();
        package.Certification.Should().NotBeNull();
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.Draft);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "MissingExternalGlExportLedgerBookScope" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
    }

    [Fact]
    public async Task CreateExportPackageAsync_RequiresLedgerBookScopedMappingProfileBeforeReview()
    {
        var service = CreateService(CreateMatchedQuickBooksFixtureLedgerStore());
        var profile = CertifiedQuickBooksMappingProfile();

        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));

        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: [ExportControlEvidence(ExternalGlLedgerBookId)]));

        package.PostingEnabled.Should().BeFalse();
        package.GeneratedLines.Should().BeEmpty();
        package.Certification.Should().NotBeNull();
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.Draft);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "ExternalGlMappingProfileLedgerBookMismatch" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == profile.ProfileId);
    }

    [Fact]
    public async Task CreateExportPackageAsync_RequiresTenantScopedMappingProfileBeforeReview()
    {
        var service = CreateService(CreateMatchedQuickBooksFixtureLedgerStore());
        var profile = CertifiedQuickBooksMappingProfile();

        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"],
            TenantId: "tenant-alpha",
            CompanyId: "company-alpha"));

        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: [ExportControlEvidence(ExternalGlLedgerBookId)],
            TenantId: "tenant-beta",
            CompanyId: "company-beta"));

        package.PostingEnabled.Should().BeFalse();
        package.MappingProfileId.Should().BeNull();
        package.GeneratedLines.Should().BeEmpty();
        package.Certification.Should().NotBeNull();
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.Draft);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "MissingExternalGlMappingProfile" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
    }

    [Fact]
    public async Task LatestImportAndReconciliation_AreIsolatedByTenantAndCompanyScope()
    {
        var service = CreateService(CreateMatchedQuickBooksFixtureLedgerStore());

        var alphaImport = await service.ImportAsync(new AccountingSystemImportRequestDto(
            "quickbooks-fixture",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            PeriodStart: new DateOnly(2026, 2, 1),
            PeriodEnd: new DateOnly(2026, 2, 28),
            TenantId: "tenant-alpha",
            CompanyId: "company-alpha"));
        var alphaLatest = await service.GetLatestImportAsync(
            "quickbooks-fixture",
            "default-fund",
            ExternalGlLedgerBookId,
            tenantId: "tenant-alpha",
            companyId: "company-alpha");
        var betaLatest = await service.GetLatestImportAsync(
            "quickbooks-fixture",
            "default-fund",
            ExternalGlLedgerBookId,
            tenantId: "tenant-beta",
            companyId: "company-beta");
        var betaReconciliation = await service.ReconcileLatestAsync(
            "quickbooks-fixture",
            "default-fund",
            ExternalGlLedgerBookId,
            tenantId: "tenant-beta",
            companyId: "company-beta");

        alphaImport.Summary.TenantId.Should().Be("tenant-alpha");
        alphaImport.Summary.CompanyId.Should().Be("company-alpha");
        alphaLatest.Summary.ImportId.Should().Be(alphaImport.Summary.ImportId);
        alphaLatest.Summary.PeriodEnd.Should().Be(new DateOnly(2026, 2, 28));
        alphaLatest.Summary.TenantId.Should().Be("tenant-alpha");
        alphaLatest.Summary.CompanyId.Should().Be("company-alpha");
        betaLatest.Summary.PeriodEnd.Should().Be(new DateOnly(2026, 1, 31));
        betaLatest.Summary.TenantId.Should().Be("tenant-beta");
        betaLatest.Summary.CompanyId.Should().Be("company-beta");
        betaLatest.Summary.ImportId.Should().NotBe(alphaImport.Summary.ImportId);
        betaReconciliation.ImportId.Should().Be(betaLatest.Summary.ImportId);
        betaReconciliation.PeriodEnd.Should().Be(betaLatest.Summary.PeriodEnd);
    }


    [Theory]
    [InlineData("xero-fixture", "xero-default-fund-certified", "090", "xero-bank-001", 179_050)]
    [InlineData("netsuite-fixture", "netsuite-default-fund-certified", "1000", "ns-1000", 308_125)]
    public async Task MappingProfiles_XeroAndNetSuiteFixturesFeedGuardedExportPackagesWithoutEnablingPosting(
        string providerId,
        string profileId,
        string cashAccountCode,
        string externalCashAccountId,
        decimal expectedCashDebit)
    {
        var service = CreateService(CreateMatchedExternalGlFixtureLedgerStore(providerId));
        var profile = CertifiedFixtureMappingProfile(providerId, profileId);

        var upserted = await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            ProviderId: providerId,
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            EvidenceLinks: [$"approval:external-gl-mapping:{profileId}"]));
        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: providerId,
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: [ExportControlEvidence(ExternalGlLedgerBookId, providerId)]));

        upserted.ProviderId.Should().Be(providerId);
        upserted.CertificationState.Should().Be(AccountingCertificationStateDto.Certified);
        package.ProviderId.Should().Be(providerId);
        package.PostingEnabled.Should().BeFalse();
        package.PostingDisabledReason.Should().Contain("Guarded export package only");
        package.Certification.Should().NotBeNull();
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.ReadyForReview);
        package.GeneratedLines.Should().HaveCount(3);
        package.GeneratedLines.Should().Contain(line =>
            line.MeridianAccountCode == cashAccountCode &&
            line.ExternalAccountId == externalCashAccountId &&
            line.Debit == expectedCashDebit);
        package.GeneratedLines.Should().OnlyContain(line =>
            line.EvidenceLinks.Any(link => link.StartsWith("ledger-entry:", StringComparison.OrdinalIgnoreCase)));
        package.ValidationIssues.Should().ContainSingle(issue =>
            issue.Code == "LiveExternalPostingDisabled" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Info);
        package.ValidationIssues.Should().NotContain(issue =>
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
    }

    [Fact]
    public async Task MappingProfiles_DowngradesCertifiedProfileWithoutRetainedEvidence()
    {
        var service = CreateService();
        var profile = CertifiedQuickBooksMappingProfile() with
        {
            ProfileId = "qbo-default-fund-no-evidence-certified"
        };

        var upserted = await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund"));
        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: ["approval:export-package:qbo-no-mapping-evidence"]));

        upserted.CertificationState.Should().Be(AccountingCertificationStateDto.Draft);
        package.PostingEnabled.Should().BeFalse();
        package.Certification.Should().NotBeNull();
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.Draft);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "UncertifiedExternalGlMappingProfile" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == profile.ProfileId);
        package.EvidenceLinks.Should().NotContain(link => link.StartsWith("approval:external-gl-mapping:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MappingProfiles_DowngradesCertifiedProfileWithWeakRetainedEvidence()
    {
        var service = CreateService(CreateMatchedQuickBooksFixtureLedgerStore());
        var profile = CertifiedQuickBooksMappingProfile() with
        {
            ProfileId = "qbo-default-fund-weak-evidence-certified"
        };

        var upserted = await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            EvidenceLinks: ["evidence:external-gl:support-packet"]));
        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: [ExportControlEvidence(ExternalGlLedgerBookId)]));

        upserted.CertificationState.Should().Be(AccountingCertificationStateDto.Draft);
        package.PostingEnabled.Should().BeFalse();
        package.Certification.Should().NotBeNull();
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.Draft);
        package.GeneratedLines.Should().BeEmpty();
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "UncertifiedExternalGlMappingProfile" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == profile.ProfileId);
    }

    [Fact]
    public async Task MappingProfiles_DowngradesCertifiedProfileWithWrongProfileEvidence()
    {
        var service = CreateService(CreateMatchedQuickBooksFixtureLedgerStore());
        var profile = CertifiedQuickBooksMappingProfile() with
        {
            ProfileId = "qbo-default-fund-wrong-profile-evidence-certified"
        };

        var upserted = await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            EvidenceLinks: ["approval:external-gl-mapping:other-profile-certified"]));
        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: ["approval:export-package:qbo-wrong-profile-evidence"]));

        upserted.CertificationState.Should().Be(AccountingCertificationStateDto.Draft);
        package.PostingEnabled.Should().BeFalse();
        package.GeneratedLines.Should().BeEmpty();
        package.Certification.Should().NotBeNull();
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.Draft);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "UncertifiedExternalGlMappingProfile" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == profile.ProfileId);
    }

    [Fact]
    public async Task MappingProfiles_DowngradesCertifiedProfileWithSplitRetainedEvidence()
    {
        var service = CreateService(CreateMatchedQuickBooksFixtureLedgerStore());
        var profile = CertifiedQuickBooksMappingProfile() with
        {
            ProfileId = "qbo-default-fund-split-evidence-certified"
        };

        var upserted = await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            EvidenceLinks:
            [
                "evidence:external-gl-mapping:qbo-default-fund-split-evidence-certified:support-packet",
                "approval:external-gl-mapping:other-profile-certified"
            ]));
        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: ["approval:export-package:qbo-split-mapping-evidence"]));

        upserted.CertificationState.Should().Be(AccountingCertificationStateDto.Draft);
        package.PostingEnabled.Should().BeFalse();
        package.GeneratedLines.Should().BeEmpty();
        package.Certification.Should().NotBeNull();
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.Draft);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "UncertifiedExternalGlMappingProfile" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == profile.ProfileId);
    }

    [Fact]
    public async Task CreateExportPackageAsync_RequiresRetainedExportControlEvidence()
    {
        var service = CreateService();
        var profile = CertifiedQuickBooksMappingProfile();
        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));

        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false));

        package.PostingEnabled.Should().BeFalse();
        package.Certification.Should().NotBeNull();
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.Draft);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "MissingExternalGlExportControlEvidence" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
    }

    [Fact]
    public async Task CreateExportPackageAsync_RejectsGenericExportControlEvidence()
    {
        var service = CreateService(CreateMatchedQuickBooksFixtureLedgerStore());
        var profile = CertifiedQuickBooksMappingProfile();
        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));

        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: ["approval:export-package:generic-review-packet"]));

        package.PostingEnabled.Should().BeFalse();
        package.GeneratedLines.Should().HaveCount(3);
        package.Certification.Should().NotBeNull();
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.Draft);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "UnscopedExternalGlExportControlEvidence" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        package.ValidationIssues.Should().NotContain(issue => issue.Code == "MissingExternalGlExportControlEvidence");
    }

    [Fact]
    public async Task CreateExportPackageAsync_RejectsSplitExportControlEvidence()
    {
        var service = CreateService(CreateMatchedQuickBooksFixtureLedgerStore());
        var profile = CertifiedQuickBooksMappingProfile();
        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));

        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks:
            [
                "support-packet:default-fund:2026-01-01:2026-01-31",
                "approval:export-package:generic-review-packet"
            ]));

        package.PostingEnabled.Should().BeFalse();
        package.GeneratedLines.Should().HaveCount(3);
        package.Certification.Should().NotBeNull();
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.Draft);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "UnscopedExternalGlExportControlEvidence" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.Message.Contains("same evidence artifact", StringComparison.OrdinalIgnoreCase));
        package.ValidationIssues.Should().NotContain(issue => issue.Code == "MissingExternalGlExportControlEvidence");
    }

    [Fact]
    public async Task CreateExportPackageAsync_RequiresLedgerBookScopedExportControlEvidenceBeforeReview()
    {
        var service = CreateService(CreateMatchedQuickBooksFixtureLedgerStore());
        var profile = CertifiedQuickBooksMappingProfile();
        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));
        var otherLedgerBookId = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");

        var missingBookPackage = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: ["approval:export-package:qbo-default-fund:2026-01-01:2026-01-31"]));
        var wrongBookPackage = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: [$"approval:export-package:qbo-default-fund:ledger-book:{otherLedgerBookId:D}:2026-01-01:2026-01-31"]));

        missingBookPackage.PostingEnabled.Should().BeFalse();
        missingBookPackage.GeneratedLines.Should().HaveCount(3);
        missingBookPackage.Certification.Should().NotBeNull();
        missingBookPackage.Certification!.State.Should().Be(AccountingCertificationStateDto.Draft);
        missingBookPackage.ValidationIssues.Should().Contain(issue =>
            issue.Code == "UnscopedExternalGlExportControlEvidence" &&
            issue.Message.Contains("ledger book", StringComparison.OrdinalIgnoreCase));
        wrongBookPackage.Certification.Should().NotBeNull();
        wrongBookPackage.Certification!.State.Should().Be(AccountingCertificationStateDto.Draft);
        wrongBookPackage.ValidationIssues.Should().Contain(issue =>
            issue.Code == "UnscopedExternalGlExportControlEvidence" &&
            issue.Message.Contains("ledger book", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task CreateExportPackageAsync_RequiresGeneratedMeridianOwnedExportLines()
    {
        var service = CreateService();
        var profile = CertifiedQuickBooksMappingProfile();
        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));

        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: [ExportControlEvidence(ExternalGlLedgerBookId)]));

        package.PostingEnabled.Should().BeFalse();
        package.GeneratedLines.Should().BeEmpty();
        package.Certification.Should().NotBeNull();
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.Draft);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "MissingGeneratedExternalGlExportLines" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
    }

    [Fact]
    public async Task CertifyExportPackageAsync_CertifiesReadyArtifactWithoutEnablingPosting()
    {
        var service = CreateService(CreateMatchedQuickBooksFixtureLedgerStore());
        var profile = CertifiedQuickBooksMappingProfile();
        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));
        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: [ExportControlEvidence(ExternalGlLedgerBookId)]));

        package.MappingProfileId.Should().Be(profile.ProfileId);
        package.ReconciliationId.Should().NotBeNullOrWhiteSpace();
        package.ReconciliationSnapshotHash.Should().NotBeNullOrWhiteSpace();
        package.RequireBalancedReconciliation.Should().BeFalse();

        var weakCertification = () => service.CertifyExportPackageAsync(new CertifyAccountingSystemExportPackageRequestDto(
            package.ExportPackageId,
            "controller",
            "Generic support packet should not certify the guarded external GL export package.",
            ["evidence:external-gl-export:support-packet"]));

        await weakCertification.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*approval, certification, sign-off, or review evidence*");

        var stalePeriodCertification = () => service.CertifyExportPackageAsync(new CertifyAccountingSystemExportPackageRequestDto(
            package.ExportPackageId,
            "controller",
            "February evidence should not certify the January guarded export package.",
            ["approval:external-gl-export-certification:2026-02-01:2026-02-28"]));

        await stalePeriodCertification.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*must reference the retained export package id, certification id, export ledger book, and exact export period in the same artifact*");

        var splitCertificationEvidence = () => service.CertifyExportPackageAsync(new CertifyAccountingSystemExportPackageRequestDto(
            package.ExportPackageId,
            "controller",
            "Split evidence should not certify the guarded external GL export package.",
            [
                "evidence:external-gl-export:support-packet:2026-01-01:2026-01-31",
                "approval:external-gl-export-certification:2026-02-01:2026-02-28"
            ]));

        await splitCertificationEvidence.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*must reference the retained export package id, certification id, export ledger book, and exact export period in the same artifact*");

        var missingCertificationIdEvidence = () => service.CertifyExportPackageAsync(new CertifyAccountingSystemExportPackageRequestDto(
            package.ExportPackageId,
            "controller",
            "Package and period evidence without certification id should not certify the guarded export package.",
            [$"approval:external-gl-export-certification:{package.ExportPackageId}:2026-01-01:2026-01-31"]));

        await missingCertificationIdEvidence.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*must reference the retained export package id, certification id, export ledger book, and exact export period in the same artifact*");

        var missingLedgerBookCertification = () => service.CertifyExportPackageAsync(new CertifyAccountingSystemExportPackageRequestDto(
            package.ExportPackageId,
            "controller",
            "Package, certification, and period evidence without ledger-book scope should not certify the guarded export package.",
            [$"approval:external-gl-export-certification:{package.ExportPackageId}:{package.Certification!.CertificationId}:2026-01-01:2026-01-31"]));

        await missingLedgerBookCertification.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*must reference the retained export package id, certification id, export ledger book, and exact export period in the same artifact*");

        var otherLedgerBookId = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");
        var wrongLedgerBookCertification = () => service.CertifyExportPackageAsync(new CertifyAccountingSystemExportPackageRequestDto(
            package.ExportPackageId,
            "controller",
            "Wrong-book evidence should not certify the guarded export package.",
            [$"approval:external-gl-export-certification:{package.ExportPackageId}:{package.Certification!.CertificationId}:ledger-book:{otherLedgerBookId:D}:2026-01-01:2026-01-31"]));

        await wrongLedgerBookCertification.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*must reference the retained export package id, certification id, export ledger book, and exact export period in the same artifact*");


        var certificationEvidence = ExportCertificationEvidence(package);
        var assistantCertification = () => service.CertifyExportPackageAsync(new CertifyAccountingSystemExportPackageRequestDto(
            package.ExportPackageId,
            "assistant",
            "Assistant drafted certification should not certify the guarded external GL export package.",
            [certificationEvidence],
            ActionOrigin: OperationsActionOriginDto.AssistantDraft));

        await assistantCertification.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Reviewed automation cannot certify external GL export packages*human operator*");

        var certified = await service.CertifyExportPackageAsync(new CertifyAccountingSystemExportPackageRequestDto(
            package.ExportPackageId,
            "controller",
            "Controller certified the guarded external GL export package.",
            [certificationEvidence]));

        certified.Should().NotBeNull();
        certified!.ExportPackageId.Should().Be(package.ExportPackageId);
        certified.PostingEnabled.Should().BeFalse();
        certified.PostingDisabledReason.Should().Contain("live external GL posting remains disabled");
        certified.Certification.Should().NotBeNull();
        certified.Certification!.State.Should().Be(AccountingCertificationStateDto.Certified);
        certified.ReconciliationSafeguardState.Should().Be(ExternalGlExportReconciliationSafeguardStateDto.Certified);
        certified.ReconciliationSafeguardIssueCodes.Should().BeEmpty();
        certified.Certification.Actor.Should().Be("controller");
        certified.Certification.Summary.Should().Be("Controller certified the guarded external GL export package.");
        certified.Certification.EvidenceLinks.Should().Contain(certificationEvidence);
        certified.EvidenceLinks.Should().Contain(certificationEvidence);

        var manifest = await service.GetExportPackageManifestAsync(certified.ExportPackageId);

        manifest.Should().NotBeNull();
        manifest!.ExportPackageId.Should().Be(certified.ExportPackageId);
        manifest.ProviderId.Should().Be("quickbooks-fixture");
        manifest.CertificationState.Should().Be(AccountingCertificationStateDto.Certified);
        manifest.ExternalPostingAllowed.Should().BeFalse();
        manifest.PostingDisabledReason.Should().Contain("live external GL posting remains disabled");
        manifest.ContentType.Should().Be("application/json");
        manifest.ContentHash.Should().HaveLength(64);
        manifest.GeneratedLines.Should().HaveSameCount(certified.GeneratedLines);
        manifest.MappingProfileId.Should().Be(profile.ProfileId);
        manifest.ReconciliationId.Should().Be(package.ReconciliationId);
        manifest.ReconciliationSnapshotHash.Should().Be(package.ReconciliationSnapshotHash);
        manifest.RequireBalancedReconciliation.Should().BeFalse();
        manifest.ReconciliationSafeguardState.Should().Be(ExternalGlExportReconciliationSafeguardStateDto.Certified);
        manifest.ReconciliationSafeguardIssueCodes.Should().BeEmpty();
        manifest.EvidenceLinks.Should().Contain(certificationEvidence);
        manifest.Payload.Should().Contain(certified.ExportPackageId);
        manifest.Payload.Should().Contain(certificationEvidence);
        manifest.Payload.Should().Contain(package.ReconciliationSnapshotHash);
        manifest.Payload.Should().Contain("\"reconciliationSafeguardState\": \"Certified\"");

        var changedLedgerBookId = Guid.Parse("99999999-9999-4999-9999-999999999999");
        RetainExportPackage(service, certified with { LedgerBookId = changedLedgerBookId });
        var changedScopeManifest = await service.GetExportPackageManifestAsync(certified.ExportPackageId);

        changedScopeManifest.Should().NotBeNull();
        changedScopeManifest!.LedgerBookId.Should().Be(changedLedgerBookId);
        changedScopeManifest.ContentHash.Should().NotBe(manifest.ContentHash);
        changedScopeManifest.Payload.Should().Contain(changedLedgerBookId.ToString("D"));
    }

    [Fact]
    public async Task CertifyExportPackageAsync_RevalidatesCurrentMappingProfileBeforeCertification()
    {
        var service = CreateService(CreateMatchedQuickBooksFixtureLedgerStore());
        var profile = CertifiedQuickBooksMappingProfile();
        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));
        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: [ExportControlEvidence(ExternalGlLedgerBookId)]));
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.ReadyForReview);

        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile with { DisplayName = "Default fund QBO mapping requiring recertification" },
            "accounting-ops",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId));

        var act = () => service.CertifyExportPackageAsync(new CertifyAccountingSystemExportPackageRequestDto(
            package.ExportPackageId,
            "controller",
            "Controller attempted to certify after mapping evidence changed.",
            [ExportCertificationEvidence(package)]));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*current mapping or reconciliation blockers*UncertifiedExternalGlMappingProfile*");
    }

    [Fact]
    public async Task CertifyExportPackageAsync_RevalidatesCurrentReconciliationSnapshotBeforeCertification()
    {
        var service = CreateService(CreateMatchedQuickBooksFixtureLedgerStore());
        var profile = CertifiedQuickBooksMappingProfile();
        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));
        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: [ExportControlEvidence(ExternalGlLedgerBookId)]));
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.ReadyForReview);
        package.ReconciliationId.Should().NotBeNullOrWhiteSpace();

        RetainExportPackage(service, package with { ReconciliationId = "gl-recon-stale-retained-snapshot" });

        var act = () => service.CertifyExportPackageAsync(new CertifyAccountingSystemExportPackageRequestDto(
            package.ExportPackageId,
            "controller",
            "Controller attempted to certify after reconciliation evidence changed.",
            [ExportCertificationEvidence(package)]));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*current mapping or reconciliation blockers*ExternalGlReconciliationSnapshotChanged*");
    }

    [Fact]
    public async Task CertifyExportPackageAsync_RevalidatesCurrentReconciliationSnapshotContentBeforeCertification()
    {
        var service = CreateService(CreateMatchedQuickBooksFixtureLedgerStore());
        var profile = CertifiedQuickBooksMappingProfile();
        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));
        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: [ExportControlEvidence(ExternalGlLedgerBookId)]));
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.ReadyForReview);
        package.ReconciliationId.Should().NotBeNullOrWhiteSpace();
        package.ReconciliationSnapshotHash.Should().NotBeNullOrWhiteSpace();

        RetainExportPackage(service, package with { ReconciliationSnapshotHash = "stale-retained-content-hash" });

        var act = () => service.CertifyExportPackageAsync(new CertifyAccountingSystemExportPackageRequestDto(
            package.ExportPackageId,
            "controller",
            "Controller attempted to certify after reconciliation content changed.",
            [ExportCertificationEvidence(package)]));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*current mapping or reconciliation blockers*ExternalGlReconciliationSnapshotChanged*");
    }

    [Fact]
    public async Task CertifyExportPackageAsync_BlocksRetainedPackageWithPostingEnabled()
    {
        var service = CreateService(CreateMatchedQuickBooksFixtureLedgerStore());
        var profile = CertifiedQuickBooksMappingProfile();
        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));
        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: [ExportControlEvidence(ExternalGlLedgerBookId)]));
        RetainExportPackage(service, package with
        {
            PostingEnabled = true,
            PostingDisabledReason = string.Empty
        });

        var act = () => service.CertifyExportPackageAsync(new CertifyAccountingSystemExportPackageRequestDto(
            package.ExportPackageId,
            "controller",
            "Attempt to certify a retained package with live posting enabled.",
            [ExportCertificationEvidence(package)]));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*live external GL posting is enabled*posting-disabled reason*");
    }

    [Fact]
    public async Task CertifyExportPackageAsync_BlocksDraftArtifact()
    {
        var service = CreateService();
        var profile = CertifiedQuickBooksMappingProfile();
        await service.ImportAsync(new AccountingSystemImportRequestDto(
            "quickbooks-fixture",
            "default-fund",
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31)));
        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));
        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            PeriodStart: new DateOnly(2026, 2, 1),
            PeriodEnd: new DateOnly(2026, 2, 28),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: ["approval:export-package:qbo-february"]));
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.Draft);

        var act = () => service.CertifyExportPackageAsync(new CertifyAccountingSystemExportPackageRequestDto(
            package.ExportPackageId,
            "controller",
            "Attempt to certify stale reconciliation package.",
            [ExportCertificationEvidence(package)]));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*must be ready for review*");
    }

    [Fact]
    public async Task CreateExportPackageAsync_RejectsStaleReconciliationPeriodEvidence()
    {
        var service = CreateService();
        var profile = CertifiedQuickBooksMappingProfile();
        await service.ImportAsync(new AccountingSystemImportRequestDto(
            "quickbooks-fixture",
            "default-fund",
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31)));
        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));

        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            PeriodStart: new DateOnly(2026, 2, 1),
            PeriodEnd: new DateOnly(2026, 2, 28),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: ["approval:export-package:qbo-february"]));

        package.PostingEnabled.Should().BeFalse();
        package.Certification.Should().NotBeNull();
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.Draft);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "ExternalGlReconciliationPeriodMismatch" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.Message.Contains("2026-01-01", StringComparison.OrdinalIgnoreCase) &&
            issue.Message.Contains("2026-02-28", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateExportPackageAsync_RequiresAccountMappingsForEveryReconciledAccount()
    {
        var service = CreateService();
        var sparseProfile = CertifiedQuickBooksMappingProfile() with
        {
            ProfileId = "qbo-default-fund-sparse-certified",
            AccountMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Assets:Cash:Operating"] = "qbo-1000",
                ["Income:Investment"] = "qbo-4000"
            }
        };
        await service.ImportAsync(new AccountingSystemImportRequestDto(
            "quickbooks-fixture",
            "default-fund",
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31)));
        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            sparseProfile,
            "accounting-ops",
            FundProfileId: "default-fund",
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-sparse-certified"]));

        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: sparseProfile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: ["approval:export-package:qbo-sparse"]));

        package.PostingEnabled.Should().BeFalse();
        package.Certification.Should().NotBeNull();
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.Draft);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "MissingExternalGlAccountMapping" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "Expenses:Trading");
    }

    [Fact]
    public async Task CreateExportPackageAsync_ForPlannedProviderRequiresImportReconciliationBeforeCertification()
    {
        var service = CreateService();
        var profile = CertifiedQuickBooksMappingProfile() with
        {
            ProfileId = "netsuite-default-fund-certified",
            ProviderId = "netsuite",
            DisplayName = "Default fund NetSuite mapping"
        };
        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            ProviderId: "netsuite",
            FundProfileId: "default-fund"));

        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "netsuite",
            FundProfileId: "default-fund",
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId));

        package.PostingEnabled.Should().BeFalse();
        package.Certification.Should().NotBeNull();
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.Draft);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "MissingExternalGlReconciliation" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.Message.Contains("netsuite", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateExportPackageAsync_RequiresCertifiedAccountAndDimensionMappingCoverage()
    {
        var service = CreateService();
        var profile = CertifiedQuickBooksMappingProfile() with
        {
            ProfileId = "qbo-default-fund-weak-certified",
            AccountMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            DimensionMappings =
            [
                new DimensionMappingProfileDto(
                    "qbo-default-fund-uncertified-dimensions",
                    "Uncertified dimensions",
                    "quickbooks-fixture",
                    new LedgerDimensionSetDto(FundId: "default-fund"),
                    new LedgerDimensionSetDto(FundId: "Class:DefaultFund"),
                    AccountingCertificationStateDto.ReadyForReview)
            ]
        };
        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-weak-certified"]));

        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: ["approval:export-package:qbo-weak"]));

        package.PostingEnabled.Should().BeFalse();
        package.Certification.Should().NotBeNull();
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.Draft);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "MissingExternalGlAccountMappings" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "UncertifiedExternalGlDimensionMapping" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "IncompleteExternalGlDimensionMapping" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
    }

    [Fact]
    public async Task CreateExportPackageAsync_DoesNotGenerateLinesWithoutCertifiedCompleteDimensionMapping()
    {
        var service = CreateService();
        var profile = CertifiedQuickBooksMappingProfile() with
        {
            ProfileId = "qbo-default-fund-uncertified-dimension-lines",
            DimensionMappings =
            [
                new DimensionMappingProfileDto(
                    "qbo-default-fund-uncertified-dimension-lines",
                    "Uncertified dimensions",
                    "quickbooks-fixture",
                    new LedgerDimensionSetDto(FundId: "default-fund"),
                    new LedgerDimensionSetDto(FundId: "Class:DefaultFund"),
                    AccountingCertificationStateDto.ReadyForReview)
            ]
        };
        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            FundProfileId: "default-fund",
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-uncertified-dimension-lines"]));

        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: ["approval:export-package:qbo-dimension-lines"]));

        package.GeneratedLines.Should().BeEmpty();
        package.Certification.Should().NotBeNull();
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.Draft);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "UncertifiedExternalGlDimensionMapping" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "IncompleteExternalGlDimensionMapping" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "MissingGeneratedExternalGlExportLines" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
    }

    [Fact]
    public async Task CreateExportPackageAsync_BlocksReconciliationLedgerBookMismatch()
    {
        var exportLedgerBookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var reconciliationLedgerBookId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var service = CreateService(
            (ILedgerJournalStore?)null,
            new WrongBookAccountingSystemProvider(reconciliationLedgerBookId));
        var profile = CertifiedQuickBooksMappingProfile() with
        {
            ProfileId = "wrong-book-export-mapping",
            ProviderId = WrongBookAccountingSystemProvider.Id
        };
        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile,
            "accounting-ops",
            ProviderId: WrongBookAccountingSystemProvider.Id,
            FundProfileId: "default-fund",
            LedgerBookId: exportLedgerBookId,
            EvidenceLinks: ["approval:external-gl-mapping:wrong-book-export-mapping"]));

        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: WrongBookAccountingSystemProvider.Id,
            FundProfileId: "default-fund",
            LedgerBookId: exportLedgerBookId,
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: ["approval:external-gl-export-package:wrong-book-default-fund-2026-01-01-2026-01-31"]));

        package.LedgerBookId.Should().Be(exportLedgerBookId);
        package.Certification.Should().NotBeNull();
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.Draft);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "ExternalGlReconciliationLedgerBookMismatch" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == package.ReconciliationId);
        package.ReconciliationSafeguardState.Should().Be(ExternalGlExportReconciliationSafeguardStateDto.Blocked);
        package.ReconciliationSafeguardIssueCodes.Should().Contain("ExternalGlReconciliationLedgerBookMismatch");
        package.ReconciliationSafeguardIssueCodes.Should().Contain("MissingGeneratedExternalGlExportLines");
    }

    [Fact]
    public async Task ImportAsync_PropagatesCancellation()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.ImportAsync(new AccountingSystemImportRequestDto("quickbooks-fixture"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AccountingSystemEndpoints_ReturnProviderAndReconciliationContracts()
    {
        await using var app = await CreateAppAsync(UserPermission.AdminMaintenance);

        var providersResponse = await app.GetTestClient().GetAsync("/api/accounting-system/providers");
        var reconciliationResponse = await app.GetTestClient().GetAsync("/api/accounting-system/reconciliation/latest");
        var mappingProfileResponse = await app.GetTestClient().PostAsync(
            "/api/accounting-system/mapping-profiles",
            JsonContent(new AccountingSystemMappingProfileUpsertRequestDto(
                CertifiedQuickBooksMappingProfile(),
                "endpoint-operator",
                FundProfileId: "default-fund",
                LedgerBookId: ExternalGlLedgerBookId,
                EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"],
                TenantId: "spoofed-tenant",
                CompanyId: "spoofed-company")));
        var mappingProfilesResponse = await app.GetTestClient().GetAsync("/api/accounting-system/mapping-profiles?providerId=quickbooks-fixture&fundProfileId=default-fund");
        var exportPackageResponse = await app.GetTestClient().PostAsync(
            "/api/accounting-system/export-packages",
            JsonContent(new AccountingSystemExportPackageRequestDto(
                "endpoint-operator",
                ProviderId: "quickbooks-fixture",
                FundProfileId: "default-fund",
                LedgerBookId: ExternalGlLedgerBookId,
                PeriodStart: new DateOnly(2026, 1, 1),
                PeriodEnd: new DateOnly(2026, 1, 31),
                MappingProfileId: "qbo-default-fund-certified",
                RequireBalancedReconciliation: false,
                EvidenceLinks: [ExportControlEvidence(ExternalGlLedgerBookId)],
                TenantId: "spoofed-tenant",
                CompanyId: "spoofed-company")));

        providersResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        reconciliationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        mappingProfileResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        mappingProfilesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        exportPackageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var providers = await ReadAsync<AccountingSystemProviderDto[]>(providersResponse);
        var reconciliation = await ReadAsync<AccountingSystemReconciliationSummaryDto>(reconciliationResponse);
        var mappingProfile = await ReadAsync<ExternalGlMappingProfileDto>(mappingProfileResponse);
        var mappingProfiles = await ReadAsync<ExternalGlMappingProfileDto[]>(mappingProfilesResponse);
        var exportPackage = await ReadAsync<ExternalGlExportPackageDto>(exportPackageResponse);
        providers.Should().Contain(row => row.ProviderId == "quickbooks-fixture" && row.State == AccountingSystemProviderStateDto.Available);
        providers.Should().Contain(row => row.ProviderId == "quickbooks" && row.State == AccountingSystemProviderStateDto.Planned);
        providers.Should().Contain(row => row.ProviderId == "xero" && row.State == AccountingSystemProviderStateDto.Planned && !row.SupportsPosting);
        providers.Should().Contain(row => row.ProviderId == "netsuite" && row.State == AccountingSystemProviderStateDto.Planned && !row.SupportsPosting);
        reconciliation.ProviderId.Should().Be("quickbooks-fixture");
        reconciliation.PostingEnabled.Should().BeFalse();
        mappingProfile.ProviderId.Should().Be("quickbooks-fixture");
        mappingProfile.CertificationState.Should().Be(AccountingCertificationStateDto.Certified);
        mappingProfiles.Should().ContainSingle(row => row.ProfileId == "qbo-default-fund-certified");
        exportPackage.PostingEnabled.Should().BeFalse();
        exportPackage.TenantId.Should().Be("company-alpha");
        exportPackage.CompanyId.Should().Be("company-alpha");
        exportPackage.ExportPackageId.Should().Contain("tenant-company-alpha-company-company-alpha");
        exportPackage.Certification.Should().NotBeNull();
        exportPackage.Certification!.State.Should().Be(AccountingCertificationStateDto.ReadyForReview);
        exportPackage.GeneratedLines.Should().Contain(line =>
            line.MeridianAccountCode == "Assets:Cash:Operating" &&
            line.ExternalAccountId == "qbo-1000");

        var exportPackagesResponse = await app.GetTestClient().GetAsync(
            $"{UiApiRoutes.AccountingSystemExportPackages}?providerId=quickbooks-fixture&fundProfileId=default-fund&ledgerBookId={ExternalGlLedgerBookId:D}&certificationState=ReadyForReview&tenantId=spoofed-tenant&companyId=spoofed-company");

        exportPackagesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var exportPackages = await ReadAsync<ExternalGlExportPackageDto[]>(exportPackagesResponse);
        exportPackages.Should().ContainSingle(package =>
            package.ExportPackageId == exportPackage.ExportPackageId &&
            package.TenantId == "company-alpha" &&
            package.CompanyId == "company-alpha" &&
            package.LedgerBookId == ExternalGlLedgerBookId &&
            package.Certification!.State == AccountingCertificationStateDto.ReadyForReview);

        var certificationResponse = await app.GetTestClient().PostAsync(
            UiApiRoutes.AccountingSystemExportPackageCertification,
            JsonContent(new CertifyAccountingSystemExportPackageRequestDto(
                exportPackage.ExportPackageId,
                "spoofed-endpoint-controller",
                "Endpoint controller certified the guarded export package.",
                [ExportCertificationEvidence(exportPackage)],
                TenantId: "spoofed-tenant",
                CompanyId: "spoofed-company")));

        certificationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var certifiedExportPackage = await ReadAsync<ExternalGlExportPackageDto>(certificationResponse);
        certifiedExportPackage.PostingEnabled.Should().BeFalse();
        certifiedExportPackage.Certification.Should().NotBeNull();
        certifiedExportPackage.Certification!.State.Should().Be(AccountingCertificationStateDto.Certified);
        certifiedExportPackage.TenantId.Should().Be("company-alpha");
        certifiedExportPackage.CompanyId.Should().Be("company-alpha");
        certifiedExportPackage.Certification.Actor.Should().Be("controller.admin");
        certifiedExportPackage.Certification.EvidenceLinks.Should().Contain(ExportCertificationEvidence(exportPackage));

        var manifestResponse = await app.GetTestClient().GetAsync(
            UiApiRoutes.AccountingSystemExportPackageManifest.Replace("{exportPackageId}", certifiedExportPackage.ExportPackageId));

        manifestResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var manifest = await ReadAsync<ExternalGlExportPackageManifestDto>(manifestResponse);
        manifest.ExportPackageId.Should().Be(certifiedExportPackage.ExportPackageId);
        manifest.TenantId.Should().Be("company-alpha");
        manifest.CompanyId.Should().Be("company-alpha");
        manifest.CertificationState.Should().Be(AccountingCertificationStateDto.Certified);
        manifest.ExternalPostingAllowed.Should().BeFalse();
        manifest.ContentHash.Should().HaveLength(64);
        manifest.MappingProfileId.Should().Be("qbo-default-fund-certified");
        manifest.ReconciliationId.Should().Be(exportPackage.ReconciliationId);
        manifest.RequireBalancedReconciliation.Should().BeFalse();
        manifest.ReconciliationSafeguardState.Should().Be(ExternalGlExportReconciliationSafeguardStateDto.Certified);
        manifest.ReconciliationSafeguardIssueCodes.Should().BeEmpty();
        manifest.GeneratedLines.Should().Contain(line =>
            line.MeridianAccountCode == "Assets:Cash:Operating" &&
            line.ExternalAccountId == "qbo-1000");
        manifest.EvidenceLinks.Should().Contain(ExportCertificationEvidence(exportPackage));
    }

    [Fact]
    public async Task AccountingSystemGovernedExportEndpoints_RequireAdminMaintenance()
    {
        await using var app = await CreateAppAsync(UserPermission.ManageFundStructure);

        var mappingProfileResponse = await app.GetTestClient().PostAsync(
            "/api/accounting-system/mapping-profiles",
            JsonContent(new AccountingSystemMappingProfileUpsertRequestDto(
                CertifiedQuickBooksMappingProfile(),
                "endpoint-operator",
                FundProfileId: "default-fund",
                LedgerBookId: ExternalGlLedgerBookId,
                EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"])));
        var exportPackageResponse = await app.GetTestClient().PostAsync(
            "/api/accounting-system/export-packages",
            JsonContent(new AccountingSystemExportPackageRequestDto(
                "endpoint-operator",
                ProviderId: "quickbooks-fixture",
                FundProfileId: "default-fund",
                LedgerBookId: ExternalGlLedgerBookId,
                PeriodStart: new DateOnly(2026, 1, 1),
                PeriodEnd: new DateOnly(2026, 1, 31),
                MappingProfileId: "qbo-default-fund-certified",
                RequireBalancedReconciliation: false,
                EvidenceLinks: [ExportControlEvidence(ExternalGlLedgerBookId)])));

        mappingProfileResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        exportPackageResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var certificationResponse = await app.GetTestClient().PostAsync(
            UiApiRoutes.AccountingSystemExportPackageCertification,
            JsonContent(new CertifyAccountingSystemExportPackageRequestDto(
                "external-gl-export-guarded-package",
                "endpoint-controller",
                "Fund-structure operator attempted to certify the guarded export package.",
                ["approval:external-gl-export-package:external-gl-export-guarded-package"])));

        certificationResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListProvidersAsync_WithQuickBooksOnlineLocalConfig_ReturnsCompanyReadiness()
    {
        var store = FakeQuickBooksConnectionStore.Configured();
        var service = CreateService(new QuickBooksOnlineAccountingProvider(store, new FakeQuickBooksClient()));

        var providers = await service.ListProvidersAsync();

        var quickBooks = providers.Single(row => row.ProviderId == "quickbooks");
        quickBooks.State.Should().Be(AccountingSystemProviderStateDto.Available);
        quickBooks.RequiresCredentials.Should().BeTrue();
        quickBooks.SupportsPosting.Should().BeFalse();
        quickBooks.Connection.Should().NotBeNull();
        quickBooks.Connection!.CompanyId.Should().Be("9130359087654321");
        quickBooks.Connection.CompanyName.Should().Be("Meridian-Dev");
        quickBooks.Connection.MissingFields.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportAsync_WithQuickBooksOnlineProvider_ReturnsReadOnlyCompanyEvidence()
    {
        var store = FakeQuickBooksConnectionStore.Configured();
        var client = new FakeQuickBooksClient();
        var service = CreateService(new QuickBooksOnlineAccountingProvider(store, client));

        var detail = await service.ImportAsync(new AccountingSystemImportRequestDto(
            "quickbooks",
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31)));

        detail.Summary.ProviderId.Should().Be("quickbooks");
        detail.Summary.ProviderDisplayName.Should().Contain("Meridian-Dev");
        detail.Summary.ChartAccountCount.Should().Be(2);
        detail.Summary.JournalEntryCount.Should().Be(1);
        detail.Summary.TrialBalanceLineCount.Should().Be(2);
        detail.Summary.Warnings.Should().Contain(warning => warning.Contains("read-only", StringComparison.OrdinalIgnoreCase));
        detail.Summary.Warnings.Should().Contain(warning => warning.Contains("source of all ledger truth", StringComparison.OrdinalIgnoreCase));
        detail.JournalEntries.Should().OnlyContain(entry => entry.TotalDebits == entry.TotalCredits);
        store.SavedRefreshToken.Should().Be("rotated-refresh-token");
        store.LastVerificationSuccess.Should().BeTrue();
        client.ImportCalls.Should().Be(1);
    }

    [Fact]
    public async Task QuickBooksOnlineHttpClient_ReadCompanyEvidenceAsync_MapsReadOnlyApiPayloads()
    {
        using var httpClient = new HttpClient(new QuickBooksStubHandler());
        var client = new QuickBooksOnlineHttpClient(httpClient);
        var connection = new QuickBooksOnlineConnection(
            "qbo-client-id",
            "qbo-client-secret",
            "qbo-refresh-token",
            "9130359087654321",
            "sandbox",
            "Meridian-Dev");

        var token = await client.RefreshAccessTokenAsync(connection);
        var evidence = await client.ReadCompanyEvidenceAsync(
            connection,
            token.AccessToken,
            new AccountingSystemImportRequestDto(
                "quickbooks",
                PeriodStart: new DateOnly(2026, 1, 1),
                PeriodEnd: new DateOnly(2026, 1, 31)));

        token.AccessToken.Should().Be("qbo-access-token");
        token.RefreshToken.Should().Be("rotated-refresh-token");
        evidence.ChartAccounts.Should().HaveCount(2);
        evidence.ChartAccounts.Should().Contain(row => row.ExternalAccountId == "35" && row.AccountCode == "Assets:Checking");
        evidence.JournalEntries.Should().ContainSingle();
        evidence.JournalEntries[0].TotalDebits.Should().Be(4_151.74m);
        evidence.JournalEntries[0].TotalCredits.Should().Be(4_151.74m);
        evidence.JournalEntries[0].Lines.Should().OnlyContain(line => line.Currency == "USD");
        evidence.TrialBalance.Should().HaveCount(2);
        evidence.TrialBalance.Should().Contain(row => row.ExternalAccountId == "35" && row.Debit == 4_151.74m);
        evidence.EvidenceReferences.Should().Contain("quickbooks:company:9130359087654321:trial-balance");
    }

    [Fact]
    public async Task GetLatestImportAsync_WithoutQuickBooksOnlineLocalConfig_DefaultsToFixtureEvidence()
    {
        var service = CreateService(new QuickBooksOnlineAccountingProvider(
            FakeQuickBooksConnectionStore.Missing(),
            new FakeQuickBooksClient()));

        var detail = await service.GetLatestImportAsync();

        detail.Summary.ProviderId.Should().Be("quickbooks-fixture");
        detail.Summary.Warnings.Should().Contain(warning => warning.Contains("Fixture data", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AccountingSystemProductionReadinessEndpoint_ReturnsSharedControlPlanePosture()
    {
        await using var app = await CreateAppAsync(UserPermission.ManageFundStructure);
        var client = app.GetTestClient();

        var response = await client.PostAsync(
            UiApiRoutes.AccountingSystemProductionReadiness,
            JsonContent(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                RequiredLedgerBookScopes:
                [
                    new LedgerBookRequiredScopeDto(
                        Guid.Parse("99999999-2222-3333-4444-555555555555"),
                        FundStructureNodeKindDto.Fund,
                        AccountingBasisKindDto.Gaap,
                        "Default fund GAAP")
                ])));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var readiness = await ReadAsync<AccountingProductionReadinessDto>(response);
        readiness.Status.Should().Be(AccountingProductionReadinessStatusDto.Blocked);
        readiness.Components.Should().Contain(component => component.Area == AccountingProductionReadinessAreaDto.LedgerBooks);
        readiness.Components.Should().Contain(component => component.Area == AccountingProductionReadinessAreaDto.ExternalGl);
        readiness.Components.Should().Contain(component => component.Area == AccountingProductionReadinessAreaDto.MigrationRollout);
        readiness.TenantAdministration.Should().NotBeNull();
        readiness.TenantAdministration!.TenantId.Should().Be("company-alpha");
        readiness.TenantAdministration.CompanyId.Should().Be("company-alpha");
        readiness.Issues.Should().NotContain(issue => issue.Code == "tenant-admin.tenant-scope-missing");
        readiness.Issues.Should().Contain(issue => issue.Code == "migration.historical-journal-backfill-not-certified");
        readiness.Issues.Should().Contain(issue => issue.Code == "external-gl.live-posting-disabled");
    }

    [Fact]
    public async Task ProductionReadinessService_LoadsMigrationArtifactsByTenantCompanyScope()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAccountingMigrationRunArtifactStore, InMemoryAccountingMigrationRunArtifactStore>();
        services.AddSingleton<AccountingProductionReadinessService>();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IAccountingMigrationRunArtifactStore>();
        var ledgerBookId = Guid.Parse("77777777-2222-3333-4444-555555555555");

        await store.UpsertAsync(new AccountingMigrationRunArtifactUpsertRequestDto(
            new AccountingMigrationRunArtifactDto(
                "migration-run-dimensional-backfill-shared-run",
                AccountingMigrationRunKindDto.DimensionalBackfill,
                AccountingMigrationRunStatusDto.Certified,
                DateTimeOffset.Parse("2026-03-01T00:00:00Z"),
                CompletedAtUtc: DateTimeOffset.Parse("2026-03-01T00:15:00Z"),
                MigratedRecordCount: 1250,
                IssueCount: 0,
                EvidenceReferences: ["evidence://migration/dimensional-backfill/default-fund/company-alpha"],
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId,
                Summary: "Alpha company dimensional backfill.",
                Dimensions: FullProductionDimensions(ledgerBookId),
                TenantId: "company-alpha",
                CompanyId: "company-alpha"),
            "controller",
            CorrelationId: "dimensional-backfill-company-alpha",
            EvidenceLinks: ["approval:dimensional-backfill:company-alpha"]));

        await store.UpsertAsync(new AccountingMigrationRunArtifactUpsertRequestDto(
            new AccountingMigrationRunArtifactDto(
                "migration-run-dimensional-backfill-shared-run",
                AccountingMigrationRunKindDto.DimensionalBackfill,
                AccountingMigrationRunStatusDto.Certified,
                DateTimeOffset.Parse("2026-03-01T00:05:00Z"),
                CompletedAtUtc: DateTimeOffset.Parse("2026-03-01T00:20:00Z"),
                MigratedRecordCount: 900,
                IssueCount: 0,
                EvidenceReferences: ["evidence://migration/dimensional-backfill/default-fund/company-beta"],
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId,
                Summary: "Beta company dimensional backfill.",
                Dimensions: FullProductionDimensions(ledgerBookId),
                TenantId: "company-beta",
                CompanyId: "company-beta"),
            "controller",
            CorrelationId: "dimensional-backfill-company-beta",
            EvidenceLinks: ["approval:dimensional-backfill:company-beta"]));

        var readiness = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                TenantId: "company-alpha",
                CompanyId: "company-alpha",
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId,
                DimensionalBackfillCertified: true));

        readiness.MigrationRunArtifacts.Should().ContainSingle(artifact =>
            artifact.RunId == "migration-run-dimensional-backfill-shared-run" &&
            artifact.TenantId == "company-alpha" &&
            artifact.CompanyId == "company-alpha");
        readiness.MigrationRunArtifacts.Should().NotContain(artifact => artifact.CompanyId == "company-beta");
        readiness.Issues.Should().NotContain(issue => issue.Code == "migration.dimensional-backfill-certified-run-missing");

        var mismatched = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                TenantId: "company-alpha",
                CompanyId: "company-gamma",
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId,
                DimensionalBackfillCertified: true));

        mismatched.MigrationRunArtifacts.Should().BeEmpty();
        mismatched.Issues.Should().Contain(issue => issue.Code == "migration.dimensional-backfill-certified-run-missing");
    }

    [Fact]
    public async Task ProductionReadinessService_RejectsMigrationArtifactsOutsideRequestedTenantCompanyScope()
    {
        var services = new ServiceCollection();
        services.AddSingleton<AccountingProductionReadinessService>();
        await using var provider = services.BuildServiceProvider();
        var ledgerBookId = Guid.Parse("77777777-2222-3333-4444-555555555555");

        var readiness = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                TenantId: "tenant-alpha",
                CompanyId: "company-alpha",
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId,
                LedgerBookMigrationCertified: true,
                MigrationRunArtifacts:
                [
                    new AccountingMigrationRunArtifactDto(
                        "migration-run-ledger-book-scope-beta",
                        AccountingMigrationRunKindDto.LedgerBookScope,
                        AccountingMigrationRunStatusDto.Certified,
                        DateTimeOffset.Parse("2026-03-02T00:00:00Z"),
                        CompletedAtUtc: DateTimeOffset.Parse("2026-03-02T00:15:00Z"),
                        MigratedRecordCount: 275,
                        IssueCount: 0,
                        EvidenceReferences: ["evidence://migration/ledger-book-scope/default-fund/company-beta"],
                        FundProfileId: "default-fund",
                        LedgerBookId: ledgerBookId,
                        Summary: "Beta company ledger-book migration.",
                        TenantId: "tenant-beta",
                        CompanyId: "company-beta")
                ]));

        readiness.Issues.Should().Contain(issue =>
            issue.Code == "migration.ledger-book-scope-artifact-scope-mismatch" &&
            issue.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.EvidenceReferences.Contains("evidence://migration/ledger-book-scope/default-fund/company-beta"));
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "migration.ledger-book-scope-certified-run-missing" &&
            issue.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        readiness.Issues.Should().NotContain(issue => issue.Code == "migration.tenant-scope-missing");
        readiness.Issues.Should().NotContain(issue => issue.Code == "migration.company-scope-missing");
        readiness.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            component.Status == AccountingProductionReadinessStatusDto.Blocked);
    }

    [Fact]
    public async Task AccountingSystemMigrationRunArtifactEndpoints_PersistAndFeedReadiness()
    {
        await using var app = await CreateAppAsync(UserPermission.AdminMaintenance);
        var client = app.GetTestClient();
        var ledgerBookId = Guid.Parse("77777777-2222-3333-4444-555555555555");

        var upsertResponse = await client.PostAsync(
            UiApiRoutes.AccountingSystemMigrationRunArtifacts,
            JsonContent(new AccountingMigrationRunArtifactUpsertRequestDto(
                new AccountingMigrationRunArtifactDto(
                    "migration-run-dimensional-backfill-default-fund",
                    AccountingMigrationRunKindDto.DimensionalBackfill,
                    AccountingMigrationRunStatusDto.Certified,
                    DateTimeOffset.Parse("2026-03-01T00:00:00Z"),
                    CompletedAtUtc: DateTimeOffset.Parse("2026-03-01T00:15:00Z"),
                    MigratedRecordCount: 1250,
                    IssueCount: 0,
                    EvidenceReferences: ["evidence://migration/dimensional-backfill/default-fund/certified"],
                    FundProfileId: "default-fund",
                    LedgerBookId: ledgerBookId,
                    Summary: "Dimensional backfill certified for retained journal/report paths.",
                    Dimensions: FullProductionDimensions(ledgerBookId),
                    TenantId: "spoofed-tenant",
                    CompanyId: "spoofed-company"),
                "spoofed-browser-user",
                CorrelationId: "dimensional-backfill-default-fund",
                EvidenceLinks: ["approval:dimensional-backfill:default-fund"])));

        upsertResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var upserted = await ReadAsync<AccountingMigrationRunArtifactDto>(upsertResponse);
        upserted.Actor.Should().Be("controller.admin");
        upserted.FundProfileId.Should().Be("default-fund");
        upserted.TenantId.Should().Be("company-alpha");
        upserted.CompanyId.Should().Be("company-alpha");
        upserted.Dimensions.Should().NotBeNull();
        upserted.Dimensions!.BookId.Should().Be(ledgerBookId.ToString("D"));
        upserted.EvidenceReferences.Should().Contain("approval:dimensional-backfill:default-fund");

        var unscopedResponse = await client.PostAsync(
            UiApiRoutes.AccountingSystemMigrationRunArtifacts,
            JsonContent(new AccountingMigrationRunArtifactUpsertRequestDto(
                new AccountingMigrationRunArtifactDto(
                    "migration-run-dimensional-backfill-unscoped",
                    AccountingMigrationRunKindDto.DimensionalBackfill,
                    AccountingMigrationRunStatusDto.Certified,
                    DateTimeOffset.Parse("2026-03-01T01:00:00Z"),
                    CompletedAtUtc: DateTimeOffset.Parse("2026-03-01T01:15:00Z"),
                    MigratedRecordCount: 800,
                    IssueCount: 0,
                    EvidenceReferences: ["evidence://migration/dimensional-backfill/default-fund/unscoped"],
                    FundProfileId: "default-fund",
                    Summary: "Legacy fund-level dimensional backfill retained without a book scope."),
                "spoofed-browser-user",
                CorrelationId: "dimensional-backfill-unscoped")));

        unscopedResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listResponse = await client.GetAsync(
            $"{UiApiRoutes.AccountingSystemMigrationRunArtifacts}?fundProfileId=default-fund&ledgerBookId={ledgerBookId:D}");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listed = await ReadAsync<AccountingMigrationRunArtifactListDto>(listResponse);
        listed.TenantId.Should().Be("company-alpha");
        listed.CompanyId.Should().Be("company-alpha");
        listed.Artifacts.Should().ContainSingle(artifact =>
            artifact.RunId == upserted.RunId &&
            artifact.TenantId == "company-alpha" &&
            artifact.CompanyId == "company-alpha");

        var readinessResponse = await client.PostAsync(
            UiApiRoutes.AccountingSystemProductionReadiness,
            JsonContent(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId,
                DimensionalBackfillCertified: true)));

        readinessResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var readiness = await ReadAsync<AccountingProductionReadinessDto>(readinessResponse);
        readiness.MigrationRunArtifacts.Should().ContainSingle(artifact =>
            artifact.RunId == upserted.RunId &&
            artifact.TenantId == "company-alpha" &&
            artifact.CompanyId == "company-alpha");
        readiness.Issues.Should().NotContain(issue => issue.Code == "migration.dimensional-backfill-certified-run-missing");
        readiness.Issues.Should().NotContain(issue => issue.Code == "migration.dimensional-backfill-dimensions-scope-mismatch");
        readiness.Issues.Should().NotContain(issue => issue.Code == "migration.dimensional-backfill-canonical-coverage-missing");
        readiness.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            component.EvidenceReferences.Contains("approval:dimensional-backfill:default-fund"));
    }

    [Fact]
    public async Task AccountingSystemTenantAdministrationProfileEndpoint_PersistsAndFeedsReadiness()
    {
        await using var app = await CreateAppAsync(UserPermission.AdminMaintenance);
        var client = app.GetTestClient();

        var upsertResponse = await client.PostAsync(
            UiApiRoutes.AccountingSystemTenantAdministrationProfile,
            JsonContent(new AccountingTenantAdministrationProfileUpsertRequestDto(
                new AccountingTenantAdministrationProfileDto(
                    "company-alpha",
                    "company-alpha",
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
                    UpdatedAtUtc: DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                    UpdatedBy: "spoofed-profile-updater",
                    EvidenceReferences: ["evidence://tenant-admin/company-alpha/tenant-admin/full"]),
                "spoofed-browser-user",
                CorrelationId: "tenant-admin-company-alpha",
                EvidenceLinks: ["approval:tenant-admin:company-alpha"])));

        upsertResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var upserted = await ReadAsync<AccountingTenantAdministrationProfileDto>(upsertResponse);
        upserted.TenantId.Should().Be("company-alpha");
        upserted.CompanyId.Should().Be("company-alpha");
        upserted.UpdatedBy.Should().Be("controller.admin");
        upserted.EvidenceReferences.Should().Contain("approval:tenant-admin:company-alpha");

        var getResponse = await client.GetAsync(UiApiRoutes.AccountingSystemTenantAdministrationProfile);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var retained = await ReadAsync<AccountingTenantAdministrationProfileDto>(getResponse);
        retained.EvidenceReferences.Should().Contain("correlation:tenant-admin-company-alpha");

        var readinessResponse = await client.PostAsync(
            UiApiRoutes.AccountingSystemProductionReadiness,
            JsonContent(new AccountingProductionReadinessRequestDto(FundProfileId: "default-fund")));

        readinessResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var readiness = await ReadAsync<AccountingProductionReadinessDto>(readinessResponse);
        readiness.TenantAdministration.Should().NotBeNull();
        readiness.TenantAdministration!.CompletedControlCount.Should().Be(23);
        readiness.Issues.Should().NotContain(issue => issue.Area == AccountingProductionReadinessAreaDto.TenantAdministration);
        readiness.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.TenantAdministration &&
            component.Status == AccountingProductionReadinessStatusDto.Ready &&
            component.EvidenceReferences.Contains("approval:tenant-admin:company-alpha"));
    }

    [Fact]
    public async Task AccountingSystemTenantAdministrationProfileEndpoint_UsesTrustedTenantContext()
    {
        await using var app = await CreateAppAsync(UserPermission.AdminMaintenance);
        var client = app.GetTestClient();

        var upsertResponse = await client.PostAsync(
            UiApiRoutes.AccountingSystemTenantAdministrationProfile,
            JsonContent(new AccountingTenantAdministrationProfileUpsertRequestDto(
                new AccountingTenantAdministrationProfileDto(
                    "tenant-spoof",
                    "company-spoof",
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
                    UpdatedAtUtc: DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                    UpdatedBy: "spoofed-profile-updater",
                    EvidenceReferences: ["evidence://tenant-admin/tenant-spoof/setup-certified"]),
                "spoofed-browser-user",
                CorrelationId: "tenant-admin-spoof",
                EvidenceLinks: ["approval:tenant-admin:tenant-spoof"])));

        upsertResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var upserted = await ReadAsync<AccountingTenantAdministrationProfileDto>(upsertResponse);
        upserted.TenantId.Should().Be("company-alpha");
        upserted.CompanyId.Should().Be("company-alpha");
        upserted.UpdatedBy.Should().Be("controller.admin");

        var getResponse = await client.GetAsync(
            $"{UiApiRoutes.AccountingSystemTenantAdministrationProfile}?tenantId=tenant-spoof&companyId=company-spoof");

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var retained = await ReadAsync<AccountingTenantAdministrationProfileDto>(getResponse);
        retained.TenantId.Should().Be("company-alpha");
        retained.CompanyId.Should().Be("company-alpha");
        retained.EvidenceReferences.Should().Contain("correlation:tenant-admin-spoof");
    }

    [Fact]
    public async Task AccountingSystemProductionCertificationProfileEndpoint_PersistsAndFeedsReadiness()
    {
        await using var app = await CreateAppAsync(UserPermission.AdminMaintenance);
        var client = app.GetTestClient();

        var upsertResponse = await client.PostAsync(
            UiApiRoutes.AccountingSystemProductionCertificationProfile,
            JsonContent(new AccountingProductionCertificationProfileUpsertRequestDto(
                new AccountingProductionCertificationProfileDto(
                    "default-fund",
                    ExternalGlLedgerBookId,
                    PostingRulesLedgerBookNativeCertified: true,
                    JournalLifecycleLedgerBookNativeCertified: true,
                    CloseReportingLedgerBookNativeCertified: true,
                    ExternalGlLedgerBookNativeCertified: true,
                    PeriodReportDimensionQueriesCertified: true,
                    CrossPeriodReportDimensionQueriesCertified: true,
                    JournalQueryDimensionFiltersCertified: true,
                    ExternalExportDimensionMappingCertified: true,
                    UpdatedAtUtc: DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                    UpdatedBy: "controller",
                    EvidenceReferences: [$"evidence://ledger-book/{ExternalGlLedgerBookId:D}/production-certification/full/dimension-scope/canonical-production"],
                    ReconciliationLedgerBookNativeCertified: true,
                    DirectLendingLedgerBookNativeCertified: true,
                    StrategyLedgerReadLedgerBookNativeCertified: true,
                    LedgerLineDimensionsPersistedCertified: true,
                    TrialBalanceDimensionFiltersCertified: true,
                    ReportPackageDimensionProvenanceCertified: true),
                "spoofed-browser-user",
                CorrelationId: "production-certification-default-fund",
                EvidenceLinks:
                [
                    $"approval:tenant:company-alpha:company:company-alpha:fund:default-fund:ledger-book:{ExternalGlLedgerBookId:D}:production-certification"
                ])));

        upsertResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var upserted = await ReadAsync<AccountingProductionCertificationProfileDto>(upsertResponse);
        upserted.FundProfileId.Should().Be("default-fund");
        upserted.LedgerBookId.Should().Be(ExternalGlLedgerBookId);
        upserted.TenantId.Should().Be("company-alpha");
        upserted.CompanyId.Should().Be("company-alpha");
        upserted.UpdatedBy.Should().Be("controller.admin");
        upserted.EvidenceReferences.Should().Contain($"approval:tenant:company-alpha:company:company-alpha:fund:default-fund:ledger-book:{ExternalGlLedgerBookId:D}:production-certification");

        var getResponse = await client.GetAsync(
            $"{UiApiRoutes.AccountingSystemProductionCertificationProfile}?fundProfileId=default-fund&ledgerBookId={ExternalGlLedgerBookId:D}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var retained = await ReadAsync<AccountingProductionCertificationProfileDto>(getResponse);
        retained.TenantId.Should().Be("company-alpha");
        retained.CompanyId.Should().Be("company-alpha");
        retained.EvidenceReferences.Should().Contain("correlation:production-certification-default-fund");

        var readinessResponse = await client.PostAsync(
            UiApiRoutes.AccountingSystemProductionReadiness,
            JsonContent(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookId: ExternalGlLedgerBookId)));

        readinessResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var readiness = await ReadAsync<AccountingProductionReadinessDto>(readinessResponse);
        readiness.LedgerBookWorkflows.Should().NotBeNull();
        readiness.LedgerBookWorkflows!.CompletedControlCount.Should().Be(9);
        readiness.DimensionalReporting.Should().NotBeNull();
        readiness.DimensionalReporting!.CompletedControlCount.Should().Be(10);
        readiness.Issues.Should().NotContain(issue =>
            issue.Code == "ledger-books.workflow-evidence-missing" ||
            issue.Code == "dimensions.reporting-evidence-missing");
    }

    [Fact]
    public async Task AccountingSystemProductionCertificationProfileEndpoint_BlocksMismatchedRolloutEvidence()
    {
        await using var app = await CreateAppAsync(UserPermission.AdminMaintenance);
        var client = app.GetTestClient();

        var upsertResponse = await client.PostAsync(
            UiApiRoutes.AccountingSystemProductionCertificationProfile,
            JsonContent(new AccountingProductionCertificationProfileUpsertRequestDto(
                new AccountingProductionCertificationProfileDto(
                    "default-fund",
                    ExternalGlLedgerBookId,
                    PostingRulesLedgerBookNativeCertified: true,
                    JournalLifecycleLedgerBookNativeCertified: true,
                    CloseReportingLedgerBookNativeCertified: true,
                    ExternalGlLedgerBookNativeCertified: true,
                    PeriodReportDimensionQueriesCertified: true,
                    CrossPeriodReportDimensionQueriesCertified: true,
                    JournalQueryDimensionFiltersCertified: true,
                    ExternalExportDimensionMappingCertified: true,
                    UpdatedAtUtc: DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                    UpdatedBy: "controller",
                    EvidenceReferences:
                    [
                        $"evidence://tenant/company-beta/company/company-beta/fund/default-fund/ledger-book/{ExternalGlLedgerBookId:D}/production-certification/full/dimension-scope/canonical-production"
                    ],
                    ReconciliationLedgerBookNativeCertified: true,
                    DirectLendingLedgerBookNativeCertified: true,
                    StrategyLedgerReadLedgerBookNativeCertified: true,
                    LedgerLineDimensionsPersistedCertified: true,
                    TrialBalanceDimensionFiltersCertified: true,
                    ReportPackageDimensionProvenanceCertified: true),
                "spoofed-browser-user",
                CorrelationId: "production-certification-mismatched-rollout")));

        upsertResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var readinessResponse = await client.PostAsync(
            UiApiRoutes.AccountingSystemProductionReadiness,
            JsonContent(new AccountingProductionReadinessRequestDto(
                FundProfileId: "default-fund",
                LedgerBookId: ExternalGlLedgerBookId)));

        readinessResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var readiness = await ReadAsync<AccountingProductionReadinessDto>(readinessResponse);
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "ledger-books.workflow-evidence-rollout-scope-mismatch" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "dimensions.reporting-evidence-rollout-scope-mismatch" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
    }

    [Fact]
    public async Task AccountingSystemGovernedSetupEndpoints_RejectAssistantOriginMutations()
    {
        await using var app = await CreateAppAsync(UserPermission.AdminMaintenance);
        var client = app.GetTestClient();
        var ledgerBookId = Guid.Parse("77777777-2222-3333-4444-555555555555");

        var migrationResponse = await client.PostAsync(
            UiApiRoutes.AccountingSystemMigrationRunArtifacts,
            JsonContent(new AccountingMigrationRunArtifactUpsertRequestDto(
                new AccountingMigrationRunArtifactDto(
                    "assistant-migration-run",
                    AccountingMigrationRunKindDto.LedgerBookScope,
                    AccountingMigrationRunStatusDto.Certified,
                    DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                    CompletedAtUtc: DateTimeOffset.Parse("2026-06-01T00:05:00Z"),
                    Actor: "assistant",
                    MigratedRecordCount: 1,
                    IssueCount: 0,
                    EvidenceReferences: ["evidence://migration/ledger-book-scope/default-fund/assistant"],
                    FundProfileId: "default-fund",
                    LedgerBookId: ledgerBookId,
                    Summary: "Assistant drafted migration artifact."),
                "assistant",
                CorrelationId: "assistant-migration",
                EvidenceLinks: ["approval:assistant-migration"],
                ActionOrigin: OperationsActionOriginDto.AssistantDraft)));

        var tenantAdministrationResponse = await client.PostAsync(
            UiApiRoutes.AccountingSystemTenantAdministrationProfile,
            JsonContent(new AccountingTenantAdministrationProfileUpsertRequestDto(
                new AccountingTenantAdministrationProfileDto(
                    "company-alpha",
                    "company-alpha",
                    TenantScopeConfigured: true,
                    AdminRoleProfileConfigured: true,
                    ScopedAccessPoliciesConfigured: true,
                    ReportingGroupsConfigured: true,
                    AccountingAdminSurfaceConfigured: true,
                    UpdatedAtUtc: DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                    UpdatedBy: "assistant",
                    EvidenceReferences: ["evidence://tenant-admin/company-alpha/assistant"]),
                "assistant",
                CorrelationId: "assistant-tenant-admin",
                EvidenceLinks: ["approval:assistant-tenant-admin"],
                ActionOrigin: OperationsActionOriginDto.AssistantDraft)));

        var productionCertificationResponse = await client.PostAsync(
            UiApiRoutes.AccountingSystemProductionCertificationProfile,
            JsonContent(new AccountingProductionCertificationProfileUpsertRequestDto(
                new AccountingProductionCertificationProfileDto(
                    "default-fund",
                    ledgerBookId,
                    PostingRulesLedgerBookNativeCertified: true,
                    JournalLifecycleLedgerBookNativeCertified: true,
                    CloseReportingLedgerBookNativeCertified: true,
                    ExternalGlLedgerBookNativeCertified: true,
                    PeriodReportDimensionQueriesCertified: true,
                    CrossPeriodReportDimensionQueriesCertified: true,
                    JournalQueryDimensionFiltersCertified: true,
                    ExternalExportDimensionMappingCertified: true,
                    UpdatedAtUtc: DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                    UpdatedBy: "assistant",
                    EvidenceReferences: [$"evidence://ledger-book/{ledgerBookId:D}/assistant-production-certification"]),
                "assistant",
                CorrelationId: "assistant-production-certification",
                EvidenceLinks: [$"approval:ledger-book:{ledgerBookId:D}:assistant-production-certification"],
                ActionOrigin: OperationsActionOriginDto.AssistantDraft)));

        var mappingProfileResponse = await client.PostAsync(
            UiApiRoutes.AccountingSystemMappingProfiles,
            JsonContent(new AccountingSystemMappingProfileUpsertRequestDto(
                CertifiedQuickBooksMappingProfile(),
                "assistant",
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId,
                EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"],
                ActionOrigin: OperationsActionOriginDto.AssistantDraft)));

        var exportPackageResponse = await client.PostAsync(
            UiApiRoutes.AccountingSystemExportPackages,
            JsonContent(new AccountingSystemExportPackageRequestDto(
                "assistant",
                ProviderId: "quickbooks-fixture",
                FundProfileId: "default-fund",
                LedgerBookId: ledgerBookId,
                PeriodStart: new DateOnly(2026, 1, 1),
                PeriodEnd: new DateOnly(2026, 1, 31),
                MappingProfileId: "qbo-default-fund-certified",
                RequireBalancedReconciliation: false,
                EvidenceLinks: [ExportControlEvidence(ledgerBookId)],
                ActionOrigin: OperationsActionOriginDto.AssistantDraft)));

        foreach (var response in new[] { migrationResponse, tenantAdministrationResponse, productionCertificationResponse, mappingProfileResponse, exportPackageResponse })
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Contain("human operator");
        }
    }

    [Fact]
    public async Task AccountingSystemEndpoints_WithoutAccountingAccess_ReturnForbidden()
    {
        await using var app = await CreateAppAsync(UserPermission.ViewMarketData);

        var response = await app.GetTestClient().GetAsync("/api/accounting-system/providers");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static AccountingSystemIntegrationService CreateService(params IAccountingSystemProvider[] additionalProviders)
        => CreateService(null, additionalProviders);

    private static AccountingSystemIntegrationService CreateService(
        ILedgerJournalStore? ledgerJournalStore,
        params IAccountingSystemProvider[] additionalProviders)
        => new(new IAccountingSystemProvider[]
        {
            new QuickBooksFixtureAccountingProvider(),
            new XeroFixtureAccountingProvider(),
            new NetSuiteFixtureAccountingProvider()
        }.Concat(additionalProviders), ledgerJournalStore);

    private static LedgerDimensionSetDto FullProductionDimensions(Guid ledgerBookId)
        => new(
            FundId: "default-fund",
            EntityId: "entity-alpha",
            SleeveId: "sleeve-opportunistic-credit",
            StrategyId: "strategy-income",
            InvestorId: "investor-lp-alpha",
            CapitalAccountId: "capital-account-lp-alpha",
            InstrumentId: Guid.Parse("22222222-3333-4444-5555-666666666666"),
            TaxLotId: "tax-lot-2026-001",
            CostCenterId: "ops",
            CounterpartyId: "administrator",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["department"] = "fund-accounting",
                ["class"] = "default-fund"
            },
            BookId: ledgerBookId.ToString("D"));

    private sealed class PostingCapableAccountingSystemProvider : IAccountingSystemProvider
    {
        public string ProviderId => "posting-provider";

        public string DisplayName => "Posting Provider";

        public AccountingSystemProviderCapabilities Capabilities { get; } = new(
            SupportsChartOfAccounts: true,
            SupportsJournalEntries: true,
            SupportsTrialBalance: true,
            SupportsPosting: true,
            EvidenceKinds: ["chart", "journal", "trial-balance", "posting"]);

        public Task<AccountingSystemImportDetailDto> ImportAsync(
            AccountingSystemImportRequestDto request,
            CancellationToken ct = default)
            => throw new NotSupportedException("The test provider exists only to expose live posting capability metadata.");
    }

    private static ILedgerJournalStore CreateMatchedQuickBooksFixtureLedgerStore(Guid? ledgerBookIdOverride = null)
    {
        var ledgerBookId = ledgerBookIdOverride ?? ExternalGlLedgerBookId;
        var periodId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero);
        var journalEntryId = Guid.NewGuid();
        var cashLineId = Guid.NewGuid();
        var expenseLineId = Guid.NewGuid();
        var incomeLineId = Guid.NewGuid();
        const string journalDescription = "Fixture ledger tie-out for guarded external GL export";
        var journal = new JournalEntry(
            journalEntryId,
            timestamp,
            journalDescription,
            [
                new LedgerEntry(
                    cashLineId,
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("Assets:Cash:Operating", LedgerAccountType.Asset),
                    248_750m,
                    0m,
                    journalDescription),
                new LedgerEntry(
                    expenseLineId,
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("Expenses:Trading", LedgerAccountType.Expense),
                    1_250m,
                    0m,
                    journalDescription),
                new LedgerEntry(
                    incomeLineId,
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("Income:Investment", LedgerAccountType.Revenue),
                    0m,
                    250_000m,
                    journalDescription)
            ]);

        return new StaticLedgerJournalStore(
            new LedgerBookRecord(
                ledgerBookId,
                "default-fund",
                Guid.NewGuid(),
                FundStructureNodeKindDto.Fund,
                "Default fund primary book",
                "USD",
                timestamp,
                timestamp),
            new LedgerAccountingPeriod(
                periodId,
                ledgerBookId,
                2026,
                1,
                "2026-01",
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 31),
                "Open",
                timestamp,
                null,
                1),
            [
                new LedgerJournalEntryRecord(
                    journal,
                    Guid.NewGuid(),
                    periodId,
                    CommandId: null,
                    CorrelationId: null,
                    GlobalSequence: 1,
                    CreatedAt: timestamp,
                    SourceEventId: Guid.NewGuid(),
                SourceJournalEntryId: Guid.NewGuid())
            ]);
    }

    private static ILedgerJournalStore CreateMatchedExternalGlFixtureLedgerStore(string providerId)
    {
        (string AccountCode, LedgerAccountType AccountType, decimal Debit, decimal Credit)[] lines = providerId switch
        {
            "xero-fixture" =>
            [
                ("090", LedgerAccountType.Asset, 179_050m, 0m),
                ("404", LedgerAccountType.Expense, 950m, 0m),
                ("200", LedgerAccountType.Revenue, 0m, 180_000m)
            ],
            "netsuite-fixture" =>
            [
                ("1000", LedgerAccountType.Asset, 308_125m, 0m),
                ("7100", LedgerAccountType.Expense, 1_875m, 0m),
                ("4100", LedgerAccountType.Revenue, 0m, 310_000m)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(providerId), providerId, "Unsupported external GL fixture provider.")
        };

        return CreateMatchedFixtureLedgerStore(lines);
    }

    private static ILedgerJournalStore CreateMatchedFixtureLedgerStore(
        IReadOnlyList<(string AccountCode, LedgerAccountType AccountType, decimal Debit, decimal Credit)> lines)
    {
        var ledgerBookId = ExternalGlLedgerBookId;
        var periodId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero);
        var journalEntryId = Guid.NewGuid();
        const string journalDescription = "Fixture ledger tie-out for guarded external GL export";
        var journal = new JournalEntry(
            journalEntryId,
            timestamp,
            journalDescription,
            lines
                .Select(line => new LedgerEntry(
                    Guid.NewGuid(),
                    journalEntryId,
                    timestamp,
                    new LedgerAccount(line.AccountCode, line.AccountType),
                    line.Debit,
                    line.Credit,
                    journalDescription))
                .ToArray());

        return new StaticLedgerJournalStore(
            new LedgerBookRecord(
                ledgerBookId,
                "default-fund",
                Guid.NewGuid(),
                FundStructureNodeKindDto.Fund,
                "Default fund primary book",
                "USD",
                timestamp,
                timestamp),
            new LedgerAccountingPeriod(
                periodId,
                ledgerBookId,
                2026,
                1,
                "2026-01",
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 31),
                "Open",
                timestamp,
                null,
                1),
            [
                new LedgerJournalEntryRecord(
                    journal,
                    Guid.NewGuid(),
                    periodId,
                    CommandId: null,
                    CorrelationId: null,
                    GlobalSequence: 1,
                    CreatedAt: timestamp,
                    SourceEventId: Guid.NewGuid(),
                    SourceJournalEntryId: Guid.NewGuid())
            ]);
    }

    private static async Task<WebApplication> CreateAppAsync(UserPermission permissions)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IAccountingSystemProvider, QuickBooksFixtureAccountingProvider>();
        builder.Services.AddSingleton<IAccountingSystemProvider, XeroFixtureAccountingProvider>();
        builder.Services.AddSingleton<IAccountingSystemProvider, NetSuiteFixtureAccountingProvider>();
        builder.Services.AddSingleton<ILedgerJournalStore>(_ => CreateMatchedQuickBooksFixtureLedgerStore(ExternalGlLedgerBookId));
        builder.Services.AddSingleton<ILedgerBookService>(sp => new PostgresLedgerBookService(sp.GetRequiredService<ILedgerJournalStore>()));
        builder.Services.AddSingleton<AccountingSystemIntegrationService>();
        builder.Services.AddSingleton<IAccountingConfigurationStore, InMemoryAccountingConfigurationStore>();
        builder.Services.AddSingleton<IAccountingActionAuditStore, InMemoryAccountingActionAuditStore>();
        builder.Services.AddSingleton<IAccountingConfigurationService, AccountingConfigurationService>();
        builder.Services.AddSingleton<IAccountingMigrationRunArtifactStore, InMemoryAccountingMigrationRunArtifactStore>();
        builder.Services.AddSingleton<IAccountingTenantAdministrationProfileStore, InMemoryAccountingTenantAdministrationProfileStore>();
        builder.Services.AddSingleton<IAccountingProductionCertificationProfileStore, InMemoryAccountingProductionCertificationProfileStore>();
        builder.Services.AddSingleton<AccountingProductionReadinessService>();
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy(UiEndpoints.MutationRateLimitPolicy, _ =>
                RateLimitPartition.GetNoLimiter<string>("test"));
        });

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "controller.admin";
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-alpha";
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "company-alpha";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            await next();
        });
        app.UseRateLimiter();
        app.MapAccountingSystemEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await app.StartAsync();
        return app;
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        result.Should().NotBeNull($"expected {typeof(T).Name}, got {json}");
        return result!;
    }

    private static void RetainExportPackage(
        AccountingSystemIntegrationService service,
        ExternalGlExportPackageDto package)
    {
        var field = typeof(AccountingSystemIntegrationService).GetField(
            "_exportPackages",
            BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull("the test needs to simulate retained package posture that the public creation path refuses to emit");
        var packages = field!.GetValue(service) as ConcurrentDictionary<string, ExternalGlExportPackageDto>;
        packages.Should().NotBeNull();
        packages![package.ExportPackageId] = package;
    }

    private static string ExportCertificationEvidence(ExternalGlExportPackageDto package)
    {
        package.Certification.Should().NotBeNull();
        var ledgerBookScope = package.LedgerBookId is Guid ledgerBookId
            ? $":ledger-book:{ledgerBookId:D}"
            : string.Empty;
        return $"approval:external-gl-export-certification:{package.ExportPackageId}:{package.Certification!.CertificationId}{ledgerBookScope}:{package.PeriodStart:yyyy-MM-dd}:{package.PeriodEnd:yyyy-MM-dd}";
    }

    private static string ExportControlEvidence(Guid ledgerBookId, string providerId = "quickbooks-fixture", string fundProfileId = "default-fund")
        => $"approval:export-package:{providerId}:{fundProfileId}:ledger-book:{ledgerBookId:D}:2026-01-01:2026-01-31";


    private static ExternalGlMappingProfileDto CertifiedQuickBooksMappingProfile()
        => new(
            "qbo-default-fund-certified",
            "quickbooks-fixture",
            "Default fund QBO mapping",
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            [
                new DimensionMappingProfileDto(
                    "qbo-default-fund-dimensions",
                    "Default fund dimensions",
                    "quickbooks-fixture",
                    new LedgerDimensionSetDto(FundId: "default-fund", EntityId: "fund-entity-main"),
                    new LedgerDimensionSetDto(
                        FundId: "Class:DefaultFund",
                        EntityId: "Location:Main",
                        ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Class"] = "DefaultFund",
                            ["Location"] = "Main"
                        }),
                    AccountingCertificationStateDto.Certified)
            ],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Assets:Cash:Operating"] = "qbo-1000",
                ["Assets:Investments:Public"] = "qbo-1500",
                ["Income:Investment"] = "qbo-4000",
                ["Expenses:Trading"] = "qbo-6100"
            },
            AccountingCertificationStateDto.Certified);

    private static ExternalGlMappingProfileDto CertifiedFixtureMappingProfile(string providerId, string profileId)
    {
        var accountMappings = providerId switch
        {
            "xero-fixture" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["090"] = "xero-bank-001",
                ["620"] = "xero-invest-001",
                ["200"] = "xero-income-001",
                ["404"] = "xero-expense-001"
            },
            "netsuite-fixture" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["1000"] = "ns-1000",
                ["1200"] = "ns-1200",
                ["4100"] = "ns-4100",
                ["7100"] = "ns-7100"
            },
            _ => throw new ArgumentOutOfRangeException(nameof(providerId), providerId, "Unsupported external GL fixture provider.")
        };

        var externalDimensionPrefix = providerId == "xero-fixture" ? "Tracking" : "Segment";
        return new ExternalGlMappingProfileDto(
            profileId,
            providerId,
            $"{providerId} default fund mapping",
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            [
                new DimensionMappingProfileDto(
                    $"{profileId}-dimensions",
                    "Default fund dimensions",
                    providerId,
                    new LedgerDimensionSetDto(FundId: "default-fund", EntityId: "fund-entity-main"),
                    new LedgerDimensionSetDto(
                        FundId: $"{externalDimensionPrefix}:DefaultFund",
                        EntityId: $"{externalDimensionPrefix}:Main",
                        ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Fund"] = "DefaultFund",
                            ["Entity"] = "Main"
                        }),
                    AccountingCertificationStateDto.Certified)
            ],
            accountMappings,
            AccountingCertificationStateDto.Certified);
    }

    private sealed class WrongBookAccountingSystemProvider(Guid ledgerBookId) : IAccountingSystemProvider
    {
        public const string Id = "wrong-book-fixture";

        private readonly QuickBooksFixtureAccountingProvider _inner = new();

        public string ProviderId => Id;

        public string DisplayName => "Wrong Book Fixture";

        public AccountingSystemProviderCapabilities Capabilities => _inner.Capabilities;

        public async Task<AccountingSystemImportDetailDto> ImportAsync(
            AccountingSystemImportRequestDto request,
            CancellationToken ct = default)
        {
            var detail = await _inner.ImportAsync(request with { ProviderId = QuickBooksFixtureAccountingProvider.Id }, ct)
                .ConfigureAwait(false);
            return detail with
            {
                Summary = detail.Summary with
                {
                    ImportId = $"wrong-book-{detail.Summary.ImportId}",
                    ProviderId = Id,
                    ProviderDisplayName = DisplayName,
                    LedgerBookId = ledgerBookId
                }
            };
        }
    }

    private sealed class StaticLedgerJournalStore(
        LedgerBookRecord book,
        LedgerAccountingPeriod period,
        IReadOnlyList<LedgerJournalEntryRecord> records) : ILedgerJournalStore
    {
        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default)
            => throw new NotSupportedException("Static ledger journal store is read-only.");

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(periodId == period.PeriodId ? records : []);
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
                records.Where(record => record.AggregateId == aggregateId).ToArray());
        }

        public Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<LedgerAccountingPeriod?>(periodId == period.PeriodId ? period : null);
        }

        public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
            Guid? ledgerBookId = null,
            string? status = null,
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var matches =
                (!ledgerBookId.HasValue || ledgerBookId.Value == period.LedgerBookId) &&
                (string.IsNullOrWhiteSpace(status) || string.Equals(status, period.Status, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(fundProfileId) || string.Equals(fundProfileId, book.FundProfileId, StringComparison.OrdinalIgnoreCase)) &&
                (!fundStructureNodeId.HasValue || fundStructureNodeId.Value == book.FundStructureNodeId);
            return Task.FromResult<IReadOnlyList<LedgerAccountingPeriod>>(matches ? [period] : []);
        }

        public Task<LedgerAccountingPeriod> SavePeriodAsync(
            LedgerAccountingPeriod period,
            long expectedVersion,
            PeriodCloseEventRecord? closeEvent = null,
            CancellationToken ct = default)
            => throw new NotSupportedException("Static ledger journal store is read-only.");

        public Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<LedgerBookRecord?>(ledgerBookId == book.LedgerBookId ? book : null);
        }

        public Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            FundStructureNodeKindDto? fundStructureNodeKind = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var matches =
                (string.IsNullOrWhiteSpace(fundProfileId) || string.Equals(fundProfileId, book.FundProfileId, StringComparison.OrdinalIgnoreCase)) &&
                (!fundStructureNodeId.HasValue || fundStructureNodeId.Value == book.FundStructureNodeId) &&
                (!fundStructureNodeKind.HasValue || fundStructureNodeKind.Value == book.FundStructureNodeKind);
            return Task.FromResult<IReadOnlyList<LedgerBookRecord>>(matches ? [book] : []);
        }

        public Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord book, CancellationToken ct = default)
            => throw new NotSupportedException("Static ledger journal store is read-only.");
    }

    private sealed class FakeQuickBooksConnectionStore : IQuickBooksOnlineConnectionStore
    {
        private readonly QuickBooksOnlineConnection? _connection;
        private readonly IReadOnlyList<string> _missingFields;

        private FakeQuickBooksConnectionStore(QuickBooksOnlineConnection? connection, IReadOnlyList<string> missingFields)
        {
            _connection = connection;
            _missingFields = missingFields;
        }

        public string? SavedRefreshToken { get; private set; }

        public bool? LastVerificationSuccess { get; private set; }

        public static FakeQuickBooksConnectionStore Configured()
            => new(
                new QuickBooksOnlineConnection(
                    "qbo-client-id",
                    "qbo-client-secret",
                    "qbo-refresh-token",
                    "9130359087654321",
                    "sandbox",
                    "Meridian-Dev"),
                []);

        public static FakeQuickBooksConnectionStore Missing()
            => new(null, ["ClientId", "ClientSecret", "RefreshToken", "RealmId"]);

        public Task<QuickBooksOnlineConnection?> ReadAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_connection);
        }

        public Task<AccountingSystemConnectionMetadataDto> GetMetadataAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new AccountingSystemConnectionMetadataDto(
                "quickbooks",
                _connection?.Environment ?? "sandbox",
                _connection?.RealmId,
                _connection?.CompanyName,
                HasLocalConfig: _connection is not null,
                HasRefreshToken: _connection is not null,
                LastConnectedAtUtc: LastVerificationSuccess == true ? DateTimeOffset.UtcNow : null,
                StatusLabel: _connection is null ? "Local config required" : "Local config ready",
                StatusDetail: _connection is null
                    ? "QuickBooks Online local config is incomplete."
                    : "Read-only QuickBooks Online evidence is configured for Meridian-Dev.",
                MissingFields: _missingFields));
        }

        public Task SaveRefreshTokenAsync(string refreshToken, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            SavedRefreshToken = refreshToken;
            return Task.CompletedTask;
        }

        public Task RecordConnectionAsync(
            bool success,
            string? externalCompanyId,
            string? error,
            DateTimeOffset? occurredAtUtc = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            LastVerificationSuccess = success;
            return Task.CompletedTask;
        }
    }

    private sealed class QuickBooksStubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var uri = request.RequestUri?.ToString() ?? string.Empty;
            var payload = ResolvePayload(request, uri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }

        private static string ResolvePayload(HttpRequestMessage request, string uri)
        {
            if (request.Method == HttpMethod.Post && uri.Contains("oauth.platform.intuit.com", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Authorization.Should().NotBeNull();
                request.Headers.Authorization!.Scheme.Should().Be("Basic");
                return """
                    {
                      "access_token": "qbo-access-token",
                      "refresh_token": "rotated-refresh-token",
                      "expires_in": 3600,
                      "token_type": "bearer"
                    }
                    """;
            }

            if (uri.Contains("/query?", StringComparison.OrdinalIgnoreCase) &&
                Uri.UnescapeDataString(uri).Contains("from Account", StringComparison.OrdinalIgnoreCase))
            {
                return """
                    {
                      "QueryResponse": {
                        "Account": [
                          {
                            "Id": "35",
                            "Name": "Checking",
                            "FullyQualifiedName": "Assets:Checking",
                            "Classification": "Asset",
                            "AccountType": "Bank",
                            "CurrencyRef": { "value": "USD" },
                            "Active": true
                          },
                          {
                            "Id": "400",
                            "Name": "Investment Income",
                            "FullyQualifiedName": "Income:Investment",
                            "Classification": "Revenue",
                            "AccountType": "Income",
                            "CurrencyRef": { "value": "USD" },
                            "Active": true
                          }
                        ]
                      }
                    }
                    """;
            }

            if (uri.Contains("/query?", StringComparison.OrdinalIgnoreCase) &&
                Uri.UnescapeDataString(uri).Contains("from JournalEntry", StringComparison.OrdinalIgnoreCase))
            {
                return """
                    {
                      "QueryResponse": {
                        "JournalEntry": [
                          {
                            "Id": "228",
                            "TxnDate": "2026-01-15",
                            "PrivateNote": "Capital entry",
                            "CurrencyRef": { "value": "USD" },
                            "Line": [
                              {
                                "Id": "1",
                                "Description": "Debit checking",
                                "Amount": "4151.74",
                                "JournalEntryLineDetail": {
                                  "PostingType": "Debit",
                                  "AccountRef": { "value": "35", "name": "Checking" }
                                }
                              },
                              {
                                "Id": "2",
                                "Description": "Credit income",
                                "Amount": "4151.74",
                                "JournalEntryLineDetail": {
                                  "PostingType": "Credit",
                                  "AccountRef": { "value": "400", "name": "Investment Income" }
                                }
                              }
                            ]
                          }
                        ]
                      }
                    }
                    """;
            }

            if (uri.Contains("/reports/TrialBalance", StringComparison.OrdinalIgnoreCase))
            {
                return """
                    {
                      "Rows": {
                        "Row": [
                          {
                            "ColData": [
                              { "id": "35", "value": "Checking" },
                              { "value": "4151.74" },
                              { "value": "" }
                            ]
                          },
                          {
                            "ColData": [
                              { "id": "400", "value": "Investment Income" },
                              { "value": "" },
                              { "value": "4151.74" }
                            ]
                          },
                          {
                            "group": "GrandTotal",
                            "type": "Section",
                            "Summary": {
                              "ColData": [
                                { "value": "TOTAL" },
                                { "value": "4151.74" },
                                { "value": "4151.74" }
                              ]
                            }
                          }
                        ]
                      }
                    }
                    """;
            }

            throw new InvalidOperationException($"Unexpected QuickBooks request: {request.Method} {uri}");
        }
    }

    private static AccountingReportPackageBundleDto BuildReportPackage(
        Guid ledgerBookId,
        AccountingCertificationStateDto state,
        string scopeHash)
    {
        var dimensions = new LedgerDimensionSetDto(
            FundId: "default-fund",
            EntityId: "entity-alpha",
            SleeveId: "sleeve-alpha",
            StrategyId: "strategy-alpha",
            InvestorId: "investor-alpha",
            CapitalAccountId: "capital-alpha",
            InstrumentId: Guid.Parse("11111111-2222-3333-4444-555555555555"),
            TaxLotId: "tax-lot-alpha",
            CostCenterId: "cost-center-alpha",
            CounterpartyId: "counterparty-alpha",
            BookId: ledgerBookId.ToString("D"),
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "Investments"
            });
        var certification = new ReportCertificationDto(
            $"cert-report-package-{scopeHash}",
            state,
            "controller@meridian.local",
            DateTimeOffset.Parse("2026-06-30T22:00:00Z"),
            "Report package dimensional provenance retained.",
            [$"evidence://ledger-book/{ledgerBookId:D}/report-package/{scopeHash}"]);
        var packageId = $"report-package-default-fund-2026-06-{scopeHash}";

        return new AccountingReportPackageBundleDto(
            new FinancialStatementPackageDto(
                packageId,
                "default-fund",
                ledgerBookId,
                "2026-06",
                state,
                ["balance-sheet", "income-statement"],
                certification.EvidenceLinks,
                certification,
                LineProvenance:
                [
                    new ReportLineProvenanceDto(
                        "balance-sheet",
                        "cash",
                        "Cash",
                        "ledger",
                        100m,
                        "USD",
                        dimensions,
                        certification.EvidenceLinks)
                ],
                Dimensions: dimensions),
            [],
            new RealizedGainLossReportDto(
                $"realized-gain-loss-{scopeHash}",
                "default-fund",
                ledgerBookId,
                "2026-06",
                dimensions,
                0m,
                "USD",
                state,
                certification.EvidenceLinks),
            new NavPackageDto(
                $"nav-package-{scopeHash}",
                "default-fund",
                ledgerBookId,
                "2026-06",
                dimensions,
                100m,
                "USD",
                state,
                certification.EvidenceLinks,
                certification),
            certification,
            DimensionScope: new ReportDimensionScopeDto(
                ledgerBookId,
                dimensions,
                true,
                scopeHash,
                $"dimension-scope:{scopeHash}",
                ["bookId", "entityId", "externalGl.Department", "fundId"]));
    }

    private sealed class StubAccountingReportPackageService : IAccountingReportPackageService
    {
        private readonly IReadOnlyList<AccountingReportPackageBundleDto> _packages;

        public StubAccountingReportPackageService(IReadOnlyList<AccountingReportPackageBundleDto> packages)
        {
            _packages = packages;
        }

        public int ListCalls { get; private set; }

        public Task<AccountingReportPackageBundleDto> BuildPackageAsync(
            AccountingReportPackageRequestDto request,
            CancellationToken ct = default)
            => throw new NotSupportedException();

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
            ListCalls++;
            return Task.FromResult<IReadOnlyList<AccountingReportPackageBundleDto>>(_packages
                .Where(package => string.IsNullOrWhiteSpace(fundProfileId) ||
                                  string.Equals(package.FinancialStatements.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
                .Where(package => string.IsNullOrWhiteSpace(periodId) ||
                                  string.Equals(package.FinancialStatements.PeriodId, periodId, StringComparison.OrdinalIgnoreCase))
                .Where(package => !ledgerBookId.HasValue ||
                                  package.FinancialStatements.LedgerBookId == ledgerBookId)
                .ToArray());
        }

        public Task<AccountingReportPackageBundleDto?> CertifyPackageAsync(
            CertifyAccountingReportPackageRequestDto request,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ReportExportArtifactManifestDto?> GetExportArtifactManifestAsync(
            string packageId,
            string artifactId,
            string? tenantId = null,
            string? companyId = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeQuickBooksClient : IQuickBooksOnlineClient
    {
        public int ImportCalls { get; private set; }

        public Task<QuickBooksOnlineTokenExchangeResult> ExchangeAuthorizationCodeAsync(
            QuickBooksOnlineAuthorizationCodeRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new QuickBooksOnlineTokenExchangeResult(
                "qbo-access-token",
                "rotated-refresh-token",
                DateTimeOffset.UtcNow.AddHours(1),
                Warnings: []));
        }

        public Task<QuickBooksOnlineTokenExchangeResult> RefreshAccessTokenAsync(
            QuickBooksOnlineConnection connection,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new QuickBooksOnlineTokenExchangeResult(
                "qbo-access-token",
                "rotated-refresh-token",
                DateTimeOffset.UtcNow.AddHours(1),
                Warnings: []));
        }

        public Task<QuickBooksOnlineCompanyEvidence> ReadCompanyEvidenceAsync(
            QuickBooksOnlineConnection connection,
            string accessToken,
            AccountingSystemImportRequestDto request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ImportCalls++;
            var periodEnd = request.PeriodEnd ?? new DateOnly(2026, 1, 31);
            IReadOnlyList<AccountingSystemChartAccountDto> chart =
            [
                new("qbo-1000", "Assets:Cash:Operating", "Operating Cash", "Asset", "USD", true, EvidenceRef: "quickbooks:company:9130359087654321:account:qbo-1000"),
                new("qbo-4000", "Income:Investment", "Investment Income", "Income", "USD", true, EvidenceRef: "quickbooks:company:9130359087654321:account:qbo-4000")
            ];
            IReadOnlyList<AccountingSystemJournalEntryDto> journal =
            [
                new(
                    "qbo-je-100",
                    new DateOnly(2026, 1, 5),
                    "Capital contribution",
                    "USD",
                    250_000m,
                    250_000m,
                    [
                        new("qbo-je-100-1", "qbo-1000", "Assets:Cash:Operating", "Capital received", 250_000m, 0m, "USD", "quickbooks:company:9130359087654321:journal:qbo-je-100:line:1"),
                        new("qbo-je-100-2", "qbo-4000", "Income:Investment", "Capital offset", 0m, 250_000m, "USD", "quickbooks:company:9130359087654321:journal:qbo-je-100:line:2")
                    ],
                    "quickbooks:company:9130359087654321:journal:qbo-je-100")
            ];
            IReadOnlyList<AccountingSystemTrialBalanceLineDto> trialBalance =
            [
                new("qbo-1000", "Assets:Cash:Operating", "Operating Cash", "Asset", 250_000m, 0m, "USD", periodEnd, "quickbooks:company:9130359087654321:trial-balance:qbo-1000"),
                new("qbo-4000", "Income:Investment", "Investment Income", "Income", 0m, 250_000m, "USD", periodEnd, "quickbooks:company:9130359087654321:trial-balance:qbo-4000")
            ];

            return Task.FromResult(new QuickBooksOnlineCompanyEvidence(
                chart,
                journal,
                trialBalance,
                [
                    "quickbooks:company:9130359087654321:chart-of-accounts",
                    "quickbooks:company:9130359087654321:journal",
                    "quickbooks:company:9130359087654321:trial-balance"
                ],
                Warnings: []));
        }
    }
}
