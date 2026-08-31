using System.Text.Json;
using Meridian.Application.SecurityMaster.CorporateActions;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class SecurityMasterEndpoints
{
    private const string CorporateActionSourceDecisionFanOutBlocker =
        CorporateActionOperationsService.SourceDecisionFanOutBlocker;

    // Global source proposals are decided per accounting scope, so the public decision boundary
    // stays closed unless the authoritative scope fan-out gate is composed. This is a composition
    // fact rather than an operator-configurable toggle: a deployment without the authority cannot
    // enumerate the affected scopes at all, and no setting may open the boundary over that.
    //
    // Composition alone does not authorize a decision. It only means the question is answerable —
    // every decision still asks the gate for the exact affected scope, and the gate refuses when
    // the fan-out is incomplete, empty, reaches another tenant, or spans more scopes than one
    // atomic command can case. This read-side posture is therefore advisory by construction: it
    // reports whether decisions are possible in this deployment, not whether this particular
    // proposal will pass, which only the decision path can know and which can change between the
    // read and the write regardless.
    private static bool CorporateActionSourceDecisionsComposed(HttpContext context) =>
        context.RequestServices.GetService<ICorporateActionScopeFanOutGate>() is not null;

    private static void MapCorporateActionOperationsEndpoints(
        RouteGroupBuilder group,
        JsonSerializerOptions jsonOptions)
    {
        group.MapPost(UiApiRoutes.SecurityMasterCorporateActionsIngest, async (
            CorporateActionIngestRequest? request,
            HttpContext context,
            [FromServices] CorporateActionIngestOrchestrator orchestrator,
            CancellationToken ct) =>
        {
            try
            {
                var actor = ResolveActor(context);
                var trustedRequest = (request ?? new CorporateActionIngestRequest()) with
                {
                    Actor = actor,
                    CorrelationId = context.TraceIdentifier,
                };
                return Results.Json(
                    await orchestrator.IngestAsync(trustedRequest, ct).ConfigureAwait(false),
                    jsonOptions);
            }
            catch (CorporateActionOperationException exception)
            {
                return CorporateActionProblem(context, exception);
            }
        })
        .WithName("IngestSecurityMasterCorporateActions")
        .RequirePermission(UserPermission.IngestCorporateActions)
        .Accepts<CorporateActionIngestRequest>("application/json")
        .Produces<CorporateActionIngestResult>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Browser compatibility route. Unlike the removed snapshot implementation, this reads
        // durable observations and returns proposal identity, version, exact trusted scope, and
        // user-specific server action availability.
        group.MapGet(UiApiRoutes.SecurityMasterCorporateActionsInbox, async (
            HttpContext context,
            [FromServices] ICorporateActionOperationsService service,
            CancellationToken ct) =>
        {
            if (!TryResolveCorporateActionScope(context, out var scope))
            {
                return CorporateActionProblem(
                    context,
                    new CorporateActionScopeMismatchException(
                        "A tenant- and company-scoped workstation request context is required."));
            }

            try
            {
                var inbox = await service.GetInboxAsync(scope, take: 250, ct).ConfigureAwait(false);
                return Results.Json(ApplyCallerActionAvailability(inbox, context), jsonOptions);
            }
            catch (CorporateActionOperationException exception)
            {
                return CorporateActionProblem(context, exception);
            }
        })
        .WithName("GetSecurityMasterCorporateActionInbox")
        .RequirePermission(UserPermission.ViewCorporateActions)
        .RequireWorkstationTenantCompanyScope()
        .Produces<CorporateActionDurableInboxDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // The browser previously posted acceptance commands to this unscoped collection route.
        // Keep an explicit tombstone so stale clients cannot silently mutate durable state.
        group.MapPost(UiApiRoutes.SecurityMasterCorporateActionsInboxApply, () =>
            Results.Problem(
                statusCode: StatusCodes.Status410Gone,
                title: "Legacy corporate-action acceptance route retired",
                detail: "Use the source-proposal acceptance route with the durable proposal identifier."))
        .WithName("RetiredSecurityMasterCorporateActionInboxApply")
        .RequireAllPermissions(
            UserPermission.ModifySecurityMaster,
            UserPermission.ResolveCorporateActionTerms)
        .RequireWorkstationTenantCompanyScope()
        .ProducesProblem(StatusCodes.Status410Gone)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.SecurityMasterCorporateActionSourceProposals, async (
            RecordCorporateActionSourceProposalRequestDto request,
            HttpContext context,
            [FromServices] ICorporateActionOperationsService service,
            CancellationToken ct) =>
        {
            try
            {
                var trusted = request with
                {
                    Actor = ResolveActor(context),
                    CorrelationId = context.TraceIdentifier,
                    // Public ingest callers cannot grant provider-release acceptance authority.
                    // Certified registered adapters assert it only through the internal orchestrator.
                    ProviderIdentity = request.ProviderIdentity with
                    {
                        ReleaseStatus = CorporateActionProviderReleaseStatusDto.ReviewOnly,
                    },
                };
                return Results.Json(
                    await service.RecordSourceProposalAsync(trusted, ct).ConfigureAwait(false),
                    jsonOptions,
                    statusCode: StatusCodes.Status201Created);
            }
            catch (CorporateActionOperationException exception)
            {
                return CorporateActionProblem(context, exception);
            }
        })
        .WithName("RecordCorporateActionSourceProposal")
        .RequirePermission(UserPermission.IngestCorporateActions)
        .Produces<CorporateActionSourceProposalDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.SecurityMasterCorporateActionCaseConflictResolution, async (
            Guid caseId,
            Guid conflictId,
            ResolveCorporateActionConflictRequestDto request,
            HttpContext context,
            [FromServices] ICorporateActionOperationsService service,
            CancellationToken ct) =>
        {
            if (conflictId != request.ConflictId)
            {
                return CorporateActionProblem(
                    context,
                    new CorporateActionValidationException(
                        "Route conflictId must match the request ConflictId."));
            }

            return await ExecuteScopedCaseCommandAsync(
                caseId, request.CaseId, request.TenantId, request.CompanyId, context,
                trusted => service.ResolveConflictAsync(request with
                {
                    TenantId = trusted.TenantId,
                    CompanyId = trusted.CompanyId,
                    Actor = trusted.Actor,
                    CorrelationId = trusted.CorrelationId,
                }, ct), jsonOptions).ConfigureAwait(false);
        })
        .WithName("ResolveCorporateActionCaseConflict")
        .RequirePermission(UserPermission.ResolveCorporateActionTerms)
        .RequireWorkstationTenantCompanyScope()
        .Produces<CorporateActionConflictResolutionResultDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet(UiApiRoutes.SecurityMasterCorporateActionSourceProposals, async (
            Guid? securityId,
            string? state,
            int? take,
            HttpContext context,
            [FromServices] ICorporateActionOperationsService service,
            CancellationToken ct) =>
        {
            try
            {
                var proposals = await service.ListSourceProposalsAsync(
                    securityId, state, take ?? 100, ct).ConfigureAwait(false);
                return Results.Json(
                    proposals.Select(proposal => ApplyCallerActionAvailability(proposal, context)).ToArray(),
                    jsonOptions);
            }
            catch (CorporateActionOperationException exception)
            {
                return CorporateActionProblem(context, exception);
            }
        })
        .WithName("ListCorporateActionSourceProposals")
        .RequirePermission(UserPermission.ViewCorporateActions)
        .Produces<IReadOnlyList<CorporateActionSourceProposalDto>>(StatusCodes.Status200OK);

        group.MapGet(UiApiRoutes.SecurityMasterCorporateActionSourceProposal, async (
            Guid proposalId,
            HttpContext context,
            [FromServices] ICorporateActionOperationsService service,
            CancellationToken ct) =>
        {
            try
            {
                var proposal = await service.GetSourceProposalAsync(proposalId, ct).ConfigureAwait(false);
                return proposal is null
                    ? CorporateActionProblem(
                        context,
                        new CorporateActionNotFoundException("Corporate-action source proposal", proposalId))
                    : Results.Json(ApplyCallerActionAvailability(proposal, context), jsonOptions);
            }
            catch (CorporateActionOperationException exception)
            {
                return CorporateActionProblem(context, exception);
            }
        })
        .WithName("GetCorporateActionSourceProposal")
        .RequirePermission(UserPermission.ViewCorporateActions)
        .Produces<CorporateActionSourceProposalDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost(UiApiRoutes.SecurityMasterCorporateActionSourceProposalAccept, async (
            Guid proposalId,
            AcceptCorporateActionSourceProposalRequestDto request,
            HttpContext context,
            [FromServices] ICorporateActionOperationsService service,
            CancellationToken ct) =>
            await AcceptSourceProposalAsync(proposalId, request, context, service, jsonOptions, ct).ConfigureAwait(false))
        .WithName("AcceptCorporateActionSourceProposal")
        .RequireAllPermissions(
            UserPermission.ModifySecurityMaster,
            UserPermission.ResolveCorporateActionTerms)
        .RequireWorkstationTenantCompanyScope()
        .Produces<CorporateActionSourceProposalAcceptanceResultDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.SecurityMasterCorporateActionSourceProposalReject, async (
            Guid proposalId,
            RejectCorporateActionSourceProposalRequestDto request,
            HttpContext context,
            [FromServices] ICorporateActionOperationsService service,
            CancellationToken ct) =>
        {
            if (proposalId != request.ProposalId)
            {
                return CorporateActionProblem(
                    context,
                    new CorporateActionValidationException("Route proposalId must match the request ProposalId."));
            }

            if (!TryResolveCorporateActionScope(context, out var scope))
            {
                return CorporateActionProblem(context, new CorporateActionScopeMismatchException(
                    "A tenant- and company-scoped workstation request context is required."));
            }

            if (!CorporateActionSourceDecisionsComposed(context))
            {
                return CorporateActionProblem(
                    context,
                    new CorporateActionPersistenceUnavailableException(CorporateActionSourceDecisionFanOutBlocker));
            }

            try
            {
                // Rejecting retires a globally visible provider observation, so it removes the
                // proposal from every scope's actionable inbox, not just the decider's. It may
                // therefore only be taken by a caller who owns the whole affected set. Unlike
                // acceptance — where the authority runs inside the atomic command because it
                // resolves the case scope that command opens — a rejection resolves nothing, so
                // the check is a plain authorization gate and belongs here.
                var proposal = await service.GetSourceProposalAsync(request.ProposalId, ct).ConfigureAwait(false);
                if (proposal is null)
                {
                    return CorporateActionProblem(
                        context,
                        new CorporateActionNotFoundException("Corporate-action source proposal", request.ProposalId));
                }

                if (await RefuseUnauthorizedSourceDecisionAsync(context, proposal, scope, ct).ConfigureAwait(false)
                    is { } refusal)
                {
                    return refusal;
                }

                var trusted = request with
                {
                    Actor = ResolveActor(context),
                    CorrelationId = context.TraceIdentifier,
                };
                return Results.Json(
                    await service.RejectSourceProposalAsync(trusted, ct).ConfigureAwait(false),
                    jsonOptions);
            }
            catch (CorporateActionOperationException exception)
            {
                return CorporateActionProblem(context, exception);
            }
        })
        .WithName("RejectCorporateActionSourceProposal")
        .RequireAllPermissions(
            UserPermission.ModifySecurityMaster,
            UserPermission.ResolveCorporateActionTerms)
        .RequireWorkstationTenantCompanyScope()
        .Produces<CorporateActionSourceProposalDecisionResultDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet(UiApiRoutes.SecurityMasterCorporateActionCases, async (
            Guid? securityId,
            string? state,
            int? take,
            HttpContext context,
            [FromServices] ICorporateActionOperationsService service,
            CancellationToken ct) =>
        {
            if (!TryResolveCorporateActionScope(context, out var scope))
            {
                return CorporateActionProblem(context, new CorporateActionScopeMismatchException(
                    "A tenant- and company-scoped workstation request context is required."));
            }

            try
            {
                var cases = await service.ListCasesAsync(
                    scope.TenantId, scope.CompanyId, securityId, state, take ?? 100, ct).ConfigureAwait(false);
                // Narrowly scoped cases are listed now that the full-scope command path exists:
                // their server-resolved scope is displayed as record data, and every mutation on
                // them requires an exact full-scope assertion at the write boundary.
                return Results.Json(
                    cases.Select(item => ApplyCallerActionAvailability(item, context)).ToArray(),
                    jsonOptions);
            }
            catch (CorporateActionOperationException exception)
            {
                return CorporateActionProblem(context, exception);
            }
        })
        .WithName("ListCorporateActionCases")
        .RequirePermission(UserPermission.ViewCorporateActions)
        .RequireWorkstationTenantCompanyScope()
        .Produces<IReadOnlyList<CorporateActionProcessingCaseDto>>(StatusCodes.Status200OK);

        group.MapGet(UiApiRoutes.SecurityMasterCorporateActionCase, async (
            Guid caseId,
            HttpContext context,
            [FromServices] ICorporateActionOperationsService service,
            CancellationToken ct) =>
        {
            if (!TryResolveCorporateActionScope(context, out var scope))
            {
                return CorporateActionProblem(context, new CorporateActionScopeMismatchException(
                    "A tenant- and company-scoped workstation request context is required."));
            }

            try
            {
                var processingCase = await service.GetCaseAsync(
                    caseId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false);
                // Narrowly scoped cases are readable now that the full-scope command path exists:
                // the server-resolved scope is record data the caller must echo exactly on every
                // mutation, so displaying it never lets a caller assert an assignment.
                return processingCase is null
                    ? CorporateActionProblem(
                        context,
                        new CorporateActionNotFoundException("Corporate-action processing case", caseId))
                    : Results.Json(ApplyCallerActionAvailability(processingCase, context), jsonOptions);
            }
            catch (CorporateActionOperationException exception)
            {
                return CorporateActionProblem(context, exception);
            }
        })
        .WithName("GetCorporateActionCase")
        .RequirePermission(UserPermission.ViewCorporateActions)
        .RequireWorkstationTenantCompanyScope()
        .Produces<CorporateActionProcessingCaseDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet(UiApiRoutes.SecurityMasterCorporateActionCaseConflicts, async (
            Guid caseId,
            string? state,
            int? take,
            HttpContext context,
            [FromServices] ICorporateActionOperationsService service,
            CancellationToken ct) =>
        {
            if (!TryResolveCorporateActionScope(context, out var scope))
            {
                return CorporateActionProblem(context, new CorporateActionScopeMismatchException(
                    "A tenant- and company-scoped workstation request context is required."));
            }

            try
            {
                var processingCase = await service.GetCaseAsync(
                    caseId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false);
                if (processingCase is null)
                {
                    return CorporateActionProblem(
                        context,
                        new CorporateActionNotFoundException("Corporate-action processing case", caseId));
                }

                if (HasNarrowCorporateActionScope(processingCase.Scope))
                {
                    return CorporateActionProblem(
                        context,
                        new CorporateActionScopeMismatchException(
                            "This case has a narrow scope that this endpoint cannot authoritatively resolve."));
                }

                return Results.Json(
                    await service.ListConflictsAsync(
                        caseId, scope.TenantId, scope.CompanyId, state, take ?? 100, ct)
                        .ConfigureAwait(false),
                    jsonOptions);
            }
            catch (CorporateActionOperationException exception)
            {
                return CorporateActionProblem(context, exception);
            }
        })
        .WithName("ListCorporateActionCaseConflicts")
        .RequirePermission(UserPermission.ViewCorporateActions)
        .RequireWorkstationTenantCompanyScope()
        .Produces<IReadOnlyList<CorporateActionConflictDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet(UiApiRoutes.SecurityMasterCorporateActionCaseConflict, async (
            Guid caseId,
            Guid conflictId,
            HttpContext context,
            [FromServices] ICorporateActionOperationsService service,
            CancellationToken ct) =>
        {
            if (!TryResolveCorporateActionScope(context, out var scope))
            {
                return CorporateActionProblem(context, new CorporateActionScopeMismatchException(
                    "A tenant- and company-scoped workstation request context is required."));
            }

            try
            {
                var processingCase = await service.GetCaseAsync(
                    caseId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false);
                if (processingCase is null)
                {
                    return CorporateActionProblem(
                        context,
                        new CorporateActionNotFoundException("Corporate-action processing case", caseId));
                }

                if (HasNarrowCorporateActionScope(processingCase.Scope))
                {
                    return CorporateActionProblem(
                        context,
                        new CorporateActionScopeMismatchException(
                            "This case has a narrow scope that this endpoint cannot authoritatively resolve."));
                }

                var conflict = await service.GetConflictAsync(
                    caseId, conflictId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false);
                return conflict is null
                    ? CorporateActionProblem(
                        context,
                        new CorporateActionNotFoundException("Corporate-action case conflict", conflictId))
                    : Results.Json(conflict, jsonOptions);
            }
            catch (CorporateActionOperationException exception)
            {
                return CorporateActionProblem(context, exception);
            }
        })
        .WithName("GetCorporateActionCaseConflict")
        .RequirePermission(UserPermission.ViewCorporateActions)
        .RequireWorkstationTenantCompanyScope()
        .Produces<CorporateActionConflictDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost(UiApiRoutes.SecurityMasterCorporateActionCaseEvidence, async (
            Guid caseId,
            AddCorporateActionEvidenceRequestDto request,
            HttpContext context,
            [FromServices] ICorporateActionOperationsService service,
            CancellationToken ct) =>
            await ExecuteScopedCaseCommandAsync(
                caseId, request.CaseId, request.TenantId, request.CompanyId, context,
                trusted => service.AddEvidenceAsync(request with
                {
                    TenantId = trusted.TenantId,
                    CompanyId = trusted.CompanyId,
                    Actor = trusted.Actor,
                    CorrelationId = trusted.CorrelationId,
                }, ct), jsonOptions).ConfigureAwait(false))
        .WithName("AddCorporateActionCaseEvidence")
        .RequireAnyPermission(
            UserPermission.ResolveCorporateActionTerms,
            UserPermission.RecordCorporateActionElection,
            UserPermission.PrepareCorporateActionAccounting)
        .RequireWorkstationTenantCompanyScope()
        .Produces<CorporateActionEvidenceMutationResultDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.SecurityMasterCorporateActionCaseConflicts, async (
            Guid caseId,
            RecordCorporateActionConflictRequestDto request,
            HttpContext context,
            [FromServices] ICorporateActionOperationsService service,
            CancellationToken ct) =>
            await ExecuteScopedCaseCommandAsync(
                caseId, request.CaseId, request.TenantId, request.CompanyId, context,
                trusted => service.RecordConflictAsync(request with
                {
                    TenantId = trusted.TenantId,
                    CompanyId = trusted.CompanyId,
                    Actor = trusted.Actor,
                    CorrelationId = trusted.CorrelationId,
                }, ct), jsonOptions).ConfigureAwait(false))
        .WithName("RecordCorporateActionCaseConflict")
        .RequirePermission(UserPermission.ResolveCorporateActionTerms)
        .RequireWorkstationTenantCompanyScope()
        .Produces<CorporateActionConflictMutationResultDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.SecurityMasterCorporateActionCaseOptions, async (
            Guid caseId,
            UpsertCorporateActionProcessingOptionRequestDto request,
            HttpContext context,
            [FromServices] ICorporateActionOperationsService service,
            CancellationToken ct) =>
            await ExecuteScopedCaseCommandAsync(
                caseId, request.CaseId, request.TenantId, request.CompanyId, context,
                trusted => service.UpsertOptionAsync(request with
                {
                    TenantId = trusted.TenantId,
                    CompanyId = trusted.CompanyId,
                    Actor = trusted.Actor,
                    CorrelationId = trusted.CorrelationId,
                }, ct), jsonOptions).ConfigureAwait(false))
        .WithName("UpsertCorporateActionCaseOption")
        .RequirePermission(UserPermission.PrepareCorporateActionAccounting)
        .RequireWorkstationTenantCompanyScope()
        .Produces<CorporateActionProcessingOptionMutationResultDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.SecurityMasterCorporateActionCaseTransition, async (
            Guid caseId,
            TransitionCorporateActionCaseRequestDto request,
            HttpContext context,
            [FromServices] ICorporateActionOperationsService service,
            CancellationToken ct) =>
            await ExecuteScopedCaseCommandAsync(
                caseId, request.CaseId, request.TenantId, request.CompanyId, context,
                trusted => service.TransitionCaseAsync(request with
                {
                    TenantId = trusted.TenantId,
                    CompanyId = trusted.CompanyId,
                    Actor = trusted.Actor,
                    CorrelationId = trusted.CorrelationId,
                    Authority = ResolveTransitionAuthority(context),
                }, ct), jsonOptions).ConfigureAwait(false))
        .WithName("TransitionCorporateActionCase")
        .RequireAnyPermission(
            UserPermission.ResolveCorporateActionTerms,
            UserPermission.RecordCorporateActionElection,
            UserPermission.PrepareCorporateActionAccounting,
            UserPermission.OverrideCorporateActionPolicy,
            UserPermission.ReopenCorporateActionCase)
        .RequireWorkstationTenantCompanyScope()
        .Produces<CorporateActionCaseTransitionResultDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.SecurityMasterCorporateActionCaseAccountingProjection, async (
            Guid caseId,
            AttachCorporateActionAccountingProjectionRequestDto request,
            HttpContext context,
            [FromServices] ICorporateActionCaseAccountingService service,
            CancellationToken ct) =>
            await ExecuteScopedCaseCommandAsync(
                caseId, request.CaseId, request.TenantId, request.CompanyId, context,
                trusted => service.AttachProjectionAsync(request with
                {
                    TenantId = trusted.TenantId,
                    CompanyId = trusted.CompanyId,
                    Actor = trusted.Actor,
                    CorrelationId = trusted.CorrelationId,
                    Authority = ResolveTransitionAuthority(context),
                }, ct), jsonOptions).ConfigureAwait(false))
        .WithName("AttachCorporateActionCaseAccountingProjection")
        .RequirePermission(UserPermission.PrepareCorporateActionAccounting)
        .RequireWorkstationTenantCompanyScope()
        .Produces<CorporateActionAccountingProjectionMutationResultDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.SecurityMasterCorporateActionCaseAccountingApproval, async (
            Guid caseId,
            ApproveCorporateActionCaseAccountingRequestDto request,
            HttpContext context,
            [FromServices] ICorporateActionCaseAccountingService service,
            CancellationToken ct) =>
            await ExecuteScopedCaseCommandAsync(
                caseId, request.CaseId, request.TenantId, request.CompanyId, context,
                trusted => service.ApproveAsync(request with
                {
                    TenantId = trusted.TenantId,
                    CompanyId = trusted.CompanyId,
                    Actor = trusted.Actor,
                    CorrelationId = trusted.CorrelationId,
                    Authority = ResolveAccountingDecisionAuthority(context),
                }, ct), jsonOptions).ConfigureAwait(false))
        .WithName("ApproveCorporateActionCaseAccounting")
        .RequirePermission(UserPermission.ApproveCorporateActionAccounting)
        .RequireWorkstationTenantCompanyScope()
        .Produces<CorporateActionAccountingApprovalResultDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.SecurityMasterCorporateActionCaseAccountingPosting, async (
            Guid caseId,
            PostCorporateActionCaseAccountingRequestDto request,
            HttpContext context,
            [FromServices] ICorporateActionCaseAccountingService service,
            CancellationToken ct) =>
            await ExecuteScopedCaseCommandAsync(
                caseId, request.CaseId, request.TenantId, request.CompanyId, context,
                trusted => service.PostAsync(request with
                {
                    TenantId = trusted.TenantId,
                    CompanyId = trusted.CompanyId,
                    Actor = trusted.Actor,
                    CorrelationId = trusted.CorrelationId,
                    Authority = ResolveAccountingDecisionAuthority(context),
                }, ct), jsonOptions).ConfigureAwait(false))
        .WithName("PostCorporateActionCaseAccounting")
        .RequirePermission(UserPermission.PostCorporateActionAccounting)
        .RequireWorkstationTenantCompanyScope()
        .Produces<CorporateActionAccountingPostingResultDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static async Task<IResult> AcceptSourceProposalAsync(
        Guid proposalId,
        AcceptCorporateActionSourceProposalRequestDto request,
        HttpContext context,
        ICorporateActionOperationsService service,
        JsonSerializerOptions jsonOptions,
        CancellationToken ct)
    {
        if (proposalId != request.ProposalId)
        {
            return CorporateActionProblem(
                context,
                new CorporateActionValidationException("Route proposalId must match the request ProposalId."));
        }

        if (!TryResolveCorporateActionScope(context, out var scope))
        {
            return CorporateActionProblem(context, new CorporateActionScopeMismatchException(
                "A tenant- and company-scoped workstation request context is required."));
        }

        if (!MatchesTrustedScope(request.Scope, scope))
        {
            return CorporateActionProblem(context, new CorporateActionScopeMismatchException(
                "Acceptance scope tenant/company does not match the authenticated workstation scope."));
        }

        // Narrow scope stays caller-forbidden now that the server can resolve it. A caller that
        // supplies a fund, account, or book is asserting an assignment, and the whole point of the
        // authority is that assignments are read from the record rather than asserted. The
        // acceptance command stamps the resolved values itself.
        if (HasNarrowCorporateActionScope(request.Scope))
        {
            return CorporateActionProblem(context, new CorporateActionScopeMismatchException(
                "Fund, account, portfolio, custody, ledger-book, basis, and other narrow corporate-action scope fields are server-resolved from the authoritative scoped assignment and must not be supplied by the caller."));
        }

        // Only the cheap composition check happens here. The authoritative resolution itself runs
        // inside the acceptance command, which must first replay a committed receipt — re-asking
        // the authority for a retry whose holdings have since moved would refuse a command that
        // already committed.
        if (!CorporateActionSourceDecisionsComposed(context))
        {
            return CorporateActionProblem(
                context,
                new CorporateActionPersistenceUnavailableException(CorporateActionSourceDecisionFanOutBlocker));
        }

        try
        {
            var trusted = request with
            {
                Scope = request.Scope with { TenantId = scope.TenantId, CompanyId = scope.CompanyId },
                Actor = ResolveActor(context),
                CorrelationId = context.TraceIdentifier,
            };
            return Results.Json(
                await service.AcceptSourceProposalAsync(trusted, ct).ConfigureAwait(false),
                jsonOptions);
        }
        catch (CorporateActionOperationException exception)
        {
            return CorporateActionProblem(context, exception);
        }
    }

    private static async Task<IResult> ExecuteScopedCaseCommandAsync<TResult>(
        Guid routeCaseId,
        Guid requestCaseId,
        string? requestTenantId,
        string? requestCompanyId,
        HttpContext context,
        Func<TrustedCorporateActionCommandScope, Task<TResult>> command,
        JsonSerializerOptions jsonOptions)
    {
        if (routeCaseId != requestCaseId)
        {
            return CorporateActionProblem(
                context,
                new CorporateActionValidationException("Route caseId must match the request CaseId."));
        }

        if (!TryResolveCorporateActionScope(context, out var scope))
        {
            return CorporateActionProblem(context, new CorporateActionScopeMismatchException(
                "A tenant- and company-scoped workstation request context is required."));
        }

        if (!string.Equals(requestTenantId, scope.TenantId, StringComparison.Ordinal)
            || !string.Equals(requestCompanyId, scope.CompanyId, StringComparison.Ordinal))
        {
            return CorporateActionProblem(context, new CorporateActionScopeMismatchException(
                "Case command tenant/company does not match the authenticated workstation scope."));
        }

        try
        {
            var trusted = new TrustedCorporateActionCommandScope(
                scope.TenantId,
                scope.CompanyId,
                ResolveActor(context),
                context.TraceIdentifier);
            return Results.Json(await command(trusted).ConfigureAwait(false), jsonOptions);
        }
        catch (CorporateActionOperationException exception)
        {
            return CorporateActionProblem(context, exception);
        }
    }

    private static CorporateActionDurableInboxDto ApplyCallerActionAvailability(
        CorporateActionDurableInboxDto inbox,
        HttpContext context) =>
        inbox with
        {
            Staged = inbox.Staged.Select(entry => entry with
            {
                ActionAvailability = ApplyCallerActionAvailability(entry.ActionAvailability, context),
            }).ToArray(),
            Cases = inbox.Cases.Select(item => ApplyCallerActionAvailability(item, context)).ToArray(),
        };

    private static CorporateActionSourceProposalDto ApplyCallerActionAvailability(
        CorporateActionSourceProposalDto proposal,
        HttpContext context) =>
        proposal with
        {
            ActionAvailability = ApplyCallerActionAvailability(
                proposal.ActionAvailability
                    ?? new CorporateActionSourceProposalActionAvailabilityDto(false, false, false, ["Action availability was not projected."]),
                context),
        };

    private static CorporateActionSourceProposalActionAvailabilityDto ApplyCallerActionAvailability(
        CorporateActionSourceProposalActionAvailabilityDto availability,
        HttpContext context)
    {
        var canResolve = EndpointAuthorization.HasPermission(context, UserPermission.ModifySecurityMaster)
            && EndpointAuthorization.HasPermission(context, UserPermission.ResolveCorporateActionTerms);
        var permissionBlocker = canResolve
            ? null
            : "Accepting or rejecting a canonical fact requires ModifySecurityMaster and ResolveCorporateActionTerms.";
        var blockers = availability.Blockers.ToList();
        if (permissionBlocker is not null
            && !blockers.Contains(permissionBlocker, StringComparer.Ordinal))
        {
            blockers.Add(permissionBlocker);
        }

        var decisionsComposed = CorporateActionSourceDecisionsComposed(context);
        if (!decisionsComposed
            && !blockers.Contains(CorporateActionSourceDecisionFanOutBlocker, StringComparer.Ordinal))
        {
            blockers.Add(CorporateActionSourceDecisionFanOutBlocker);
        }

        return availability with
        {
            CanAccept = availability.CanAccept && canResolve && decisionsComposed,
            CanReject = availability.CanReject && canResolve && decisionsComposed,
            // Staged inbox rows do not yet carry enough retained per-source evidence to make a
            // comparison control truthful. Keep this unavailable even if a stale projection says otherwise.
            CanCompareEvidence = false,
            Blockers = blockers,
        };
    }

    /// <summary>
    /// Refuses a source decision the scope authority will not authorize, or returns null when it
    /// will. Each refusal keeps its own problem code so a caller can tell an authority that is
    /// absent (retry later) from a fan-out that genuinely reaches beyond this command.
    /// </summary>
    private static async Task<IResult?> RefuseUnauthorizedSourceDecisionAsync(
        HttpContext context,
        CorporateActionSourceProposalDto proposal,
        CorporateActionCaseScopeDto scope,
        CancellationToken ct)
    {
        var gate = context.RequestServices.GetService<ICorporateActionScopeFanOutGate>();
        if (gate is null)
        {
            return CorporateActionProblem(
                context,
                new CorporateActionPersistenceUnavailableException(CorporateActionSourceDecisionFanOutBlocker));
        }

        var effectiveDate = proposal.ProposedAction.RecordDate ?? proposal.ProposedAction.ExDate;
        var decision = await gate
            .ResolveDecisionScopeAsync(
                proposal.SecurityId,
                effectiveDate,
                scope.TenantId,
                scope.CompanyId,
                ct)
            .ConfigureAwait(false);
        if (decision.IsPermitted)
        {
            return null;
        }

        var detail = decision.Blockers.Count == 0
            ? CorporateActionSourceDecisionFanOutBlocker
            : string.Join(" ", decision.Blockers);
        return CorporateActionProblem(context, decision.Refusal switch
        {
            CorporateActionScopeFanOutRefusal.MultiScope => new CorporateActionOperationException(
                CorporateActionProblemCodes.DownstreamAuthorityRequired,
                detail),
            CorporateActionScopeFanOutRefusal.ForeignScope or
            CorporateActionScopeFanOutRefusal.NoAffectedScope => new CorporateActionScopeMismatchException(detail),
            _ => new CorporateActionPersistenceUnavailableException(detail),
        });
    }

    private static CorporateActionProcessingCaseDto ApplyCallerActionAvailability(
        CorporateActionProcessingCaseDto processingCase,
        HttpContext context)
    {
        var availability = processingCase.ActionAvailability
            ?? new CorporateActionCaseActionAvailabilityDto(false, false, false, false, false, [], ["Action availability was not projected."]);
        var canResolve = EndpointAuthorization.HasPermission(context, UserPermission.ResolveCorporateActionTerms);
        var canElect = EndpointAuthorization.HasPermission(context, UserPermission.RecordCorporateActionElection);
        var canPrepare = EndpointAuthorization.HasPermission(context, UserPermission.PrepareCorporateActionAccounting);
        var authority = ResolveTransitionAuthority(context);
        var canMutateCase = canResolve || canElect || canPrepare;
        var canTransitionCase = canMutateCase || authority.CanReopenCase;
        var authorizedTargets = availability.AllowedTransitionTargets
            .Where(target => CorporateActionCaseTransitionAuthorization.IsAuthorized(
                target, authority, policyOverride: false, out _))
            .ToArray();
        var blockers = !canTransitionCase
            ? availability.Blockers.Concat(["No corporate-action case mutation permission is assigned."]).ToArray()
            : availability.AllowedTransitionTargets.Count > 0 && authorizedTargets.Length == 0
                ? availability.Blockers.Concat(["The caller has no authority for the case's available transition targets."]).ToArray()
                : availability.Blockers;
        return processingCase with
        {
            ActionAvailability = availability with
            {
                CanAddEvidence = availability.CanAddEvidence && canMutateCase,
                CanRecordConflict = availability.CanRecordConflict && canResolve,
                CanResolveConflict = availability.CanResolveConflict && canResolve,
                CanManageOptions = availability.CanManageOptions && canPrepare,
                CanTransition = availability.CanTransition && authorizedTargets.Length > 0,
                // Approval and posting use their own exact evidence/projection commands with
                // dedicated downstream permissions; the read model advertises them only when the
                // durable binding supports them AND the caller holds the governed capability.
                CanApproveAccounting = availability.CanApproveAccounting
                    && EndpointAuthorization.HasPermission(context, UserPermission.ApproveCorporateActionAccounting),
                AllowedTransitionTargets = authorizedTargets,
                Blockers = blockers,
                CanPostAccounting = availability.CanPostAccounting
                    && EndpointAuthorization.HasPermission(context, UserPermission.PostCorporateActionAccounting),
            },
        };
    }

    private static CorporateActionCaseTransitionAuthorityDto ResolveTransitionAuthority(HttpContext context) =>
        new(
            EndpointAuthorization.HasPermission(context, UserPermission.ResolveCorporateActionTerms),
            EndpointAuthorization.HasPermission(context, UserPermission.RecordCorporateActionElection),
            EndpointAuthorization.HasPermission(context, UserPermission.PrepareCorporateActionAccounting),
            EndpointAuthorization.HasPermission(context, UserPermission.OverrideCorporateActionPolicy),
            EndpointAuthorization.HasPermission(context, UserPermission.ReopenCorporateActionCase));

    private static CorporateActionAccountingDecisionAuthorityDto ResolveAccountingDecisionAuthority(HttpContext context) =>
        new(
            EndpointAuthorization.HasPermission(context, UserPermission.ApproveCorporateActionAccounting),
            EndpointAuthorization.HasPermission(context, UserPermission.PostCorporateActionAccounting));

    private static bool TryResolveCorporateActionScope(
        HttpContext context,
        out CorporateActionCaseScopeDto scope)
    {
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        if (string.IsNullOrWhiteSpace(tenant.TenantId) || string.IsNullOrWhiteSpace(tenant.CompanyId))
        {
            scope = null!;
            return false;
        }

        scope = new CorporateActionCaseScopeDto(tenant.TenantId.Trim(), tenant.CompanyId.Trim());
        return true;
    }

    private static bool MatchesTrustedScope(
        CorporateActionCaseScopeDto? requestScope,
        CorporateActionCaseScopeDto trustedScope) =>
        requestScope is not null
        && string.Equals(requestScope.TenantId, trustedScope.TenantId, StringComparison.Ordinal)
        && string.Equals(requestScope.CompanyId, trustedScope.CompanyId, StringComparison.Ordinal);

    private static bool HasNarrowCorporateActionScope(CorporateActionCaseScopeDto scope) =>
        !string.IsNullOrWhiteSpace(scope.StructureNodeId)
        || !string.IsNullOrWhiteSpace(scope.FundProfileId)
        || !string.IsNullOrWhiteSpace(scope.FinancialAccountId)
        || !string.IsNullOrWhiteSpace(scope.PortfolioId)
        || !string.IsNullOrWhiteSpace(scope.CustodyAccountId)
        || !string.IsNullOrWhiteSpace(scope.LedgerBookId)
        || !string.IsNullOrWhiteSpace(scope.PeriodId)
        || !string.IsNullOrWhiteSpace(scope.AccountingBasis)
        || !string.IsNullOrWhiteSpace(scope.FunctionalCurrency)
        || !string.IsNullOrWhiteSpace(scope.Jurisdiction);

    private static IResult CorporateActionProblem(
        HttpContext context,
        CorporateActionOperationException exception)
    {
        var status = exception.Code switch
        {
            CorporateActionProblemCodes.NotFound => StatusCodes.Status404NotFound,
            CorporateActionProblemCodes.ScopeMismatch or
            CorporateActionProblemCodes.PermissionDenied => StatusCodes.Status403Forbidden,
            CorporateActionProblemCodes.TermsIncomplete or
            CorporateActionProblemCodes.ElectionRequired or
            CorporateActionProblemCodes.ElectionExpired or
            CorporateActionProblemCodes.EntitlementStale or
            CorporateActionProblemCodes.AllocationInvalid or
            CorporateActionProblemCodes.PolicyMissing or
            CorporateActionProblemCodes.ProjectionStale or
            CorporateActionProblemCodes.PeriodLocked or
            CorporateActionProblemCodes.JournalUnbalanced or
            CorporateActionProblemCodes.MakerCheckerRequired or
            CorporateActionProblemCodes.ReconciliationIncomplete or
            CorporateActionProblemCodes.ValidationFailed or
            CorporateActionProblemCodes.DownstreamAuthorityRequired or
            CorporateActionProblemCodes.SpecialistReviewRequired => StatusCodes.Status422UnprocessableEntity,
            CorporateActionProblemCodes.PersistenceUnavailable => StatusCodes.Status503ServiceUnavailable,
            CorporateActionProblemCodes.VersionConflict or
            CorporateActionProblemCodes.IdempotencyCollision or
            CorporateActionProblemCodes.SourceConflict or
            CorporateActionProblemCodes.StateConflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = exception.Code,
            ["traceId"] = context.TraceIdentifier,
        };
        if (exception is CorporateActionVersionConflictException versionConflict)
        {
            extensions["resourceId"] = versionConflict.ResourceId;
            extensions["expectedVersion"] = versionConflict.ExpectedVersion;
            extensions["currentVersion"] = versionConflict.CurrentVersion;
            extensions["currentETag"] = $"W/\"{versionConflict.CurrentVersion}\"";
            // The aggregate version is the only field that every command can identify without
            // re-reading and disclosing a potentially out-of-scope snapshot. Clients must reload
            // the scoped resource to obtain the business-field diff before retrying.
            extensions["changedFields"] = new[] { "version" };
        }

        return Results.Problem(
            detail: exception.Message,
            statusCode: status,
            title: "Corporate action command failed",
            extensions: extensions);
    }

    private sealed record TrustedCorporateActionCommandScope(
        string TenantId,
        string CompanyId,
        string Actor,
        string CorrelationId);
}
