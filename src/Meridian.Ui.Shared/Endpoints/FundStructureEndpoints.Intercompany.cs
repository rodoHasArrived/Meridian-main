using Meridian.Contracts.FundStructure;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static class IntercompanyAgreementEndpoints
{
    public static void MapIntercompanyAgreementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/fund-structure/intercompany-agreements").WithTags("Intercompany agreements")
            .RequireWorkstationTenantScope().RequirePermission(UserPermission.ManageFundStructure);
        group.MapGet("", (Guid? organizationId, HttpContext context) => ExecuteAsync(context, async (service, tenant, _) =>
        {
            var rows = await service.ListAsync(tenant, organizationId, context.RequestAborted).ConfigureAwait(false);
            var allowed = await AccessibleAsync(context, rows).ConfigureAwait(false);
            return Results.Json(allowed, IntercompanyContractsJsonContext.Default.IntercompanyAgreementDtoArray);
        }));
        group.MapGet("/{agreementId:guid}", (Guid agreementId, HttpContext context) => ExecuteAsync(context, async (service, tenant, _) =>
        {
            var result = await service.GetAsync(tenant, agreementId, context.RequestAborted).ConfigureAwait(false);
            if (result is null || !(await AccessibleAsync(context, [result]).ConfigureAwait(false)).Any()) return Results.NotFound();
            return Results.Json(result, IntercompanyContractsJsonContext.Default.IntercompanyAgreementDto);
        }));
        group.MapGet("/{agreementId:guid}/history", (Guid agreementId, HttpContext context) => ExecuteAsync(context, async (service, tenant, _) =>
        {
            var result = await service.GetHistoryAsync(tenant, agreementId, context.RequestAborted).ConfigureAwait(false);
            if (result is null || !(await AccessibleAsync(context, [result.Current]).ConfigureAwait(false)).Any()) return Results.NotFound();
            return Results.Json(result, IntercompanyContractsJsonContext.Default.IntercompanyAgreementHistoryDto);
        }));
        group.MapPost("", (CreateIntercompanyAgreementRequest request, HttpContext context) => ExecuteAsync(context, async (service, tenant, actor) =>
        {
            if (!await CanAccessAsync(context, request.OrganizationId, request.PartyOneEntityId, request.PartyTwoEntityId).ConfigureAwait(false)) return Results.Forbid();
            return Results.Json(await service.CreateAsync(tenant, actor, request, context.RequestAborted).ConfigureAwait(false),
                IntercompanyContractsJsonContext.Default.IntercompanyAgreementDto, statusCode: StatusCodes.Status201Created);
        })).RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
        group.MapPost("/{agreementId:guid}/revisions", (Guid agreementId, ReviseIntercompanyAgreementRequest request, HttpContext context) =>
            ExecuteAsync(context, async (service, tenant, actor) =>
            {
                var old = await service.GetAsync(tenant, agreementId, context.RequestAborted).ConfigureAwait(false);
                if (old is null || !(await AccessibleAsync(context, [old]).ConfigureAwait(false)).Any()) return Results.NotFound();
                return Results.Json(await service.ReviseAsync(tenant, actor, agreementId, request, context.RequestAborted).ConfigureAwait(false),
                    IntercompanyContractsJsonContext.Default.IntercompanyAgreementDto);
            })).RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
        group.MapPost("/{agreementId:guid}/{action}", (Guid agreementId, string action, IntercompanyAgreementActionRequest request, HttpContext context) =>
            ExecuteAsync(context, async (service, tenant, actor) =>
            {
                if (!Enum.TryParse<IntercompanyAgreementActionDto>(action, true, out var parsed) || parsed is IntercompanyAgreementActionDto.Create or IntercompanyAgreementActionDto.Revise)
                    return Results.BadRequest(new { error = "Unsupported agreement action." });
                var old = await service.GetAsync(tenant, agreementId, context.RequestAborted).ConfigureAwait(false);
                if (old is null || !(await AccessibleAsync(context, [old]).ConfigureAwait(false)).Any()) return Results.NotFound();
                if (request.SuccessorAgreementId is { } successorId)
                {
                    var successor = await service.GetAsync(tenant, successorId, context.RequestAborted).ConfigureAwait(false);
                    if (successor is null || !(await AccessibleAsync(context, [successor]).ConfigureAwait(false)).Any()) return Results.NotFound();
                }
                return Results.Json(await service.ApplyActionAsync(tenant, actor, agreementId, parsed, request, context.RequestAborted).ConfigureAwait(false),
                    IntercompanyContractsJsonContext.Default.IntercompanyAgreementDto);
            })).RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static async Task<IResult> ExecuteAsync(HttpContext context, Func<IntercompanyAgreementService, string, string, Task<IResult>> action)
    {
        var scope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        if (!scope.HasTenantScope || string.IsNullOrWhiteSpace(scope.Actor)) return Results.Forbid();
        var service = context.RequestServices.GetService<IntercompanyAgreementService>();
        if (service is null) return Results.Problem("Intercompany agreement service is unavailable.", statusCode: 501);
        try { return await action(service, scope.TenantId!, scope.Actor).ConfigureAwait(false); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
    }

    private static async Task<IntercompanyAgreementDto[]> AccessibleAsync(HttpContext context, IReadOnlyList<IntercompanyAgreementDto> rows)
    {
        var result = new List<IntercompanyAgreementDto>();
        foreach (var row in rows)
            if (await CanAccessAsync(context, row.OrganizationId, row.PartyOneEntityId, row.PartyTwoEntityId).ConfigureAwait(false)) result.Add(row);
        return result.ToArray();
    }

    private static async Task<bool> CanAccessAsync(HttpContext context, Guid organizationId, Guid first, Guid second)
    {
        var organizations = await EndpointAuthorization.AuthorizeScopedManyAsync(context, UserPermission.ManageFundStructure,
            AccessScopeKindDto.Organization, [organizationId], context.RequestAborted).ConfigureAwait(false);
        var entities = await EndpointAuthorization.AuthorizeScopedManyAsync(context, UserPermission.ManageFundStructure,
            AccessScopeKindDto.LegalEntity, [first, second], context.RequestAborted).ConfigureAwait(false);
        return organizations.Contains(organizationId) && entities.Contains(first) && entities.Contains(second);
    }
}
