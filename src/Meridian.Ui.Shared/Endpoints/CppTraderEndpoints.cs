using Meridian.Contracts.Api;
using Meridian.Infrastructure.CppTrader.Diagnostics;
using Meridian.Infrastructure.CppTrader.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

public static class CppTraderEndpoints
{
    public static void MapCppTraderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("").WithTags("CppTrader");

        group.MapGet(UiApiRoutes.CppTraderStatus, ([FromServices] ICppTraderStatusService statusService) =>
            Results.Json(statusService.GetStatus()))
            .WithName("GetCppTraderStatus")
            .Produces(200);

        group.MapGet(UiApiRoutes.CppTraderSessions, ([FromServices] ICppTraderSessionDiagnosticsService diagnosticsService) =>
            Results.Json(diagnosticsService.GetSessions()))
            .WithName("GetCppTraderSessions")
            .Produces(200);

        group.MapGet(UiApiRoutes.CppTraderSymbols, ([FromServices] CppTraderLiveFeedAdapter feed) =>
            Results.Json(feed.SubscribedSymbols.OrderBy(symbol => symbol).ToArray()))
            .WithName("GetCppTraderSymbols")
            .Produces(200);

        group.MapGet(UiApiRoutes.CppTraderExecutionSnapshot, (string symbol, [FromServices] CppTraderLiveFeedAdapter feed) =>
        {
            var snapshot = feed.GetLastOrderBook(symbol);
            return snapshot is null ? Results.NotFound() : Results.Json(snapshot);
        })
        .WithName("GetCppTraderExecutionSnapshot")
        .Produces(200)
        .Produces(404);
    }
}
