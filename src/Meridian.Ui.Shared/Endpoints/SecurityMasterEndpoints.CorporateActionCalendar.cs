using System.Text.Json;
using Meridian.Application.SecurityMaster.CorporateActions;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class SecurityMasterEndpoints
{
    private static void MapCorporateActionCalendarEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(UiApiRoutes.SecurityMasterCorporateActionsCalendar, async (
            DateOnly from, DateOnly to, string? financialAccountId, string? custodyAccountId, string? cursor, int? take,
            HttpContext context, [FromServices] CorporateActionCalendarService service, CancellationToken ct) =>
        {
            if (!TryResolveCorporateActionScope(context, out var scope))
                return CorporateActionProblem(context, new CorporateActionScopeMismatchException("An authenticated tenant/company scope is required."));
            try
            {
                return Results.Json(await service.GetCalendarAsync(scope.TenantId, scope.CompanyId, from, to,
                    financialAccountId, custodyAccountId, cursor, take ?? 250, ct).ConfigureAwait(false), jsonOptions);
            }
            catch (CorporateActionOperationException exception) { return CorporateActionProblem(context, exception); }
        })
        .WithName("GetCorporateActionCalendar")
        .RequirePermission(UserPermission.ViewCorporateActions)
        .RequireWorkstationTenantCompanyScope()
        .Produces<CorporateActionCalendarPageDto>();

        group.MapGet(UiApiRoutes.SecurityMasterCorporateActionCaseDeadlines, async (
            Guid caseId, HttpContext context, [FromServices] CorporateActionCalendarService service, CancellationToken ct) =>
        {
            if (!TryResolveCorporateActionScope(context, out var scope))
                return CorporateActionProblem(context, new CorporateActionScopeMismatchException("An authenticated tenant/company scope is required."));
            try
            {
                var result = await service.GetDeadlinesAsync(caseId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false);
                var permitted = CanRecordCorporateActionDeadline(context);
                return Results.Json(result with
                {
                    Case = ApplyCallerActionAvailability(result.Case, context),
                    CanRecord = result.CanRecord && permitted,
                    Blocker = permitted ? result.Blocker : "Recording deadlines requires corporate-action terms or election permission."
                }, jsonOptions);
            }
            catch (CorporateActionOperationException exception) { return CorporateActionProblem(context, exception); }
        })
        .WithName("GetCorporateActionCaseDeadlines")
        .RequirePermission(UserPermission.ViewCorporateActions)
        .RequireWorkstationTenantCompanyScope()
        .Produces<CorporateActionCaseDeadlinesDto>();

        group.MapPost(UiApiRoutes.SecurityMasterCorporateActionCaseDeadlines, async (
            Guid caseId, RecordCorporateActionDeadlineRequestDto request, HttpContext context,
            [FromServices] CorporateActionCalendarService service, CancellationToken ct) =>
            await ExecuteScopedCaseCommandAsync(caseId, request.CaseId, request.TenantId, request.CompanyId, context,
                trusted => service.RecordAsync(request with { TenantId = trusted.TenantId, CompanyId = trusted.CompanyId },
                    trusted.Actor, trusted.CorrelationId, ct), jsonOptions).ConfigureAwait(false))
        .WithName("RecordCorporateActionDeadline")
        .RequireAnyPermission(UserPermission.ResolveCorporateActionTerms, UserPermission.RecordCorporateActionElection)
        .RequireWorkstationTenantCompanyScope()
        .Produces<CorporateActionEvidenceMutationResultDto>()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.SecurityMasterCorporateActionDeadlineImportPreview, async (
            Guid caseId, CorporateActionDeadlineImportRequestDto request, HttpContext context,
            [FromServices] CorporateActionCalendarService service, CancellationToken ct) =>
            await ExecuteScopedCaseCommandAsync(caseId, request.CaseId, request.TenantId, request.CompanyId, context,
                trusted => service.PreviewImportAsync(request with { TenantId = trusted.TenantId, CompanyId = trusted.CompanyId }, ct), jsonOptions).ConfigureAwait(false))
        .WithName("PreviewCorporateActionDeadlineImport")
        .RequireAnyPermission(UserPermission.ResolveCorporateActionTerms, UserPermission.RecordCorporateActionElection)
        .RequireWorkstationTenantCompanyScope()
        .Produces<CorporateActionDeadlineImportPreviewDto>()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.SecurityMasterCorporateActionDeadlineImport, async (
            Guid caseId, CorporateActionDeadlineImportRequestDto request, HttpContext context,
            [FromServices] CorporateActionCalendarService service, CancellationToken ct) =>
            await ExecuteScopedCaseCommandAsync(caseId, request.CaseId, request.TenantId, request.CompanyId, context,
                trusted => service.ImportAsync(request with { TenantId = trusted.TenantId, CompanyId = trusted.CompanyId },
                    trusted.Actor, trusted.CorrelationId, ct), jsonOptions).ConfigureAwait(false))
        .WithName("ImportCorporateActionDeadlines")
        .RequireAnyPermission(UserPermission.ResolveCorporateActionTerms, UserPermission.RecordCorporateActionElection)
        .RequireWorkstationTenantCompanyScope()
        .Produces<CorporateActionDeadlineImportResultDto>()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static bool CanRecordCorporateActionDeadline(HttpContext context) =>
        EndpointAuthorization.HasPermission(context, UserPermission.ResolveCorporateActionTerms) ||
        EndpointAuthorization.HasPermission(context, UserPermission.RecordCorporateActionElection);
}
