using System.Text.Json;
using Meridian.Application.CryptoCurrency;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Contracts.CryptoCurrency;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

public static class CryptoReferenceEndpoints
{
    public static void MapCryptoReferenceEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(string.Empty).WithTags("CryptoReference");
        group.AddEndpointFilter(EndpointAuthorization.RequireAny(
            UserPermission.ViewSecurityMaster,
            UserPermission.ModifySecurityMaster));

        group.MapGet(UiApiRoutes.ReferenceDataCryptoReference, async (
            Guid securityId,
            [FromServices] ICryptoReferenceService service,
            CancellationToken ct) =>
        {
            var reference = await service.GetReferenceAsync(securityId, ct).ConfigureAwait(false);
            return reference is null ? Results.NotFound() : Results.Json(reference, jsonOptions);
        })
        .WithName("GetCryptoReference")
        .Produces<CryptoReferenceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(UiApiRoutes.ReferenceDataCryptoByNetwork, async (
            [FromQuery] string network,
            [FromServices] ICryptoReferenceService service,
            CancellationToken ct) =>
        {
            var results = await service.GetByNetworkAsync(network, ct).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetCryptoByNetwork")
        .Produces<IReadOnlyList<CryptoReferenceDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapGet(UiApiRoutes.ReferenceDataCryptoByBaseCurrency, async (
            [FromQuery] string baseCurrency,
            [FromServices] ICryptoReferenceService service,
            CancellationToken ct) =>
        {
            var results = await service.GetByBaseCurrencyAsync(baseCurrency, ct).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetCryptoByBaseCurrency")
        .Produces<IReadOnlyList<CryptoReferenceDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);
    }
}
