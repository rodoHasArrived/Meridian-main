using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class FundStructureEndpoints
{
    public static void MapReportingIncomeComparisonEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/fund-structure/reporting/comparisons")
            .WithTags("Reporting Comparisons").RequireWorkstationTenantScope();
        group.MapGet("/candidates", (Func<HttpContext, Task<IResult>>)(context => IncomeComparisonResult(context, ReportingIncomeComparisonJsonContext.Default.IncomeComparisonRuns,
            (service, access, ct) => service.ListCandidatesAsync(access, ct))))
            .WithName("ListReportingIncomeComparisonCandidates")
            .RequireAnyPermission(UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance);
        group.MapPost("/", (ReportingIncomeComparisonRequestDto request, HttpContext context) => IncomeComparisonResult(context, ReportingIncomeComparisonJsonContext.Default.ReportingIncomeComparisonDto,
            (service, access, ct) => service.CreateAsync(request, access, ct), StatusCodes.Status201Created))
            .WithName("CreateReportingIncomeComparison")
            .RequireAnyPermission(UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance);
        group.MapGet("/{comparisonId}", (string comparisonId, HttpContext context) => IncomeComparisonResult(context, ReportingIncomeComparisonJsonContext.Default.ReportingIncomeComparisonDto,
            (service, access, ct) => service.GetAsync(comparisonId, access, ct)))
            .WithName("GetReportingIncomeComparison")
            .RequireAnyPermission(UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance);
        group.MapGet("/{comparisonId}/contributions/{contributionId}", (string comparisonId, string contributionId, HttpContext context) => IncomeComparisonResult(context, ReportingIncomeComparisonJsonContext.Default.ReportingIncomeContributionSupportDto,
            (service, access, ct) => service.GetSupportAsync(comparisonId, contributionId, access, ct)))
            .WithName("GetReportingIncomeContributionSupport")
            .RequireAnyPermission(UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance);
    }

    private static async Task<IResult> IncomeComparisonResult<T>(HttpContext context, JsonTypeInfo<T> jsonTypeInfo,
        Func<ReportingIncomeComparisonService, ReportAccessQueryContext, CancellationToken, Task<T>> action,
        int statusCode = StatusCodes.Status200OK)
    {
        if (!HasReportingReadPermission(context))
            return EndpointHelpers.Forbidden();
        if (RequireReadyReportingDeployment(context, "Retained comparisons require the authoritative reporting deployment.") is { } unavailable)
            return unavailable;
        try
        {
            var service = context.RequestServices.GetService<ReportingIncomeComparisonService>();
            if (service is null)
                return WorkspaceServiceUnavailable();
            return Results.Json(await action(service, BuildReportAccessQueryContext(context), context.RequestAborted).ConfigureAwait(false), jsonTypeInfo, statusCode: statusCode);
        }
        catch (UnauthorizedAccessException exception) { return Results.Problem(exception.Message, statusCode: StatusCodes.Status403Forbidden); }
        catch (KeyNotFoundException exception) { return Results.Problem(exception.Message, statusCode: StatusCodes.Status404NotFound); }
        catch (ArgumentException exception) { return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException)
        { return Results.Problem("The retained comparison could not be safely loaded or persisted.", statusCode: StatusCodes.Status503ServiceUnavailable); }
    }
}
