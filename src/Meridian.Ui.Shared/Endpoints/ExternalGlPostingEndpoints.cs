using System.Text.Json;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Api;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static class ExternalGlPostingEndpoints
{
    public static void MapExternalGlPostingEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(UiApiRoutes.ExternalGlPosting).WithTags("External GL Posting");

        group.MapGet("/policy", (string fundProfileId, Guid ledgerBookId, HttpContext ctx) => ExecuteAsync(ctx,
            async (service, identity, ct) => Results.Json(new
            {
                Policy = await service.GetPolicyAsync(ledgerBookId, identity.TenantId!, identity.CompanyId!, fundProfileId, ct)
                    .ConfigureAwait(false),
                identity.Actor,
                CanConfigure = EndpointAuthorization.HasPermission(ctx, UserPermission.AdminMaintenance),
                CanManage = EndpointAuthorization.HasAnyPermission(ctx, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance)
            }, jsonOptions)))
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
            .RequireAnyPermission(UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);

        group.MapPost("/policy", (string fundProfileId, Guid ledgerBookId, ExternalGlPolicyRequest request, HttpContext ctx) => ExecuteAsync(ctx,
            async (service, identity, ct) => Results.Json(await service.ConfigureBookAsync(ledgerBookId,
                identity.TenantId!, identity.CompanyId!, fundProfileId, request.ProviderId, request.ConnectionId,
                request.ConfirmationRequired, identity.Actor!, EndpointAuthorization.ResolveTrustedActionOrigin(ctx, request.ActionOrigin), ct)
                .ConfigureAwait(false), jsonOptions)))
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
            .RequirePermission(UserPermission.AdminMaintenance).RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet("/mapping-profiles", (string fundProfileId, Guid ledgerBookId, HttpContext ctx) => ExecuteAsync(ctx,
            async (service, identity, ct) => Results.Json(await service.ListMappingsAsync(ledgerBookId,
                identity.TenantId!, identity.CompanyId!, fundProfileId, ct).ConfigureAwait(false), jsonOptions)))
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
            .RequireAnyPermission(UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);

        group.MapGet("/{periodId:guid}/packages", (string fundProfileId, Guid ledgerBookId, Guid periodId, HttpContext ctx) => ExecuteAsync(ctx,
            async (service, identity, ct) => Results.Json(await service.ListAsync(
                Scope(identity, fundProfileId, ledgerBookId, periodId), ct).ConfigureAwait(false), jsonOptions)))
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
            .RequireAnyPermission(UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);

        group.MapPost("/{periodId:guid}/packages", (string fundProfileId, Guid ledgerBookId, Guid periodId, ExternalGlPrepareRequest request,
            HttpContext ctx) => ExecuteAsync(ctx, async (service, identity, ct) =>
        {
            if (!Enum.IsDefined(request.Purpose) || (request.Purpose == ExternalGlPostingPurpose.Export &&
                (request.PredecessorPackageId is not null || request.PredecessorPeriodId is not null)))
                throw new ArgumentException("Select a valid posting package purpose and predecessor.");
            var package = await service.PrepareAsync(Scope(identity, fundProfileId, ledgerBookId, periodId), request.MappingProfileId,
                request.JournalIds, request.Rationale, identity.Actor!, EndpointAuthorization.ResolveTrustedActionOrigin(ctx, request.ActionOrigin),
                request.Purpose, request.PredecessorPackageId, request.PredecessorPeriodId, ct).ConfigureAwait(false);
            return Results.Json(package, jsonOptions);
        })).RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
            .RequireAnyPermission(UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance)
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet("/{periodId:guid}/packages/{packageId}", (string fundProfileId, Guid ledgerBookId, Guid periodId,
            string packageId, HttpContext ctx) => ExecuteAsync(ctx, async (service, identity, ct) =>
        {
            var package = await service.GetAsync(Scope(identity, fundProfileId, ledgerBookId, periodId), packageId, ct).ConfigureAwait(false);
            return package is null ? Results.NotFound() : Results.Json(package, jsonOptions);
        })).RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
            .RequireAnyPermission(UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);

        group.MapPost("/{periodId:guid}/packages/{packageId}/decision", (string fundProfileId, Guid ledgerBookId, Guid periodId,
            string packageId, ExternalGlDecisionRequest request, HttpContext ctx) => ExecuteAsync(ctx, async (service, identity, ct) =>
        {
            var package = await service.DecideAsync(Scope(identity, fundProfileId, ledgerBookId, periodId), packageId,
                request.Approve, request.PayloadHash, request.Notes, identity.Actor!,
                EndpointAuthorization.ResolveTrustedActionOrigin(ctx, request.ActionOrigin), ct).ConfigureAwait(false);
            return package is null ? Results.NotFound() : Results.Json(package, jsonOptions);
        })).RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
            .RequireAnyPermission(UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance)
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet("/{periodId:guid}/packages/{packageId}/audit", (string fundProfileId, Guid ledgerBookId, Guid periodId,
            string packageId, HttpContext ctx) => ExecuteAsync(ctx, async (service, identity, ct) =>
        {
            var package = await service.GetAsync(Scope(identity, fundProfileId, ledgerBookId, periodId), packageId, ct).ConfigureAwait(false);
            if (package is null)
                return Results.NotFound();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, package }, jsonOptions);
            var hash = Sha256Digest.Compute(bytes);
            ctx.Response.Headers["X-Content-SHA256"] = hash;
            ctx.Response.Headers.CacheControl = "private, no-store";
            return Results.File(bytes, "application/json", $"external-gl-{package.PackageId}-{hash[..12]}.json");
        })).RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
            .RequireAnyPermission(UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);
    }

    private static ExternalGlPostingScope Scope(WorkstationTenantContext identity, string fund, Guid book, Guid period) =>
        new(identity.TenantId!, identity.CompanyId!, fund, book, period);

    private static async Task<IResult> ExecuteAsync(HttpContext context,
        Func<ExternalGlPostingService, WorkstationTenantContext, CancellationToken, Task<IResult>> execute)
    {
        var service = context.RequestServices.GetService<ExternalGlPostingService>();
        if (service is null)
            return Results.Problem("External GL posting requires the durable PostgreSQL ledger.", statusCode: StatusCodes.Status503ServiceUnavailable);
        var identity = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        try
        {
            return await execute(service, identity, context.RequestAborted).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            return EndpointHelpers.Forbidden();
        }
        catch (LedgerBookNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [ex.Message] });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }

    public sealed record ExternalGlPolicyRequest(string ProviderId, string ConnectionId, bool ConfirmationRequired,
        OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

    public sealed record ExternalGlPrepareRequest(string? MappingProfileId, IReadOnlyList<Guid>? JournalIds, string Rationale,
        ExternalGlPostingPurpose Purpose = ExternalGlPostingPurpose.Export, string? PredecessorPackageId = null,
        Guid? PredecessorPeriodId = null, OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

    public sealed record ExternalGlDecisionRequest(bool Approve, string PayloadHash, string Notes,
        OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);
}
