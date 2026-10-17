using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task LedgerAuditIntegrityEndpoint_VerifiesTheComposedAccountingChain()
    {
        var root = Directory.CreateTempSubdirectory("meridian-audit-integrity-endpoint-").FullName;
        try
        {
            var store = new FileAccountingConfigurationStore(Path.Combine(root, "accounting-configuration.json"));
            await store.AppendAsync(new AccountingActionAuditEventDto(
                Guid.NewGuid(), DateTimeOffset.UtcNow, "operator@example.test", "post-journal",
                "fund-alpha", null, null, new string('0', 64), new string('1', 64), [], [],
                CompanyId: "company-alpha", ReportGroupPrincipalIds: null, TenantId: "tenant-alpha"));

            await using var app = await CreateAppAsync(
                services => services.AddSingleton<IAccountingActionAuditStore>(store),
                mapLedgerApi: true,
                currentUserPermissions: UserPermission.AdminMaintenance);

            using var response = await app.GetTestClient().GetAsync(UiApiRoutes.LedgerAuditIntegrity);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var report = await response.Content.ReadFromJsonAsync<AccountingAuditIntegrityReportDto>(ServerJsonOptions);
            report!.AllVerified.Should().BeTrue();
            report.Chains.Single(chain => chain.Chain == AuditChainIdentifiers.AccountingActions)
                .Should().Match<AuditChainIntegrityResultDto>(chain =>
                    chain.Status == AuditChainIntegrityStatus.Verified && chain.LinksChecked == 1);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // A leaked temp directory must never fail a test run.
            }
        }
    }

    [Fact]
    public async Task LedgerAuditIntegrityEndpoint_RequiresAdminMaintenance()
    {
        await using var app = await CreateAppAsync(mapLedgerApi: true, currentUserPermissions: UserPermission.ViewLedgerReports);

        using var response = await app.GetTestClient().GetAsync(UiApiRoutes.LedgerAuditIntegrity);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
