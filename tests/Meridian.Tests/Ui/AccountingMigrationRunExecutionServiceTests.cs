using FluentAssertions;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
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
}
