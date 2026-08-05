using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class FundStructureEndpoints
{
    private static IResult SetScheduleState(
        HttpContext context,
        string scheduleId,
        ReportingScheduleStateDto state)
    {
        if (!HasReportingWorkflowPermission(context))
        {
            return EndpointHelpers.Forbidden();
        }

        if (RequireReadyReportingDeployment(context) is { } deploymentFailure)
        {
            return deploymentFailure;
        }

        var service = context.RequestServices.GetService<ReportingScheduleService>();
        if (service is null)
        {
            return WorkspaceServiceUnavailable();
        }

        try
        {
            return Results.Json(
                service.SetState(
                    scheduleId,
                    state,
                    BuildReportAccessQueryContext(context)),
                statusCode: StatusCodes.Status200OK);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status403Forbidden);
        }
        catch (ReportingScheduleConcurrencyException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or InvalidDataException
                                          or KeyNotFoundException)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static IResult? RequireReadyReportingDeployment(
        HttpContext context,
        string? unavailableMessage = null)
    {
        try
        {
            var readiness = context.RequestServices
                .GetService<IReportingDeploymentReadinessService>();
            if (readiness?.Evaluate().IsReady == true)
            {
                return null;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Mutation and execution fail closed when deployment authority cannot be evaluated.
        }

        return Results.Problem(
            unavailableMessage
            ?? "Reporting schedule changes and execution are unavailable until the authoritative reporting deployment is ready.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static int ReportingScheduleFailureStatus(Exception exception) =>
        exception is ReportingScheduleExecutionLeaseException
            or ReportingScheduleConcurrencyException
            ? StatusCodes.Status409Conflict
            : StatusCodes.Status400BadRequest;
}
