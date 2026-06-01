using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Auth;
using Meridian.Contracts.Plaid;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

public static class PlaidEndpoints
{
    public static void MapPlaidEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Plaid");

        group.MapGet(UiApiRoutes.PlaidItems, async (HttpContext context, IPlaidIngestionService service) =>
        {
            if (!HasPlaidReadAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var items = await service.ListItemsAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(items, jsonOptions);
        })
        .WithName("ListPlaidItems")
        .Produces<IReadOnlyList<PlaidItemDto>>(StatusCodes.Status200OK);

        group.MapGet(UiApiRoutes.PlaidAccounts, async (string? itemId, HttpContext context, IPlaidIngestionService service) =>
        {
            if (!HasPlaidReadAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var accounts = await service.ListAccountsAsync(itemId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(accounts, jsonOptions);
        })
        .WithName("ListPlaidAccounts")
        .Produces<IReadOnlyList<PlaidAccountDto>>(StatusCodes.Status200OK);

        group.MapGet(UiApiRoutes.PlaidInstitutionSearch, async (
            string query,
            string? products,
            string? countryCodes,
            HttpContext context,
            IPlaidIngestionService service) =>
        {
            if (!HasPlaidReadAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            try
            {
                var result = await service.SearchInstitutionsAsync(
                        query,
                        ParseProducts(products),
                        ParseCsv(countryCodes),
                        context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        })
        .WithName("SearchPlaidInstitutions")
        .Produces<PlaidInstitutionSearchResult>(StatusCodes.Status200OK);

        group.MapPost(UiApiRoutes.PlaidLinkToken, async (
            PlaidLinkTokenRequest request,
            HttpContext context,
            IPlaidIngestionService service) =>
        {
            if (!HasPlaidMutationAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var trusted = request with { UserId = ResolveActor(context, request.UserId) };
            try
            {
                var result = await service.CreateLinkTokenAsync(trusted, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        })
        .WithName("CreatePlaidLinkToken")
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces<PlaidLinkTokenResponse>(StatusCodes.Status200OK);

        group.MapPost(UiApiRoutes.PlaidPublicTokenExchange, async (
            PlaidPublicTokenExchangeRequest request,
            HttpContext context,
            IPlaidIngestionService service) =>
        {
            if (!HasPlaidMutationAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var trusted = request with { RequestedBy = ResolveActor(context, request.RequestedBy) };
            try
            {
                var result = await service.ExchangePublicTokenAsync(trusted, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        })
        .WithName("ExchangePlaidPublicToken")
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces<PlaidPublicTokenExchangeResult>(StatusCodes.Status201Created);

        group.MapPost(UiApiRoutes.PlaidItemSync, async (
            string itemId,
            PlaidSyncRequest request,
            HttpContext context,
            IPlaidIngestionService service) =>
        {
            if (!HasPlaidMutationAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var trusted = request with
            {
                ItemId = itemId,
                RequestedBy = ResolveActor(context, request.RequestedBy)
            };
            try
            {
                var result = await service.SyncItemAsync(trusted, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        })
        .WithName("SyncPlaidItem")
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces<PlaidSyncResult>(StatusCodes.Status200OK);

        group.MapPost(UiApiRoutes.PlaidWebhook, async (
            JsonElement body,
            HttpContext context,
            IPlaidIngestionService service) =>
        {
            var itemId = GetString(body, "item_id") ?? GetString(body, "itemId") ?? "unknown";
            var webhookType = GetString(body, "webhook_type") ?? GetString(body, "webhookType") ?? "unknown";
            var webhookCode = GetString(body, "webhook_code") ?? GetString(body, "webhookCode") ?? "unknown";
            var raw = body.GetRawText();
            var webhook = new PlaidWebhookEventDto(
                BuildWebhookEventId(itemId, webhookType, webhookCode, raw),
                itemId,
                webhookType,
                webhookCode,
                DateTimeOffset.UtcNow,
                PayloadHash: Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant());
            var result = await service.RecordWebhookAsync(webhook, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("RecordPlaidWebhook")
        .Produces<PlaidWebhookEventDto>(StatusCodes.Status200OK);

        group.MapPost(UiApiRoutes.PlaidSandboxTransfer, async (
            PlaidTransferRequest request,
            HttpContext context,
            IPlaidTransferService service) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ManageDirectLending))
            {
                return EndpointHelpers.Forbidden();
            }

            var trusted = request with { RequestedBy = ResolveActor(context, request.RequestedBy) };
            var result = await service.CreateSandboxTransferAsync(trusted, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions, statusCode: result.Status == PlaidTransferStatusDto.Created
                ? StatusCodes.Status201Created
                : StatusCodes.Status409Conflict);
        })
        .WithName("CreatePlaidSandboxTransfer")
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces<PlaidTransferResult>(StatusCodes.Status201Created)
        .Produces<PlaidTransferResult>(StatusCodes.Status409Conflict);
    }

    private static bool HasPlaidReadAccess(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.ManageCredentials,
            UserPermission.ViewTrades,
            UserPermission.ViewDirectLending);

    private static bool HasPlaidMutationAccess(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.ManageCredentials,
            UserPermission.ManageDirectLending,
            UserPermission.AdminMaintenance);

    private static string ResolveActor(HttpContext context, string? fallback)
        => context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserKey, out var user) &&
           user is string value &&
           !string.IsNullOrWhiteSpace(value)
            ? value
            : string.IsNullOrWhiteSpace(fallback) ? "plaid-endpoint" : fallback;

    private static string? GetString(JsonElement body, string propertyName)
        => body.ValueKind == JsonValueKind.Object &&
           body.TryGetProperty(propertyName, out var property) &&
           property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static IReadOnlyList<string> ParseCsv(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static IReadOnlyList<PlaidProductDto> ParseProducts(string? value)
        => ParseCsv(value)
            .Select(static product => Enum.TryParse<PlaidProductDto>(product, ignoreCase: true, out var parsed)
                ? (PlaidProductDto?)parsed
                : null)
            .Where(static product => product is not null)
            .Select(static product => product!.Value)
            .Distinct()
            .ToArray();

    private static string BuildWebhookEventId(string itemId, string webhookType, string webhookCode, string payload)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return $"{itemId}:{webhookType}:{webhookCode}:{hash[..16]}";
    }
}
