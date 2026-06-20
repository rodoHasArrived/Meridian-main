using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Evidence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static class EvidenceEndpoints
{
    public static WebApplication MapEvidenceEndpoints(
        this WebApplication app,
        JsonSerializerOptions jsonOptions)
    {
        app.MapGet("/workstation/evidence/{subjectKind}/{subjectId}/{fileName}", async (
            string subjectKind,
            string subjectId,
            string fileName,
            HttpContext context) =>
        {
            var store = context.RequestServices.GetRequiredService<IEvidenceArtifactStore>();
            var manifest = await store
                .TryOpenManifestAsync(subjectKind, subjectId, fileName, context.RequestAborted)
                .ConfigureAwait(false);
            return manifest is null
                ? Results.NotFound(Error(
                    "evidence-manifest-not-found",
                    $"Evidence manifest '{subjectKind}/{subjectId}/{fileName}' was not found.",
                    subjectKind,
                    subjectId,
                    fileName: fileName))
                : Results.File(
                    manifest.Content,
                    manifest.ContentType,
                    manifest.FileName,
                    manifest.LastModified,
                    enableRangeProcessing: true);
        })
        .WithName("GetWorkstationEvidenceManifest")
        .Produces(200, contentType: "application/json")
        .Produces<EvidenceEndpointErrorDto>(404);

        app.MapGet("/workstation/evidence/vault/{vaultId}", async (
            string vaultId,
            HttpContext context) =>
        {
            var store = context.RequestServices.GetRequiredService<IEvidenceArtifactStore>();
            var manifest = await store
                .TryOpenManifestByVaultIdAsync(vaultId, context.RequestAborted)
                .ConfigureAwait(false);
            return manifest is null
                ? Results.NotFound(Error(
                    "evidence-vault-manifest-not-found",
                    $"Evidence vault manifest '{vaultId}' was not found.",
                    vaultId: vaultId))
                : Results.File(
                    manifest.Content,
                    manifest.ContentType,
                    manifest.FileName,
                    manifest.LastModified,
                    enableRangeProcessing: true);
        })
        .WithName("GetWorkstationEvidenceVaultManifest")
        .Produces(200, contentType: "application/json")
        .Produces<EvidenceEndpointErrorDto>(404);

        var group = app.MapGroup("/api/workstation/evidence");

        group.MapGet("/subjects", async (HttpContext context) =>
        {
            var service = context.RequestServices.GetRequiredService<EvidenceGraphService>();
            var subjects = await service.ListSubjectsAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(subjects, jsonOptions);
        })
        .WithName("GetWorkstationEvidenceSubjects")
        .Produces<IReadOnlyList<EvidenceSubjectDto>>(200);

        group.MapGet("/subjects/{subjectKind}/{subjectId}/packet", async (
            string subjectKind,
            string subjectId,
            HttpContext context) =>
        {
            var result = await ResolvePacketAsync(subjectKind, subjectId, context, jsonOptions).ConfigureAwait(false);
            return result;
        })
        .WithName("GetWorkstationEvidencePacket")
        .Produces<EvidencePacketDto>(200)
        .Produces<EvidenceEndpointErrorDto>(400)
        .Produces<EvidenceEndpointErrorDto>(404);

        group.MapGet("/subjects/{subjectKind}/{subjectId}/graph", async (
            string subjectKind,
            string subjectId,
            HttpContext context) =>
        {
            var service = context.RequestServices.GetRequiredService<EvidenceGraphService>();
            if (!service.IsSupportedSubjectKind(subjectKind))
            {
                return Results.BadRequest(Error(
                    "unsupported-evidence-subject-kind",
                    $"Evidence subject kind '{subjectKind}' is not supported.",
                    subjectKind,
                    subjectId));
            }

            var ledgerBookId = ResolveLedgerBookId(context);
            var graph = await service.GetGraphAsync(subjectKind, subjectId, context.RequestAborted, ledgerBookId).ConfigureAwait(false);
            return graph is null
                ? Results.NotFound(Error(
                    "evidence-subject-not-found",
                    $"Evidence subject '{subjectKind}/{subjectId}' was not found.",
                    subjectKind,
                    subjectId))
                : Results.Json(graph, jsonOptions);
        })
        .WithName("GetWorkstationEvidenceGraph")
        .Produces<EvidenceGraphDto>(200)
        .Produces<EvidenceEndpointErrorDto>(400)
        .Produces<EvidenceEndpointErrorDto>(404);

        group.MapPost("/subjects/{subjectKind}/{subjectId}/validate", async (
            string subjectKind,
            string subjectId,
            HttpContext context) =>
        {
            var packetResult = await ResolvePacketForMutationAsync(subjectKind, subjectId, context).ConfigureAwait(false);
            return packetResult.Packet is null
                ? packetResult.Result
                : Results.Json(packetResult.Packet.Completeness, jsonOptions);
        })
        .WithName("ValidateWorkstationEvidencePacket")
        .Produces<EvidenceCompletenessDto>(200)
        .Produces<EvidenceEndpointErrorDto>(400)
        .Produces<EvidenceEndpointErrorDto>(404);

        group.MapPost("/subjects/{subjectKind}/{subjectId}/export-manifest", async (
            string subjectKind,
            string subjectId,
            HttpContext context) =>
        {
            var packetResult = await ResolvePacketForMutationAsync(subjectKind, subjectId, context).ConfigureAwait(false);
            if (packetResult.Packet is null)
            {
                return packetResult.Result;
            }

            var requestResult = await ReadExportRequestAsync(context, subjectKind, subjectId).ConfigureAwait(false);
            if (requestResult.Error is not null)
            {
                return requestResult.Error;
            }

            var store = context.RequestServices.GetRequiredService<IEvidenceArtifactStore>();
            var response = await store
                .WriteManifestAsync(packetResult.Packet, requestResult.Request, context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(response, jsonOptions);
        })
        .WithName("ExportWorkstationEvidenceManifest")
        .Produces<EvidencePacketExportResponse>(200)
        .Produces<EvidenceEndpointErrorDto>(400)
        .Produces<EvidenceEndpointErrorDto>(404)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet("/templates", (HttpContext context) =>
        {
            var registry = context.RequestServices.GetRequiredService<EvidenceTemplateRegistry>();
            return Results.Json(registry.GetTemplates(), jsonOptions);
        })
        .WithName("GetWorkstationEvidenceTemplates")
        .Produces<IReadOnlyList<EvidenceTemplateDto>>(200);

        group.MapPost("/vault/intake", async (EvidenceVaultIntakeRequestDto? request, HttpContext context) =>
        {
            if (request is null)
            {
                return Results.BadRequest(Error(
                    "invalid-evidence-vault-intake",
                    "Evidence vault intake request body must be a valid JSON object."));
            }

            try
            {
                var store = context.RequestServices.GetRequiredService<IEvidenceArtifactStore>();
                var result = await store
                    .WriteIntakeArtifactAsync(request, context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(Error(
                    "invalid-evidence-vault-intake",
                    ex.Message,
                    request.SubjectKind,
                    request.SubjectId));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(Error(
                    "invalid-evidence-vault-intake",
                    ex.Message,
                    request.SubjectKind,
                    request.SubjectId));
            }
        })
        .WithName("IntakeWorkstationEvidenceVaultArtifact")
        .Produces<EvidenceVaultIntakeResponseDto>(StatusCodes.Status201Created)
        .Produces<EvidenceEndpointErrorDto>(StatusCodes.Status400BadRequest)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost("/vault/search", async (EvidenceVaultLookupRequestDto request, HttpContext context) =>
        {
            if (!HasLookupCriteria(request))
            {
                return Results.BadRequest(Error(
                    "invalid-evidence-vault-lookup",
                    "Evidence vault search requires at least one lookup field."));
            }

            var store = context.RequestServices.GetRequiredService<IEvidenceArtifactStore>();
            var result = await store.FindByLinkageAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("SearchWorkstationEvidenceVault")
        .Produces<IReadOnlyList<EvidenceVaultIdentityDto>>(200)
        .Produces<EvidenceEndpointErrorDto>(400);

        group.MapGet("/vault/request-lists", async (
            string? requestListKind,
            string? targetKind,
            string? targetId,
            string? status,
            string? subjectKind,
            string? subjectId,
            int? maxResults,
            HttpContext context) =>
        {
            if (maxResults.HasValue && maxResults.Value <= 0)
            {
                return Results.BadRequest(Error(
                    "invalid-evidence-vault-request-list-query",
                    "Evidence vault request-list query maxResults must be greater than zero."));
            }

            var store = context.RequestServices.GetRequiredService<IEvidenceArtifactStore>();
            var result = await store.ListRequestListsAsync(
                new EvidenceVaultRequestListQueryDto(
                    RequestListKind: requestListKind,
                    TargetKind: targetKind,
                    TargetId: targetId,
                    Status: status,
                    SubjectKind: subjectKind,
                    SubjectId: subjectId,
                    MaxResults: maxResults),
                context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("ListWorkstationEvidenceVaultRequestLists")
        .Produces<IReadOnlyList<EvidenceVaultRequestListEntryDto>>(200)
        .Produces<EvidenceEndpointErrorDto>(400);

        return app;
    }

    private static bool HasLookupCriteria(EvidenceVaultLookupRequestDto request)
        => !string.IsNullOrWhiteSpace(request.EvidenceSubject)
           || !string.IsNullOrWhiteSpace(request.RunId)
           || !string.IsNullOrWhiteSpace(request.PeriodId)
           || !string.IsNullOrWhiteSpace(request.ReportPackId)
           || !string.IsNullOrWhiteSpace(request.ReconciliationCaseId)
           || !string.IsNullOrWhiteSpace(request.AccountingRecordId)
           || !string.IsNullOrWhiteSpace(request.ReportPackDeliveryAttemptId)
           || !string.IsNullOrWhiteSpace(request.ReportPackDeliveryPackageId);

    private static async Task<IResult> ResolvePacketAsync(
        string subjectKind,
        string subjectId,
        HttpContext context,
        JsonSerializerOptions jsonOptions)
    {
        var packetResult = await ResolvePacketForMutationAsync(subjectKind, subjectId, context).ConfigureAwait(false);
        return packetResult.Packet is null
            ? packetResult.Result
            : Results.Json(packetResult.Packet, jsonOptions);
    }

    private static async Task<(EvidencePacketDto? Packet, IResult Result)> ResolvePacketForMutationAsync(
        string subjectKind,
        string subjectId,
        HttpContext context)
    {
        var service = context.RequestServices.GetRequiredService<EvidenceGraphService>();
        if (!service.IsSupportedSubjectKind(subjectKind))
        {
            return (null, Results.BadRequest(Error(
                "unsupported-evidence-subject-kind",
                $"Evidence subject kind '{subjectKind}' is not supported.",
                subjectKind,
                subjectId)));
        }

        var ledgerBookId = ResolveLedgerBookId(context);
        var packet = await service.GetPacketAsync(subjectKind, subjectId, context.RequestAborted, ledgerBookId).ConfigureAwait(false);
        return packet is null
            ? (null, Results.NotFound(Error(
                "evidence-subject-not-found",
                $"Evidence subject '{subjectKind}/{subjectId}' was not found.",
                subjectKind,
                subjectId)))
            : (packet, Results.Empty);
    }

    private static Guid? ResolveLedgerBookId(HttpContext context)
        => Guid.TryParse(context.Request.Query["ledgerBookId"].FirstOrDefault(), out var ledgerBookId)
            ? ledgerBookId
            : null;

    private static EvidenceEndpointErrorDto Error(
        string code,
        string message,
        string? subjectKind = null,
        string? subjectId = null,
        string? fileName = null,
        string? vaultId = null)
        => new(code, message, subjectKind, subjectId, fileName, vaultId);

    private static async Task<(EvidencePacketExportRequest Request, IResult? Error)> ReadExportRequestAsync(
        HttpContext context,
        string subjectKind,
        string subjectId)
    {
        if (context.Request.ContentLength == 0)
        {
            return (new EvidencePacketExportRequest(null, null), null);
        }

        try
        {
            var request = await context.Request.ReadFromJsonAsync<EvidencePacketExportRequest>(
                cancellationToken: context.RequestAborted).ConfigureAwait(false);
            return (request ?? new EvidencePacketExportRequest(null, null), null);
        }
        catch (JsonException)
        {
            return (new EvidencePacketExportRequest(null, null), Results.BadRequest(Error(
                "invalid-evidence-export-request",
                "Evidence export request body must be a valid JSON object.",
                subjectKind,
                subjectId)));
        }
        catch (BadHttpRequestException)
        {
            return (new EvidencePacketExportRequest(null, null), Results.BadRequest(Error(
                "invalid-evidence-export-request",
                "Evidence export request body must be a valid JSON object.",
                subjectKind,
                subjectId)));
        }
    }
}
