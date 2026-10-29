using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.CashFlows;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class ContractualCashFlowEndpointTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly ContractualCashFlowRequest Request = new(new(2026, 1, 1), new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Preview_is_a_permissioned_read_and_invalid_scenarios_are_400()
    {
        var service = Substitute.For<IContractualCashFlowPreviewService>();
        service.PreviewSecurityAsync(Arg.Any<Guid>(), Arg.Any<ContractualCashFlowRequest>(), Arg.Any<CancellationToken>())
            .Returns<ContractualCashFlowResult?>(_ => throw new ArgumentException("Overlapping prepayment assumptions."));
        await using var app = await CreateApp(service, UserPermission.ViewSecurityMaster);
        using var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync($"/api/security-master/{Guid.NewGuid()}/cashflow-projections/preview", Request, Json);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Overlapping prepayment assumptions");
    }

    [Fact]
    public async Task Missing_view_permission_cannot_calculate_and_viewer_cannot_retain_runs()
    {
        var service = Substitute.For<IContractualCashFlowPreviewService>();
        await using (var app = await CreateApp(service, (UserPermission)0))
        {
            var denied = await app.GetTestClient().PostAsJsonAsync($"/api/security-master/{Guid.NewGuid()}/cashflow-projections/preview", Request, Json);
            denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        await using (var app = await CreateApp(service, UserPermission.ViewSecurityMaster))
        {
            var denied = await app.GetTestClient().PostAsJsonAsync($"/api/security-master/{Guid.NewGuid()}/cashflow-projections/runs", new CreateContractualCashFlowRunRequest(Guid.NewGuid(), Request), Json);
            denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        await service.DidNotReceive().PreviewSecurityAsync(Arg.Any<Guid>(), Arg.Any<ContractualCashFlowRequest>(), Arg.Any<CancellationToken>());
    }

    private static async Task<WebApplication> CreateApp(IContractualCashFlowPreviewService service, UserPermission permissions)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterQueryService>());
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterService>());
        builder.Services.AddSingleton(service);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "cashflow.operator";
            await next();
        });
        app.MapSecurityMasterEndpoints(Json);
        await app.StartAsync();
        return app;
    }
}
