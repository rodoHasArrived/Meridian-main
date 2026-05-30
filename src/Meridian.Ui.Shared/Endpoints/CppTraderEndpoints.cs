using Meridian.Contracts.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

public static class CppTraderEndpoints
{
    public static void MapCppTraderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("").WithTags("CppTrader");

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
    }

    private static IResult CppTraderUnavailable()
        => Results.Json(
            new
            {
                available = false,
                error = "CppTrader integration has been archived and is not part of active Meridian builds."
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);
}
