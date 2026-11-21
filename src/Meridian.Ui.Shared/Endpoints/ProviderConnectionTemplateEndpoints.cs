using System.Text.Json;
using Meridian.Application.ProviderRouting;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

public static class ProviderConnectionTemplateEndpoints
{
    internal const UserPermission Permissions = UserPermission.ManageCredentials | UserPermission.ManageProviders | UserPermission.ModifyConfig;

    public static void MapProviderConnectionTemplateEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Provider Connection Templates");
        group.RequireWorkstationTenantScope();
        group.RequirePermission(Permissions);

        group.MapGet(UiApiRoutes.ProviderConnectionTemplates, (HttpContext context, [FromServices] ProviderConnectionTemplateService service)
            => ExecuteAsync(context, (tenant, _) => Task.FromResult(Results.Json(service.List(tenant), jsonOptions))))
            .WithName("ListProviderConnectionTemplates");

        group.MapGet(UiApiRoutes.ProviderConnectionTemplateExport, (string name, HttpContext context, [FromServices] ProviderConnectionTemplateService service)
            => ExecuteAsync(context, (tenant, _) => Task.FromResult(Results.Json(service.Export(tenant, name), jsonOptions))))
            .WithName("ExportProviderConnectionTemplate");

        group.MapPost(UiApiRoutes.ProviderConnectionTemplateImport, (JsonElement payload, HttpContext context, [FromServices] ProviderConnectionTemplateService service)
            => ExecuteAsync(context, async (tenant, _) => Results.Json(new
            {
                imported = await service.ImportAsync(tenant, payload, context.RequestAborted).ConfigureAwait(false)
            }, jsonOptions)))
            .WithName("ImportProviderConnectionTemplates").RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.ProviderConnectionTemplates, (SaveProviderConnectionTemplateRequest request, HttpContext context,
            [FromServices] ProviderConnectionTemplateService service)
            => ExecuteAsync(context, async (tenant, _) => Results.Json(
                await service.SaveFromConnectionAsync(tenant, request, context.RequestAborted).ConfigureAwait(false), jsonOptions)))
            .WithName("SaveProviderConnectionAsTemplate").RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.ProviderConnectionTemplateSetups, (BeginProviderTemplateSetupRequest request, HttpContext context,
            [FromServices] ProviderConnectionTemplateService service)
            => ExecuteAsync(context, async (tenant, actor) => Results.Json(
                await service.BeginAsync(tenant, actor, request, context.RequestAborted).ConfigureAwait(false), jsonOptions)))
            .WithName("BeginProviderTemplateSetup").RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.ProviderConnectionTemplateSetupTest, (Guid setupId, HttpContext context,
            [FromServices] ProviderConnectionTemplateService service, [FromServices] ProviderConnectionLifecycleService lifecycle)
            => ExecuteAsync(context, async (tenant, actor) => Results.Json(
                await service.TestAsync(tenant, actor, setupId, (provider, ct) => lifecycle.ForTemplateSetup(setupId, tenant, actor, provider)
                    .TestTemplateSetupAsync(provider, actor, ct), context.RequestAborted).ConfigureAwait(false), jsonOptions)))
            .WithName("TestProviderTemplateSetup").RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.ProviderConnectionTemplateSetup, (Guid setupId, HttpContext context,
            [FromServices] ProviderConnectionTemplateService service)
            => ExecuteAsync(context, async (tenant, actor) => Results.Json(
                await service.SaveAsync(tenant, actor, setupId, context.RequestAborted).ConfigureAwait(false), jsonOptions)))
            .WithName("SaveProviderTemplateSetup").RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapDelete(UiApiRoutes.ProviderConnectionTemplateSetup, (Guid setupId, HttpContext context,
            [FromServices] ProviderConnectionTemplateService service)
            => ExecuteAsync(context, async (tenant, actor) =>
            {
                var discarded = await service.DiscardAsync(tenant, actor, setupId, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(new { discarded }, jsonOptions);
            }))
            .WithName("DiscardProviderTemplateSetup").RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static async Task<IResult> ExecuteAsync(HttpContext context, Func<string, string, Task<IResult>> operation)
    {
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        if (!tenant.HasTenantScope || !EndpointAuthorization.HasPermission(context, Permissions) ||
            !EndpointAuthorization.TryResolveActor(context, out var actor))
            return EndpointHelpers.Forbidden();
        try
        {
            return await operation(tenant.TenantId!, actor).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
        catch (ProviderTemplateCompatibilityException exception)
        {
            return Results.BadRequest(new
            {
                error = "The provider template is incompatible.",
                issues = exception.Issues,
                errors = exception.Issues.GroupBy(issue => issue.Field)
                    .ToDictionary(field => field.Key, field => field.Select(issue => issue.Message).ToArray())
            });
        }
        catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
    }
}
