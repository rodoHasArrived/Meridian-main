using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Ui.Shared.Evidence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    // IB Flex XML exports routinely exceed the 5 MB data-upload cap; statements get their own limit.
    private const long StatementConnectorMaxFileBytes = 20 * 1024 * 1024;

    private static readonly string[] StatementConnectorAcceptedExtensions =
        [".csv", ".txt", ".ofx", ".qfx", ".xml", ".json"];

    private static void MapStatementConnectorEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        MapStatementToReportEndpoints(group, jsonOptions);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementConnectors), (
            HttpContext context,
            [FromServices] StatementConnectorRegistry? registry) =>
        {
            if (registry is null)
            {
                return StatementConnectorsNotRegistered();
            }

            var connectors = registry.List()
                .Select(static descriptor => new StatementConnectorDescriptorDto(
                    descriptor.ConnectorId,
                    descriptor.DisplayName,
                    descriptor.FileExtensions,
                    descriptor.SupportsFileImport,
                    descriptor.SupportsRemoteFetch,
                    descriptor.RequiresMappingProfile,
                    descriptor.DefaultProfileId))
                .ToArray();
            return Results.Json(connectors, jsonOptions);
        })
        .WithName("ListStatementConnectors")
        .Produces<IReadOnlyList<StatementConnectorDescriptorDto>>(200);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementMappingProfiles), async (
            HttpContext context,
            [FromServices] StatementMappingProfileCatalog? catalog) =>
        {
            if (catalog is null)
            {
                return StatementConnectorsNotRegistered();
            }

            var profiles = await catalog.ListAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(profiles.Select(ToProfileDto).ToArray(), jsonOptions);
        })
        .WithName("ListStatementMappingProfiles")
        .Produces<IReadOnlyList<StatementMappingProfileDto>>(200);

        group.MapPut(WorkstationSubroute(UiApiRoutes.ReconciliationStatementMappingProfiles), async (
            StatementMappingProfileDto request,
            HttpContext context,
            [FromServices] StatementMappingProfileCatalog? catalog) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (catalog is null)
            {
                return StatementConnectorsNotRegistered();
            }

            try
            {
                var saved = await catalog.UpsertAsync(ToProfileDocument(request), context.RequestAborted).ConfigureAwait(false);
                return Results.Json(ToProfileDto(saved), jsonOptions);
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
            {
                return MissingDataUploadPayload("profile", ex.Message);
            }
        })
        .WithName("UpsertStatementMappingProfile")
        .Produces<StatementMappingProfileDto>(200)
        .ProducesValidationProblem()
        .Produces(403)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapDelete(WorkstationSubroute(UiApiRoutes.ReconciliationStatementMappingProfileById), async (
            string profileId,
            HttpContext context,
            [FromServices] StatementMappingProfileCatalog? catalog) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (catalog is null)
            {
                return StatementConnectorsNotRegistered();
            }

            try
            {
                var deleted = await catalog.DeleteAsync(profileId, context.RequestAborted).ConfigureAwait(false);
                return deleted ? Results.NoContent() : Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return MissingDataUploadPayload("profileId", ex.Message);
            }
        })
        .WithName("DeleteStatementMappingProfile")
        .Produces(204)
        .Produces(403)
        .Produces(404)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationStatementImportPreview), async (
            HttpContext context,
            HttpRequest request,
            [FromServices] StatementImportService? importService) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (importService is null)
            {
                return StatementConnectorsNotRegistered();
            }

            var (document, connectorId, problem) = await ReadStatementDocumentAsync(request, context).ConfigureAwait(false);
            if (problem is not null)
            {
                return problem;
            }

            var preview = await importService.PreviewAsync(document!, connectorId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(preview, jsonOptions);
        })
        .WithName("PreviewStatementImport")
        .Produces<StatementImportPreviewDto>(200)
        .ProducesValidationProblem()
        .Produces(403)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationStatementImportCommit), async (
            HttpContext context,
            HttpRequest request,
            [FromServices] StatementImportService? importService,
            [FromServices] StatementImportEvidenceBridge? evidenceBridge) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return EndpointHelpers.Forbidden();
            }

            if (importService is null)
            {
                return StatementConnectorsNotRegistered();
            }

            var (document, connectorId, problem) = await ReadStatementDocumentAsync(request, context).ConfigureAwait(false);
            if (problem is not null)
            {
                return problem;
            }

            var form = await request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
            var sourceKind = form["sourceKind"].FirstOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(sourceKind))
            {
                sourceKind = "broker";
            }

            var sourceInstitution = form["sourceInstitution"].FirstOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(sourceInstitution))
            {
                return MissingDataUploadPayload("sourceInstitution", "Statement import requires the source institution (broker or custodian name).");
            }

            var fundAccountId = form["fundAccountId"].FirstOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(fundAccountId))
            {
                return MissingDataUploadPayload("fundAccountId", "Statement import requires the fund account id.");
            }

            var externalAccountId = form["externalAccountId"].FirstOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(externalAccountId))
            {
                return MissingDataUploadPayload("externalAccountId", "Statement import requires the external (broker/custodian) account id.");
            }

            if (!TryParseDataUploadDate(form["periodStart"].FirstOrDefault() ?? string.Empty, out var periodStart))
            {
                return MissingDataUploadPayload("periodStart", "Statement period start must use YYYY-MM-DD format.");
            }

            if (!TryParseDataUploadDate(form["periodEnd"].FirstOrDefault() ?? string.Empty, out var periodEnd))
            {
                return MissingDataUploadPayload("periodEnd", "Statement period end must use YYYY-MM-DD format.");
            }

            if (periodEnd < periodStart)
            {
                return MissingDataUploadPayload("periodEnd", "Statement period end must be on or after the period start.");
            }

            try
            {
                var result = await importService.CommitAsync(
                        new StatementImportCommitRequest(
                            document!,
                            connectorId,
                            sourceKind,
                            sourceInstitution,
                            fundAccountId,
                            externalAccountId,
                            periodStart,
                            periodEnd,
                            form["toleranceProfileId"].FirstOrDefault()?.Trim(),
                            currentUser),
                        context.RequestAborted)
                    .ConfigureAwait(false);
                if (evidenceBridge is not null)
                {
                    result = await evidenceBridge.RetainAsync(
                            result,
                            new StatementImportEvidenceBridgeRequest(
                                sourceKind,
                                sourceInstitution,
                                fundAccountId,
                                externalAccountId,
                                periodStart,
                                periodEnd,
                                currentUser),
                            context.RequestAborted)
                        .ConfigureAwait(false);
                }

                return Results.Json(result, jsonOptions);
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            {
                return MissingDataUploadPayload("statement", ex.Message);
            }
        })
        .WithName("CommitStatementImport")
        .Produces<StatementImportCommitResultDto>(200)
        .ProducesValidationProblem()
        .Produces(403)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationStatementFetchPreview), async (
            StatementFetchPreviewRequest request,
            HttpContext context,
            [FromServices] StatementImportService? importService) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (importService is null)
            {
                return StatementConnectorsNotRegistered();
            }

            if (string.IsNullOrWhiteSpace(request.ConnectorId) || string.IsNullOrWhiteSpace(request.ExternalAccountId))
            {
                return MissingDataUploadPayload("connectorId", "Fetch preview requires a connector id and an external account id.");
            }

            try
            {
                var document = await importService.FetchDocumentAsync(
                        new StatementFetchRequest(
                            request.ConnectorId,
                            request.ExternalAccountId,
                            request.Since,
                            request.MappingProfileId,
                            ParseFetchDatasets(request.Datasets)),
                        context.RequestAborted)
                    .ConfigureAwait(false);
                var preview = await importService.PreviewAsync(document, request.ConnectorId, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(preview, jsonOptions);
            }
            catch (NotSupportedException ex)
            {
                return MissingDataUploadPayload("connectorId", ex.Message);
            }
        })
        .WithName("PreviewStatementFetch")
        .Produces<StatementImportPreviewDto>(200)
        .ProducesValidationProblem()
        .Produces(403)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementFetchSchedules), async (
            HttpContext context,
            [FromServices] IStatementFetchScheduleStore? scheduleStore) =>
        {
            if (scheduleStore is null)
            {
                return StatementConnectorsNotRegistered();
            }

            var schedules = await scheduleStore.ListAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(schedules.Select(ToScheduleDto).ToArray(), jsonOptions);
        })
        .WithName("ListStatementFetchSchedules")
        .Produces<IReadOnlyList<StatementFetchScheduleDto>>(200);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationStatementFetchSchedules), async (
            StatementFetchScheduleUpsertRequestDto request,
            HttpContext context,
            [FromServices] IStatementFetchScheduleStore? scheduleStore) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (scheduleStore is null)
            {
                return StatementConnectorsNotRegistered();
            }

            try
            {
                var saved = await scheduleStore.UpsertAsync(
                        new StatementFetchSchedule(
                            request.ScheduleId ?? string.Empty,
                            request.ConnectorId,
                            request.ExternalAccountId,
                            request.FundAccountId,
                            request.SourceInstitution,
                            request.MappingProfileId,
                            string.IsNullOrWhiteSpace(request.ToleranceProfileId)
                                ? "statement-default"
                                : request.ToleranceProfileId,
                            request.CadenceHours,
                            request.Enabled,
                            SourceKind: string.IsNullOrWhiteSpace(request.SourceKind)
                                ? "broker"
                                : request.SourceKind),
                        context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(ToScheduleDto(saved), jsonOptions);
            }
            catch (InvalidDataException ex)
            {
                return MissingDataUploadPayload("schedule", ex.Message);
            }
        })
        .WithName("UpsertStatementFetchSchedule")
        .Produces<StatementFetchScheduleDto>(200)
        .ProducesValidationProblem()
        .Produces(403)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapDelete(WorkstationSubroute(UiApiRoutes.ReconciliationStatementFetchScheduleById), async (
            string scheduleId,
            HttpContext context,
            [FromServices] IStatementFetchScheduleStore? scheduleStore) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (scheduleStore is null)
            {
                return StatementConnectorsNotRegistered();
            }

            var deleted = await scheduleStore.DeleteAsync(scheduleId, context.RequestAborted).ConfigureAwait(false);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteStatementFetchSchedule")
        .Produces(204)
        .Produces(403)
        .Produces(404)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationStatementFetchScheduleRun), async (
            string scheduleId,
            HttpContext context,
            [FromServices] StatementFetchScheduleRunner? runner,
            [FromServices] IStatementFetchScheduleStore? scheduleStore,
            [FromServices] StatementImportEvidenceBridge? evidenceBridge) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (runner is null)
            {
                return StatementConnectorsNotRegistered();
            }

            var nowUtc = DateTimeOffset.UtcNow;
            var schedule = scheduleStore is null
                ? null
                : (await scheduleStore.ListAsync(context.RequestAborted).ConfigureAwait(false))
                    .FirstOrDefault(candidate => string.Equals(candidate.ScheduleId, scheduleId?.Trim(), StringComparison.OrdinalIgnoreCase));
            var since = schedule?.LastRunAtUtc ?? nowUtc.AddHours(-Math.Max(1, schedule?.CadenceHours ?? 24));
            var result = await runner.RunScheduleAsync(scheduleId, nowUtc, context.RequestAborted).ConfigureAwait(false);
            if (result is not null && schedule is not null && evidenceBridge is not null)
            {
                result = await evidenceBridge.RetainAsync(
                        result,
                        new StatementImportEvidenceBridgeRequest(
                            SourceKind: "broker",
                            SourceInstitution: schedule.SourceInstitution,
                            FundAccountId: schedule.FundAccountId,
                            ExternalAccountId: schedule.ExternalAccountId,
                            PeriodStart: DateOnly.FromDateTime(since.UtcDateTime),
                            PeriodEnd: DateOnly.FromDateTime(nowUtc.UtcDateTime),
                            ImportedBy: "statement-fetch-scheduler"),
                        context.RequestAborted)
                    .ConfigureAwait(false);
            }

            return result is null
                ? Results.NotFound()
                : Results.Json(result, jsonOptions);
        })
        .WithName("RunStatementFetchSchedule")
        .Produces<StatementImportCommitResultDto>(200)
        .Produces(403)
        .Produces(404)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private sealed record StatementFetchPreviewRequest(
        string ConnectorId,
        string ExternalAccountId,
        DateTimeOffset? Since = null,
        string? MappingProfileId = null,
        string? Datasets = null);

    private static async Task<(StatementSourceDocument? Document, string? ConnectorId, IResult? Problem)> ReadStatementDocumentAsync(
        HttpRequest request,
        HttpContext context)
    {
        if (!request.HasFormContentType)
        {
            return (null, null, MissingDataUploadPayload("contentType", "Statement import requires multipart/form-data."));
        }

        var form = await request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
        {
            return (null, null, MissingDataUploadPayload("file", "Choose a non-empty statement file."));
        }

        if (file.Length > StatementConnectorMaxFileBytes)
        {
            return (null, null, MissingDataUploadPayload(
                "file",
                $"Statement import accepts files up to {FormatBytes(StatementConnectorMaxFileBytes)}."));
        }

        if (!HasAcceptedDataUploadExtension(file.FileName, StatementConnectorAcceptedExtensions))
        {
            return (null, null, MissingDataUploadPayload(
                "file",
                $"Statement import accepts {string.Join(", ", StatementConnectorAcceptedExtensions)} files."));
        }

        byte[] fileBytes;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream((int)file.Length))
        {
            await stream.CopyToAsync(buffer, context.RequestAborted).ConfigureAwait(false);
            fileBytes = buffer.ToArray();
        }

        var connectorId = form["connectorId"].FirstOrDefault()?.Trim();
        var document = new StatementSourceDocument(
            file.FileName,
            fileBytes,
            form["mappingProfileId"].FirstOrDefault()?.Trim(),
            form["externalAccountId"].FirstOrDefault()?.Trim());
        return (document, string.IsNullOrWhiteSpace(connectorId) ? null : connectorId, null);
    }

    private static StatementFetchDatasets ParseFetchDatasets(string? datasets) =>
        datasets?.Trim().ToLowerInvariant() switch
        {
            "activity" => StatementFetchDatasets.Activity,
            "positions" => StatementFetchDatasets.Positions,
            _ => StatementFetchDatasets.All
        };

    private static IResult StatementConnectorsNotRegistered()
        => Results.Problem(
            "Statement connector services are not registered.",
            statusCode: StatusCodes.Status501NotImplemented);

    private static StatementMappingProfileDto ToProfileDto(StatementMappingProfileDocument document)
        => new(
            document.SchemaVersion,
            document.ProfileId,
            document.DisplayName,
            document.Format,
            document.Csv is null ? null : new StatementMappingProfileCsvOptionsDto(document.Csv.Delimiter, document.Csv.Quote, document.Csv.HasHeader),
            document.Culture,
            document.DateFormats,
            document.Fields
                .Select(static field => new StatementMappingProfileFieldDto(field.CanonicalField, field.SourceColumn, field.Aliases, field.Required))
                .ToArray(),
            (document.ActivityCodes ?? [])
                .Select(static code => new StatementMappingProfileActivityCodeDto(code.SourceCode, code.CanonicalActivityType))
                .ToArray(),
            document.LastAcceptedFingerprint,
            document.IsBuiltIn,
            document.Notes);

    private static StatementMappingProfileDocument ToProfileDocument(StatementMappingProfileDto dto)
        => new(
            dto.SchemaVersion,
            dto.ProfileId,
            dto.DisplayName,
            dto.Format,
            dto.Csv is null ? null : new StatementProfileCsvOptions(dto.Csv.Delimiter, dto.Csv.Quote, dto.Csv.HasHeader),
            dto.Culture,
            dto.DateFormats,
            (dto.Fields ?? [])
                .Select(static field => new StatementProfileFieldMapping(field.CanonicalField, field.SourceColumn, field.Aliases, field.Required))
                .ToArray(),
            (dto.ActivityCodes ?? [])
                .Select(static code => new StatementProfileActivityCode(code.SourceCode, code.CanonicalActivityType))
                .ToArray(),
            dto.LastAcceptedFingerprint,
            IsBuiltIn: false,
            dto.Notes);

    private static StatementFetchScheduleDto ToScheduleDto(StatementFetchSchedule schedule)
        => new(
            schedule.ScheduleId,
            schedule.ConnectorId,
            schedule.ExternalAccountId,
            schedule.FundAccountId,
            schedule.SourceInstitution,
            schedule.MappingProfileId,
            schedule.ToleranceProfileId,
            schedule.CadenceHours,
            schedule.Enabled,
            schedule.LastRunAtUtc,
            schedule.LastRunStatus,
            schedule.NextDueAtUtc,
            schedule.SourceKind);
}
