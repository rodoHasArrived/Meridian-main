using FluentAssertions;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class AccountingMigrationRunExecutionServiceTests
{
    private static readonly Guid LedgerBookId = Guid.Parse("77777777-2222-3333-4444-555555555555");

    [Fact]
    public async Task ExecuteAsync_RequiresScopedOperatorEvidenceBeforeCertification()
    {
        var service = new AccountingMigrationRunExecutionService(new InMemoryAccountingMigrationRunArtifactStore());

        var generic = await service.ExecuteAsync(new AccountingMigrationRunExecutionRequestDto(
            AccountingMigrationRunKindDto.LedgerBookScope,
            "controller.admin",
            FundProfileId: "default-fund",
            LedgerBookId: LedgerBookId,
            RunId: "migration-run-ledger-book-scope-generic-evidence",
            CertifyOnSuccess: true,
            EvidenceLinks:
            [
                "approval://migration/tenant/company-alpha/company/company-alpha/fund/default-fund/ledger-book-scope"
            ],
            TenantId: "company-alpha",
            CompanyId: "company-alpha",
            ActionOrigin: OperationsActionOriginDto.HumanOperator));

        generic.Status.Should().Be(AccountingMigrationRunStatusDto.Failed);
        generic.IsCertified.Should().BeFalse();
        generic.Artifact.Status.Should().Be(AccountingMigrationRunStatusDto.Failed);
        generic.Issues.Should().ContainSingle(issue =>
            issue.Code == "migration-run.certification-evidence-scope-missing" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        generic.Artifact.EvidenceReferences.Should().Contain(reference =>
            reference.Contains($"/ledger-book/{LedgerBookId:D}/", StringComparison.OrdinalIgnoreCase));

        var scoped = await service.ExecuteAsync(new AccountingMigrationRunExecutionRequestDto(
            AccountingMigrationRunKindDto.LedgerBookScope,
            "controller.admin",
            FundProfileId: "default-fund",
            LedgerBookId: LedgerBookId,
            RunId: "migration-run-ledger-book-scope-scoped-evidence",
            CertifyOnSuccess: true,
            EvidenceLinks:
            [
                $"approval://migration/tenant/company-alpha/company/company-alpha/fund/default-fund/ledger-book/{LedgerBookId:D}/ledger-book-scope"
            ],
            TenantId: "company-alpha",
            CompanyId: "company-alpha",
            ActionOrigin: OperationsActionOriginDto.HumanOperator));

        scoped.Status.Should().Be(AccountingMigrationRunStatusDto.Certified);
        scoped.IsCertified.Should().BeTrue();
        scoped.Issues.Should().BeEmpty();
        scoped.Artifact.EvidenceReferences.Should().Contain(reference =>
            reference.Contains($"/ledger-book/{LedgerBookId:D}/", StringComparison.OrdinalIgnoreCase) &&
            reference.Contains("/ledger-book-scope", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_ExtractsRowCountReconciliationFromRetainedEvidence()
    {
        var service = new AccountingMigrationRunExecutionService(new InMemoryAccountingMigrationRunArtifactStore());

        var result = await service.ExecuteAsync(new AccountingMigrationRunExecutionRequestDto(
            AccountingMigrationRunKindDto.HistoricalJournalBackfill,
            "controller.admin",
            FundProfileId: "default-fund",
            LedgerBookId: LedgerBookId,
            RunId: "migration-run-historical-backfill-evidence-counts",
            CertifyOnSuccess: true,
            EvidenceLinks:
            [
                $"approval://migration/tenant/company-alpha/company/company-alpha/fund/default-fund/ledger-book/{LedgerBookId:D}/historical-journal-backfill",
                "source-store-count=275 migrated-row-count=275"
            ],
            TenantId: "company-alpha",
            CompanyId: "company-alpha",
            ActionOrigin: OperationsActionOriginDto.HumanOperator));

        result.Status.Should().Be(AccountingMigrationRunStatusDto.Certified);
        result.IsCertified.Should().BeTrue();
        result.Issues.Should().BeEmpty();
        result.Artifact.SourceRecordCount.Should().Be(275);
        result.Artifact.MigratedRecordCount.Should().Be(275);
        result.Artifact.RowCountReconciled.Should().BeTrue();
        result.Artifact.EvidenceReferences.Should().Contain("row-count-reconciliation:source=275:migrated=275:reconciled=true");
    }

    [Fact]
    public async Task ExecuteAsync_FailsClosedWhenSubmittedCountsConflictWithRetainedEvidence()
    {
        var service = new AccountingMigrationRunExecutionService(new InMemoryAccountingMigrationRunArtifactStore());

        var result = await service.ExecuteAsync(new AccountingMigrationRunExecutionRequestDto(
            AccountingMigrationRunKindDto.DimensionalBackfill,
            "controller.admin",
            FundProfileId: "default-fund",
            LedgerBookId: LedgerBookId,
            RunId: "migration-run-dimensional-backfill-conflicting-evidence-counts",
            CertifyOnSuccess: true,
            Dimensions: new LedgerDimensionSetDto(FundId: "default-fund", BookId: LedgerBookId.ToString("D")),
            EvidenceLinks:
            [
                $"approval://migration/tenant/company-alpha/company/company-alpha/fund/default-fund/ledger-book/{LedgerBookId:D}/dimensional-backfill dimension-scope ledger-dimension-set",
                "source-store-count=275 migrated-row-count=274"
            ],
            TenantId: "company-alpha",
            CompanyId: "company-alpha",
            SourceRecordCount: 275,
            MigratedRecordCount: 275,
            ActionOrigin: OperationsActionOriginDto.HumanOperator));

        result.Status.Should().Be(AccountingMigrationRunStatusDto.Failed);
        result.IsCertified.Should().BeFalse();
        result.Artifact.SourceRecordCount.Should().Be(275);
        result.Artifact.MigratedRecordCount.Should().Be(0);
        result.Artifact.RowCountReconciled.Should().BeFalse();
        result.Artifact.EvidenceReferences.Should().Contain("row-count-reconciliation:source=275:migrated=275:reconciled=false");
        result.Artifact.EvidenceReferences.Should().NotContain("row-count-reconciliation:source=275:migrated=275:reconciled=true");
        result.Issues.Should().ContainSingle(issue =>
            issue.Code == "migration-run.migrated-row-count-evidence-mismatch" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
    }

    [Fact]
    public async Task ExecuteAsync_CertifiesHistoricalBackfillFromRetainedWorkerPlan()
    {
        var artifactStore = new InMemoryAccountingMigrationRunArtifactStore();
        var workerPlans = new InMemoryAccountingMigrationRunWorkerPlanStore();
        await workerPlans.UpsertAsync(new AccountingMigrationRunWorkerPlanDto(
            "worker-plan-historical-journal-default-fund",
            AccountingMigrationRunKindDto.HistoricalJournalBackfill,
            "default-fund",
            LedgerBookId,
            SourceRecordCount: 384,
            MigratedRecordCount: 384,
            EvidenceReferences:
            [
                $"approval://migration/tenant/company-alpha/company/company-alpha/fund/default-fund/ledger-book/{LedgerBookId:D}/historical-journal-backfill"
            ],
            TenantId: "company-alpha",
            CompanyId: "company-alpha",
            Summary: "Historical journal worker matched source and migrated journals."));
        var service = new AccountingMigrationRunExecutionService(artifactStore, workerPlans);

        var result = await service.ExecuteAsync(new AccountingMigrationRunExecutionRequestDto(
            AccountingMigrationRunKindDto.HistoricalJournalBackfill,
            "controller.admin",
            FundProfileId: "default-fund",
            LedgerBookId: LedgerBookId,
            RunId: "migration-run-historical-backfill-worker-plan",
            CertifyOnSuccess: true,
            TenantId: "company-alpha",
            CompanyId: "company-alpha",
            ActionOrigin: OperationsActionOriginDto.HumanOperator,
            WorkerPlanId: "worker-plan-historical-journal-default-fund"));

        result.Status.Should().Be(AccountingMigrationRunStatusDto.Certified);
        result.IsCertified.Should().BeTrue();
        result.Issues.Should().BeEmpty();
        result.Artifact.SourceRecordCount.Should().Be(384);
        result.Artifact.MigratedRecordCount.Should().Be(384);
        result.Artifact.RowCountReconciled.Should().BeTrue();
        result.Artifact.EvidenceReferences.Should().Contain("worker-plan:worker-plan-historical-journal-default-fund");
        result.Artifact.EvidenceReferences.Should().Contain("row-count-reconciliation:source=384:migrated=384:reconciled=true");
        result.Artifact.Summary.Should().Contain("worker-plan-historical-journal-default-fund");
    }

    [Fact]
    public async Task ExecuteAsync_CertifiesHistoricalBackfillFromPersistedWorkerPlan()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Meridian.Tests",
            "MigrationWorkerPlans",
            Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(root, "worker-plans.json");
        try
        {
            var writer = new FileAccountingMigrationRunWorkerPlanStore(
                snapshotPath,
                NullLogger<FileAccountingMigrationRunWorkerPlanStore>.Instance);
            await writer.UpsertAsync(new AccountingMigrationRunWorkerPlanDto(
                "worker-plan-historical-journal-file-backed",
                AccountingMigrationRunKindDto.HistoricalJournalBackfill,
                "default-fund",
                LedgerBookId,
                SourceRecordCount: 144,
                MigratedRecordCount: 144,
                EvidenceReferences:
                [
                    $"approval://migration/tenant/company-alpha/company/company-alpha/fund/default-fund/ledger-book/{LedgerBookId:D}/historical-journal-backfill"
                ],
                TenantId: "company-alpha",
                CompanyId: "company-alpha"));

            var reader = new FileAccountingMigrationRunWorkerPlanStore(
                snapshotPath,
                NullLogger<FileAccountingMigrationRunWorkerPlanStore>.Instance);
            var service = new AccountingMigrationRunExecutionService(
                new InMemoryAccountingMigrationRunArtifactStore(),
                reader);

            var result = await service.ExecuteAsync(new AccountingMigrationRunExecutionRequestDto(
                AccountingMigrationRunKindDto.HistoricalJournalBackfill,
                "controller.admin",
                FundProfileId: "default-fund",
                LedgerBookId: LedgerBookId,
                RunId: "migration-run-historical-backfill-file-worker-plan",
                CertifyOnSuccess: true,
                TenantId: "company-alpha",
                CompanyId: "company-alpha",
                ActionOrigin: OperationsActionOriginDto.HumanOperator,
                WorkerPlanId: "worker-plan-historical-journal-file-backed"));

            result.Status.Should().Be(AccountingMigrationRunStatusDto.Certified);
            result.Artifact.SourceRecordCount.Should().Be(144);
            result.Artifact.MigratedRecordCount.Should().Be(144);
            result.Artifact.EvidenceReferences.Should().Contain("worker-plan:worker-plan-historical-journal-file-backed");
            result.Artifact.EvidenceReferences.Should().Contain(reference =>
                reference.Contains($"/ledger-book/{LedgerBookId:D}/", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_CertifiesWorkerPlanWhenRequestOmitsScopeOwnedByPlan()
    {
        var artifactStore = new InMemoryAccountingMigrationRunArtifactStore();
        var workerPlans = new InMemoryAccountingMigrationRunWorkerPlanStore();
        await workerPlans.UpsertAsync(new AccountingMigrationRunWorkerPlanDto(
            "worker-plan-historical-scope-authoritative",
            AccountingMigrationRunKindDto.HistoricalJournalBackfill,
            "fund-from-plan",
            LedgerBookId,
            SourceRecordCount: 64,
            MigratedRecordCount: 64,
            EvidenceReferences:
            [
                $"approval://migration/tenant/tenant-plan/company/company-plan/fund/fund-from-plan/ledger-book/{LedgerBookId:D}/historical-journal-backfill"
            ],
            TenantId: "tenant-plan",
            CompanyId: "company-plan",
            Summary: "Historical journal worker retained authoritative scope."));
        var service = new AccountingMigrationRunExecutionService(artifactStore, workerPlans);

        var result = await service.ExecuteAsync(new AccountingMigrationRunExecutionRequestDto(
            AccountingMigrationRunKindDto.HistoricalJournalBackfill,
            "controller.admin",
            RunId: "migration-run-historical-backfill-worker-plan-scope",
            CertifyOnSuccess: true,
            ActionOrigin: OperationsActionOriginDto.HumanOperator,
            WorkerPlanId: "worker-plan-historical-scope-authoritative"));

        result.Status.Should().Be(AccountingMigrationRunStatusDto.Certified);
        result.Issues.Should().BeEmpty();
        result.Artifact.FundProfileId.Should().Be("fund-from-plan");
        result.Artifact.LedgerBookId.Should().Be(LedgerBookId);
        result.Artifact.TenantId.Should().Be("tenant-plan");
        result.Artifact.CompanyId.Should().Be("company-plan");
        result.Artifact.SourceRecordCount.Should().Be(64);
        result.Artifact.MigratedRecordCount.Should().Be(64);
        result.Artifact.RowCountReconciled.Should().BeTrue();
        result.Artifact.EvidenceReferences.Should().Contain("worker-plan:worker-plan-historical-scope-authoritative");
    }

    [Fact]
    public async Task ExecuteAsync_PreservesWorkerPlanDimensionsWhenRequestSuppliesDifferentDimensions()
    {
        var artifactStore = new InMemoryAccountingMigrationRunArtifactStore();
        var workerPlans = new InMemoryAccountingMigrationRunWorkerPlanStore();
        await workerPlans.UpsertAsync(new AccountingMigrationRunWorkerPlanDto(
            "worker-plan-dimensional-default-fund",
            AccountingMigrationRunKindDto.DimensionalBackfill,
            "default-fund",
            LedgerBookId,
            SourceRecordCount: 812,
            MigratedRecordCount: 812,
            Dimensions: new LedgerDimensionSetDto(
                FundId: "default-fund",
                BookId: LedgerBookId.ToString("D"),
                OrganizationId: "company-alpha",
                EntityId: "fund-entity-main",
                SleeveId: "sleeve-core",
                StrategyId: "strategy-income",
                InvestorId: "investor-lp-001",
                CapitalAccountId: "capital-account-lp-001",
                InstrumentId: Guid.Parse("99999999-1111-2222-3333-444444444444"),
                TaxLotId: "tax-lot-2026-001",
                CostCenterId: "fund-accounting",
                CounterpartyId: "counterparty-bank-alpha",
                ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["external-company"] = "company-alpha"
                },
                PortfolioId: "portfolio-income",
                AccountId: "account-cash",
                CustomerId: "customer-reporting",
                VendorId: "vendor-administrator",
                ProjectId: "project-migration-2026"),
            EvidenceReferences:
            [
                $"approval://migration/tenant/company-alpha/company/company-alpha/fund/default-fund/ledger-book/{LedgerBookId:D}/dimensional-backfill dimension-scope ledger-dimension-set"
            ],
            TenantId: "company-alpha",
            CompanyId: "company-alpha"));
        var service = new AccountingMigrationRunExecutionService(artifactStore, workerPlans);

        var result = await service.ExecuteAsync(new AccountingMigrationRunExecutionRequestDto(
            AccountingMigrationRunKindDto.DimensionalBackfill,
            "controller.admin",
            FundProfileId: "default-fund",
            LedgerBookId: LedgerBookId,
            RunId: "migration-run-dimensional-worker-plan",
            CertifyOnSuccess: true,
            Dimensions: new LedgerDimensionSetDto(
                FundId: "default-fund",
                BookId: LedgerBookId.ToString("D"),
                EntityId: "untrusted-entity",
                InvestorId: "untrusted-investor"),
            TenantId: "company-alpha",
            CompanyId: "company-alpha",
            ActionOrigin: OperationsActionOriginDto.HumanOperator,
            WorkerPlanId: "worker-plan-dimensional-default-fund"));

        result.Status.Should().Be(AccountingMigrationRunStatusDto.Certified);
        result.Artifact.Dimensions.Should().NotBeNull();
        result.Artifact.Dimensions!.FundId.Should().Be("default-fund");
        result.Artifact.Dimensions.BookId.Should().Be(LedgerBookId.ToString("D"));
        result.Artifact.Dimensions.EntityId.Should().Be("fund-entity-main");
        result.Artifact.Dimensions.InvestorId.Should().Be("investor-lp-001");
        result.Artifact.Dimensions.ExternalGlDimensions.Should().ContainKey("external-company");
        result.Artifact.SourceRecordCount.Should().Be(812);
        result.Artifact.MigratedRecordCount.Should().Be(812);
        result.Artifact.RowCountReconciled.Should().BeTrue();
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_FailsClosedWhenWorkerPlanCountsConflictWithRequest()
    {
        var artifactStore = new InMemoryAccountingMigrationRunArtifactStore();
        var workerPlans = new InMemoryAccountingMigrationRunWorkerPlanStore();
        await workerPlans.UpsertAsync(new AccountingMigrationRunWorkerPlanDto(
            "worker-plan-dimensional-count-conflict",
            AccountingMigrationRunKindDto.DimensionalBackfill,
            "default-fund",
            LedgerBookId,
            SourceRecordCount: 50,
            MigratedRecordCount: 50,
            Dimensions: new LedgerDimensionSetDto(FundId: "default-fund", BookId: LedgerBookId.ToString("D")),
            EvidenceReferences:
            [
                $"approval://migration/tenant/company-alpha/company/company-alpha/fund/default-fund/ledger-book/{LedgerBookId:D}/dimensional-backfill"
            ],
            TenantId: "company-alpha",
            CompanyId: "company-alpha"));
        var service = new AccountingMigrationRunExecutionService(artifactStore, workerPlans);

        var result = await service.ExecuteAsync(new AccountingMigrationRunExecutionRequestDto(
            AccountingMigrationRunKindDto.DimensionalBackfill,
            "controller.admin",
            FundProfileId: "default-fund",
            LedgerBookId: LedgerBookId,
            RunId: "migration-run-dimensional-worker-plan-conflict",
            CertifyOnSuccess: true,
            SourceRecordCount: 49,
            MigratedRecordCount: 50,
            TenantId: "company-alpha",
            CompanyId: "company-alpha",
            ActionOrigin: OperationsActionOriginDto.HumanOperator,
            WorkerPlanId: "worker-plan-dimensional-count-conflict"));

        result.Status.Should().Be(AccountingMigrationRunStatusDto.Failed);
        result.IsCertified.Should().BeFalse();
        result.Artifact.MigratedRecordCount.Should().Be(0);
        result.Artifact.RowCountReconciled.Should().BeFalse();
        result.Issues.Should().Contain(issue =>
            issue.Code == "migration-run.worker-plan-source-count-mismatch" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        result.Issues.Should().Contain(issue =>
            issue.Code == "migration-run.row-count-mismatch" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
    }
}
