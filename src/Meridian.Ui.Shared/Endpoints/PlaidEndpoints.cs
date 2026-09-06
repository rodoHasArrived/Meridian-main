using System.Text;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Integrity;
using Meridian.Identity.Auth;
using Meridian.Contracts.Plaid;
using Meridian.Ui.Shared.Services;
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
        .WithName("ListPlaidItems").RequireAnyPermission(UserPermission.ManageCredentials, UserPermission.ViewTrades, UserPermission.ViewDirectLending)
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
        .WithName("ListPlaidAccounts").RequireAnyPermission(UserPermission.ManageCredentials, UserPermission.ViewTrades, UserPermission.ViewDirectLending)
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
        .WithName("SearchPlaidInstitutions").RequireAnyPermission(UserPermission.ManageCredentials, UserPermission.ViewTrades, UserPermission.ViewDirectLending)
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

            var trusted = request with { UserId = ResolveActor(context) };
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
        .WithName("CreatePlaidLinkToken").RequireAnyPermission(UserPermission.ManageCredentials, UserPermission.ManageDirectLending, UserPermission.AdminMaintenance)
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

            var trusted = request with { RequestedBy = ResolveActor(context) };
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
        .WithName("ExchangePlaidPublicToken").RequireAnyPermission(UserPermission.ManageCredentials, UserPermission.ManageDirectLending, UserPermission.AdminMaintenance)
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
                RequestedBy = ResolveActor(context)
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
        .WithName("SyncPlaidItem").RequireAnyPermission(UserPermission.ManageCredentials, UserPermission.ManageDirectLending, UserPermission.AdminMaintenance)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces<PlaidSyncResult>(StatusCodes.Status200OK);

        group.MapPost(UiApiRoutes.PlaidWebhook, async (
            HttpContext context,
            IPlaidIngestionService service,
            PlaidOptions options) =>
        {
            // Read the raw bytes rather than a bound JsonElement: the signature covers exactly
            // what Plaid sent, and re-serializing a parsed document would verify a different
            // byte sequence than the one that was signed.
            context.Request.EnableBuffering();
            using var bodyBuffer = new MemoryStream();
            await context.Request.Body.CopyToAsync(bodyBuffer, context.RequestAborted).ConfigureAwait(false);
            var rawBody = bodyBuffer.ToArray();

            var verification = PlaidWebhookVerifier.Verify(
                options,
                context.Request.Headers["Plaid-Verification"].ToString(),
                rawBody,
                DateTimeOffset.UtcNow);
            if (verification != PlaidWebhookVerifier.VerificationOutcome.Verified)
            {
                // Deliberately uniform and detail-free: telling an unauthenticated caller which
                // check failed helps it iterate towards a forgery.
                return ApiProblemDetails.Unauthorized(context, "The webhook signature could not be verified.");
            }

            JsonElement body;
            try
            {
                body = JsonDocument.Parse(rawBody).RootElement.Clone();
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "The webhook payload was not valid JSON." });
            }

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
                PayloadHash: Sha256Digest.ComputeUtf8(raw));
            var result = await service.RecordWebhookAsync(webhook, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("RecordPlaidWebhook")
        .DeclareIndependentAuthentication(
            "Inbound Plaid callback, authenticated by verifying the ES256 Plaid-Verification " +
            "header and the signed body hash against the received bytes; the caller is Plaid, " +
            "never the ambient operator principal, and carries no session to hold a permission.")
        .Produces<PlaidWebhookEventDto>(StatusCodes.Status200OK);

        group.MapPost(UiApiRoutes.PlaidSandboxTransfer, async (
            PlaidTransferRequest request,
            HttpContext context,
            IPlaidTransferService service) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ManageDirectLending) || !EndpointAuthorization.TryResolveActor(context, out _))
            {
                return EndpointHelpers.Forbidden();
            }

            var trusted = request with { RequestedBy = ResolveActor(context) };
            var result = await service.CreateSandboxTransferAsync(trusted, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions, statusCode: result.Status == PlaidTransferStatusDto.Created
                ? StatusCodes.Status201Created
                : StatusCodes.Status409Conflict);
        })
        .WithName("CreatePlaidSandboxTransfer").RequirePermission(UserPermission.ManageDirectLending)
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
        => EndpointAuthorization.TryResolveActor(context, out _) && EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.ManageCredentials,
            UserPermission.ManageDirectLending,
            UserPermission.AdminMaintenance);

    private static string ResolveActor(HttpContext context)
        => EndpointAuthorization.TryResolveActor(context, out var actor)
            ? actor
            : throw new InvalidOperationException("An authenticated Plaid operator is required.");

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
        var hash = Sha256Digest.ComputeUtf8(payload);
        return $"{itemId}:{webhookType}:{webhookCode}:{hash[..16]}";
    }
}
