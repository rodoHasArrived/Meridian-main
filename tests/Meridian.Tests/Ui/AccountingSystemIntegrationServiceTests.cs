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

namespace Meridian.Tests.Ui;

public sealed class AccountingSystemIntegrationServiceTests
{
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
            issue.Code == "external-gl.live-posting-disabled" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Info);
        readiness.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.RulesStudio &&
            component.Status == AccountingProductionReadinessStatusDto.Blocked);
        readiness.Components.Should().Contain(component =>
            component.Area == AccountingProductionReadinessAreaDto.MigrationRollout &&
            component.Status == AccountingProductionReadinessStatusDto.Blocked &&
            component.Summary.Contains("migration control", StringComparison.OrdinalIgnoreCase));
        readiness.ExternalGlProviderCount.Should().BeGreaterThan(0);
        readiness.CertifiedExternalGlMappingProfileCount.Should().Be(0);
        readiness.ExternalGlLivePostingEnabled.Should().BeFalse();
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
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));
        var profiles = await service.ListMappingProfilesAsync("quickbooks-fixture", "default-fund");
        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: ["approval:export-package:qbo-default-fund-2026-01-01-2026-01-31"]));

        upserted.ProviderId.Should().Be("quickbooks-fixture");
        upserted.CertificationState.Should().Be(AccountingCertificationStateDto.Certified);
        profiles.Should().ContainSingle(row => row.ProfileId == profile.ProfileId);
        package.PostingEnabled.Should().BeFalse();
        package.PostingDisabledReason.Should().Contain("Guarded export package only");
        package.Certification.Should().NotBeNull();
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.ReadyForReview);
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
            EvidenceLinks: [$"approval:external-gl-mapping:{profileId}"]));
        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: providerId,
            FundProfileId: "default-fund",
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: [$"approval:export-package:{providerId}:default-fund:2026-01-01:2026-01-31"]));

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
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: ["approval:export-package:qbo-default-fund-2026-01-01-2026-01-31"]));

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
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));

        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
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
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));

        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
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
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));

        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
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
    public async Task CreateExportPackageAsync_RequiresGeneratedMeridianOwnedExportLines()
    {
        var service = CreateService();
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
            EvidenceLinks: ["approval:export-package:qbo-default-fund-2026-01-01-2026-01-31"]));

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
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));
        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: ["approval:export-package:qbo-default-fund-2026-01-01-2026-01-31"]));

        package.MappingProfileId.Should().Be(profile.ProfileId);
        package.ReconciliationId.Should().NotBeNullOrWhiteSpace();
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
            .WithMessage("*must reference the retained export package id, certification id, and exact export period in the same artifact*");

        var splitCertificationEvidence = () => service.CertifyExportPackageAsync(new CertifyAccountingSystemExportPackageRequestDto(
            package.ExportPackageId,
            "controller",
            "Split evidence should not certify the guarded external GL export package.",
            [
                "evidence:external-gl-export:support-packet:2026-01-01:2026-01-31",
                "approval:external-gl-export-certification:2026-02-01:2026-02-28"
            ]));

        await splitCertificationEvidence.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*must reference the retained export package id, certification id, and exact export period in the same artifact*");

        var missingCertificationIdEvidence = () => service.CertifyExportPackageAsync(new CertifyAccountingSystemExportPackageRequestDto(
            package.ExportPackageId,
            "controller",
            "Package and period evidence without certification id should not certify the guarded export package.",
            [$"approval:external-gl-export-certification:{package.ExportPackageId}:2026-01-01:2026-01-31"]));

        await missingCertificationIdEvidence.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*must reference the retained export package id, certification id, and exact export period in the same artifact*");

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
        manifest.RequireBalancedReconciliation.Should().BeFalse();
        manifest.EvidenceLinks.Should().Contain(certificationEvidence);
        manifest.Payload.Should().Contain(certified.ExportPackageId);
        manifest.Payload.Should().Contain(certificationEvidence);

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
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));
        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: ["approval:export-package:qbo-default-fund-2026-01-01-2026-01-31"]));
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.ReadyForReview);

        await service.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
            profile with { DisplayName = "Default fund QBO mapping requiring recertification" },
            "accounting-ops",
            FundProfileId: "default-fund"));

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
            EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));
        var package = await service.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
            "accounting-ops",
            ProviderId: "quickbooks-fixture",
            FundProfileId: "default-fund",
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            MappingProfileId: profile.ProfileId,
            RequireBalancedReconciliation: false,
            EvidenceLinks: ["approval:export-package:qbo-default-fund-2026-01-01-2026-01-31"]));
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
            EvidenceLinks: ["approval:export-package:qbo-default-fund-2026-01-01-2026-01-31"]));
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
                EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"])));
        var mappingProfilesResponse = await app.GetTestClient().GetAsync("/api/accounting-system/mapping-profiles?providerId=quickbooks-fixture&fundProfileId=default-fund");
        var exportPackageResponse = await app.GetTestClient().PostAsync(
            "/api/accounting-system/export-packages",
            JsonContent(new AccountingSystemExportPackageRequestDto(
                "endpoint-operator",
                ProviderId: "quickbooks-fixture",
                FundProfileId: "default-fund",
                PeriodStart: new DateOnly(2026, 1, 1),
                PeriodEnd: new DateOnly(2026, 1, 31),
                MappingProfileId: "qbo-default-fund-certified",
                RequireBalancedReconciliation: false,
                EvidenceLinks: ["approval:external-gl-export-package:endpoint-default-fund-2026-01-01-2026-01-31"])));

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
        exportPackage.Certification.Should().NotBeNull();
        exportPackage.Certification!.State.Should().Be(AccountingCertificationStateDto.ReadyForReview);
        exportPackage.GeneratedLines.Should().Contain(line =>
            line.MeridianAccountCode == "Assets:Cash:Operating" &&
            line.ExternalAccountId == "qbo-1000");

        var certificationResponse = await app.GetTestClient().PostAsync(
            UiApiRoutes.AccountingSystemExportPackageCertification,
            JsonContent(new CertifyAccountingSystemExportPackageRequestDto(
                exportPackage.ExportPackageId,
                "endpoint-controller",
                "Endpoint controller certified the guarded export package.",
                [ExportCertificationEvidence(exportPackage)])));

        certificationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var certifiedExportPackage = await ReadAsync<ExternalGlExportPackageDto>(certificationResponse);
        certifiedExportPackage.PostingEnabled.Should().BeFalse();
        certifiedExportPackage.Certification.Should().NotBeNull();
        certifiedExportPackage.Certification!.State.Should().Be(AccountingCertificationStateDto.Certified);
        certifiedExportPackage.Certification.Actor.Should().Be("endpoint-controller");
        certifiedExportPackage.Certification.EvidenceLinks.Should().Contain(ExportCertificationEvidence(exportPackage));

        var manifestResponse = await app.GetTestClient().GetAsync(
            UiApiRoutes.AccountingSystemExportPackageManifest.Replace("{exportPackageId}", certifiedExportPackage.ExportPackageId));

        manifestResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var manifest = await ReadAsync<ExternalGlExportPackageManifestDto>(manifestResponse);
        manifest.ExportPackageId.Should().Be(certifiedExportPackage.ExportPackageId);
        manifest.CertificationState.Should().Be(AccountingCertificationStateDto.Certified);
        manifest.ExternalPostingAllowed.Should().BeFalse();
        manifest.ContentHash.Should().HaveLength(64);
        manifest.MappingProfileId.Should().Be("qbo-default-fund-certified");
        manifest.ReconciliationId.Should().Be(exportPackage.ReconciliationId);
        manifest.RequireBalancedReconciliation.Should().BeFalse();
        manifest.GeneratedLines.Should().Contain(line =>
            line.MeridianAccountCode == "Assets:Cash:Operating" &&
            line.ExternalAccountId == "qbo-1000");
        manifest.EvidenceLinks.Should().Contain(ExportCertificationEvidence(exportPackage));
    }

    [Fact]
    public async Task AccountingSystemExportCertificationEndpoint_RequiresAdminMaintenance()
    {
        await using var app = await CreateAppAsync(UserPermission.ManageFundStructure);

        var mappingProfileResponse = await app.GetTestClient().PostAsync(
            "/api/accounting-system/mapping-profiles",
            JsonContent(new AccountingSystemMappingProfileUpsertRequestDto(
                CertifiedQuickBooksMappingProfile(),
                "endpoint-operator",
                FundProfileId: "default-fund",
                EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"])));
        var exportPackageResponse = await app.GetTestClient().PostAsync(
            "/api/accounting-system/export-packages",
            JsonContent(new AccountingSystemExportPackageRequestDto(
                "endpoint-operator",
                ProviderId: "quickbooks-fixture",
                FundProfileId: "default-fund",
                PeriodStart: new DateOnly(2026, 1, 1),
                PeriodEnd: new DateOnly(2026, 1, 31),
                MappingProfileId: "qbo-default-fund-certified",
                RequireBalancedReconciliation: false,
                EvidenceLinks: ["approval:external-gl-export-package:endpoint-default-fund-2026-01-01-2026-01-31"])));

        mappingProfileResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        exportPackageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var exportPackage = await ReadAsync<ExternalGlExportPackageDto>(exportPackageResponse);

        var certificationResponse = await app.GetTestClient().PostAsync(
            UiApiRoutes.AccountingSystemExportPackageCertification,
            JsonContent(new CertifyAccountingSystemExportPackageRequestDto(
                exportPackage.ExportPackageId,
                "endpoint-controller",
                "Fund-structure operator attempted to certify the guarded export package.",
                [ExportCertificationEvidence(exportPackage)])));

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
        readiness.Issues.Should().Contain(issue => issue.Code == "migration.historical-journal-backfill-not-certified");
        readiness.Issues.Should().Contain(issue => issue.Code == "external-gl.live-posting-disabled");
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

    private static ILedgerJournalStore CreateMatchedQuickBooksFixtureLedgerStore(Guid? ledgerBookIdOverride = null)
    {
        var ledgerBookId = ledgerBookIdOverride ?? Guid.NewGuid();
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
        var ledgerBookId = Guid.NewGuid();
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
        builder.Services.AddSingleton<ILedgerJournalStore>(_ => CreateMatchedQuickBooksFixtureLedgerStore());
        builder.Services.AddSingleton<ILedgerBookService>(sp => new PostgresLedgerBookService(sp.GetRequiredService<ILedgerJournalStore>()));
        builder.Services.AddSingleton<AccountingSystemIntegrationService>();
        builder.Services.AddSingleton<IAccountingConfigurationStore, InMemoryAccountingConfigurationStore>();
        builder.Services.AddSingleton<IAccountingActionAuditStore, InMemoryAccountingActionAuditStore>();
        builder.Services.AddSingleton<IAccountingConfigurationService, AccountingConfigurationService>();
        builder.Services.AddSingleton<AccountingProductionReadinessService>();
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy(UiEndpoints.MutationRateLimitPolicy, _ =>
                RateLimitPartition.GetNoLimiter<string>("test"));
        });

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
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
        return $"approval:external-gl-export-certification:{package.ExportPackageId}:{package.Certification!.CertificationId}:{package.PeriodStart:yyyy-MM-dd}:{package.PeriodEnd:yyyy-MM-dd}";
    }

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
