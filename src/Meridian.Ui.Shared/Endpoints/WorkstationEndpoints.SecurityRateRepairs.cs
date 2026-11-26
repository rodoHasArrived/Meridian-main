using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    internal static void MapSecurityRateRepairEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/security-master/rate-repairs").WithTags("SecurityMasterRateRepair")
            .RequireWorkstationTenantScope();
        group.MapGet("/candidates", async (int? skip, int? pageSize, HttpContext context, [FromServices] SecurityRateRepairService? service) =>
        {
            if (!TryRepairActor(context, false, out var actor, out var refusal))
                return refusal!;
            if (service is null)
                return WorkbenchServiceUnavailable();
            return await ExecuteRepairAsync(async () => (object)await service.DiscoverPageAsync(actor!, skip ?? 0, pageSize ?? 200,
                context.RequestAborted), jsonOptions);
        }).WithName("SecurityRateRepairCandidates").RequirePermission(UserPermission.ViewSecurityMaster);

        group.MapGet("", async (HttpContext context, [FromServices] SecurityRateRepairService? service) =>
        {
            if (!TryRepairActor(context, false, out var actor, out var refusal))
                return refusal!;
            if (service is null)
                return WorkbenchServiceUnavailable();
            return await ExecuteRepairAsync(async () => (object)await service.ListAsync(actor!, context.RequestAborted), jsonOptions);
        }).WithName("SecurityRateRepairBatches").RequirePermission(UserPermission.ViewSecurityMaster);

        group.MapGet("/{batchId:guid}", async (Guid batchId, HttpContext context, [FromServices] SecurityRateRepairService? service) =>
        {
            if (!TryRepairActor(context, false, out var actor, out var refusal))
                return refusal!;
            if (service is null)
                return WorkbenchServiceUnavailable();
            return await ExecuteRepairAsync(async () => await service.GetAsync(batchId, actor!, context.RequestAborted)
                ?? throw new KeyNotFoundException("Repair batch was not found."), jsonOptions);
        }).WithName("SecurityRateRepairBatch").RequirePermission(UserPermission.ViewSecurityMaster);

        group.MapPost("", async (CreateSecurityRateRepairBatchRequest? request, HttpContext context,
            [FromServices] SecurityRateRepairService? service, [FromServices] IFundProfileTenantGuard? guard,
            [FromServices] IFundProfileTenancyRegistry? registry) =>
        {
            if (!TryRepairActor(context, true, out var actor, out var refusal))
                return refusal!;
            if (service is null)
                return WorkbenchServiceUnavailable();
            if (request?.Scope is null)
                return WorkbenchMissingPayload("A complete frozen rate-repair request is required.");
            var fundRefusal = await GuardRepairFundAsync(context, request.Scope.FundProfileId, guard, registry);
            if (fundRefusal is not null)
                return fundRefusal;
            return await ExecuteRepairAsync(async () => (object)await service.CreateAsync(request, actor!, context.RequestAborted), jsonOptions);
        }).WithName("SecurityRateRepairCreate").RequirePermission(UserPermission.ModifySecurityMaster);

        foreach (var command in new[] { "Stage", "Submit", "Approve", "Publish" })
        {
            group.MapPost($"/{{batchId:guid}}/{command.ToLowerInvariant()}", async (Guid batchId,
                SecurityRateRepairBatchCommandRequest? request, HttpContext context,
                [FromServices] SecurityRateRepairService? service, [FromServices] IFundProfileTenantGuard? guard,
                [FromServices] IFundProfileTenancyRegistry? registry) =>
            {
                if (!TryRepairActor(context, true, out var actor, out var refusal))
                    return refusal!;
                if (service is null)
                    return WorkbenchServiceUnavailable();
                if (request is null)
                    return WorkbenchMissingPayload("A versioned batch command is required.");
                var batch = await service.GetAsync(batchId, actor!, context.RequestAborted);
                if (batch is null)
                    return Results.NotFound();
                var fundRefusal = await GuardRepairFundAsync(context, batch.Scope.FundProfileId, guard, registry);
                if (fundRefusal is not null)
                    return fundRefusal;
                return await ExecuteRepairAsync(async () => (object)await service.ExecuteAsync(batchId, command, request,
                    actor!, context.RequestAborted), jsonOptions);
            }).WithName($"SecurityRateRepair{command}").RequirePermission(UserPermission.ModifySecurityMaster);
        }
    }

    private static bool TryRepairActor(HttpContext context, bool modify, out SecurityRateRepairActor? actor, out IResult? refusal)
    {
        actor = null;
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        if (!tenant.HasTenantScope || !EndpointAuthorization.TryResolveActor(context, out var authenticated))
        { refusal = Results.Unauthorized(); return false; }
        if (!EndpointAuthorization.HasPermission(context, modify ? UserPermission.ModifySecurityMaster : UserPermission.ViewSecurityMaster))
        { refusal = EndpointHelpers.Forbidden(); return false; }
        actor = new(tenant.TenantId!, tenant.CompanyId, authenticated,
            EndpointAuthorization.HasPermission(context, UserPermission.ModifySecurityMaster));
        refusal = null;
        return true;
    }

    private static async Task<IResult?> GuardRepairFundAsync(HttpContext context, string fundProfileId,
        IFundProfileTenantGuard? guard, IFundProfileTenancyRegistry? registry)
    {
        if (guard is null || registry is null || string.IsNullOrWhiteSpace(fundProfileId))
            return Results.Problem("Durable fund ownership authority is required for a bulk repair.", statusCode: 503);
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        if (!(await guard.EvaluateAsync(tenant, fundProfileId, context.RequestAborted)).IsAllowed)
            return EndpointHelpers.Forbidden();
        try
        {
            var owner = await registry.BindAsync(fundProfileId, tenant.TenantId!, tenant.CompanyId, context.RequestAborted);
            return owner?.IsHeldBy(tenant.TenantId) == true ? null : EndpointHelpers.Forbidden();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Results.Problem("Fund ownership could not be verified; no bulk repair was performed.", statusCode: 503);
        }
    }

    private static async Task<IResult> ExecuteRepairAsync(Func<Task<object>> operation, JsonSerializerOptions jsonOptions)
    {
        try
        { return Results.Json(await operation(), jsonOptions); }
        catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (SecurityRateRepairConflictException exception) { return Results.Problem(exception.Message, statusCode: 409); }
        catch (ArgumentException exception) { return Results.Problem(exception.Message, statusCode: 422); }
    }
}
