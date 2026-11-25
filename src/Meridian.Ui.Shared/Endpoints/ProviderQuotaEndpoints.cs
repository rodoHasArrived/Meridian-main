using System.Text.Json;
using Meridian.Application.Backfill;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.ProviderSdk;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

internal static class ProviderQuotaEndpoints
{
    private const UserPermission EditPermissions = UserPermission.ManageProviders | UserPermission.ModifyConfig;

    internal static void MapProviderQuotaEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Providers");
        group.MapGet(UiApiRoutes.ProviderQuota, (string providerId, HttpContext context,
            [FromServices] ConfigStore store, [FromServices] ProviderRegistry? registry, [FromServices] BackfillRoutingRuntime? routing) =>
        {
            var response = BuildResponse(providerId.Trim().ToLowerInvariant(), store.Load(),
                store.GetChangeStatus(), HistoricalAdapters(registry, routing),
                EndpointAuthorization.HasPermission(context, EditPermissions));
            return response is null
                ? ApiProblemDetails.NotFound(context, "Unknown provider.")
                : Results.Json(response, jsonOptions);
        })
        .WithName("GetProviderQuota")
        .WithDescription("Returns saved account limits and the observed running provider budget.")
        .Produces<ProviderQuotaResponse>()
        .ProducesProblem(404)
        .RequireAnyPermission(UserPermission.ViewConfig, UserPermission.ViewDiagnostics,
            UserPermission.ManageProviders, UserPermission.AdminMaintenance);

        group.MapPut(UiApiRoutes.ProviderQuota, async (string providerId, ProviderQuotaEditDto request,
            HttpContext context, [FromServices] ConfigStore store, [FromServices] ProviderRegistry? registry,
            [FromServices] BackfillRoutingRuntime? routing, CancellationToken ct) =>
        {
            var id = providerId.Trim().ToLowerInvariant();
            if (!ProviderQuotaCatalog.IsEditable(id))
                return ApiProblemDetails.NotFound(context, "This provider has no editable account quota.");
            try
            {
                var saved = await store.UpdateAsync(config =>
                {
                    var updated = ProviderQuotaCatalog.Apply(config, id, request);
                    return (updated, updated);
                }, ct).ConfigureAwait(false);
                var response = BuildResponse(id, saved, store.GetChangeStatus(),
                    HistoricalAdapters(registry, routing), canEdit: true);
                return Results.Json(response, jsonOptions);
            }
            catch (ArgumentException error)
            {
                return ApiProblemDetails.Validation(context, "quota", error.Message);
            }
            catch (UiConfigurationValidationException error)
            {
                return ApiProblemDetails.Validation(context, "configuration", error.Message);
            }
        })
        .WithName("UpdateProviderQuota")
        .WithDescription("Saves account quota and request pacing changes for the next host startup.")
        .Produces<ProviderQuotaResponse>()
        .ProducesProblem(404)
        .ProducesValidationProblem()
        .RequirePermission(EditPermissions)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static IReadOnlyList<IHistoricalDataProvider> HistoricalAdapters(ProviderRegistry? registry, BackfillRoutingRuntime? routing)
        => routing?.Current?.Providers.Select(entry => entry.Provider).ToArray() ?? registry?.GetBackfillProviders() ?? [];

    internal static ProviderQuotaResponse? BuildResponse(string id, AppConfig config, ConfigurationChangeStatus changes,
        IReadOnlyList<IHistoricalDataProvider> adapters, bool canEdit)
    {
        var descriptor = BackfillProviderCatalog.Providers.FirstOrDefault(candidate => candidate.Id == id);
        var adapter = adapters.FirstOrDefault(candidate => string.Equals(candidate.Name, id, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null && adapter is null && id != "fred")
            return null;

        var editable = ProviderQuotaCatalog.IsEditable(id);
        var saved = editable ? ProviderQuotaCatalog.Read(config, id) : null;
        var snapshot = (adapter as IProviderRateLimitDiagnosticsSource)?.GetRateLimitDiagnosticsSnapshot();
        var running = adapter is not null
            ? new ProviderQuotaRuntimeDto(snapshot?.MaxRequestsPerWindow ?? adapter.MaxRequestsPerWindow,
                (snapshot?.Window ?? adapter.RateLimitWindow).TotalSeconds, snapshot?.DailyMaximum, adapter.RateLimitDelay.TotalSeconds)
            : null;
        var quotaPath = id == ProviderQuotaCatalog.TwelveData ? "ProviderQuotas.TwelveData" : "ProviderQuotas.AlphaVantage";
        var backfillPath = id == ProviderQuotaCatalog.TwelveData ? "Backfill.Providers.TwelveData" : "Backfill.Providers.AlphaVantage";
        var pending = editable && changes.PendingChanges.Any(change =>
            change.Path.StartsWith(quotaPath + ".", StringComparison.OrdinalIgnoreCase)
            || change.Path.Equals(quotaPath, StringComparison.OrdinalIgnoreCase)
            || change.Path.Equals(backfillPath + ".RateLimitPerMinute", StringComparison.OrdinalIgnoreCase)
            || change.Path.Equals(backfillPath + ".TierId", StringComparison.OrdinalIgnoreCase));
        return new(id, descriptor?.DisplayName ?? adapter?.DisplayName ?? "FRED",
            id == ProviderQuotaCatalog.TwelveData ? "credits" : snapshot?.QuotaUnit ?? "requests",
            editable ? id == ProviderQuotaCatalog.TwelveData ? "utc-midnight" : "rolling-24-hours" : snapshot?.DailyResetPolicy,
            editable, editable && canEdit, editable ? "account-quota" : descriptor is null ? "adapter-fixed" : "backfill-settings",
            saved, running, pending, ProviderQuotaCatalog.GetTiers(id));
    }
}
