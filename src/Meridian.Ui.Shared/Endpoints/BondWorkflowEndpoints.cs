using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Api;
using Meridian.Contracts.FixedIncome;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>Shared retained-input bond analysis. A calculation never grants financial write authority.</summary>
public static class BondWorkflowEndpoints
{
    internal static readonly JsonSerializerOptions Json = CreateJson();

    public static void MapBondWorkflowEndpoints(this WebApplication app)
    {
        var read = app.MapGroup(string.Empty)
            .RequireAuthenticatedSession().RequireWorkstationTenantScope()
            .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster);
        read.MapPost(UiApiRoutes.ReferenceDataBondWorkflow, async (Guid securityId, HttpContext context) =>
            await ExecuteAsync(async () =>
            {
                var request = await BodyAsync<BondWorkflowRequest>(context).ConfigureAwait(false);
                if (request.AccountId is not null)
                    return EndpointHelpers.Forbidden();
                if (request.SecurityId != Guid.Empty && request.SecurityId != securityId)
                    return Results.BadRequest(new { error = "Route and requested security must agree." });
                var result = await context.RequestServices.GetRequiredService<IBondWorkflowService>()
                    .CalculateAsync(request with { SecurityId = securityId }, context.RequestAborted).ConfigureAwait(false);
                return result is null ? Results.NotFound() : Results.Json(result, Json);
            }).ConfigureAwait(false)).WithName("CalculateRetainedBondWorkflow");
        read.MapGet(UiApiRoutes.ReferenceDataBondMarketObservations, async (HttpContext context, string? kind, string? seriesKey,
            Guid? securityId, DateOnly? economicDate, DateOnly? fromDate, DateOnly? throughDate, DateTimeOffset? knownAt) =>
            await ExecuteAsync(async () =>
            {
                BondMarketObservationKind? category = null;
                if (kind is not null)
                {
                    if (!Enum.TryParse<BondMarketObservationKind>(kind, true, out var parsed) || !Enum.IsDefined(parsed))
                        return Results.BadRequest(new { error = "Unknown bond observation kind." });
                    category = parsed;
                }
                var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var rows = await context.RequestServices.GetRequiredService<IBondMarketDataService>().QueryAsync(
                    tenant.TenantId!, new(category, seriesKey, securityId, economicDate, fromDate, throughDate, knownAt),
                    context.RequestAborted).ConfigureAwait(false);
                return Results.Json(rows, Json);
            }).ConfigureAwait(false)).WithName("QueryBondMarketObservations");
        read.MapPost(UiApiRoutes.ReferenceDataBondMarketObservations, async Task<IResult> (HttpContext context) =>
            await ExecuteAsync(async () =>
            {
                if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                    return EndpointHelpers.Forbidden();
                var request = await BodyAsync<BondMarketImportRequest>(context).ConfigureAwait(false);
                var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                return Results.Json(await context.RequestServices.GetRequiredService<IBondMarketDataService>()
                    .ImportAsync(tenant.TenantId!, actor, request, context.RequestAborted).ConfigureAwait(false), Json);
            }).ConfigureAwait(false))
            .RequireAnyPermission(UserPermission.ModifySecurityMaster)
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy).WithName("ImportBondMarketObservations");
        read.MapGet(UiApiRoutes.ReferenceDataBondConnectionReadiness, async (HttpContext context, string? accountId, Guid? securityId) =>
            await ExecuteAsync(async () => Results.Json(await context.RequestServices
                .GetRequiredService<BondWorkflowConnectionReadinessService>().GetAsync(
                    HttpContextWorkstationTenantContextAccessor.Resolve(context).TenantId!, accountId, securityId,
                    context.RequestAborted).ConfigureAwait(false), Json)).ConfigureAwait(false))
            .WithName("GetBondConnectionReadiness");
        read.MapPost(UiApiRoutes.ReferenceDataBondScreen, async Task<IResult> (HttpContext context) =>
            await ExecuteAsync(async () => Results.Json(await context.RequestServices
                .GetRequiredService<IBondScreeningService>().ScreenAsync(
                    HttpContextWorkstationTenantContextAccessor.Resolve(context).TenantId!,
                    await BodyAsync<BondScreeningQuery>(context).ConfigureAwait(false), context.RequestAborted)
                .ConfigureAwait(false), Json)).ConfigureAwait(false)).WithName("ScreenBondReference");
        read.MapPost(UiApiRoutes.ReferenceDataBondIdentifierDiscovery, async Task<IResult> (HttpContext context) =>
            await ExecuteAsync(async () => Results.Json(await context.RequestServices.GetRequiredService<BondProviderIntakeService>()
                .DiscoverIdentifierAsync(await BodyAsync<BondIdentifierDiscoveryRequest>(context).ConfigureAwait(false),
                    context.RequestAborted).ConfigureAwait(false), Json)).ConfigureAwait(false))
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy).WithName("DiscoverBondIdentifier");
        read.MapPost(UiApiRoutes.ReferenceDataBondFredImport, async Task<IResult> (HttpContext context) =>
            await ExecuteAsync(async () =>
            {
                var scope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                if (string.IsNullOrWhiteSpace(scope.CompanyId) || !EndpointAuthorization.TryResolveActor(context, out var actor))
                    return EndpointHelpers.Forbidden();
                return Results.Json(await context.RequestServices.GetRequiredService<BondProviderIntakeService>()
                    .ImportFredAsync(scope.TenantId!, scope.CompanyId, actor,
                        await BodyAsync<BondFredIntakeRequest>(context).ConfigureAwait(false), context.RequestAborted).ConfigureAwait(false), Json);
            }).ConfigureAwait(false)).RequireWorkstationTenantCompanyScope()
            .RequireAnyPermission(UserPermission.ModifySecurityMaster).RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
            .WithName("ImportReviewedBondFredObservations");
        read.MapGet(UiApiRoutes.ReferenceDataBondProviderResponse, async (string contentSha256, HttpContext context) =>
            await ExecuteAsync(async () =>
            {
                var scope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                if (string.IsNullOrWhiteSpace(scope.CompanyId))
                    return EndpointHelpers.Forbidden();
                var response = await context.RequestServices.GetRequiredService<BondProviderIntakeService>()
                    .GetProviderResponseAsync(scope.TenantId!, scope.CompanyId, contentSha256, context.RequestAborted).ConfigureAwait(false);
                return response is null ? Results.NotFound() : Results.Json(response, Json);
            }).ConfigureAwait(false)).RequireWorkstationTenantCompanyScope().WithName("GetRetainedBondProviderResponse");
        read.MapPost(UiApiRoutes.ReferenceDataBondComparisonPreview, async Task<IResult> (HttpContext context) =>
            await ExecuteAsync(async () =>
            {
                var request = await BodyAsync<BondComparisonRequest>(context).ConfigureAwait(false);
                if (request.Bonds?.Any(row => row?.AccountId is not null) == true)
                    return EndpointHelpers.Forbidden();
                return Results.Json(await context.RequestServices.GetRequiredService<IBondComparisonService>()
                    .CompareAsync(request, context.RequestAborted).ConfigureAwait(false), Json);
            }).ConfigureAwait(false)).WithName("PreviewBondComparison");
    }

    internal static async Task<T> BodyAsync<T>(HttpContext context) =>
        await context.Request.ReadFromJsonAsync<T>(Json, context.RequestAborted).ConfigureAwait(false)
        ?? throw new ArgumentException("A workflow request body is required.");

    internal static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        { return await action().ConfigureAwait(false); }
        catch (JsonException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (ArgumentException ex) { return Results.UnprocessableEntity(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (InvalidDataException) { return Results.Problem("Retained bond evidence is corrupt or unavailable.", statusCode: 503); }
        catch (TimeoutException) { return Results.Problem("Retained bond evidence is locked; retry shortly.", statusCode: 503); }
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = UiEndpoints.CreateEndpointJsonOptions();
        options.NumberHandling = JsonNumberHandling.AllowReadingFromString;
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new ExactDecimalConverter());
        return options;
    }

    private sealed class ExactDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var numeric) ? numeric
            : reader.TokenType == JsonTokenType.String && decimal.TryParse(reader.GetString(),
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var text) ? text
            : throw new JsonException("An exact decimal amount is required.");
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
