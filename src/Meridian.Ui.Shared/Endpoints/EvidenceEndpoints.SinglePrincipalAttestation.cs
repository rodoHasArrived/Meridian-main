using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Evidence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class EvidenceEndpoints
{
    private static void MapSinglePrincipalAttestationEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        AttestationReadRoute(group.MapGet("/attestations/receipts/{receiptId:guid}/export", (Guid receiptId, HttpContext context) =>
            AttestationResultAsync(context, async () =>
            {
                var store = context.RequestServices.GetRequiredService<SinglePrincipalAttestationStore>();
                var scope = AttestationScope(context);
                var authority = AttestationAuthority(context);
                var state = await store.GetAsync(scope, authority, context.RequestAborted).ConfigureAwait(false);
                var receipt = state.Receipts.SingleOrDefault(item => item.ReceiptId == receiptId);
                if (receipt is null || !CanReadAttestationReceipt(receipt, context))
                    return Results.NotFound(Error("attestation_receipt_unavailable", "The scoped receipt was not found."));
                var proof = await store.GetReceiptExportAsync(scope, authority, receiptId,
                    context.RequestAborted).ConfigureAwait(false);
                context.Response.Headers.CacheControl = "no-store";
                return Results.File(proof.Content, "application/json", proof.FileName,
                    lastModified: proof.RecordedAt,
                    entityTag: new EntityTagHeaderValue($"\"{proof.ContentHashSha256}\""));
            })))
            .WithName("ExportPreparerAttestationProof")
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status404NotFound);

        AttestationReadRoute(group.MapGet("/attestations", async Task<IResult> (HttpContext context) => await AttestationResultAsync(context, async () =>
        {
            var authority = AttestationAuthority(context);
            var state = await context.RequestServices.GetRequiredService<SinglePrincipalAttestationStore>()
                .GetAsync(AttestationScope(context), authority, context.RequestAborted).ConfigureAwait(false);
            state = FilterReadableAttestations(state, context);
            return Results.Json(new SinglePrincipalAttestationWorkspaceDto(state, authority.Actor,
                authority.CanAdministerDeclaration, authority.CanAttest && authority.CanModifyTarget, authority.CanModifyTarget,
                authority.CanAttest && authority.CanModifyDraftTarget, authority.CanModifyDraftTarget), jsonOptions);
        })))
            .WithName("GetSinglePrincipalAttestationWorkspace")
            .Produces<SinglePrincipalAttestationWorkspaceDto>();

        AttestationReadRoute(group.MapGet("/attestations/subjects", (string vaultId, string documentId, HttpContext context) =>
            AttestationResultAsync(context, async () =>
            {
                if (!await CanReadAttestationSourceAsync(vaultId, context).ConfigureAwait(false))
                    return Results.NotFound(Error("attestation_subject_unavailable", "The scoped evidence was not found."));
                var scope = AttestationScope(context);
                var subject = await context.RequestServices.GetRequiredService<EvidenceAttestationSubjectResolver>()
                    .ResolveAsync(vaultId, documentId, scope.TenantId, scope.CompanyId, context.RequestAborted).ConfigureAwait(false);
                return subject is null
                    ? Results.NotFound(Error("attestation_subject_unavailable", "The scoped evidence was not found."))
                    : Results.Json(ToAttestationSubject(subject), jsonOptions);
            })))
            .WithName("GetPreparerAttestationSubject")
            .Produces<SinglePrincipalAttestationSubjectSnapshotDto>();

        AttestationReadRoute(group.MapGet("/attestations/drafts/{securityId:guid}/{runId:guid}",
            (Guid securityId, Guid runId, HttpContext context) => AttestationResultAsync(context, async () =>
            {
                if (!EndpointAuthorization.HasPermission(context, UserPermission.ViewSecurityMaster))
                    return Results.NotFound(Error("attestation_subject_unavailable", "The scoped draft was not found."));
                var scope = AttestationScope(context);
                var authority = AttestationAuthority(context);
                var subject = await context.RequestServices.GetRequiredService<ContractualCashFlowDraftAttestationSubjectResolver>()
                    .ResolveAsync(securityId, runId, scope.TenantId, scope.CompanyId, authority.Actor,
                        context.RequestAborted).ConfigureAwait(false);
                return subject is null
                    ? Results.NotFound(Error("attestation_subject_unavailable", "The scoped draft was not found."))
                    : Results.Json(subject, jsonOptions);
            })))
            .WithName("GetPreparerDraftCheckSubject")
            .Produces<SinglePrincipalAttestationSubjectSnapshotDto>()
            .Produces(StatusCodes.Status404NotFound);

        AttestationReadRoute(group.MapPost("/attestations/declaration", (ChangeSinglePrincipalDeclarationRequestDto request, HttpContext context) =>
            AttestationResultAsync(context, async () => Results.Json(
                await context.RequestServices.GetRequiredService<SinglePrincipalAttestationStore>()
                    .ChangeDeclarationAsync(AttestationScope(context), AttestationAuthority(context, request.Origin), request,
                        context.RequestAborted).ConfigureAwait(false), jsonOptions))))
            .WithName("ChangeSinglePrincipalDeclaration")
            .Produces<SinglePrincipalDeclarationDto>()
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        AttestationReadRoute(group.MapPost("/attestations/receipts", (RecordPreparerAttestationRequestDto request, HttpContext context) =>
            AttestationResultAsync(context, async () =>
            {
                // Only exact retained evidence acknowledgments are registered. A body cannot
                // select an economic effect, impersonate its preparer, or supply source authority.
                var authority = AttestationAuthority(context, request.Origin);
                var store = context.RequestServices.GetRequiredService<SinglePrincipalAttestationStore>();
                var scope = AttestationScope(context);
                var state = await store.GetAsync(scope, authority, context.RequestAborted).ConfigureAwait(false);
                var historical = state.Receipts.FirstOrDefault(item => item.CommandId == request.CommandId);
                var sourceReadable = historical is not null
                    ? CanReadAttestationReceipt(historical, context)
                    : await CanReadNewAttestationSubjectAsync(request, context).ConfigureAwait(false);
                if (!sourceReadable)
                    return Results.NotFound(Error("attestation_subject_unavailable", "The scoped evidence was not found."));
                return Results.Json(await store.AttestAsync(scope, authority, request, context.RequestAborted).ConfigureAwait(false), jsonOptions);
            })))
            .WithName("RecordPreparerAttestation")
            .Produces<PreparerAttestationReceiptDto>()
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        AttestationReadRoute(group.MapPost("/attestations/revocations", (RevokePreparerAttestationRequestDto request, HttpContext context) =>
            AttestationResultAsync(context, async () =>
            {
                var scope = AttestationScope(context);
                var store = context.RequestServices.GetRequiredService<SinglePrincipalAttestationStore>();
                var authority = AttestationAuthority(context, request.Origin);
                var state = await store.GetAsync(scope, authority, context.RequestAborted).ConfigureAwait(false);
                var receipt = state.Receipts.FirstOrDefault(item => item.ReceiptId == request.ReceiptId);
                if (receipt is null || !CanReadAttestationReceipt(receipt, context))
                    return Results.NotFound(Error("attestation_receipt_unavailable", "The scoped receipt was not found."));
                return Results.Json(await store.RevokeAsync(scope, authority, request, context.RequestAborted).ConfigureAwait(false), jsonOptions);
            })))
            .WithName("RevokePreparerAttestation")
            .Produces<PreparerAttestationRevocationDto>()
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static RouteHandlerBuilder AttestationReadRoute(RouteHandlerBuilder route) => route
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status503ServiceUnavailable)
        .RequireAuthenticatedSession()
        .RequireAllPermissions(UserPermission.ViewPreparerAttestations, UserPermission.ViewReporting)
        .RequireWorkstationTenantCompanyScope();

    private static SinglePrincipalAttestationScopeDto AttestationScope(HttpContext context)
    {
        var scope = ResolveRequiredDocumentScope(context);
        return new(scope.TenantId, scope.CompanyId);
    }

    private static SinglePrincipalAttestationAuthority AttestationAuthority(HttpContext context,
        OperationsActionOriginDto origin = OperationsActionOriginDto.HumanOperator)
    {
        EndpointAuthorization.TryResolveActor(context, out var actor);
        var canRead = EndpointAuthorization.HasPermission(context, UserPermission.ViewPreparerAttestations)
            && EndpointAuthorization.HasPermission(context, UserPermission.ViewReporting);
        var canAttest = EndpointAuthorization.HasPermission(context, UserPermission.RecordPreparerAttestations);
        return new(actor ?? string.Empty, EndpointAuthorization.ResolveTrustedActionOrigin(context, origin), canRead,
            EndpointAuthorization.HasPermission(context, UserPermission.ManageSinglePrincipalDeclarations),
            canAttest, CanRevoke: false,
            CanModifyTarget: EndpointAuthorization.HasPermission(context, UserPermission.ManageReporting),
            CanModifyDraftTarget: EndpointAuthorization.HasPermission(context, UserPermission.ModifySecurityMaster));
    }

    private static async Task<bool> CanReadNewAttestationSubjectAsync(RecordPreparerAttestationRequestDto request,
        HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(request.SubjectId))
            return false;
        if (request.SubjectKind == ContractualCashFlowDraftAttestationSubjectResolver.SubjectKind)
            return request.SubjectId.Split('/') is ["Security", var security, var run]
                && Guid.TryParseExact(security, "D", out _)
                && Guid.TryParseExact(run, "D", out _)
                && EndpointAuthorization.HasPermission(context, UserPermission.ViewSecurityMaster);
        return request.SubjectKind == EvidenceAttestationSubjectResolver.SubjectKind
            && request.SubjectId.Split('/') is [var vaultId, var documentId]
            && !string.IsNullOrWhiteSpace(documentId)
            && await CanReadAttestationSourceAsync(vaultId, context).ConfigureAwait(false);
    }

    private static async Task<bool> CanReadAttestationSourceAsync(string vaultId, HttpContext context)
    {
        var scope = AttestationScope(context);
        var identity = await context.RequestServices.GetRequiredService<IEvidenceArtifactStore>()
            .TryGetVaultIdentityAsync(vaultId, scope.TenantId, scope.CompanyId, context.RequestAborted).ConfigureAwait(false);
        return identity is not null && CanReadSubject(identity.SubjectKind, context, identity.SubjectId);
    }

    private static SinglePrincipalAttestationSubjectSnapshotDto ToAttestationSubject(EvidenceAttestationSubjectSnapshot subject) =>
        new(subject.SubjectKind, subject.SubjectId, subject.Revision, subject.Hash, subject.SourceArtifactId,
            subject.SourceHash, subject.SourceActor, subject.SourceProvenance, subject.Eligible, subject.Checks,
            subject.SourceSubjectKind, subject.SourceSubjectId, subject.SubjectSnapshotJson);

    private static bool CanReadAttestationReceipt(PreparerAttestationReceiptDto receipt, HttpContext context) =>
        !string.IsNullOrWhiteSpace(receipt.SourceSubjectKind)
        && !string.IsNullOrWhiteSpace(receipt.SourceSubjectId)
        && CanReadAttestationSourceSubject(receipt.SourceSubjectKind, receipt.SourceSubjectId, context);

    private static bool CanReadAttestationSourceSubject(string kind, string id, HttpContext context) =>
        string.Equals(kind, "contractual-cashflow-security", StringComparison.Ordinal)
            ? Guid.TryParseExact(id, "D", out _) && EndpointAuthorization.HasPermission(context, UserPermission.ViewSecurityMaster)
            : CanReadSubject(kind, context, id);

    private static PreparerAttestationEvidenceReadContext BuildPreparerAttestationReadContext(HttpContext context) =>
        new(AttestationScope(context), AttestationAuthority(context),
            (kind, id) => CanReadAttestationSourceSubject(kind, id, context));

    private static SinglePrincipalAttestationStateDto FilterReadableAttestations(SinglePrincipalAttestationStateDto state,
        HttpContext context)
    {
        var receipts = state.Receipts.Where(receipt => CanReadAttestationReceipt(receipt, context)).ToArray();
        var visibleIds = receipts.Select(receipt => receipt.ReceiptId).ToHashSet();
        return state with
        {
            Receipts = receipts,
            Revocations = state.Revocations.Where(item => visibleIds.Contains(item.ReceiptId)).ToArray(),
            AuditTrail = state.AuditTrail.Where(item => item.ReceiptId is null || visibleIds.Contains(item.ReceiptId.Value)).ToArray()
        };
    }

    private static async Task<IResult> AttestationResultAsync(HttpContext context, Func<Task<IResult>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (SinglePrincipalAttestationException exception)
        {
            var status = exception.Code switch
            {
                "stale_declaration" or "stale_subject" or "stale_receipt" or "command_conflict" => StatusCodes.Status409Conflict,
                "receipt_not_found" => StatusCodes.Status404NotFound,
                "state_unavailable" => StatusCodes.Status503ServiceUnavailable,
                "declaration_permission" or "attestation_permission" or "revocation_permission" or "read_permission"
                    or "human_required" or "principal_mismatch" or "preparer_mismatch" => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status400BadRequest
            };
            return Results.Json(Error(exception.Code, exception.Message), statusCode: status);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or TimeoutException)
        {
            return Results.Json(Error("attestation_store_unavailable", "Retained attestation state is unavailable; no authority was granted."),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(Error("invalid_attestation_request", "The attestation request is incomplete or invalid."));
        }
    }
}
