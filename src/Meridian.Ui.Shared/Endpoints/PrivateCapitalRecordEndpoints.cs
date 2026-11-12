using System.Text.Json;
using Meridian.Contracts.PrivateCapital;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.PrivateCapital;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>Explicit source-record capture; accepting records grants no accounting authority.</summary>
public static class PrivateCapitalRecordEndpoints
{
    public static void MapPrivateCapitalRecordEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/private-capital").WithTags("Private capital records");
        group.MapGet("/records", (HttpContext context, [FromServices] PrivateCapitalRecordService service, CancellationToken ct) =>
            ExecuteAsync(async () =>
            {
                var records = await service.ReadAsync(Scope(context), ct).ConfigureAwait(false);
                var holders = new HashSet<Guid>();
                foreach (var holder in records.Commitments.Select(row => row.HolderEntityId).Distinct())
                    if (await CanReadHolderAsync(context, holder, ct).ConfigureAwait(false))
                        holders.Add(holder);
                var commitments = records.Commitments.Where(row => holders.Contains(row.HolderEntityId)).ToArray();
                var ids = commitments.Select(row => row.CommitmentId).ToHashSet();
                return Results.Json(new
                {
                    Commitments = commitments,
                    Snapshots = records.Snapshots.Where(row => ids.Contains(row.CommitmentId)).ToArray(),
                    records.GeneratedAt,
                    CanManage = EndpointAuthorization.HasPermission(context, UserPermission.ManagePrivateCapitalRecords)
                }, jsonOptions);
            })).RequireAnyPermission(UserPermission.ViewPrivateCapitalRecords, UserPermission.ManagePrivateCapitalRecords)
            .RequireWorkstationTenantCompanyScope().WithName("GetPrivateCapitalRecords");
        group.MapGet("/choices", (HttpContext context, [FromServices] PrivateCapitalRecordService service, CancellationToken ct) =>
            ExecuteAsync(async () =>
            {
                var choices = await service.ChoicesAsync(Scope(context), ct).ConfigureAwait(false);
                var entities = new List<PrivateCapitalChoice>();
                foreach (var entity in choices.Entities)
                    if (await EntityAllowedAsync(context, entity.Id, UserPermission.ManagePrivateCapitalRecords, ct).ConfigureAwait(false))
                        entities.Add(entity);
                return Results.Json(new PrivateCapitalChoices(entities, entities.Count > 0 ? choices.Vehicles : []), jsonOptions);
            }))
            .RequirePermission(UserPermission.ManagePrivateCapitalRecords).RequireWorkstationTenantCompanyScope().WithName("GetPrivateCapitalRecordChoices");
        group.MapGet("/imports/{batchId:guid}", (Guid batchId, HttpContext context,
            [FromServices] PrivateCapitalRecordService service, CancellationToken ct) =>
            ExecuteAsync(async () =>
            {
                var inspection = await service.InspectImportAsync(Scope(context), batchId, ct).ConfigureAwait(false);
                if (inspection is null)
                    return Results.NotFound();
                await RequireRowsAllowedAsync(context, service, inspection.Preview.Rows, write: false, ct).ConfigureAwait(false);
                return Results.Json(inspection, jsonOptions);
            }))
            .RequireAnyPermission(UserPermission.ViewPrivateCapitalRecords, UserPermission.ManagePrivateCapitalRecords)
            .RequireWorkstationTenantCompanyScope().WithName("InspectPrivateCapitalImport");
        group.MapPost("/commitments", (CreatePrivateCapitalCommitment request, HttpContext context,
            [FromServices] PrivateCapitalRecordService service, CancellationToken ct) =>
            ExecuteAsync(async () =>
            {
                if (!await EntityAllowedAsync(context, request.HolderEntityId, UserPermission.ManagePrivateCapitalRecords, ct).ConfigureAwait(false))
                    throw new UnauthorizedAccessException("The holder entity is not authorized for private-capital capture.");
                return Results.Json(await service.CreateAsync(Scope(context), Actor(context), request, ct).ConfigureAwait(false), jsonOptions);
            }))
            .RequirePermission(UserPermission.ManagePrivateCapitalRecords).RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
            .RequireWorkstationTenantCompanyScope().WithName("CreatePrivateCapitalCommitment");
        group.MapPost("/preview", (PreviewPrivateCapitalImport request, HttpContext context,
            [FromServices] PrivateCapitalRecordService service, CancellationToken ct) =>
            ExecuteAsync(async () =>
            {
                if ((request.Rows is not null) == (request.Csv is not null))
                    throw new ArgumentException("Supply exactly one of rows or CSV.");
                var rows = request.Rows ?? PrivateCapitalCsv.Parse(request.Csv!);
                await RequireRowsAllowedAsync(context, service, rows, write: true, ct).ConfigureAwait(false);
                return Results.Json(await service.PreviewAsync(Scope(context), Actor(context), request, ct).ConfigureAwait(false), jsonOptions);
            }))
            .RequirePermission(UserPermission.ManagePrivateCapitalRecords).RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
            .RequireWorkstationTenantCompanyScope().WithName("PreviewPrivateCapitalImport");
        group.MapPost("/accept", (AcceptPrivateCapitalImport request, HttpContext context,
            [FromServices] PrivateCapitalRecordService service, CancellationToken ct) =>
            ExecuteAsync(async () =>
            {
                var inspection = await service.InspectImportAsync(Scope(context), request.BatchId, ct).ConfigureAwait(false);
                if (inspection is null)
                    return Results.NotFound();
                await RequireRowsAllowedAsync(context, service, inspection.Preview.Rows, write: true, ct).ConfigureAwait(false);
                return Results.Json(await service.AcceptAsync(Scope(context), Actor(context), request, ct).ConfigureAwait(false), jsonOptions);
            }))
            .RequirePermission(UserPermission.ManagePrivateCapitalRecords).RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
            .RequireWorkstationTenantCompanyScope().WithName("AcceptPrivateCapitalImport");
    }

    private static async Task<bool> EntityAllowedAsync(HttpContext context, Guid holder, UserPermission permission, CancellationToken ct)
        => holder != Guid.Empty && EndpointAuthorization.HasPermission(context, permission) &&
           (await EndpointAuthorization.AuthorizeScopedAsync(context, permission, AccessScopeKindDto.LegalEntity, holder, ct)
               .ConfigureAwait(false)).IsAllowed;

    private static async Task<bool> CanReadHolderAsync(HttpContext context, Guid holder, CancellationToken ct)
        => await EntityAllowedAsync(context, holder, UserPermission.ViewPrivateCapitalRecords, ct).ConfigureAwait(false) ||
           await EntityAllowedAsync(context, holder, UserPermission.ManagePrivateCapitalRecords, ct).ConfigureAwait(false);

    private static async Task RequireRowsAllowedAsync(HttpContext context, PrivateCapitalRecordService service,
        IReadOnlyList<PrivateCapitalSnapshotInput> rows, bool write, CancellationToken ct)
    {
        if (rows.Count is < 1 or > 500 || rows.Any(row => row is null))
            throw new ArgumentException("Supply between one and 500 valid observations.");
        var records = await service.ReadAsync(Scope(context), ct).ConfigureAwait(false);
        var commitments = records.Commitments.ToDictionary(row => row.CommitmentId);
        var checkedHolders = new HashSet<Guid>();
        foreach (var id in rows.Select(row => row.CommitmentId).Distinct())
        {
            if (!commitments.TryGetValue(id, out var commitment))
                throw new UnauthorizedAccessException("The observation's commitment is unavailable in the authorized scope.");
            if (!checkedHolders.Add(commitment.HolderEntityId))
                continue;
            var allowed = write
                ? await EntityAllowedAsync(context, commitment.HolderEntityId, UserPermission.ManagePrivateCapitalRecords, ct).ConfigureAwait(false)
                : await CanReadHolderAsync(context, commitment.HolderEntityId, ct).ConfigureAwait(false);
            if (!allowed)
                throw new UnauthorizedAccessException("Private-capital observations for this holder are not authorized.");
        }
    }

    private static PrivateCapitalScope Scope(HttpContext context)
    {
        var scope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        if (string.IsNullOrWhiteSpace(scope.TenantId) || string.IsNullOrWhiteSpace(scope.CompanyId))
            throw new UnauthorizedAccessException("Authenticated tenant and company scope are required.");
        return new(scope.TenantId, scope.CompanyId);
    }

    private static string Actor(HttpContext context) => EndpointAuthorization.TryResolveActor(context, out var actor)
        ? actor : throw new UnauthorizedAccessException("An authenticated operator is required.");

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        { return await action().ConfigureAwait(false); }
        catch (UnauthorizedAccessException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
        catch (ArgumentException exception) { return Results.BadRequest(new { code = "PrivateCapitalInputInvalid", error = EndpointHelpers.RefusalDetail(exception) }); }
        catch (InvalidOperationException exception) { return Results.Conflict(new { code = "PrivateCapitalConflict", error = EndpointHelpers.RefusalDetail(exception) }); }
    }
}
