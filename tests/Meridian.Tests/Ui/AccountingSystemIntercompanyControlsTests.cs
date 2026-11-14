using Meridian.Contracts.Ledger;
using Meridian.DataIntegration.AccountingSystem.Fixtures;
using Meridian.FinancialOperations.AccountingSystem;

namespace Meridian.Tests.Ui;

public sealed class AccountingSystemIntercompanyControlsTests
{
    [Fact]
    public async Task NetSuiteExport_ExternalEvidenceCannotReplaceUnavailableStructuredMappingAuthority()
    {
        var service = new AccountingSystemIntegrationService([new NetSuiteFixtureAccountingProvider()]);
        var package = await service.CreateExportPackageAsync(new("reviewer", ProviderId: "netsuite-fixture",
            FundProfileId: "synthetic-fund", LedgerBookId: Guid.NewGuid(), TenantId: "tenant-a", CompanyId: "company-a",
            EvidenceLinks: ["netsuite:intercompany-controls:reviewed", "intercompany-schedule:caller-invented"]));

        Assert.Equal(AccountingCertificationStateDto.Draft, package.Certification!.State);
        Assert.Contains(package.ValidationIssues, issue => issue.Code == "ExternalGlProviderIntercompanyScheduleUnavailable"
            && issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        Assert.DoesNotContain("intercompany-schedule:caller-invented", package.EvidenceLinks);
        Assert.False(package.PostingEnabled);
    }
}
