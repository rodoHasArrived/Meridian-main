using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static string WorkstationSubroute(string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        return route.StartsWith(WorkstationApiRoutePrefix, StringComparison.Ordinal)
            ? route[WorkstationApiRoutePrefix.Length..]
            : route;
    }

    private static string PortfolioSubroute(string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        return route.StartsWith(PortfolioApiRoutePrefix, StringComparison.Ordinal)
            ? route[PortfolioApiRoutePrefix.Length..]
            : route;
    }

    /// <summary>
    /// Standard 503 for workstation read surfaces whose backing services are not registered.
    /// Workspace endpoints must fail honestly instead of serving fabricated fallback data.
    /// </summary>
    private static IResult WorkstationServiceUnavailable(string detail)
        => Results.Problem(detail, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult StrategyReadServiceUnavailable()
        => WorkstationServiceUnavailable("Strategy run read service is not registered; live workspace data is unavailable.");

    private static IResult DataReadServicesUnavailable()
        => WorkstationServiceUnavailable("Neither the strategy run read service nor the configuration store is registered; live data-workspace telemetry is unavailable.");
}
