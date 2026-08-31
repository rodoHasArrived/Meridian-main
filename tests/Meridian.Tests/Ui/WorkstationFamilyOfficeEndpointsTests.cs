using System.Net;
using System.Net.Http.Json;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Contracts;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_FamilyOfficeEndpoints_ShouldReturnEmptyStateGuidance()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(new FamilyOfficeReadService(
                fundStructureService: null,
                fundAccountService: null));
        }, currentUserPermissions: UserPermission.ViewDirectLending);

        var client = app.GetTestClient();
        var overview = await client.GetFromJsonAsync<FamilyOfficeOverviewDto>(
            "/api/workstation/family-office/overview",
            ServerJsonOptions,
            CancellationToken.None);
        var entitiesResponse = await client.GetFromJsonAsync<FamilyOfficeEntitiesResponseDto>(
            "/api/workstation/family-office/entities",
            ServerJsonOptions,
            CancellationToken.None);

        Assert.NotNull(overview);
        Assert.Equal("NotConfigured", overview!.Readiness.Status);
        Assert.Contains("Configure a FamilyOffice client", overview.Readiness.Blockers[0]);
        Assert.NotNull(entitiesResponse);
        Assert.True(entitiesResponse!.State.IsEmpty);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/workstation/family-office/balance-sheet", CancellationToken.None)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/workstation/family-office/ownership-graph", CancellationToken.None)).StatusCode);
    }
}
