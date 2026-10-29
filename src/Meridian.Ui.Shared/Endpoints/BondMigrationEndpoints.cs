using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>Reviewed migration preparation and an administrative live-calculation cutover.</summary>
public static class BondMigrationEndpoints
{
    public static void MapBondMigrationEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(string.Empty).WithTags("BondMigration").RequireWorkstationTenantScope();
        group.MapPost(UiApiRoutes.ReferenceDataBondMigrationPreview, (
            BondMigrationPreviewRequest request, [FromServices] IBondMigrationService service, CancellationToken ct) =>
            ExecuteAsync(async () => Results.Json(await service.PreviewAsync(request, ct).ConfigureAwait(false), jsonOptions)))
            .WithName("PreviewBondMigration")
            .RequireAnyPermission(UserPermission.ModifySecurityMaster);

        group.MapPost(UiApiRoutes.ReferenceDataBondMigrationBatchPreview, (
            BondMigrationBatchPreviewRequest request, [FromServices] IBondMigrationService service, CancellationToken ct) =>
            ExecuteAsync(async () =>
            {
                if (request.Bonds is null || request.Bonds.Count is < 1 or > 100)
                    return Results.BadRequest(new { error = "Supply between 1 and 100 bond previews." });
                if (request.Bonds.Select(static row => row.SecurityId).Distinct().Count() != request.Bonds.Count)
                    return Results.BadRequest(new { error = "Each bond may appear once in a batch." });
                var previews = new List<BondMigrationPreviewDto>(request.Bonds.Count);
                foreach (var bond in request.Bonds)
                {
                    ct.ThrowIfCancellationRequested();
                    previews.Add(await service.PreviewAsync(bond, ct).ConfigureAwait(false));
                }
                return Results.Json(previews, jsonOptions);
            }))
            .WithName("PreviewBondMigrationBatch")
            .RequireAnyPermission(UserPermission.ModifySecurityMaster);

        group.MapPost(UiApiRoutes.ReferenceDataBondMigrationSubmit, (
            BondMigrationSubmitRequest request, HttpContext context,
            [FromServices] IBondMigrationService service,
            [FromServices] ISecurityMasterWorkbenchCommandService workbench, CancellationToken ct) =>
            ExecuteAsync(async () =>
            {
                if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                    return Results.Unauthorized();
                // Recompute on the server: browser decimal display must never round-trip reviewed economics.
                var preview = await service.PreviewAsync(request.Request, ct).ConfigureAwait(false);
                if (!Sha256Digest.FixedEquals(preview.PreviewSha256, request.PreviewSha256)
                    || !Sha256Digest.FixedEquals(preview.SecurityContentSha256, request.SecurityContentSha256))
                    return Results.Conflict(new { error = "Migration inputs changed after review; refresh the preview." });
                var edit = await service.PrepareEditAsync(preview, actor, request.Justification, ct).ConfigureAwait(false);
                // This creates a governed draft. Existing independent approval and publish remain required.
                var result = await workbench.UpdateSecurityFieldAsync(edit, ct).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }))
            .WithName("StageBondMigration")
            .RequireAnyPermission(UserPermission.ModifySecurityMaster);

        group.MapGet(UiApiRoutes.ReferenceDataBondCutoverReadiness, (
            [FromServices] IBondMigrationService service, CancellationToken ct) =>
            ExecuteAsync(async () => Results.Json(await service.GetInventoryAsync(ct).ConfigureAwait(false), jsonOptions)))
            .WithName("GetBondCutoverReadiness")
            .RequireAnyPermission(UserPermission.AdminMaintenance);

        group.MapPost(UiApiRoutes.ReferenceDataBondCutover, (
            BondMigrationCutoverRequest request, HttpContext context,
            [FromServices] IBondMigrationService service, CancellationToken ct) =>
            ExecuteAsync(async () =>
            {
                if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                    return Results.Unauthorized();
                var receipt = await service.CutoverAsync(request with { Reviewer = actor }, ct).ConfigureAwait(false);
                return Results.Json(receipt, jsonOptions);
            }))
            .WithName("EnableBondCalculationCutover")
            .RequireAnyPermission(UserPermission.AdminMaintenance);
    }

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        { return await action().ConfigureAwait(false); }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
    }
}
