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
        .Produces(400)
        .Produces(404);

        group.MapGet("/subjects/{subjectKind}/{subjectId}/graph", async (
            string subjectKind,
            string subjectId,
            HttpContext context) =>
        {
            var service = context.RequestServices.GetRequiredService<EvidenceGraphService>();
            if (!service.IsSupportedSubjectKind(subjectKind))
            {
                return Results.BadRequest(new { error = $"Evidence subject kind '{subjectKind}' is not supported." });
            }

            var graph = await service.GetGraphAsync(subjectKind, subjectId, context.RequestAborted).ConfigureAwait(false);
            return graph is null
                ? Results.NotFound(new { error = $"Evidence subject '{subjectKind}/{subjectId}' was not found." })
                : Results.Json(graph, jsonOptions);
        })
        .WithName("GetWorkstationEvidenceGraph")
        .Produces<EvidenceGraphDto>(200)
        .Produces(400)
        .Produces(404);

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
        .Produces(400)
        .Produces(404);

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

            var request = await ReadExportRequestAsync(context).ConfigureAwait(false);
            var store = context.RequestServices.GetRequiredService<IEvidenceArtifactStore>();
            var response = await store
                .WriteManifestAsync(packetResult.Packet, request, context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(response, jsonOptions);
        })
        .WithName("ExportWorkstationEvidenceManifest")
        .Produces<EvidencePacketExportResponse>(200)
        .Produces(400)
        .Produces(404);

        group.MapGet("/templates", (HttpContext context) =>
        {
            var registry = context.RequestServices.GetRequiredService<EvidenceTemplateRegistry>();
            return Results.Json(registry.GetTemplates(), jsonOptions);
        })
        .WithName("GetWorkstationEvidenceTemplates")
        .Produces<IReadOnlyList<EvidenceTemplateDto>>(200);

        return app;
    }

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
            return (null, Results.BadRequest(new { error = $"Evidence subject kind '{subjectKind}' is not supported." }));
        }

        var packet = await service.GetPacketAsync(subjectKind, subjectId, context.RequestAborted).ConfigureAwait(false);
        return packet is null
            ? (null, Results.NotFound(new { error = $"Evidence subject '{subjectKind}/{subjectId}' was not found." }))
            : (packet, Results.Empty);
    }

    private static async Task<EvidencePacketExportRequest> ReadExportRequestAsync(HttpContext context)
    {
        if (context.Request.ContentLength == 0)
        {
            return new EvidencePacketExportRequest(null, null);
        }

        var request = await context.Request.ReadFromJsonAsync<EvidencePacketExportRequest>(
            cancellationToken: context.RequestAborted).ConfigureAwait(false);
        return request ?? new EvidencePacketExportRequest(null, null);
    }
}
