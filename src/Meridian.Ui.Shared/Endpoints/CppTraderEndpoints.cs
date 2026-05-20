using Meridian.Contracts.Api;
#if MERIDIAN_ENABLE_CPPTRADER
using Meridian.Infrastructure.CppTrader.Diagnostics;
using Meridian.Infrastructure.CppTrader.Execution;
#endif
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

public static class CppTraderEndpoints
{
    public static void MapCppTraderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("").WithTags("CppTrader");

#if MERIDIAN_ENABLE_CPPTRADER
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
#else
        group.MapGet(UiApiRoutes.CppTraderStatus, () => CppTraderUnavailable())
            .WithName("GetCppTraderStatus")
            .Produces(503);

        group.MapGet(UiApiRoutes.CppTraderSessions, () => CppTraderUnavailable())
            .WithName("GetCppTraderSessions")
            .Produces(503);

        group.MapGet(UiApiRoutes.CppTraderSymbols, () => CppTraderUnavailable())
            .WithName("GetCppTraderSymbols")
            .Produces(503);

        group.MapGet(UiApiRoutes.CppTraderExecutionSnapshot, (string _) => CppTraderUnavailable())
            .WithName("GetCppTraderExecutionSnapshot")
            .Produces(503);
#endif
    }

#if !MERIDIAN_ENABLE_CPPTRADER
    private static IResult CppTraderUnavailable()
        => Results.Json(
            new
            {
                available = false,
                error = "CppTrader integration is not included in this host build. Build with EnableCppTraderIntegration=true to enable it."
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);
#endif
}
