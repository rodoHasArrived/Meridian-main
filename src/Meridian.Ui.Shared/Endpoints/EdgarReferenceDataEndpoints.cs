using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Endpoints for EDGAR reference-data ingest and local partition reads.
/// </summary>
public static class EdgarReferenceDataEndpoints
{
    public static void MapEdgarReferenceDataEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(string.Empty).WithTags("EDGAR");

        group.MapPost(UiApiRoutes.SecurityMasterEdgarIngest, async (
            EdgarIngestRequest request,
            [FromServices] IEdgarIngestOrchestrator orchestrator,
            CancellationToken ct) =>
        {
            var result = await orchestrator.IngestAsync(request, ct).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("IngestEdgarSecurityMaster")
        .Accepts<EdgarIngestRequest>("application/json")
        .Produces<EdgarIngestResult>(StatusCodes.Status200OK);

        group.MapGet(UiApiRoutes.ReferenceDataEdgarFiler, async (
            string cik,
            [FromServices] IEdgarReferenceDataStore store,
            CancellationToken ct) =>
        {
            var record = await store.LoadFilerAsync(cik, ct).ConfigureAwait(false);
            return record is null
                ? Results.NotFound()
                : Results.Json(record, jsonOptions);
        })
        .WithName("GetEdgarFiler")
        .Produces<EdgarFilerRecord>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(UiApiRoutes.ReferenceDataEdgarFacts, async (
            string cik,
            [FromServices] IEdgarReferenceDataStore store,
            CancellationToken ct) =>
        {
            var record = await store.LoadFactsAsync(cik, ct).ConfigureAwait(false);
            return record is null
                ? Results.NotFound()
                : Results.Json(record, jsonOptions);
        })
        .WithName("GetEdgarFacts")
        .Produces<EdgarFactsRecord>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(UiApiRoutes.ReferenceDataEdgarSecurityData, async (
            string cik,
            [FromServices] IEdgarReferenceDataStore store,
            CancellationToken ct) =>
        {
            var record = await store.LoadSecurityDataAsync(cik, ct).ConfigureAwait(false);
            return record is null
                ? Results.NotFound()
                : Results.Json(record, jsonOptions);
        })
        .WithName("GetEdgarSecurityData")
        .Produces<EdgarSecurityDataRecord>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);
    }
}
