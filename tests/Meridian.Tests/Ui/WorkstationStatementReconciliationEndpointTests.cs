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
}
