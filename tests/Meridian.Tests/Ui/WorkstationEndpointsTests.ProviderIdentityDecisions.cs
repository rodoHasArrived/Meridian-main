using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.TestHost;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Theory]
    [InlineData(UserPermission.ViewConfig, HttpStatusCode.Forbidden)]
    [InlineData(UserPermission.ManageProviders, HttpStatusCode.OK)]
    public async Task MapWorkstationEndpoints_IdentityDecision_RequiresConfigurePermissionAndRetainsAuthenticatedActor(
        UserPermission permission, HttpStatusCode expectedStatus)
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = await CreatePromotionReadyProviderIntegrationStoreAsync(testRoot);
            var tenantStore = DefaultProviderIntegrationTenantStore(store);
            var row = (await new ProviderIntegrationIdentityResolutionPreviewService(tenantStore)
                .PreviewAsync("connection-alpha")).Rows.Single();
            await using var app = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: permission, currentUserName: "authenticated-reviewer");
            var response = await app.GetTestClient().PostAsJsonAsync(ProviderIntegrationIdentityResolutionRoute("connection-alpha"),
                new ProviderIntegrationIdentityResolutionRequestDto(row.SyncRunId, row.StagingRecordId,
                    Guid.Parse(row.InternalSecurityId!), row.EvidenceHash!, "Reviewed original source evidence."), ServerJsonOptions);

            response.StatusCode.Should().Be(expectedStatus);
            var decisions = await ((IProviderIntegrationIdentityDecisionStore)tenantStore).ListIdentityDecisionsAsync(row.SyncRunId);
            if (expectedStatus == HttpStatusCode.OK)
            {
                decisions.Should().ContainSingle().Which.ReviewedBy.Should().Be("authenticated-reviewer");
                decisions.Single().EvidenceHash.Should().Be(row.EvidenceHash);
            }
            else
                decisions.Should().BeEmpty();
            (await store.ListStagingRecordsAsync(row.SyncRunId)).Should().BeEmpty();
        }
        finally { DeleteProviderIntegrationTestRoot(testRoot); }
    }
}
