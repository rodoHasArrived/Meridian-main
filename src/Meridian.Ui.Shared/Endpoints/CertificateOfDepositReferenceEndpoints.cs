using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.CertificatesOfDeposit;
using Meridian.Identity.Auth;
using Meridian.Instruments.CertificatesOfDeposit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

public static class CertificateOfDepositReferenceEndpoints
{
    public static void MapCertificateOfDepositReferenceEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(string.Empty).WithTags("CertificateOfDepositReference");

        group.MapGet(UiApiRoutes.ReferenceDataCertificateOfDepositReference, async (
            Guid securityId,
            [FromServices] ICertificateOfDepositReferenceService service,
            CancellationToken ct) =>
        {
            var reference = await service.GetReferenceAsync(securityId, ct).ConfigureAwait(false);
            return reference is null ? Results.NotFound() : Results.Json(reference, jsonOptions);
        })
        .WithName("GetCertificateOfDepositReference").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<CertificateOfDepositReferenceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(UiApiRoutes.ReferenceDataCertificatesOfDepositByIssuer, async (
            [FromQuery] string issuerName,
            [FromServices] ICertificateOfDepositReferenceService service,
            CancellationToken ct) =>
        {
            var results = await service.GetByIssuerAsync(issuerName, ct).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetCertificatesOfDepositByIssuer").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<IReadOnlyList<CertificateOfDepositReferenceDto>>(StatusCodes.Status200OK);

        group.MapGet(UiApiRoutes.ReferenceDataCertificatesOfDepositMaturingBefore, async (
            [FromQuery] DateOnly beforeDate,
            [FromServices] ICertificateOfDepositReferenceService service,
            CancellationToken ct) =>
        {
            var results = await service.GetMaturingBeforeAsync(beforeDate, ct).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetCertificatesOfDepositMaturingBefore").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<IReadOnlyList<CertificateOfDepositReferenceDto>>(StatusCodes.Status200OK);
    }
}
