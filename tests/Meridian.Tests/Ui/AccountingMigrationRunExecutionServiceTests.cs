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
}
