using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_StatementRunRoutes_ShouldReturnListDetailExceptionsAndNotFound()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IReconciliationApiService>(new StubReconciliationApiService());
        });
        var client = app.GetTestClient();

        var list = await client.GetFromJsonAsync<List<StatementRunSummaryDto>>(
            UiApiRoutes.ReconciliationStatementRuns,
            ServerJsonOptions);
        var detail = await client.GetFromJsonAsync<StatementRunSummaryDto>(
            UiApiRoutes.WithParam(UiApiRoutes.ReconciliationStatementRunById, "runId", "statement-run-1"),
            ServerJsonOptions);
        var exceptions = await client.GetFromJsonAsync<List<StatementRunExceptionDto>>(
            UiApiRoutes.ReconciliationStatementExceptions,
            ServerJsonOptions);
        var missing = await client.GetAsync(UiApiRoutes.WithParam(
            UiApiRoutes.ReconciliationStatementRunById,
            "runId",
            "missing-run"));

        list.Should().ContainSingle(run => run.RunId == "statement-run-1" && run.OpenExceptionCount == 1);
        detail.Should().NotBeNull();
        detail!.RunId.Should().Be("statement-run-1");
        exceptions.Should().ContainSingle(item => item.RunId == "statement-run-1" && item.ToleranceBreached);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StatementRunRoutes_ShouldSurfaceRegistrationBlocker()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync(UiApiRoutes.ReconciliationStatementRuns);

        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StatementRunMutationRoutes_ShouldTrustAuthenticatedActor()
    {
        var service = new StubReconciliationApiService();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IReconciliationApiService>(service);
        });
        var client = app.GetTestClient();

        var create = await client.PostAsJsonAsync(
            UiApiRoutes.ReconciliationStatementRuns,
            new StatementRunCreateDto(
                Broker: "custodian",
                SourceInstitution: "Sample Custodian",
                FundAccountId: "fund-1",
                ExternalAccountId: "external-1",
                StatementPeriodStart: new DateOnly(2026, 5, 1),
                StatementPeriodEnd: new DateOnly(2026, 5, 31),
                SourcePath: @"C:\imports\statement.csv",
                OriginalFileName: "statement.csv",
                MappingProfileId: "mapping-v1",
                ToleranceProfileId: "tolerance-v1",
                ImportedBy: "browser-spoof"),
            ServerJsonOptions);
        var reconcile = await client.PostAsJsonAsync(
            UiApiRoutes.WithParam(UiApiRoutes.ReconciliationStatementRunReconcile, "runId", "statement-run-1"),
            new StatementRunReconcileRequestDto(Actor: "browser-spoof", Reason: "reconcile"),
            ServerJsonOptions);

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        reconcile.StatusCode.Should().Be(HttpStatusCode.OK);
        service.CreatedRequests.Should().ContainSingle();
        service.CreatedRequests[0].ImportedBy.Should().Be("ops-user");
        service.ReconciledRequests.Should().ContainSingle();
        service.ReconciledRequests[0].RunId.Should().Be("statement-run-1");
        service.ReconciledRequests[0].Request.Actor.Should().Be("ops-user");
    }
}
