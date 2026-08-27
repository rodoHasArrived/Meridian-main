using System.Text.Json;
using Meridian.Application.SecurityMaster.CorporateActions;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class SecurityMasterEndpoints
{
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
        .Produces<CorporateActionDurableInboxDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // Legacy URL retained for browser compatibility; command semantics are the strong durable
        // accept contract and never consume an in-memory row before the transaction commits.
        group.MapPost(UiApiRoutes.SecurityMasterCorporateActionsInboxApply, async (
            AcceptCorporateActionSourceProposalRequestDto? request,
            HttpContext context,
            [FromServices] ICorporateActionOperationsService service,
            CancellationToken ct) =>
        {
            if (request is null)
            {
                return CorporateActionProblem(
                    context,
                    new CorporateActionValidationException("A durable source-proposal acceptance request is required."));
            }

            if (!TryResolveCorporateActionScope(context, out var trustedScope))
            {
                return CorporateActionProblem(
                    context,
                    new CorporateActionScopeMismatchException(
                        "A tenant- and company-scoped workstation request context is required."));
            }

            if (!MatchesTrustedScope(request.Scope, trustedScope))
            {
                return CorporateActionProblem(
                    context,
                    new CorporateActionScopeMismatchException(
                        "Acceptance scope tenant/company does not match the authenticated workstation scope."));
            }

            if (HasNarrowCorporateActionScope(request.Scope))
            {
                return CorporateActionProblem(
                    context,
                    new CorporateActionScopeMismatchException(
                        "Fund, account, portfolio, custody, ledger-book, basis, and other narrow corporate-action scope fields are denied until the endpoint can resolve them from an authoritative scoped assignment."));
            }

            try
            {
                var trustedRequest = request with
                {
                    Scope = request.Scope with
                    {
                        TenantId = trustedScope.TenantId,
                        CompanyId = trustedScope.CompanyId,
                    },
                    Actor = ResolveActor(context),
                    CorrelationId = context.TraceIdentifier,
                };
                var result = await service.AcceptSourceProposalAsync(trustedRequest, ct).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (CorporateActionOperationException exception)
            {
                return CorporateActionProblem(context, exception);
            }
        })
        .WithName("AcceptSecurityMasterCorporateActionInboxProposal")
        .RequirePermission(UserPermission.ModifySecurityMaster)
        .RequirePermission(UserPermission.ResolveCorporateActionTerms)
        .Accepts<AcceptCorporateActionSourceProposalRequestDto>("application/json")
        .Produces<CorporateActionSourceProposalAcceptanceResultDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
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
        .RequirePermission(UserPermission.ModifySecurityMaster)
        .RequirePermission(UserPermission.ResolveCorporateActionTerms)
        .Produces<CorporateActionSourceProposalAcceptanceResultDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
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

            try
            {
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
        .RequirePermission(UserPermission.ModifySecurityMaster)
        .RequirePermission(UserPermission.ResolveCorporateActionTerms)
        .Produces<CorporateActionSourceProposalDecisionResultDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status409Conflict)
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
                return Results.Json(
                    cases.Where(static item => !HasNarrowCorporateActionScope(item.Scope))
                        .Select(item => ApplyCallerActionAvailability(item, context)).ToArray(),
                    jsonOptions);
            }
            catch (CorporateActionOperationException exception)
            {
                return CorporateActionProblem(context, exception);
            }
        })
        .WithName("ListCorporateActionCases")
        .RequirePermission(UserPermission.ViewCorporateActions)
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
                return processingCase is null
                    ? CorporateActionProblem(
                        context,
                        new CorporateActionNotFoundException("Corporate-action processing case", caseId))
                    : HasNarrowCorporateActionScope(processingCase.Scope)
                        ? CorporateActionProblem(
                            context,
                            new CorporateActionScopeMismatchException(
                                "This case has a narrow scope that this endpoint cannot authoritatively resolve."))
                    : Results.Json(ApplyCallerActionAvailability(processingCase, context), jsonOptions);
            }
            catch (CorporateActionOperationException exception)
            {
                return CorporateActionProblem(context, exception);
            }
        })
        .WithName("GetCorporateActionCase")
        .RequirePermission(UserPermission.ViewCorporateActions)
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
        .Produces<CorporateActionCaseTransitionResultDto>(StatusCodes.Status200OK)
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


        if (HasNarrowCorporateActionScope(request.Scope))
        {
            return CorporateActionProblem(context, new CorporateActionScopeMismatchException(
                "Fund, account, portfolio, custody, ledger-book, basis, and other narrow corporate-action scope fields are denied until the endpoint can resolve them from an authoritative scoped assignment."));
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
            Cases = inbox.Cases.Where(static item => !HasNarrowCorporateActionScope(item.Scope))
                .Select(item => ApplyCallerActionAvailability(item, context)).ToArray(),
        };

    private static CorporateActionSourceProposalDto ApplyCallerActionAvailability(
        CorporateActionSourceProposalDto proposal,
        HttpContext context) =>
        proposal with
        {
            ActionAvailability = ApplyCallerActionAvailability(
                proposal.ActionAvailability
                    ?? new CorporateActionSourceProposalActionAvailabilityDto(false, false, true, ["Action availability was not projected."]),
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
        return availability with
        {
            CanAccept = availability.CanAccept && canResolve,
            CanReject = availability.CanReject && canResolve,
            Blockers = permissionBlocker is null
                ? availability.Blockers
                : availability.Blockers.Concat([permissionBlocker]).Distinct(StringComparer.Ordinal).ToArray(),
        };
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
                // Approval uses its own exact evidence/projection command; this generic surface
                // never advertises approval, even to a Controller.
                CanApproveAccounting = false,
                AllowedTransitionTargets = authorizedTargets,
                Blockers = blockers,
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
