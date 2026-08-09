using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    // IB Flex XML exports routinely exceed the 5 MB data-upload cap; statements get their own limit,
    // shared with the CLI import/validate commands so the two paths cannot drift.
    private const long StatementConnectorMaxFileBytes = StatementConnectorLimits.MaxFileBytes;

    private static readonly string[] StatementConnectorAcceptedExtensions =
        [".csv", ".txt", ".ofx", ".qfx", ".xml", ".json", ".bai", ".bai2", ".camt", ".053"];

    private static void MapStatementConnectorEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationMarginControl), async (
            HttpContext context,
            [FromServices] Meridian.Ui.Shared.Services.MarginControlCenterReadService? marginControl) =>
        {
            if (marginControl is null)
                return StatementConnectorsNotRegistered();

            var result = await marginControl.GetAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetMarginControlCenter")
        .Produces<MarginControlCenterDto>(200);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationMarginCertifications), async (
            MarginCertificationRequestDto request,
            HttpContext context,
            [FromServices] Meridian.Ui.Shared.Services.MarginControlCenterReadService? marginControl) =>
        {
            if (!HasReconciliationMutationPermission(context))
                return EndpointHelpers.Forbidden();
            if (marginControl is null)
                return StatementConnectorsNotRegistered();

            try
            {
                var result = await marginControl.CertifyAsync(
                    request,
                    ResolveCurrentActor(context),
                    context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidDataException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["note"] = [ex.Message] });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("CertifyMarginAccountSnapshot")
        .Produces<MarginCertificationResultDto>(200)
        .ProducesValidationProblem()
        .Produces(403)
        .Produces(404)
        .Produces(409)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        MapStatementReconciliationReportEndpoints(group, jsonOptions);

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

            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (string.IsNullOrWhiteSpace(tenant.TenantId)
                || string.IsNullOrWhiteSpace(tenant.CompanyId))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!request.HasFormContentType)
            {
                return MissingDataUploadPayload("contentType", "Statement import requires multipart/form-data.");
            }

            var form = await request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
            if (!TryReadStatementReconciliationReportScope(form, out var scope, out var validationProblem))
            {
                return validationProblem!;
            }

            var ownershipProblem = await RequireStatementReconciliationReportAccountOwnershipAsync(
                    scope,
                    tenant,
                    context)
                .ConfigureAwait(false);
            if (ownershipProblem is not null)
            {
                return ownershipProblem;
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
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .RequireWorkstationTenantCompanyScope();

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationStatementImportCommit), async (
            HttpContext context,
            HttpRequest request,
            [FromServices] StatementReconciliationReportWorkflowService? workflowService,
            [FromServices] IStatementReconciliationIntakeAuthority? intakeAuthority) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return EndpointHelpers.Forbidden();
            }

            if (workflowService is null || intakeAuthority is null)
            {
                return StatementReconciliationReportNotRegistered();
            }

            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (string.IsNullOrWhiteSpace(tenant.TenantId)
                || string.IsNullOrWhiteSpace(tenant.CompanyId))
            {
                return EndpointHelpers.Forbidden();
            }

            var form = await request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
            if (!TryReadStatementReconciliationReportScope(form, out var scope, out var validationProblem))
            {
                return validationProblem!;
            }

            var ownershipProblem = await RequireStatementReconciliationReportAccountOwnershipAsync(
                    scope,
                    tenant,
                    context)
                .ConfigureAwait(false);
            if (ownershipProblem is not null)
            {
                return ownershipProblem;
            }

            var (document, connectorId, problem) = await ReadStatementDocumentAsync(request, context).ConfigureAwait(false);
            if (problem is not null)
            {
                return problem;
            }

            try
            {
                var execution = await workflowService.StartAsync(
                        new StatementReconciliationReportStartCommand(
                            new StatementImportCommitRequest(
                                document!,
                                connectorId,
                                scope.SourceKind,
                                scope.SourceInstitution,
                                scope.FundAccountId,
                                scope.ExternalAccountId,
                                scope.PeriodStart,
                                scope.PeriodEnd,
                                scope.ToleranceProfileId,
                                currentUser)
                            {
                                AccountingScope = scope.AccountingScope
                            },
                            tenant.TenantId,
                            tenant.CompanyId),
                        context.RequestAborted)
                    .ConfigureAwait(false);
                if (execution.Workflow.Status == StatementReconciliationReportWorkflowStatusDto.Failed)
                {
                    return Results.Problem(
                        title: "Statement reconciliation report ingestion failed",
                        detail: execution.Workflow.FailureReason,
                        statusCode: StatusCodes.Status500InternalServerError);
                }

                if (execution.ImportResult is null
                    || execution.Workflow.OperationsWorkflowId is null
                    || execution.Workflow.AccountingScope is null)
                {
                    return Results.Problem(
                        title: "Statement reconciliation authority is unavailable",
                        detail: "The import was not confirmed through exact accounting scope, Operations Continuity, and canonical reconciliation casework.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                return Results.Json(
                    execution.ImportResult with
                    {
                        StatementReconciliationReportWorkflowId = execution.Workflow.WorkflowId,
                        StatementReconciliationReportStatusRoute = execution.Workflow.StatusRoute,
                        OperationsWorkflowId = execution.Workflow.OperationsWorkflowId,
                        AccountingScope = execution.Workflow.AccountingScope
                    },
                    jsonOptions);
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            {
                return MissingDataUploadPayload("statement", ex.Message);
            }
            catch (StatementReconciliationIntakeAuthorityException ex)
            {
                return Results.Problem(
                    title: "Statement accounting authority is unavailable",
                    detail: ex.Message,
                    statusCode: string.Equals(
                        ex.Code,
                        "STATEMENT_INTAKE_AUTHORITY_UNAVAILABLE",
                        StringComparison.Ordinal)
                        ? StatusCodes.Status503ServiceUnavailable
                        : StatusCodes.Status409Conflict);
            }
        })
        .WithName("CommitStatementImport")
        .Produces<StatementImportCommitResultDto>(200)
        .ProducesValidationProblem()
        .Produces(403)
        .Produces(409)
        .Produces(503)
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

            if (string.IsNullOrWhiteSpace(request.ConnectorId)
                || string.IsNullOrWhiteSpace(request.ExternalAccountId)
                || string.IsNullOrWhiteSpace(request.FundAccountId)
                || string.IsNullOrWhiteSpace(request.SourceInstitution))
            {
                return MissingDataUploadPayload(
                    "statementScope",
                    "Fetch preview requires a connector id, fund account id, source institution, and external account id.");
            }

            var sourceKind = string.IsNullOrWhiteSpace(request.SourceKind)
                ? "broker"
                : request.SourceKind.Trim().ToLowerInvariant();
            if (sourceKind is not ("broker" or "custodian"))
            {
                return MissingDataUploadPayload(
                    "sourceKind",
                    "Statement source kind must be broker or custodian.");
            }

            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (string.IsNullOrWhiteSpace(tenant.TenantId)
                || string.IsNullOrWhiteSpace(tenant.CompanyId))
            {
                return EndpointHelpers.Forbidden();
            }

            var scope = new StatementReconciliationReportScope(
                sourceKind,
                request.SourceInstitution,
                request.FundAccountId,
                request.ExternalAccountId,
                default,
                default,
                ToleranceProfileId: null,
                AccountingScope: null);
            var ownershipProblem = await RequireStatementReconciliationReportAccountOwnershipAsync(
                    scope,
                    tenant,
                    context)
                .ConfigureAwait(false);
            if (ownershipProblem is not null)
            {
                return ownershipProblem;
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
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
            {
                return Results.Problem(
                    title: "Statement fetch account scope mismatch",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status409Conflict);
            }
        })
        .WithName("PreviewStatementFetch")
        .Produces<StatementImportPreviewDto>(200)
        .ProducesValidationProblem()
        .Produces(403)
        .Produces(409)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .RequireWorkstationTenantCompanyScope();

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementFetchSchedules), async (
            HttpContext context,
            [FromServices] IStatementFetchScheduleStore? scheduleStore) =>
        {
            if (scheduleStore is null)
            {
                return StatementConnectorsNotRegistered();
            }

            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (string.IsNullOrWhiteSpace(tenant.TenantId)
                || string.IsNullOrWhiteSpace(tenant.CompanyId))
            {
                return EndpointHelpers.Forbidden();
            }

            var schedules = (await scheduleStore.ListAsync(context.RequestAborted).ConfigureAwait(false))
                .Where(schedule => IsStatementFetchScheduleOwnedBy(schedule, tenant))
                .ToArray();
            return Results.Json(schedules.Select(ToScheduleDto).ToArray(), jsonOptions);
        })
        .WithName("ListStatementFetchSchedules")
        .Produces<IReadOnlyList<StatementFetchScheduleDto>>(200);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationStatementFetchSchedules), async (
            StatementFetchScheduleUpsertRequestDto request,
            HttpContext context,
            [FromServices] IStatementFetchScheduleStore? scheduleStore,
            [FromServices] IStatementReconciliationIntakeAuthority? intakeAuthority) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (scheduleStore is null || intakeAuthority is null)
            {
                return StatementReconciliationReportNotRegistered();
            }

            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (string.IsNullOrWhiteSpace(tenant.TenantId)
                || string.IsNullOrWhiteSpace(tenant.CompanyId))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!request.PeriodStart.HasValue
                || !request.PeriodEnd.HasValue
                || request.PeriodEnd.Value < request.PeriodStart.Value)
            {
                return MissingDataUploadPayload(
                    "period",
                    "Statement fetch schedule requires an exact ledger period start and end.");
            }

            var sourceKind = string.IsNullOrWhiteSpace(request.SourceKind)
                ? "broker"
                : request.SourceKind.Trim();
            var endpointScope = new StatementReconciliationReportScope(
                sourceKind,
                request.SourceInstitution,
                request.FundAccountId,
                request.ExternalAccountId,
                request.PeriodStart.Value,
                request.PeriodEnd.Value,
                request.ToleranceProfileId,
                AccountingScope: null);
            var ownershipProblem = await RequireStatementReconciliationReportAccountOwnershipAsync(
                    endpointScope,
                    tenant,
                    context)
                .ConfigureAwait(false);
            if (ownershipProblem is not null)
            {
                return ownershipProblem;
            }

            if (!string.IsNullOrWhiteSpace(request.ScheduleId))
            {
                var existing = (await scheduleStore.ListAsync(context.RequestAborted).ConfigureAwait(false))
                    .FirstOrDefault(candidate => string.Equals(
                        candidate.ScheduleId,
                        request.ScheduleId.Trim(),
                        StringComparison.OrdinalIgnoreCase));
                if (existing is not null
                    && !IsStatementFetchScheduleOwnedBy(existing, tenant)
                    && !IsMatchingLegacyStatementFetchSchedule(existing, request))
                {
                    return Results.NotFound();
                }
            }

            try
            {
                var accountingScope = await intakeAuthority.ResolveAccountingScopeAsync(
                        new StatementReconciliationIntakeScopeRequest(
                            tenant.TenantId,
                            tenant.CompanyId,
                            request.FundAccountId,
                            request.ExternalAccountId,
                            request.SourceInstitution,
                            request.PeriodStart.Value,
                            request.PeriodEnd.Value),
                        context.RequestAborted)
                    .ConfigureAwait(false);
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
                            SourceKind: sourceKind,
                            TenantId: tenant.TenantId,
                            CompanyId: tenant.CompanyId,
                            PeriodStart: request.PeriodStart,
                            PeriodEnd: request.PeriodEnd,
                            AccountingScope: accountingScope),
                        context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(ToScheduleDto(saved), jsonOptions);
            }
            catch (InvalidDataException ex)
            {
                return MissingDataUploadPayload("schedule", ex.Message);
            }
            catch (StatementReconciliationIntakeAuthorityException ex)
            {
                return Results.Problem(
                    title: "Statement accounting authority is unavailable",
                    detail: ex.Message,
                    statusCode: string.Equals(
                        ex.Code,
                        "STATEMENT_INTAKE_AUTHORITY_UNAVAILABLE",
                        StringComparison.Ordinal)
                        ? StatusCodes.Status503ServiceUnavailable
                        : StatusCodes.Status409Conflict);
            }
        })
        .WithName("UpsertStatementFetchSchedule")
        .Produces<StatementFetchScheduleDto>(200)
        .ProducesValidationProblem()
        .Produces(403)
        .Produces(409)
        .Produces(503)
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

            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (string.IsNullOrWhiteSpace(tenant.TenantId)
                || string.IsNullOrWhiteSpace(tenant.CompanyId))
            {
                return EndpointHelpers.Forbidden();
            }

            var schedule = (await scheduleStore.ListAsync(context.RequestAborted).ConfigureAwait(false))
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.ScheduleId, scheduleId?.Trim(), StringComparison.OrdinalIgnoreCase)
                    && IsStatementFetchScheduleOwnedBy(candidate, tenant));
            if (schedule is null)
            {
                return Results.NotFound();
            }

            var deleted = await scheduleStore.DeleteAsync(schedule.ScheduleId, context.RequestAborted).ConfigureAwait(false);
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
            [FromServices] IStatementFetchScheduleStore? scheduleStore) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (runner is null || scheduleStore is null)
            {
                return StatementConnectorsNotRegistered();
            }

            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (string.IsNullOrWhiteSpace(tenant.TenantId)
                || string.IsNullOrWhiteSpace(tenant.CompanyId))
            {
                return EndpointHelpers.Forbidden();
            }

            var nowUtc = DateTimeOffset.UtcNow;
            var schedule = (await scheduleStore.ListAsync(context.RequestAborted).ConfigureAwait(false))
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.ScheduleId, scheduleId?.Trim(), StringComparison.OrdinalIgnoreCase)
                    && IsStatementFetchScheduleOwnedBy(candidate, tenant));
            if (schedule is null)
            {
                return Results.NotFound();
            }

            var result = await runner.RunScheduleAsync(schedule, nowUtc, context.RequestAborted).ConfigureAwait(false);
            if (result is null)
            {
                var failed = (await scheduleStore.ListAsync(context.RequestAborted).ConfigureAwait(false))
                    .FirstOrDefault(candidate =>
                        string.Equals(candidate.ScheduleId, schedule.ScheduleId, StringComparison.OrdinalIgnoreCase));
                return Results.Problem(
                    title: "Statement fetch ingestion failed",
                    detail: failed?.LastRunStatus
                            ?? "The scheduled statement was not accepted by the reconciliation report authority.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            return Results.Json(result, jsonOptions);
        })
        .WithName("RunStatementFetchSchedule")
        .Produces<StatementImportCommitResultDto>(200)
        .Produces(403)
        .Produces(404)
        .Produces(409)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private sealed record StatementFetchPreviewRequest(
        string ConnectorId,
        string ExternalAccountId,
        string FundAccountId,
        string SourceInstitution,
        string? SourceKind = "broker",
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
            schedule.SourceKind,
            schedule.PeriodStart,
            schedule.PeriodEnd,
            schedule.AccountingScope is null
                ? null
                : new StatementReconciliationAccountingScopeDto(
                    schedule.AccountingScope.FundProfileId,
                    schedule.AccountingScope.LedgerBookId,
                    schedule.AccountingScope.AccountingPeriodId,
                    schedule.AccountingScope.AsOfDate));

    private static bool IsStatementFetchScheduleOwnedBy(
        StatementFetchSchedule schedule,
        WorkstationTenantContext tenant)
        => !string.IsNullOrWhiteSpace(schedule.TenantId)
           && !string.IsNullOrWhiteSpace(schedule.CompanyId)
           && string.Equals(schedule.TenantId.Trim(), tenant.TenantId?.Trim(), StringComparison.OrdinalIgnoreCase)
           && string.Equals(schedule.CompanyId.Trim(), tenant.CompanyId?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsMatchingLegacyStatementFetchSchedule(
        StatementFetchSchedule schedule,
        StatementFetchScheduleUpsertRequestDto request)
        => string.IsNullOrWhiteSpace(schedule.TenantId)
           && string.IsNullOrWhiteSpace(schedule.CompanyId)
           && string.Equals(schedule.ConnectorId, request.ConnectorId?.Trim(), StringComparison.OrdinalIgnoreCase)
           && string.Equals(schedule.ExternalAccountId, request.ExternalAccountId?.Trim(), StringComparison.OrdinalIgnoreCase)
           && string.Equals(schedule.FundAccountId, request.FundAccountId?.Trim(), StringComparison.OrdinalIgnoreCase)
           && string.Equals(schedule.SourceInstitution, request.SourceInstitution?.Trim(), StringComparison.OrdinalIgnoreCase);
}
