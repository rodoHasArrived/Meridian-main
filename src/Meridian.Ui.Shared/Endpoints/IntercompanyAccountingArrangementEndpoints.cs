using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>Accounting configuration and ledger-authorized approved legal/profile selection; native legal authoring stays separate.</summary>
public static class IntercompanyAccountingArrangementEndpoints
{
    public static void MapIntercompanyAccountingArrangementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ledger/intercompany-accounting-arrangements")
            .WithTags("Intercompany accounting arrangements");
        group.MapGet("", (Guid? organizationId, Guid? bookId, DateOnly? asOf, HttpContext context) => ExecuteAsync(context,
            async (service, tenant, _) =>
            {
                if (bookId is { } selectedBook)
                {
                    var book = await AuthorizedBookAsync(context, selectedBook).ConfigureAwait(false);
                    if (book is null) return EndpointHelpers.Forbidden();
                    var date = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
                    var resolved = new List<ResolvedIntercompanyAccountingArrangementDto>();
                    var store = context.RequestServices.GetRequiredService<Meridian.Storage.FundStructure.IIntercompanyAccountingArrangementStore>();
                    foreach (var arrangement in await store.ListAsync(tenant, ct: context.RequestAborted).ConfigureAwait(false))
                    {
                        var history = await store.GetAsync(tenant, arrangement.ArrangementId, context.RequestAborted).ConfigureAwait(false);
                        if (history is null) continue;
                        foreach (var revision in history.Events.Where(item => item.Action == IntercompanyAccountingArrangementActionDto.Approve)
                            .Select(item => item.AffectedRevision).Where(item => item.PartyMappings.Any(map => map.LedgerBookId == selectedBook)))
                        {
                            try
                            {
                                var effective = await service.ResolveApprovedRevisionAsync(tenant, arrangement.ArrangementId, revision.RevisionId,
                                    new(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero), context.RequestAborted).ConfigureAwait(false);
                                if (effective.PartyMappings.Any(map => map.LedgerBookId == selectedBook && map.EntityId == book.FundStructureNodeId))
                                    resolved.Add(effective);
                            }
                            catch (InvalidOperationException) { /* Inactive/superseded/legal-version changes are not selectable. */ }
                        }
                    }
                    return Results.Json(resolved.OrderBy(item => item.ArrangementReference).ThenBy(item => item.ArrangementId).ToArray(),
                        IntercompanyAccountingArrangementsJsonContext.Default.ResolvedIntercompanyAccountingArrangementDtoArray);
                }
                if (!CanManage(context)) return EndpointHelpers.Forbidden();
                var rows = await service.ListAsync(tenant, organizationId, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(await AccessibleAsync(context, rows).ConfigureAwait(false),
                    IntercompanyAccountingArrangementsJsonContext.Default.ResolvedIntercompanyAccountingArrangementDtoArray);
            })).RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope();
        group.MapGet("/legal-agreements", (Guid bookId, DateOnly? asOf, HttpContext context) => ExecuteAsync(context,
            async (_, tenant, _) =>
            {
                var book = await AuthorizedBookAsync(context, bookId).ConfigureAwait(false);
                if (book is null) return EndpointHelpers.Forbidden();
                var legal = context.RequestServices.GetRequiredService<IIntercompanyAccountingArrangementLegalAuthority>();
                var rows = await legal.ListEffectiveRevisionsAsync(tenant, book.FundStructureNodeId,
                    asOf ?? DateOnly.FromDateTime(DateTime.UtcNow), context.RequestAborted).ConfigureAwait(false);
                return Results.Json(rows.ToArray(), IntercompanyAccountingArrangementsJsonContext.Default.IntercompanyAgreementRevisionDtoArray);
            })).RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope();
        group.MapGet("/{id:guid}", (Guid id, HttpContext context) => ExecuteAsync(context, async (service, tenant, _) =>
        {
            if (!CanManage(context)) return EndpointHelpers.Forbidden();
            var result = await service.GetAsync(tenant, id, context.RequestAborted).ConfigureAwait(false);
            if (result is null || !await CanAccessAsync(context, result).ConfigureAwait(false)) return Results.NotFound();
            return Results.Json(result, IntercompanyAccountingArrangementsJsonContext.Default.ResolvedIntercompanyAccountingArrangementDto);
        })).RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope();
        group.MapGet("/{id:guid}/history", (Guid id, HttpContext context) => ExecuteAsync(context, async (service, tenant, _) =>
        {
            if (!CanManage(context)) return EndpointHelpers.Forbidden();
            var current = await service.GetAsync(tenant, id, context.RequestAborted).ConfigureAwait(false);
            if (current is null || !await CanAccessAsync(context, current).ConfigureAwait(false)) return Results.NotFound();
            return Results.Json(await service.GetHistoryAsync(tenant, id, context.RequestAborted).ConfigureAwait(false),
                IntercompanyAccountingArrangementsJsonContext.Default.IntercompanyAccountingArrangementHistoryDto);
        })).RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope();
        group.MapPost("", (CreateIntercompanyAccountingArrangementRequest request, HttpContext context) => ExecuteAsync(context,
            async (service, tenant, actor) =>
            {
                if (!CanManage(context)) return EndpointHelpers.Forbidden();
                var legal = await context.RequestServices.GetRequiredService<IIntercompanyAccountingArrangementLegalAuthority>()
                    .GetRevisionAsync(tenant, new(request.CanonicalAgreementId, request.CanonicalAgreementVersion, request.CanonicalPayloadHash),
                        context.RequestAborted).ConfigureAwait(false);
                if (!await CanAccessAsync(context, request.OrganizationId, legal.Terms.PartyOneId, legal.Terms.PartyTwoId).ConfigureAwait(false))
                    return EndpointHelpers.Forbidden();
                return Results.Json(await service.CreateAsync(tenant, actor, request, context.RequestAborted).ConfigureAwait(false),
                    IntercompanyAccountingArrangementsJsonContext.Default.IntercompanyAccountingArrangementDto, statusCode: StatusCodes.Status201Created);
            })).RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope().RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
        group.MapPost("/{id:guid}/revisions", (Guid id, ReviseIntercompanyAccountingArrangementRequest request, HttpContext context) => ExecuteAsync(context,
            async (service, tenant, actor) =>
            {
                if (!CanManage(context)) return EndpointHelpers.Forbidden();
                var current = await service.GetAsync(tenant, id, context.RequestAborted).ConfigureAwait(false);
                if (current is null || !await CanAccessAsync(context, current).ConfigureAwait(false)) return Results.NotFound();
                return Results.Json(await service.ReviseAsync(tenant, actor, id, request, context.RequestAborted).ConfigureAwait(false),
                    IntercompanyAccountingArrangementsJsonContext.Default.IntercompanyAccountingArrangementDto);
            })).RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope().RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
        group.MapPost("/{id:guid}/{action}", (Guid id, string action, IntercompanyAccountingArrangementActionRequest request, HttpContext context) => ExecuteAsync(context,
            async (service, tenant, actor) =>
            {
                if (!CanManage(context)) return EndpointHelpers.Forbidden();
                if (!Enum.TryParse<IntercompanyAccountingArrangementActionDto>(action, true, out var parsed)
                    || parsed is not (IntercompanyAccountingArrangementActionDto.Submit or IntercompanyAccountingArrangementActionDto.Approve
                        or IntercompanyAccountingArrangementActionDto.Reject or IntercompanyAccountingArrangementActionDto.Retire))
                    return Results.BadRequest(new { error = "Unsupported accounting arrangement action." });
                var current = await service.GetAsync(tenant, id, context.RequestAborted).ConfigureAwait(false);
                if (current is null || !await CanAccessAsync(context, current).ConfigureAwait(false)) return Results.NotFound();
                OperationsOriginGuard.RequireHumanOperator(EndpointAuthorization.ResolveTrustedActionOrigin(context,
                    OperationsActionOriginDto.HumanOperator), "review intercompany accounting configuration");
                return Results.Json(await service.ApplyActionAsync(tenant, actor, id, parsed, request, context.RequestAborted).ConfigureAwait(false),
                    IntercompanyAccountingArrangementsJsonContext.Default.IntercompanyAccountingArrangementDto);
            })).RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope().RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }
    private static async Task<IResult> ExecuteAsync(HttpContext context,
        Func<IntercompanyAccountingArrangementService, string, string, Task<IResult>> action)
    {
        var scope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        if (!scope.HasTenantScope || string.IsNullOrWhiteSpace(scope.Actor) || string.IsNullOrWhiteSpace(scope.CompanyId)) return EndpointHelpers.Forbidden();
        var service = context.RequestServices.GetService<IntercompanyAccountingArrangementService>();
        if (service is null) return Results.Problem("Intercompany accounting arrangement authority is unavailable.", statusCode: 503);
        try { return await action(service, scope.TenantId!, scope.Actor).ConfigureAwait(false); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
        catch (HumanOperatorRequiredException error) { return Results.Json(new { error = error.Message }, statusCode: 403); }
        catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
        catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
    }
    private static bool CanManage(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(context, UserPermission.ManageFundStructure, UserPermission.AdminMaintenance);
    private static async Task<ResolvedIntercompanyAccountingArrangementDto[]> AccessibleAsync(HttpContext context,
        IReadOnlyList<ResolvedIntercompanyAccountingArrangementDto> rows)
    {
        var results = new List<ResolvedIntercompanyAccountingArrangementDto>();
        foreach (var row in rows) if (await CanAccessAsync(context, row).ConfigureAwait(false)) results.Add(row);
        return results.ToArray();
    }
    private static Task<bool> CanAccessAsync(HttpContext context, ResolvedIntercompanyAccountingArrangementDto row)
        => CanAccessAsync(context, row.OrganizationId, row.PartyOneEntityId, row.PartyTwoEntityId);
    private static async Task<bool> CanAccessAsync(HttpContext context, Guid organization, Guid first, Guid second)
    {
        if (EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance)) return true;
        var organizations = await EndpointAuthorization.AuthorizeScopedManyAsync(context, UserPermission.ManageFundStructure,
            AccessScopeKindDto.Organization, [organization], context.RequestAborted).ConfigureAwait(false);
        var entities = await EndpointAuthorization.AuthorizeScopedManyAsync(context, UserPermission.ManageFundStructure,
            AccessScopeKindDto.LegalEntity, [first, second], context.RequestAborted).ConfigureAwait(false);
        return organizations.Contains(organization) && entities.Contains(first) && entities.Contains(second);
    }
    private static async Task<LedgerBookRecord?> AuthorizedBookAsync(HttpContext context, Guid bookId)
    {
        if (!EndpointAuthorization.HasAnyPermission(context, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance)) return null;
        var journals = context.RequestServices.GetService<ILedgerJournalStore>();
        var owners = context.RequestServices.GetService<IFundProfileTenancyRegistry>();
        if (journals is null || owners is null) return null;
        var book = await journals.GetLedgerBookAsync(bookId, context.RequestAborted).ConfigureAwait(false);
        if (book is null || book.FundStructureNodeKind != FundStructureNodeKindDto.Entity) return null;
        var owner = await owners.ResolveAsync(book.FundProfileId, context.RequestAborted).ConfigureAwait(false);
        var scope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        if (owner is null || !owner.IsHeldBy(scope.TenantId)
            || !string.Equals(owner.CompanyId, scope.CompanyId, StringComparison.Ordinal)) return null;
        if (!EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance)
            && !await EndpointAuthorization.HasScopedPermissionAsync(context, UserPermission.ManageLedgerReports,
                AccessScopeKindDto.LegalEntity, book.FundStructureNodeId, context.RequestAborted).ConfigureAwait(false)) return null;
        return book;
    }
}
