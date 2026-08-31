using System.Globalization;
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
            return ReportingScheduleFailure(context, exception);
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

    private static IResult ReportingScheduleFailure(HttpContext context, Exception exception) =>
        exception switch
        {
            // This family versions by retained-revision timestamp, carried in the same string
            // extensions the numeric families use (round-trip "o" format).
            ReportingScheduleConcurrencyException concurrency => ApiProblemDetails.VersionConflict(
                context,
                concurrency.Message,
                resourceId: concurrency.ScheduleId,
                expectedVersion: concurrency.ExpectedUpdatedAtUtc?.ToString("o", CultureInfo.InvariantCulture),
                currentVersion: concurrency.ActualUpdatedAtUtc?.ToString("o", CultureInfo.InvariantCulture)),
            ReportingScheduleExecutionLeaseException => ApiProblemDetails.Conflict(context, exception.Message),
            _ => Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest),
        };
}
