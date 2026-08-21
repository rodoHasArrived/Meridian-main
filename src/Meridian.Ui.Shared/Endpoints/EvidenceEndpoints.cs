using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
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
            var trustedScope = ResolveRequiredDocumentScope(context);
            var manifest = await store
                .TryOpenManifestAsync(
                    subjectKind,
                    subjectId,
                    fileName,
                    trustedScope.TenantId,
                    trustedScope.CompanyId,
                    context.RequestAborted)
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
        .Produces<EvidenceEndpointErrorDto>(404)
        .Produces(StatusCodes.Status403Forbidden)
        .RequirePermission(UserPermission.ViewReporting)
        .RequireWorkstationTenantCompanyScope();

        app.MapGet("/workstation/evidence/vault/{vaultId}", async (
            string vaultId,
            HttpContext context) =>
        {
            var store = context.RequestServices.GetRequiredService<IEvidenceArtifactStore>();
            var trustedScope = ResolveRequiredDocumentScope(context);
            var manifest = await store
                .TryOpenManifestByVaultIdAsync(
                    vaultId,
                    trustedScope.TenantId,
                    trustedScope.CompanyId,
                    context.RequestAborted)
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
        .Produces<EvidenceEndpointErrorDto>(404)
        .Produces(StatusCodes.Status403Forbidden)
        .RequirePermission(UserPermission.ViewReporting)
        .RequireWorkstationTenantCompanyScope();

        var group = app.MapGroup("/api/workstation/evidence");

        group.MapGet(EvidenceSubroute(UiApiRoutes.WorkstationEvidenceSubjects), async (HttpContext context) =>
        {
            var service = context.RequestServices.GetRequiredService<EvidenceGraphService>();
            var subjects = await service.ListSubjectsAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(subjects, jsonOptions);
        })
        .WithName("GetWorkstationEvidenceSubjects")
        .Produces<IReadOnlyList<EvidenceSubjectDto>>(200)
        .Produces(StatusCodes.Status403Forbidden)
        .RequirePermission(UserPermission.ViewReporting)
        .RequireWorkstationTenantCompanyScope();

        group.MapGet(EvidenceSubroute(UiApiRoutes.WorkstationEvidenceSubjectPacket), async (
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
        .Produces<EvidenceEndpointErrorDto>(404)
        .Produces(StatusCodes.Status403Forbidden)
        .RequirePermission(UserPermission.ViewReporting)
        .RequireWorkstationTenantCompanyScope();

        group.MapGet(EvidenceSubroute(UiApiRoutes.WorkstationEvidenceSubjectGraph), async (
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

            if (!await CanAccessEvidenceVaultSubjectAsync(subjectKind, subjectId, context).ConfigureAwait(false))
            {
                return Results.NotFound(Error(
                    "evidence-subject-not-found",
                    $"Evidence subject '{subjectKind}/{subjectId}' was not found.",
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
        .Produces<EvidenceEndpointErrorDto>(404)
        .Produces(StatusCodes.Status403Forbidden)
        .RequirePermission(UserPermission.ViewReporting)
        .RequireWorkstationTenantCompanyScope();

        group.MapPost(EvidenceSubroute(UiApiRoutes.WorkstationEvidenceSubjectValidate), async (
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
        .Produces<EvidenceEndpointErrorDto>(404)
        .Produces(StatusCodes.Status403Forbidden)
        .RequirePermission(UserPermission.ManageReporting)
        .RequireWorkstationTenantCompanyScope();

        group.MapPost(EvidenceSubroute(UiApiRoutes.WorkstationEvidenceSubjectExportManifest), async (
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

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return ApiProblemDetails.Forbidden(
                    context,
                    "An authenticated evidence-export actor is required.");
            }

            var trustedScope = ResolveRequiredDocumentScope(context);
            var trustedRequest = requestResult.Request with
            {
                RequestedBy = actor,
                TenantId = trustedScope.TenantId,
                Scope = trustedScope.CompanyId
            };

            var store = context.RequestServices.GetRequiredService<IEvidenceArtifactStore>();
            var response = await store
                .WriteManifestAsync(packetResult.Packet, trustedRequest, context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(response, jsonOptions);
        })
        .WithName("ExportWorkstationEvidenceManifest")
        .Produces<EvidencePacketExportResponse>(200)
        .Produces<EvidenceEndpointErrorDto>(400)
        .Produces<EvidenceEndpointErrorDto>(404)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .RequirePermission(UserPermission.ManageReporting)
        .RequireWorkstationTenantCompanyScope();

        group.MapGet(EvidenceSubroute(UiApiRoutes.WorkstationEvidenceTemplates), (HttpContext context) =>
        {
            var registry = context.RequestServices.GetRequiredService<EvidenceTemplateRegistry>();
            return Results.Json(registry.GetTemplates(), jsonOptions);
        })
        .WithName("GetWorkstationEvidenceTemplates")
        .Produces<IReadOnlyList<EvidenceTemplateDto>>(200)
        .Produces(StatusCodes.Status403Forbidden)
        .RequirePermission(UserPermission.ViewReporting)
        .RequireWorkstationTenantCompanyScope();

        group.MapPost(EvidenceSubroute(UiApiRoutes.WorkstationEvidenceVaultIntake), async (EvidenceVaultIntakeRequestDto? request, HttpContext context) =>
        {
            if (request is null)
            {
                return Results.BadRequest(Error(
                    "invalid-evidence-vault-intake",
                    "Evidence vault intake request body must be a valid JSON object."));
            }

            if (request.IntakeSource?.SourceKind is
                EvidenceDocumentIntakeSourceKindDto.LocalFile or
                EvidenceDocumentIntakeSourceKindDto.ImportedFileReference)
            {
                return Results.BadRequest(Error(
                    "invalid-evidence-vault-intake-source",
                    "Workstation evidence intake cannot read a server-local file path. Upload the content or use a configured external-source adapter.",
                    request.SubjectKind,
                    request.SubjectId));
            }

            try
            {
                if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                {
                    return ApiProblemDetails.Forbidden(
                        context,
                        "An authenticated evidence-intake actor is required.");
                }

                var trustedScope = ResolveRequiredDocumentScope(context);
                request = request with
                {
                    Actor = actor,
                    ReceivedBy = actor,
                    TenantId = trustedScope.TenantId,
                    Scope = trustedScope.CompanyId,
                    ExtractionStatus = null,
                    ExtractorId = null,
                    ReviewerState = null
                };

                var extractor = context.RequestServices.GetService<IEvidenceDocumentExtractor>();
                if (extractor is not null)
                {
                    var extraction = await extractor.ExtractAsync(
                        new EvidenceDocumentExtractionRequestDto(
                            request.FileName,
                            request.ContentType,
                            request.IntakeChannel,
                            request.SourceSystem,
                            ResolveIntakeSourceReference(request),
                            request.ExtractedFields ?? []),
                        context.RequestAborted).ConfigureAwait(false);
                    request = request with
                    {
                        ExtractedFields = extraction.Fields,
                        ExtractionStatus = NormalizeExternalIntakeExtractionStatus(extraction.Status),
                        ExtractorId = extraction.ExtractorId,
                        ReviewerState = null
                    };
                }

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
        .Produces(StatusCodes.Status403Forbidden)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .RequirePermission(UserPermission.ManageReporting)
        .RequireWorkstationTenantCompanyScope();

        group.MapPost(EvidenceSubroute(UiApiRoutes.WorkstationEvidenceVaultSearch), async (EvidenceVaultLookupRequestDto request, HttpContext context) =>
        {
            if (!HasLookupCriteria(request))
            {
                return Results.BadRequest(Error(
                    "invalid-evidence-vault-lookup",
                    "Evidence vault search requires at least one lookup field."));
            }

            var store = context.RequestServices.GetRequiredService<IEvidenceArtifactStore>();
            var trustedScope = ResolveRequiredDocumentScope(context);
            var scopedRequest = request with
            {
                TenantId = trustedScope.TenantId,
                Scope = trustedScope.CompanyId
            };
            var result = await store.FindByLinkageAsync(scopedRequest, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("SearchWorkstationEvidenceVault")
        .DeclareNonMutating("Evidence-vault lookup by linkage criteria; the body carries the query because it has several optional fields, and the handler only reads through IEvidenceArtifactStore.FindByLinkageAsync.")
        .Produces<IReadOnlyList<EvidenceVaultIdentityDto>>(200)
        .Produces<EvidenceEndpointErrorDto>(400)
        .Produces(StatusCodes.Status403Forbidden)
        .RequirePermission(UserPermission.ViewReporting)
        .RequireWorkstationTenantCompanyScope();

        group.MapGet(EvidenceSubroute(UiApiRoutes.WorkstationEvidenceVaultRequestLists), async (
            string? requestListKind,
            string? requestListKindCode,
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

            var requestListKindCodeResult = ParseQueryEnum<EvidenceRequestListKindDto>(requestListKindCode, "requestListKindCode");
            if (requestListKindCodeResult.Error is not null)
            {
                return Results.BadRequest(requestListKindCodeResult.Error);
            }

            var store = context.RequestServices.GetRequiredService<IEvidenceArtifactStore>();
            var trustedScope = ResolveRequiredDocumentScope(context);
            var result = await store.ListRequestListsAsync(
                new EvidenceVaultRequestListQueryDto(
                    RequestListKind: requestListKind,
                    RequestListKindCode: requestListKindCodeResult.Value,
                    TargetKind: targetKind,
                    TargetId: targetId,
                    Status: status,
                    SubjectKind: subjectKind,
                    SubjectId: subjectId,
                    MaxResults: maxResults)
                {
                    TenantId = trustedScope.TenantId,
                    Scope = trustedScope.CompanyId
                },
                context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("ListWorkstationEvidenceVaultRequestLists")
        .Produces<IReadOnlyList<EvidenceVaultRequestListEntryDto>>(200)
        .Produces<EvidenceEndpointErrorDto>(400)
        .Produces(StatusCodes.Status403Forbidden)
        .RequirePermission(UserPermission.ViewReporting)
        .RequireWorkstationTenantCompanyScope();

        group.MapGet(EvidenceSubroute(UiApiRoutes.WorkstationEvidenceVaultDocuments), async (
            string? classification,
            string? channelKind,
            string? intakeChannelKind,
            string? extractionStatus,
            string? reviewStatus,
            string? linkKind,
            string? objectId,
            string? subjectKind,
            string? subjectId,
            string? tenantId,
            string? scope,
            int? maxResults,
            HttpContext context) =>
        {
            if (maxResults.HasValue && maxResults.Value <= 0)
            {
                return Results.BadRequest(Error(
                    "invalid-evidence-vault-document-query",
                    "Evidence vault document query maxResults must be greater than zero."));
            }

            var classificationResult = ParseQueryEnum<EvidenceDocumentClassificationDto>(classification, "classification");
            var channelKindResult = ParseQueryEnum<EvidenceDocumentIntakeChannelDto>(FirstNonEmpty(channelKind, intakeChannelKind), "channelKind");
            var extractionStatusResult = ParseQueryEnum<EvidenceExtractionStatusDto>(extractionStatus, "extractionStatus");
            var reviewStatusResult = ParseQueryEnum<EvidenceDocumentReviewStatusDto>(reviewStatus, "reviewStatus");
            var linkKindResult = ParseQueryEnum<EvidenceDocumentLinkKindDto>(linkKind, "linkKind");
            var parseError = classificationResult.Error ?? channelKindResult.Error ?? extractionStatusResult.Error ?? reviewStatusResult.Error ?? linkKindResult.Error;
            if (parseError is not null)
            {
                return Results.BadRequest(parseError);
            }

            var store = context.RequestServices.GetRequiredService<IEvidenceArtifactStore>();
            var trustedScope = ResolveRequiredDocumentScope(context);
            var result = await store.ListDocumentsAsync(
                new EvidenceVaultDocumentQueryDto(
                    Classification: classificationResult.Value,
                    ChannelKind: channelKindResult.Value,
                    ExtractionStatus: extractionStatusResult.Value,
                    ReviewStatus: reviewStatusResult.Value,
                    LinkKind: linkKindResult.Value,
                    ObjectId: objectId,
                    SubjectKind: subjectKind,
                    SubjectId: subjectId,
                    TenantId: trustedScope.TenantId,
                    Scope: trustedScope.CompanyId,
                    MaxResults: maxResults),
                context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("ListWorkstationEvidenceVaultDocuments")
        .Produces<IReadOnlyList<EvidenceVaultDocumentEntryDto>>(200)
        .Produces<EvidenceEndpointErrorDto>(400)
        .Produces(StatusCodes.Status403Forbidden)
        .RequirePermission(UserPermission.ViewReporting)
        .RequireWorkstationTenantCompanyScope();

        group.MapPost(EvidenceSubroute(UiApiRoutes.WorkstationEvidenceVaultDocumentReview), async (
            string vaultId,
            string documentId,
            EvidenceVaultDocumentReviewRequestDto? request,
            HttpContext context) =>
        {
            if (request is null)
            {
                return Results.BadRequest(Error(
                    "invalid-evidence-vault-document-review",
                    "Evidence vault document review request body must be a valid JSON object.",
                    vaultId: vaultId,
                    fileName: documentId));
            }

            try
            {
                if (!EndpointAuthorization.TryResolveActor(context, out var reviewer))
                {
                    return ApiProblemDetails.Forbidden(
                        context,
                        "An authenticated evidence-review actor is required.");
                }

                var store = context.RequestServices.GetRequiredService<IEvidenceArtifactStore>();
                var trustedScope = ResolveRequiredDocumentScope(context);
                var result = await store
                    .ReviewDocumentAsync(
                        vaultId,
                        documentId,
                        trustedScope.TenantId,
                        trustedScope.CompanyId,
                        request with { Reviewer = reviewer },
                        context.RequestAborted)
                    .ConfigureAwait(false);
                return result is null
                    ? Results.NotFound(Error(
                        "evidence-vault-document-not-found",
                        $"Evidence vault document '{vaultId}/{documentId}' was not found.",
                        vaultId: vaultId,
                        fileName: documentId))
                    : Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(Error(
                    "invalid-evidence-vault-document-review",
                    ex.Message,
                    vaultId: vaultId,
                    fileName: documentId));
            }
        })
        .WithName("ReviewWorkstationEvidenceVaultDocument")
        .Produces<EvidenceVaultDocumentReviewResponseDto>(200)
        .Produces<EvidenceEndpointErrorDto>(400)
        .Produces<EvidenceEndpointErrorDto>(404)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .RequirePermission(UserPermission.ApproveReporting)
        .RequireWorkstationTenantCompanyScope();

        return app;
    }

    private const string EvidenceApiRoutePrefix = "/api/workstation/evidence";

    private static string EvidenceSubroute(string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        return route.StartsWith(EvidenceApiRoutePrefix, StringComparison.Ordinal)
            ? route[EvidenceApiRoutePrefix.Length..]
            : route;
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

    private static string? ResolveIntakeSourceReference(EvidenceVaultIntakeRequestDto request)
        => FirstNonEmpty(
            request.SourceReference,
            request.IntakeSource?.Uri,
            request.IntakeSource?.Path,
            request.IntakeSource?.DisplayName);

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static EvidenceExtractionStatusDto NormalizeExternalIntakeExtractionStatus(
        EvidenceExtractionStatusDto status)
        => status is EvidenceExtractionStatusDto.Accepted or EvidenceExtractionStatusDto.Rejected
            ? EvidenceExtractionStatusDto.NeedsReview
            : status;

    private static (string TenantId, string CompanyId) ResolveRequiredDocumentScope(HttpContext context)
    {
        var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        return (tenantContext.TenantId!, tenantContext.CompanyId!);
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
            return (null, Results.BadRequest(Error(
                "unsupported-evidence-subject-kind",
                $"Evidence subject kind '{subjectKind}' is not supported.",
                subjectKind,
                subjectId)));
        }

        if (!await CanAccessEvidenceVaultSubjectAsync(subjectKind, subjectId, context).ConfigureAwait(false))
        {
            return (null, Results.NotFound(Error(
                "evidence-subject-not-found",
                $"Evidence subject '{subjectKind}/{subjectId}' was not found.",
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

    private static async Task<bool> CanAccessEvidenceVaultSubjectAsync(
        string subjectKind,
        string subjectId,
        HttpContext context)
    {
        if (!string.Equals(
                subjectKind,
                EvidenceSubjectResolver.EvidenceVaultKind,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var trustedScope = ResolveRequiredDocumentScope(context);
        var store = context.RequestServices.GetRequiredService<IEvidenceArtifactStore>();
        var identity = await store
            .TryGetVaultIdentityAsync(
                subjectId,
                trustedScope.TenantId,
                trustedScope.CompanyId,
                context.RequestAborted)
            .ConfigureAwait(false);
        return identity is not null;
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

    private static (T? Value, EvidenceEndpointErrorDto? Error) ParseQueryEnum<T>(
        string? value,
        string parameterName)
        where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null);
        }

        return Enum.TryParse<T>(value.Trim(), ignoreCase: true, out var parsed)
            ? (parsed, null)
            : (null, Error(
                "invalid-evidence-vault-document-query",
                $"Evidence vault document query parameter '{parameterName}' has an unsupported value '{value}'."));
    }

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
