using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster.CashFlow;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Malformed_retained_schedule_returns_a_blocker_without_retaining_a_run(bool retainRun)
    {
        var id = Guid.NewGuid();
        var security = new SecurityDetailDto(id, "Bond", SecurityStatusDto.Active, "Synthetic schedule", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.Deserialize<JsonElement>("""
                {"issueDate":"2026-01-01","maturity":"2027-01-01","par":1000,
                 "couponRate":5,"paymentFrequency":"Quarterly","dayCount":"ACT/365",
                 "principalSchedule":[{"paymentDate":"2026-07-01","amount":0}]}
                """), [], [], 1, Request.KnowledgeCutoff, null);
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(security);
        query.GetRecordedByIdAsOfAsync(id, Request.KnowledgeCutoff, Arg.Any<CancellationToken>()).Returns(security);
        var runs = Substitute.For<IContractualCashFlowRunStore>();
        var service = new ContractualCashFlowPreviewService(query, Substitute.For<ISecurityMasterCashFlowService>());
        await using var app = await CreateApp(service, UserPermission.ModifySecurityMaster, query, runs);
        using var client = app.GetTestClient();

        var response = retainRun
            ? await client.PostAsJsonAsync($"/api/security-master/{id}/cashflow-projections/runs",
                new CreateContractualCashFlowRunRequest(Guid.NewGuid(), Request), Json)
            : await client.PostAsJsonAsync($"/api/security-master/{id}/cashflow-projections/preview", Request, Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ContractualCashFlowResult>(Json);
        result!.Blockers.Should().ContainSingle().Which.Code.Should().Be("ContractInputsUnavailable");
        result.Events.Should().BeEmpty();
        result.ResolvedContract.Should().BeNull();
        await runs.DidNotReceive().SaveAsync(Arg.Any<ContractualCashFlowRun>(), Arg.Any<CancellationToken>());
    }

    private static async Task<WebApplication> CreateApp(IContractualCashFlowPreviewService service, UserPermission permissions,
        ISecurityMasterQueryService? query = null, IContractualCashFlowRunStore? runs = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(query ?? Substitute.For<ISecurityMasterQueryService>());
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterService>());
        builder.Services.AddSingleton(service);
        if (runs is not null)
            builder.Services.AddSingleton(runs);
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
