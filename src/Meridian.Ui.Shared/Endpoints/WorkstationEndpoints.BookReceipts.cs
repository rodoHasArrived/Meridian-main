using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    public sealed record BookReceiptRunRequest(Guid LedgerBookId, DateOnly AsOfDate);

    private static void MapBookReceiptQueueEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationBookReceiptRuns), async (
            Guid ledgerBookId, DateOnly? asOfDate, HttpContext context) =>
        {
            var (scope, error) = await ResolveBookReceiptScopeAsync(context, ledgerBookId).ConfigureAwait(false);
            if (error is not null)
                return error;
            var service = context.RequestServices.GetService<IBookReceiptQueueService>();
            if (service is null)
                return BookReceiptServiceUnavailable();
            var runs = await service.GetRunsAsync(scope!, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(runs.Where(run => asOfDate is null || run.AsOfDate == asOfDate), jsonOptions);
        })
        .WithName("ListWorkstationBookReceiptRuns")
        .RequireAuthenticatedSessionOrScopedLocalOperatorRead().RequireWorkstationTenantCompanyScope()
        .RequireAnyPermission(UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports,
            UserPermission.AdminMaintenance)
        .Produces<IReadOnlyList<BookReceiptRunDto>>(200).Produces(403).Produces(503);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationBookReceiptRun), async (
            string runId, Guid ledgerBookId, HttpContext context) =>
        {
            var (scope, error) = await ResolveBookReceiptScopeAsync(context, ledgerBookId).ConfigureAwait(false);
            if (error is not null)
                return error;
            var service = context.RequestServices.GetService<IBookReceiptQueueService>();
            if (service is null)
                return BookReceiptServiceUnavailable();
            var run = await service.GetRunAsync(scope!, runId, context.RequestAborted).ConfigureAwait(false);
            return run is null ? Results.NotFound() : Results.Json(run, jsonOptions);
        })
        .WithName("GetWorkstationBookReceiptRun")
        .RequireAuthenticatedSessionOrScopedLocalOperatorRead().RequireWorkstationTenantCompanyScope()
        .RequireAnyPermission(UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports,
            UserPermission.AdminMaintenance)
        .Produces<BookReceiptRunDto>(200).Produces(403).Produces(404).Produces(503);

        // Only this explicit command captures evidence and publishes source-owned exceptions.
        // Retained observation reads cannot create runs or change current casework.
        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationBookReceiptRuns), async (
            BookReceiptRunRequest request, HttpContext context) =>
        {
            if (request.LedgerBookId == Guid.Empty || request.AsOfDate == default)
                return Results.BadRequest(new { error = "A ledger book and as-of date are required." });
            var (scope, error) = await ResolveBookReceiptScopeAsync(context, request.LedgerBookId, mutate: true).ConfigureAwait(false);
            if (error is not null)
                return error;
            var service = context.RequestServices.GetService<IBookReceiptQueueService>();
            if (service is null)
                return BookReceiptServiceUnavailable();
            try
            {
                var run = await service.RunAsync(new BookReceiptQueryDto(scope!, request.AsOfDate), context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(run, jsonOptions);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { error = exception.Message });
            }
        })
        .WithName("RunWorkstationBookReceiptQueue")
        .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
        .RequireAnyPermission(UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces<BookReceiptRunDto>(200).Produces(400).Produces(403).Produces(409).Produces(503);
    }

    private static IResult BookReceiptServiceUnavailable()
        => Results.Problem("The retained receipt queue or its book authority is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);

    private static async Task<(BookReceiptScopeDto? Scope, IResult? Error)> ResolveBookReceiptScopeAsync(
        HttpContext context, Guid ledgerBookId, bool mutate = false)
    {
        if (ledgerBookId == Guid.Empty || !TryResolveReconciliationBreakQueueScope(context, out var scope))
            return (null, EndpointHelpers.Forbidden());
        var books = context.RequestServices.GetService<ILedgerJournalStore>();
        var registry = context.RequestServices.GetService<IFundProfileTenancyRegistry>();
        if (books is null || registry is null)
            return (null, BookReceiptServiceUnavailable());
        var book = await books.GetLedgerBookAsync(ledgerBookId, context.RequestAborted).ConfigureAwait(false);
        var owner = book is null ? null : await registry.ResolveAsync(book.FundProfileId, context.RequestAborted).ConfigureAwait(false);
        if (book is null || book.LedgerBookId != ledgerBookId || owner is null || !owner.IsHeldBy(scope.TenantId)
            || !string.Equals(owner.FundProfileId, book.FundProfileId, StringComparison.Ordinal)
            || !string.Equals(owner.CompanyId, scope.CompanyId, StringComparison.Ordinal))
            return (null, EndpointHelpers.Forbidden());
        if (!EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance))
        {
            var accessKind = book.FundStructureNodeKind switch
            {
                FundStructureNodeKindDto.Organization => AccessScopeKindDto.Organization,
                FundStructureNodeKindDto.Business => AccessScopeKindDto.Business,
                FundStructureNodeKindDto.Client => AccessScopeKindDto.Client,
                FundStructureNodeKindDto.Fund => AccessScopeKindDto.Fund,
                FundStructureNodeKindDto.Sleeve => AccessScopeKindDto.Sleeve,
                FundStructureNodeKindDto.Vehicle => AccessScopeKindDto.Vehicle,
                FundStructureNodeKindDto.InvestmentPortfolio => AccessScopeKindDto.InvestmentPortfolio,
                FundStructureNodeKindDto.Entity => AccessScopeKindDto.LegalEntity,
                FundStructureNodeKindDto.Account => AccessScopeKindDto.Account,
                _ => AccessScopeKindDto.Global
            };
            if (accessKind == AccessScopeKindDto.Global || book.FundStructureNodeId == Guid.Empty)
                return (null, EndpointHelpers.Forbidden());
            var canManage = EndpointAuthorization.HasPermission(context, UserPermission.ManageLedgerReports)
                && await EndpointAuthorization.HasScopedPermissionAsync(context, UserPermission.ManageLedgerReports,
                    accessKind, book.FundStructureNodeId, context.RequestAborted).ConfigureAwait(false);
            var canRead = !mutate && EndpointAuthorization.HasPermission(context, UserPermission.ViewLedgerReports)
                && await EndpointAuthorization.HasScopedPermissionAsync(context, UserPermission.ViewLedgerReports,
                    accessKind, book.FundStructureNodeId, context.RequestAborted).ConfigureAwait(false);
            if (!canManage && !canRead)
                return (null, EndpointHelpers.Forbidden());
        }
        return (new BookReceiptScopeDto(scope.TenantId, scope.CompanyId, ledgerBookId), null);
    }
}
