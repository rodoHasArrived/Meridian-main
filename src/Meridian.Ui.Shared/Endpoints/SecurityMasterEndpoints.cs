using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.ReferenceData.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Meridian.Ui.Shared.Services;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using AppSecurityMaster = Meridian.Application.SecurityMaster;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Endpoints for Security Master command/query workflows.
/// </summary>
public static class SecurityMasterEndpoints
{
    public static void MapSecurityMasterEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        // Security Master services are only registered when a PostgreSQL connection string is
        // provided (see StorageFeatureRegistration.IsConfigured). If the service is absent from
        // DI, ASP.NET Core minimal-API route inference would misclassify ISecurityMasterQueryService
        // as [FromBody], causing an InvalidOperationException on the first request to any endpoint.
        if (app.Services.GetService<ISecurityMasterQueryService>() is null)
            return;

        var group = app.MapGroup(string.Empty).WithTags("SecurityMaster");
        group.AddEndpointFilter(RequireViewSecurityMasterPermission);

        /// <summary>
        /// Retrieves a security detail by its internal UUID. Returns full economic definition including terms, identifiers, and status.
        /// </summary>
        /// <remarks>
        /// <para>Returns 404 if the security does not exist.</para>
        /// </remarks>
        group.MapGet(UiApiRoutes.SecurityMasterById, async (
            Guid securityId,
            DateTimeOffset? asOf,
            [FromServices] ISecurityMasterQueryService queryService,
            CancellationToken ct) =>
        {
            var detail = asOf.HasValue
                ? await queryService.GetByIdAsOfAsync(securityId, asOf.Value, ct).ConfigureAwait(false)
                : await queryService.GetByIdAsync(securityId, ct).ConfigureAwait(false);
            return detail is null
                ? Results.NotFound()
                : Results.Json(detail, jsonOptions);
        })
        .WithName("GetSecurityMasterById")
        .Produces<SecurityDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        /// <summary>
        /// Validates a Security Master record without mutating it and returns structured,
        /// operator-actionable issues for downstream run, ledger, reconciliation, and report-pack gates.
        /// </summary>
        group.MapGet(UiApiRoutes.SecurityMasterValidation, async (
            Guid securityId,
            [FromServices] AppSecurityMaster.Validation.ISecurityValidationService validationService,
            CancellationToken ct) =>
        {
            var report = await validationService.ValidateSecurityAsync(securityId, ct).ConfigureAwait(false);
            return Results.Json(report, jsonOptions);
        })
        .WithName("ValidateSecurityMasterRecord")
        .Produces<SecurityValidationReportDto>(StatusCodes.Status200OK);

        /// <summary>
        /// Lists approved custom asset profile definitions available to profile-backed Security Master create/amend workflows.
        /// </summary>
        group.MapGet(UiApiRoutes.SecurityMasterAssetProfiles, (
            [FromServices] ISecurityAssetProfileCatalog profileCatalog) =>
        {
            var profiles = profileCatalog.GetProfiles()
                .OrderBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static profile => profile.Version)
                .ToArray();
            return Results.Json(profiles, jsonOptions);
        })
        .WithName("ListSecurityMasterAssetProfiles")
        .Produces<IReadOnlyList<SecurityAssetProfileDefinitionDto>>(StatusCodes.Status200OK);

        /// <summary>
        /// Lists approved custom asset profile promotion assessments for first-class UFL package design.
        /// </summary>
        group.MapGet(UiApiRoutes.SecurityMasterAssetProfilePromotionCandidates, (
            [FromServices] AppSecurityMaster.ISecurityAssetProfileGovernanceService governanceService) =>
        {
            var candidates = governanceService.GetPromotionCandidates();
            return Results.Json(candidates, jsonOptions);
        })
        .WithName("ListSecurityMasterAssetProfilePromotionCandidates")
        .Produces<IReadOnlyList<SecurityAssetProfilePromotionCandidateDto>>(StatusCodes.Status200OK);

        /// <summary>
        /// Retrieves all versions and governance audit events for one custom asset profile.
        /// </summary>
        group.MapGet(UiApiRoutes.SecurityMasterAssetProfileLineage, (
            string profileId,
            [FromServices] AppSecurityMaster.ISecurityAssetProfileGovernanceService governanceService) =>
        {
            var lineage = governanceService.GetLineage(profileId);
            return lineage.Versions.Count == 0
                ? Results.NotFound()
                : Results.Json(lineage, jsonOptions);
        })
        .WithName("GetSecurityMasterAssetProfileLineage")
        .Produces<SecurityAssetProfileLineageDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        /// <summary>
        /// Stages a draft custom asset profile version for governed approval.
        /// </summary>
        group.MapPost(UiApiRoutes.SecurityMasterAssetProfileDrafts, async (
            SecurityAssetProfileDraftRequestDto? request,
            HttpContext context,
            [FromServices] AppSecurityMaster.ISecurityAssetProfileGovernanceService governanceService) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance))
                return EndpointHelpers.Forbidden();

            if (request is null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = ["A custom asset profile draft request is required."]
                });

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                return Results.Unauthorized();

            try
            {
                var result = await governanceService
                    .DraftProfileAsync(request with { RequestedBy = actor }, actor, context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = [ex.Message]
                });
            }
        })
        .WithName("DraftSecurityMasterAssetProfile")
        .Produces<SecurityAssetProfileGovernanceResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        /// <summary>
        /// Approves a staged custom asset profile draft version for Security Master use.
        /// </summary>
        group.MapPost(UiApiRoutes.SecurityMasterAssetProfileApprove, async (
            SecurityAssetProfileApprovalRequestDto? request,
            HttpContext context,
            [FromServices] AppSecurityMaster.ISecurityAssetProfileGovernanceService governanceService) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance))
                return EndpointHelpers.Forbidden();

            if (request is null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = ["A custom asset profile approval request is required."]
                });

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                return Results.Unauthorized();

            try
            {
                var result = await governanceService
                    .ApproveProfileAsync(request with { RequestedBy = actor }, actor, context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = [ex.Message]
                });
            }
        })
        .WithName("ApproveSecurityMasterAssetProfile")
        .Produces<SecurityAssetProfileGovernanceResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        /// <summary>
        /// Creates a new approved custom asset profile version from an earlier approved or superseded version.
        /// </summary>
        group.MapPost(UiApiRoutes.SecurityMasterAssetProfileRollback, async (
            SecurityAssetProfileRollbackRequestDto? request,
            HttpContext context,
            [FromServices] AppSecurityMaster.ISecurityAssetProfileGovernanceService governanceService) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance))
                return EndpointHelpers.Forbidden();

            if (request is null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = ["A custom asset profile rollback request is required."]
                });

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                return Results.Unauthorized();

            try
            {
                var result = await governanceService
                    .RollbackProfileAsync(request with { RequestedBy = actor }, actor, context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = [ex.Message]
                });
            }
        })
        .WithName("RollbackSecurityMasterAssetProfile")
        .Produces<SecurityAssetProfileGovernanceResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        /// <summary>
        /// Resolves a security by external identifier (ISIN, CUSIP, Ticker, FIGI, SEDOL, etc.).
        /// Supports filtering by provider and active status.
        /// </summary>
        /// <remarks>
        /// <para>Returns 404 if no matching identifier is found or if <c>activeOnly=true</c> and the security is inactive.</para>
        /// <para>Example: POST /api/security-master/resolve with body { "identifierKind": "ISIN", "identifierValue": "US0378331005" }</para>
        /// </remarks>
        group.MapPost(UiApiRoutes.SecurityMasterResolve, async (
            ResolveSecurityRequest request,
            [FromServices] ISecurityMasterQueryService queryService,
            CancellationToken ct) =>
        {
            var detail = await queryService.GetByIdentifierAsync(
                    request.IdentifierKind,
                    request.IdentifierValue,
                    request.Provider,
                    ct,
                    request.AsOfUtc)
                .ConfigureAwait(false);

            if (detail is null)
            {
                return Results.NotFound();
            }

            if (request.ActiveOnly && detail.Status != SecurityStatusDto.Active)
            {
                return Results.NotFound();
            }

            return Results.Json(detail, jsonOptions);
        })
        .WithName("ResolveSecurityMaster")
        .Produces<SecurityDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        /// <summary>
        /// Full-text searches for securities by display name, issuer, identifiers, or profile fields.
        /// Supports active-only filtering, pagination, and profile-backed custom asset filters.
        /// </summary>
        /// <remarks>
        /// <para>Returns a paginated list of security summaries matching the search criteria.</para>
        /// <para>Search is case-insensitive and includes partial matching.</para>
        /// </remarks>
        group.MapPost(UiApiRoutes.SecurityMasterSearch, async (
            SecuritySearchRequest request,
            [FromServices] ISecurityMasterQueryService queryService,
            CancellationToken ct) =>
        {
            if (!HasSecuritySearchCriteria(request))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["query"] = ["Query or a custom asset profile filter is required."]
                });

            if (request.Skip < 0)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["skip"] = ["Skip must be non-negative."]
                });

            var results = await queryService.SearchAsync(request, ct).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("SearchSecurityMaster")
        .Produces<IReadOnlyList<SecuritySummaryDto>>(StatusCodes.Status200OK);

        /// <summary>
        /// Retrieves the event history (audit trail) for a security, including all amendments and state changes.
        /// </summary>
        /// <remarks>
        /// <para>Query parameter <c>take</c> limits results (default: 100). Events are returned in ascending order by sequence.</para>
        /// <para>Returns 404 if the security has no event history.</para>
        /// <para>Supported event types: SecurityCreated, TermsAmended, SecurityDeactivated, IdentifierAdded, CorporateActionRecorded.</para>
        /// </remarks>
        group.MapGet(UiApiRoutes.SecurityMasterHistory, async (
            Guid securityId,
            int? take,
            [FromServices] ISecurityMasterQueryService queryService,
            CancellationToken ct) =>
        {
            var history = await queryService.GetHistoryAsync(
                    new SecurityHistoryRequest(securityId, take.GetValueOrDefault(100)),
                    ct)
                .ConfigureAwait(false);

            return history.Count == 0
                ? Results.NotFound()
                : Results.Json(history, jsonOptions);
        })
        .WithName("GetSecurityMasterHistory")
        .Produces<IReadOnlyList<SecurityMasterEventEnvelope>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        /// <summary>
        /// Creates a new security record with initial asset class-specific terms and identifiers.
        /// </summary>
        /// <remarks>
        /// <para>Returns 201 Created with the new security detail including generated UUID and version 1.</para>
        /// <para>Asset classes: Equity, Bond, Option, Future, FxSpot, Deposit, MoneyMarketFund, CertificateOfDeposit, CommercialPaper, TreasuryBill, Repo, CashSweep, Swap, DirectLoan, OtherSecurity.</para>
        /// <para>At least one identifier (ISIN, CUSIP, Ticker, etc.) is recommended.</para>
        /// </remarks>
        group.MapPost(UiApiRoutes.SecurityMasterCreate, async (
            CreateSecurityRequest request,
            HttpContext context,
            [FromServices] ISecurityMasterService service,
            CancellationToken ct) =>
        {
            var authorizationResult = RequireSecurityMasterMutationPermission(context);
            if (authorizationResult is not null)
                return authorizationResult;

            var detail = await service.CreateAsync(request, ct).ConfigureAwait(false);
            return Results.Json(detail, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("CreateSecurityMaster")
        .Produces<SecurityDetailDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        /// <summary>
        /// Amends the terms (economic definition) of an existing security with optimistic concurrency control.
        /// </summary>
        /// <remarks>
        /// <para>Must provide the current version number. If the version no longer matches, returns 409 Conflict.</para>
        /// <para>Amended terms create a new event in the audit trail and increment the version by 1.</para>
        /// <para>Supports all asset class-specific term updates (coupon, strike, maturity, etc.).</para>
        /// </remarks>
        group.MapPost(UiApiRoutes.SecurityMasterAmend, async (
            AmendSecurityTermsRequest request,
            HttpContext context,
            [FromServices] ISecurityMasterService service,
            CancellationToken ct) =>
        {
            var authorizationResult = RequireSecurityMasterMutationPermission(context);
            if (authorizationResult is not null)
                return authorizationResult;

            var detail = await service.AmendTermsAsync(request, ct).ConfigureAwait(false);
            return Results.Json(detail, jsonOptions);
        })
        .WithName("AmendSecurityMaster")
        .Produces<SecurityDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        /// <summary>
        /// Marks a security as inactive (soft delete). The security record remains in the database for audit purposes.
        /// </summary>
        /// <remarks>
        /// <para>Returns 204 No Content on success. Deactivation creates an event in the audit trail.</para>
        /// <para>Inactive securities are excluded from active-only searches and queries by default.</para>
        /// <para>Cannot be undone; create a new security if reactivation is needed.</para>
        /// </remarks>
        group.MapPost(UiApiRoutes.SecurityMasterDeactivate, async (
            DeactivateSecurityRequest request,
            HttpContext context,
            [FromServices] ISecurityMasterService service,
            CancellationToken ct) =>
        {
            var authorizationResult = RequireSecurityMasterMutationPermission(context);
            if (authorizationResult is not null)
                return authorizationResult;

            await service.DeactivateAsync(request, ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithName("DeactivateSecurityMaster")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        /// <summary>
        /// Adds or updates an external identifier (alias) for a security, supporting multi-provider symbol mapping.
        /// </summary>
        /// <remarks>
        /// <para>Upsert: if an identifier with the same kind and provider exists, it is updated; otherwise, a new alias is created.</para>
        /// <para>Supported identifier kinds: ISIN, CUSIP, Ticker, FIGI, SEDOL, LEI, RIC, Bloomberg ID, etc.</para>
        /// <para>Returns 200 OK with the upserted alias detail.</para>
        /// </remarks>
        group.MapPost(UiApiRoutes.SecurityMasterAliasesUpsert, async (
            UpsertSecurityAliasRequest request,
            HttpContext context,
            [FromServices] ISecurityMasterService service,
            CancellationToken ct) =>
        {
            var authorizationResult = RequireSecurityMasterMutationPermission(context);
            if (authorizationResult is not null)
                return authorizationResult;

            var alias = await service.UpsertAliasAsync(request, ct).ConfigureAwait(false);
            return Results.Json(alias, jsonOptions);
        })
        .WithName("UpsertSecurityMasterAlias")
        .Produces<SecurityAliasDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        /// <summary>
        /// Retrieves trading parameters for a security at the current time: lot size, tick size, and status.
        /// </summary>
        /// <remarks>
        /// <para>Returns 404 if the security does not exist or has expired.</para>
        /// <para>Trading parameters are extracted from the security's economic definition and applied to order routing and fill models.</para>
        /// <para>Useful for backtest and execution pipeline initialization.</para>
        /// </remarks>
        group.MapGet(UiApiRoutes.SecurityMasterTradingParameters, async (
            Guid securityId,
            [FromServices] ISecurityMasterQueryService queryService,
            CancellationToken ct) =>
        {
            var parameters = await queryService
                .GetTradingParametersAsync(securityId, DateTimeOffset.UtcNow, ct)
                .ConfigureAwait(false);
            return parameters is null
                ? Results.NotFound()
                : Results.Json(parameters, jsonOptions);
        })
        .WithName("GetSecurityMasterTradingParameters")
        .Produces<TradingParametersDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        /// <summary>
        /// Retrieves the current preferred-equity term definition for a security when its classification includes preferred terms.
        /// </summary>
        /// <remarks>
        /// <para>Returns 404 for non-equity securities or equities without preferred terms.</para>
        /// <para>This is the current term snapshot only; dividend schedules, yield projections, and execution history are separate follow-on APIs.</para>
        /// </remarks>
        group.MapGet(UiApiRoutes.SecurityMasterPreferredEquityTerms, async (
            Guid securityId,
            [FromServices] ISecurityMasterQueryService queryService,
            CancellationToken ct) =>
        {
            var terms = await queryService.GetPreferredEquityTermsAsync(securityId, ct).ConfigureAwait(false);
            return terms is null
                ? Results.NotFound()
                : Results.Json(terms, jsonOptions);
        })
        .WithName("GetSecurityMasterPreferredEquityTerms")
        .Produces<PreferredEquityTermsDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        /// <summary>
        /// Replaces the preferred-equity term definition for a security while preserving any non-preferred equity metadata already attached to the security.
        /// </summary>
        /// <remarks>
        /// <para>Returns 404 for missing securities or equities without preferred terms.</para>
        /// <para>This route updates the current preferred term snapshot only; dividend schedule and yield projections remain separate follow-on APIs.</para>
        /// </remarks>
        group.MapPatch(UiApiRoutes.SecurityMasterPreferredEquityTerms, async (
            Guid securityId,
            AmendPreferredEquityTermsRequest request,
            HttpContext context,
            [FromServices] ISecurityMasterQueryService queryService,
            [FromServices] ISecurityMasterService service,
            CancellationToken ct) =>
        {
            var currentTerms = await queryService.GetPreferredEquityTermsAsync(securityId, ct).ConfigureAwait(false);
            if (currentTerms is null)
            {
                return Results.NotFound();
            }

            var detail = await service
                .AmendPreferredEquityTermsAsync(securityId, request, ct)
                .ConfigureAwait(false);

            return Results.Json(detail, jsonOptions);
        })
        .WithName("AmendSecurityMasterPreferredEquityTerms")
        .Accepts<AmendPreferredEquityTermsRequest>("application/json")
        .Produces<SecurityDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        /// <summary>
        /// Retrieves the current convertible-equity term definition for a security when its classification includes conversion terms.
        /// </summary>
        /// <remarks>
        /// <para>Returns 404 for non-equity securities or equities without convertible terms.</para>
        /// <para>This returns the stored conversion terms snapshot; price-derived parity and in-the-money calculations remain separate follow-on APIs.</para>
        /// </remarks>
        group.MapGet(UiApiRoutes.SecurityMasterConvertibleEquityTerms, async (
            Guid securityId,
            [FromServices] ISecurityMasterQueryService queryService,
            CancellationToken ct) =>
        {
            var terms = await queryService.GetConvertibleEquityTermsAsync(securityId, ct).ConfigureAwait(false);
            return terms is null
                ? Results.NotFound()
                : Results.Json(terms, jsonOptions);
        })
        .WithName("GetSecurityMasterConvertibleEquityTerms")
        .Produces<ConvertibleEquityTermsDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        /// <summary>
        /// Replaces the convertible-equity term definition for a security while preserving any non-convertible equity metadata already attached to the security.
        /// </summary>
        /// <remarks>
        /// <para>Returns 404 for missing securities or equities without convertible terms.</para>
        /// <para>This route updates the current convertible term snapshot only.</para>
        /// </remarks>
        group.MapPatch(UiApiRoutes.SecurityMasterConvertibleEquityTerms, async (
            Guid securityId,
            AmendConvertibleEquityTermsRequest request,
            HttpContext context,
            [FromServices] ISecurityMasterQueryService queryService,
            [FromServices] ISecurityMasterService service,
            CancellationToken ct) =>
        {
            var currentTerms = await queryService.GetConvertibleEquityTermsAsync(securityId, ct).ConfigureAwait(false);
            if (currentTerms is null)
            {
                return Results.NotFound();
            }

            var detail = await service
                .AmendConvertibleEquityTermsAsync(securityId, request, ct)
                .ConfigureAwait(false);

            return Results.Json(detail, jsonOptions);
        })
        .WithName("AmendSecurityMasterConvertibleEquityTerms")
        .Accepts<AmendConvertibleEquityTermsRequest>("application/json")
        .Produces<SecurityDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        /// <summary>
        /// Retrieves all corporate action events for a security, sorted by ex-date (dividend, split, merger, etc.).
        /// </summary>
        /// <remarks>
        /// <para>Returns an empty list if no corporate actions are recorded.</para>
        /// <para>Supported corporate action types: Dividend, StockSplit, SpinOff, MergerAbsorption, RightsIssue, and others.</para>
        /// <para>Used by backtesting and price adjustment workflows to normalize historical prices.</para>
        /// </remarks>
        group.MapGet(UiApiRoutes.SecurityMasterCorporateActions, async (
            Guid securityId,
            [FromServices] ISecurityMasterQueryService queryService,
            CancellationToken ct) =>
        {
            var actions = await queryService
                .GetCorporateActionsAsync(securityId, ct)
                .ConfigureAwait(false);
            return Results.Json(actions, jsonOptions);
        })
        .WithName("GetSecurityMasterCorporateActions")
        .Produces<IReadOnlyList<CorporateActionDto>>(StatusCodes.Status200OK);

        /// <summary>
        /// Records a new corporate action event for a security (dividend, split, merger, etc.).
        /// </summary>
        /// <remarks>
        /// <para>Body must include SecurityId matching the route parameter, ex-date, and action-specific payload.</para>
        /// <para>Returns 200 OK on successful append. Returns 400 Bad Request if SecurityId in body does not match route parameter.</para>
        /// <para>Events are immutable once recorded; they form the basis of historical price adjustments in backtests.</para>
        /// </remarks>
        group.MapPost(UiApiRoutes.SecurityMasterCorporateActions, async (
            Guid securityId,
            CorporateActionDto dto,
            HttpContext context,
            [FromServices] ISecurityMasterCorporateActionCommandService commandService,
            CancellationToken ct) =>
        {
            try
            {
                var actor = ResolveActor(context);
                var result = await commandService.AppendAsync(
                    new SecurityMasterCorporateActionAppendRequestDto(
                        securityId,
                        dto,
                        SourceSystem: "workstation-http",
                        Actor: actor,
                        SourceRecordId: null,
                        Reason: "HTTP corporate action append",
                        CorrelationId: context.TraceIdentifier),
                    ct).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        })
        .WithName("AppendSecurityMasterCorporateAction")
        .Accepts<CorporateActionDto>("application/json")
        .Produces<SecurityMasterCorporateActionAppendResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        /// <summary>
        /// Runs provider-backed corporate-action ingest across mastered ticker symbols.
        /// </summary>
        group.MapPost(UiApiRoutes.SecurityMasterCorporateActionsIngest, async (
            AppSecurityMaster.CorporateActions.CorporateActionIngestRequest? request,
            HttpContext context,
            [FromServices] AppSecurityMaster.CorporateActions.CorporateActionIngestOrchestrator orchestrator,
            CancellationToken ct) =>
        {
            var actor = ResolveActor(context);
            var effectiveRequest = (request ?? new AppSecurityMaster.CorporateActions.CorporateActionIngestRequest()) with
            {
                Actor = actor,
                CorrelationId = context.TraceIdentifier
            };

            var result = await orchestrator.IngestAsync(effectiveRequest, ct).ConfigureAwait(false);
            context.RequestServices.GetService<AppSecurityMaster.CorporateActions.CorporateActionInboxState>()?.Record(result);
            return Results.Json(result, jsonOptions);
        })
        .WithName("IngestSecurityMasterCorporateActions")
        .Accepts<AppSecurityMaster.CorporateActions.CorporateActionIngestRequest>("application/json")
        .Produces<AppSecurityMaster.CorporateActions.CorporateActionIngestResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        /// <summary>
        /// Returns staged corporate-action proposals from the most recent ingest sweep for
        /// the workbench inbox badge and review list.
        /// </summary>
        group.MapGet(UiApiRoutes.SecurityMasterCorporateActionsInbox, (
            [FromServices] AppSecurityMaster.CorporateActions.CorporateActionInboxState inboxState) =>
            Results.Json(inboxState.GetInbox(), jsonOptions))
        .WithName("GetSecurityMasterCorporateActionInbox")
        .Produces<AppSecurityMaster.CorporateActions.CorporateActionInboxDto>(StatusCodes.Status200OK);

        /// <summary>
        /// Applies one staged inbox proposal: consumes it from the snapshot and appends the
        /// corporate action through the governed command service under the operator's identity.
        /// </summary>
        group.MapPost(UiApiRoutes.SecurityMasterCorporateActionsInboxApply, async (
            AppSecurityMaster.CorporateActions.CorporateActionInboxApplyRequest? request,
            HttpContext context,
            [FromServices] AppSecurityMaster.CorporateActions.CorporateActionInboxState inboxState,
            [FromServices] ISecurityMasterCorporateActionCommandService commandService,
            CancellationToken ct) =>
        {
            if (request is null)
                return Results.BadRequest("An apply request is required.");

            var actor = ResolveActor(context);
            if (!inboxState.TryTakeStaged(request.SecurityId, request.ActionType, request.ExDate, out var proposal))
                return Results.NotFound("No staged proposal matches the requested security, action type, and ex-date.");

            try
            {
                var result = await commandService.AppendAsync(
                    new SecurityMasterCorporateActionAppendRequestDto(
                        SecurityId: proposal.SecurityId,
                        CorporateAction: AppSecurityMaster.CorporateActions.CorporateActionProposalMapper.ToCorporateAction(proposal),
                        SourceSystem: proposal.WinningSource,
                        Actor: actor,
                        SourceRecordId: $"{proposal.Ticker}:{proposal.ActionType}:{proposal.ExDate:yyyyMMdd}:{proposal.WinningSource}",
                        Reason: proposal.DissentingSources.Count == 0
                            ? "Operator applied staged corporate-action proposal from the inbox."
                            : $"Operator applied staged proposal over dissent from {string.Join(", ", proposal.DissentingSources)}.",
                        CorrelationId: context.TraceIdentifier),
                    ct).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        })
        .WithName("ApplySecurityMasterCorporateActionInboxProposal")
        .Accepts<AppSecurityMaster.CorporateActions.CorporateActionInboxApplyRequest>("application/json")
        .Produces<SecurityMasterCorporateActionAppendResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        /// <summary>
        /// Pre-builds a machine-proposed security-master draft for an unmastered symbol so the
        /// operator can review and submit it instead of typing the record from scratch.
        /// </summary>
        group.MapGet(UiApiRoutes.SecurityMasterCoverageDraft, async (
            string symbol,
            [FromServices] AppSecurityMaster.SecurityMasterDraftProposalService draftService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return Results.BadRequest("A symbol is required.");

            var draft = await draftService.BuildDraftAsync(symbol, ct).ConfigureAwait(false);
            return Results.Json(draft, jsonOptions);
        })
        .WithName("GetSecurityMasterCoverageDraft")
        .Produces<AppSecurityMaster.SecurityMasterDraftProposalDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        // GET /api/security-master/conflicts
        group.MapGet(UiApiRoutes.SecurityMasterConflicts, async (
            HttpContext context,
            [FromServices] AppSecurityMaster.ISecurityMasterConflictService conflictService,
            CancellationToken ct) =>
        {
            var conflicts = await conflictService.GetOpenConflictsAsync(ct).ConfigureAwait(false);
            if (context.RequestServices.GetService<SecurityMasterExceptionCaseworkService>() is { } casework)
            {
                var actor = context.Items[LoginSessionMiddleware.CurrentUserKey] as string;
                await casework.SeedOpenConflictCasesAsync(conflicts, actor, ct).ConfigureAwait(false);
            }

            return Results.Json(conflicts, jsonOptions);
        })
        .WithName("GetSecurityMasterConflicts")
        .Produces<IReadOnlyList<SecurityMasterConflict>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        // POST /api/security-master/conflicts/{conflictId}/resolve
        group.MapPost(UiApiRoutes.SecurityMasterConflictResolve, async (
            Guid conflictId,
            ResolveConflictRequest request,
            HttpContext context,
            [FromServices] AppSecurityMaster.ISecurityMasterConflictService conflictService,
            CancellationToken ct) =>
        {
            if (context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] is not UserPermission permissions)
            {
                return Results.Unauthorized();
            }

            if ((permissions & UserPermission.ModifySecurityMaster) != UserPermission.ModifySecurityMaster)
            {
                return Results.Forbid();
            }

            if (request.ConflictId != conflictId)
                return Results.BadRequest(ErrorResponse.Validation(
                    "ConflictId in body must match the route parameter."));

            var resolvedBy = context.Items[LoginSessionMiddleware.CurrentUserKey] as string ?? "unknown";
            var serverRequest = request with { ResolvedBy = resolvedBy };

            var updated = await conflictService.ResolveAsync(serverRequest, ct).ConfigureAwait(false);
            if (updated is not null &&
                context.RequestServices.GetService<SecurityMasterExceptionCaseworkService>() is { } casework)
            {
                await casework.ApplyResolvedConflictAsync(updated, serverRequest, ct).ConfigureAwait(false);
            }

            return updated is null
                ? Results.NotFound()
                : Results.Json(updated, jsonOptions);
        })
        .WithName("ResolveSecurityMasterConflict")
        .Produces<SecurityMasterConflict>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        // POST /api/security-master/import
        group.MapPost(UiApiRoutes.SecurityMasterImport, async (
            SecurityMasterImportRequest request,
            HttpContext context,
            [FromServices] AppSecurityMaster.ISecurityMasterImportService importService,
            CancellationToken ct) =>
        {
            if (context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] is not UserPermission permissions)
            {
                return Results.Unauthorized();
            }

            if ((permissions & UserPermission.ModifySecurityMaster) != UserPermission.ModifySecurityMaster)
            {
                return Results.Forbid();
            }

            var result = await importService.ImportAsync(
                request.FileContent,
                request.FileExtension,
                progress: null,
                ct: ct).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("ImportSecurityMaster")
        .Produces<AppSecurityMaster.SecurityMasterImportResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        // GET /api/security-master/ingest/status
        group.MapGet(UiApiRoutes.SecurityMasterIngestStatus, async (
            [FromServices] AppSecurityMaster.ISecurityMasterConflictService conflictService,
            [FromServices] AppSecurityMaster.ISecurityMasterIngestStatusService ingestStatusService,
            CancellationToken ct) =>
        {
            var openConflicts = await conflictService.GetOpenConflictsAsync(ct).ConfigureAwait(false);
            var snapshot = ingestStatusService.GetSnapshot();
            var response = ToIngestStatusResponse(snapshot, openConflicts.Count);
            return Results.Json(response, jsonOptions);
        })
        .WithName("SecurityMasterIngestStatus")
        .Produces<SecurityMasterIngestStatusResponse>(StatusCodes.Status200OK);

        /// <summary>
        /// Retrieves operator-supplied per-security overrides used by the workstation security
        /// details view (free-form key/value strings such as ratings or sector overrides).
        /// </summary>
        /// <remarks>
        /// <para>Returns an empty payload (with default <c>UpdatedBy</c>/<c>UpdatedAt</c>) when no
        /// overrides have been recorded for the security.</para>
        /// </remarks>
        group.MapGet(UiApiRoutes.SecurityMasterOperatorOverrides, async (
            Guid securityId,
            [FromServices] IOperatorOverridesStore store,
            CancellationToken ct) =>
        {
            var overrides = await store.GetAsync(securityId, ct).ConfigureAwait(false);
            overrides ??= new OperatorOverridesDto(
                securityId,
                new Dictionary<string, string>(),
                string.Empty,
                DateTimeOffset.MinValue);
            return Results.Json(overrides, jsonOptions);
        })
        .WithName("GetSecurityMasterOperatorOverrides")
        .Produces<OperatorOverridesDto>(StatusCodes.Status200OK);

        /// <summary>
        /// Applies a partial update to operator overrides for a security. Values listed in
        /// <c>SetValues</c> are upserted; keys in <c>RemoveKeys</c> are deleted. Requires the
        /// <c>ModifySecurityMaster</c> permission.
        /// </summary>
        /// <remarks>
        /// <para>Returns the merged overrides snapshot after the patch is applied.</para>
        /// </remarks>
        group.MapPatch(UiApiRoutes.SecurityMasterOperatorOverrides, async (
            Guid securityId,
            OperatorOverridesPatchRequest request,
            HttpContext context,
            [FromServices] IOperatorOverridesStore store,
            CancellationToken ct) =>
        {
            var actor = ResolveActor(context);
            var updated = await store.PatchAsync(securityId, request, actor, ct).ConfigureAwait(false);
            if (context.RequestServices.GetService<SecurityMasterExceptionCaseworkService>() is { } casework)
            {
                await casework.SeedOperatorOverrideCaseAsync(updated, actor, ct).ConfigureAwait(false);
            }

            return Results.Json(updated, jsonOptions);
        })
        .WithName("PatchSecurityMasterOperatorOverrides")
        .Accepts<OperatorOverridesPatchRequest>("application/json")
        .Produces<OperatorOverridesDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        /// <summary>
        /// Records a reviewer's approve/reject decision for a security's pending operator overrides,
        /// transitioning the persisted approval status and appending to the durable audit trail.
        /// Requires the <c>ModifySecurityMaster</c> permission; the reviewer is server-derived from the
        /// authenticated principal.
        /// </summary>
        /// <remarks>
        /// <para>Returns the merged overrides snapshot after the decision is applied, <c>404</c> when
        /// no overrides exist for the security, or <c>400</c> when the decision is not Approved or
        /// Rejected.</para>
        /// </remarks>
        group.MapPost(UiApiRoutes.SecurityMasterOperatorOverrideDecision, async (
            Guid securityId,
            OperatorOverrideApprovalDecisionRequest request,
            HttpContext context,
            [FromServices] IOperatorOverridesStore store,
            CancellationToken ct) =>
        {
            var reviewer = ResolveActor(context);
            try
            {
                var updated = await store
                    .RecordApprovalDecisionAsync(securityId, request, reviewer, ct)
                    .ConfigureAwait(false);
                return updated is null ? Results.NotFound() : Results.Json(updated, jsonOptions);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        })
        .WithName("RecordSecurityMasterOperatorOverrideDecision")
        .Accepts<OperatorOverrideApprovalDecisionRequest>("application/json")
        .Produces<OperatorOverridesDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        // PATCH /api/security-master/equities/{securityId}/preferred-terms
        group.MapMethods("/api/security-master/equities/{securityId:guid}/preferred-terms", [HttpMethods.Patch], async (
            Guid securityId,
            AmendPreferredEquityTermsRequest request,
            HttpContext context,
            [FromServices] ISecurityMasterQueryService queryService,
            [FromServices] ISecurityMasterService service,
            CancellationToken ct) =>
        {
            var existing = await queryService.GetPreferredEquityTermsAsync(securityId, ct).ConfigureAwait(false);
            if (existing is null)
                return Results.NotFound();

            var detail = await service.AmendPreferredEquityTermsAsync(securityId, request, ct).ConfigureAwait(false);
            return Results.Json(detail, jsonOptions);
        })
        .WithName("PatchSecurityPreferredTerms")
        .Produces<SecurityDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        // Clearwater-model extensions. These map only when a real (non-null) implementation is
        // registered: when Security Master is unconfigured the NullSecurityMaster* fallbacks are
        // still present in DI, and their write/run paths throw, so mapping against them would turn
        // requests into 500s instead of leaving the routes unavailable.
        if (app.Services.GetService<ISecurityMasterPricingService>()
            is not (null or AppSecurityMaster.NullSecurityMasterPricingService))
            MapPricingEndpoints(group, jsonOptions);

        if (app.Services.GetService<ISecurityMasterCashFlowService>()
            is not (null or AppSecurityMaster.NullSecurityMasterCashFlowService))
            MapCashFlowEndpoints(group, jsonOptions);

        if (app.Services.GetService<IDataVendorEntitlementService>()
            is not (null or AppSecurityMaster.NullDataVendorEntitlementService))
            MapEntitlementEndpoints(group, jsonOptions);

        if (app.Services.GetService<ISecurityMasterDataQualityService>()
            is not (null or AppSecurityMaster.NullSecurityMasterDataQualityService))
            MapQualityReportEndpoints(group, jsonOptions);

        if (app.Services.GetService<SecurityMasterExceptionCaseworkService>() is not null)
            MapExceptionAgingEndpoints(group, jsonOptions);
    }

    private static ValueTask<object?> RequireModifySecurityMasterPermission(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (HasModifySecurityMasterPermission(context.HttpContext))
        {
            return next(context);
        }

        return ValueTask.FromResult<object?>(EndpointHelpers.Forbidden());
    }

    private static ValueTask<object?> RequireViewSecurityMasterPermission(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (!EndpointAuthorization.TryGetPermissions(context.HttpContext, out _))
        {
            return ValueTask.FromResult<object?>(Results.Unauthorized());
        }

        if (EndpointAuthorization.HasAnyPermission(
                context.HttpContext,
                UserPermission.ViewSecurityMaster,
                UserPermission.ModifySecurityMaster))
        {
            return next(context);
        }

        return ValueTask.FromResult<object?>(EndpointHelpers.Forbidden());
    }

    private static bool HasModifySecurityMasterPermission(HttpContext context)
        => EndpointAuthorization.HasPermission(context, UserPermission.ModifySecurityMaster);

    private static bool HasSecuritySearchCriteria(SecuritySearchRequest request)
        => !string.IsNullOrWhiteSpace(request.Query)
           || !string.IsNullOrWhiteSpace(request.CustomProfileId)
           || request.ProfileVersion.HasValue
           || !string.IsNullOrWhiteSpace(request.ProfileFieldKey)
           || !string.IsNullOrWhiteSpace(request.ProfileFieldValue);

    private static string ResolveActor(HttpContext context)
    {
        if (EndpointAuthorization.TryResolveActor(context, out var username))
        {
            return username;
        }

        throw new BadHttpRequestException("Authenticated actor is required for Security Master mutations.", StatusCodes.Status401Unauthorized);
    }

    private static IResult? RequireSecurityMasterMutationPermission(HttpContext context)
    {
        if (context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] is not UserPermission permissions)
            return Results.Unauthorized();

        return (permissions & UserPermission.ModifySecurityMaster) != UserPermission.ModifySecurityMaster
            ? Results.Forbid()
            : null;
    }

    private static SecurityMasterIngestStatusResponse ToIngestStatusResponse(
        AppSecurityMaster.SecurityMasterIngestStatusSnapshot snapshot,
        int openConflicts)
    {
        return new SecurityMasterIngestStatusResponse
        {
            OpenConflicts = openConflicts,
            IsImportActive = snapshot.ActiveImport is not null,
            ActiveImport = snapshot.ActiveImport is null
                ? null
                : new SecurityMasterActiveImportStatusResponse
                {
                    FileExtension = snapshot.ActiveImport.FileExtension,
                    Total = snapshot.ActiveImport.Total,
                    Processed = snapshot.ActiveImport.Processed,
                    Imported = snapshot.ActiveImport.Imported,
                    Skipped = snapshot.ActiveImport.Skipped,
                    Failed = snapshot.ActiveImport.Failed,
                    StartedAtUtc = snapshot.ActiveImport.StartedAtUtc,
                    UpdatedAtUtc = snapshot.ActiveImport.UpdatedAtUtc
                },
            LastCompleted = snapshot.LastCompleted is null
                ? null
                : new SecurityMasterCompletedImportStatusResponse
                {
                    FileExtension = snapshot.LastCompleted.FileExtension,
                    Total = snapshot.LastCompleted.Total,
                    Processed = snapshot.LastCompleted.Processed,
                    Imported = snapshot.LastCompleted.Imported,
                    Skipped = snapshot.LastCompleted.Skipped,
                    Failed = snapshot.LastCompleted.Failed,
                    ConflictsDetected = snapshot.LastCompleted.ConflictsDetected,
                    ErrorCount = snapshot.LastCompleted.ErrorCount,
                    StartedAtUtc = snapshot.LastCompleted.StartedAtUtc,
                    CompletedAtUtc = snapshot.LastCompleted.CompletedAtUtc
                },
            RetrievedAtUtc = DateTimeOffset.UtcNow
        };
    }

    // ── Pricing Hierarchy & Golden Copy ──────────────────────────────────────

    private static void MapPricingEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(UiApiRoutes.SecurityMasterPricingHierarchy, async (
            Guid securityId,
            string? accountId,
            [FromServices] ISecurityMasterPricingService pricingService,
            CancellationToken ct) =>
        {
            var hierarchy = await pricingService
                .GetPricingHierarchyAsync(securityId, accountId, ct).ConfigureAwait(false);
            return hierarchy is null ? Results.NotFound() : Results.Json(hierarchy, jsonOptions);
        })
        .WithName("GetSecurityMasterPricingHierarchy")
        .Produces<SecurityPricingHierarchyDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPut(UiApiRoutes.SecurityMasterPricingHierarchy, async (
            Guid securityId,
            SecurityPricingHierarchyDto? request,
            HttpContext context,
            [FromServices] ISecurityMasterPricingService pricingService,
            CancellationToken ct) =>
        {
            if (request is null || request.SecurityId != securityId)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["securityId"] = ["Request SecurityId must match the route parameter."]
                });

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                return Results.Unauthorized();

            await pricingService
                .UpsertPricingHierarchyAsync(request with { UpdatedBy = actor }, ct)
                .ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithName("UpsertSecurityMasterPricingHierarchy")
        .Accepts<SecurityPricingHierarchyDto>("application/json")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        group.MapPost(UiApiRoutes.SecurityMasterRecordRawPrice, async (
            Guid securityId,
            RecordRawPriceRequest? request,
            HttpContext context,
            [FromServices] ISecurityMasterPricingService pricingService,
            CancellationToken ct) =>
        {
            if (request is null || request.SecurityId != securityId)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["securityId"] = ["Request SecurityId must match the route parameter."]
                });

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                return Results.Unauthorized();

            await pricingService
                .RecordRawPriceAsync(request with { RecordedBy = actor }, ct)
                .ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithName("RecordSecurityMasterRawPrice")
        .Accepts<RecordRawPriceRequest>("application/json")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        group.MapGet(UiApiRoutes.SecurityMasterPriceGoldenCopy, async (
            Guid securityId,
            string? accountId,
            [FromServices] ISecurityMasterPricingService pricingService,
            CancellationToken ct) =>
        {
            var golden = await pricingService
                .GetGoldenCopyPriceAsync(securityId, accountId, ct).ConfigureAwait(false);
            return golden is null ? Results.NotFound() : Results.Json(golden, jsonOptions);
        })
        .WithName("GetSecurityMasterPriceGoldenCopy")
        .Produces<SecurityPriceGoldenCopyDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(UiApiRoutes.SecurityMasterPriceComparison, async (
            Guid securityId,
            [FromServices] ISecurityMasterPricingService pricingService,
            CancellationToken ct) =>
        {
            var comparison = await pricingService
                .GetComparisonPricesAsync(securityId, ct).ConfigureAwait(false);
            return Results.Json(comparison, jsonOptions);
        })
        .WithName("GetSecurityMasterPriceComparison")
        .Produces<IReadOnlyList<SecurityComparisonPriceDto>>(StatusCodes.Status200OK);
    }

    // ── Cash Flow Source Assignments ─────────────────────────────────────────

    private static void MapCashFlowEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(UiApiRoutes.SecurityMasterCashFlowSource, async (
            Guid securityId,
            [FromServices] ISecurityMasterCashFlowService cashFlowService,
            CancellationToken ct) =>
        {
            var source = await cashFlowService
                .GetCashFlowSourceAsync(securityId, ct).ConfigureAwait(false);
            return source is null ? Results.NotFound() : Results.Json(source, jsonOptions);
        })
        .WithName("GetSecurityMasterCashFlowSource")
        .Produces<SecurityCashFlowSourceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPut(UiApiRoutes.SecurityMasterCashFlowSource, async (
            Guid securityId,
            UpsertCashFlowSourceRequest? request,
            HttpContext context,
            [FromServices] ISecurityMasterCashFlowService cashFlowService,
            CancellationToken ct) =>
        {
            if (request is null || request.SecurityId != securityId)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["securityId"] = ["Request SecurityId must match the route parameter."]
                });

            if (!EndpointAuthorization.TryResolveActor(context, out _))
                return Results.Unauthorized();

            await cashFlowService.UpsertCashFlowSourceAsync(request, ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithName("UpsertSecurityMasterCashFlowSource")
        .Accepts<UpsertCashFlowSourceRequest>("application/json")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        group.MapGet(UiApiRoutes.SecurityMasterCashFlowProjections, async (
            Guid securityId,
            string? scenario,
            [FromServices] ISecurityMasterCashFlowService cashFlowService,
            CancellationToken ct) =>
        {
            var parsed = Enum.TryParse<StructuredCashFlowScenario>(scenario, ignoreCase: true, out var s)
                ? s
                : StructuredCashFlowScenario.Base;

            var projection = await cashFlowService
                .GetProjectionAsync(securityId, parsed, ct).ConfigureAwait(false);
            return projection is null ? Results.NotFound() : Results.Json(projection, jsonOptions);
        })
        .WithName("GetSecurityMasterCashFlowProjections")
        .Produces<StructuredCashFlowProjectionDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);
    }

    // ── Data Vendor Entitlements ─────────────────────────────────────────────

    private static void MapEntitlementEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(UiApiRoutes.DataVendorEntitlements, async (
            [FromServices] IDataVendorEntitlementService entitlementService,
            CancellationToken ct) =>
        {
            var all = await entitlementService.GetAllAsync(ct).ConfigureAwait(false);
            return Results.Json(all, jsonOptions);
        })
        .WithName("GetDataVendorEntitlements")
        .Produces<IReadOnlyList<DataVendorEntitlementDto>>(StatusCodes.Status200OK);

        group.MapGet(UiApiRoutes.DataVendorEntitlementsExpiring, async (
            int withinDays,
            [FromServices] IDataVendorEntitlementService entitlementService,
            CancellationToken ct) =>
        {
            var expiring = await entitlementService
                .GetExpiringAsync(withinDays <= 0 ? 30 : withinDays, ct).ConfigureAwait(false);
            return Results.Json(expiring, jsonOptions);
        })
        .WithName("GetExpiringDataVendorEntitlements")
        .Produces<IReadOnlyList<DataVendorEntitlementDto>>(StatusCodes.Status200OK);

        group.MapPost(UiApiRoutes.DataVendorEntitlements, async (
            UpsertDataVendorEntitlementRequest? request,
            HttpContext context,
            [FromServices] IDataVendorEntitlementService entitlementService,
            CancellationToken ct) =>
        {
            if (request is null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = ["A data vendor entitlement request is required."]
                });

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                return Results.Unauthorized();

            var result = await entitlementService
                .UpsertAsync(request with { Actor = actor }, ct).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("CreateDataVendorEntitlement")
        .Accepts<UpsertDataVendorEntitlementRequest>("application/json")
        .Produces<DataVendorEntitlementDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        group.MapDelete(UiApiRoutes.DataVendorEntitlementById, async (
            Guid entitlementId,
            HttpContext context,
            [FromServices] IDataVendorEntitlementService entitlementService,
            CancellationToken ct) =>
        {
            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                return Results.Unauthorized();

            try
            {
                await entitlementService.DeactivateAsync(entitlementId, actor, ct).ConfigureAwait(false);
                return Results.NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(ex.Message);
            }
        })
        .WithName("DeactivateDataVendorEntitlement")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);
    }

    // ── Data Quality Reports ─────────────────────────────────────────────────

    private static void MapQualityReportEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapPost(UiApiRoutes.SecurityMasterQualityReportRun, async (
            HttpContext context,
            [FromServices] ISecurityMasterDataQualityService qualityService,
            [FromServices] SecurityMasterExceptionCaseworkService caseworkService,
            CancellationToken ct) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance))
                return EndpointHelpers.Forbidden();

            var report = await qualityService.RunQualityChecksAsync(ct).ConfigureAwait(false);
            var actor = EndpointAuthorization.TryResolveActor(context, out var username) ? username : null;
            await caseworkService.SyncQualityViolationCasesAsync(report, actor, ct).ConfigureAwait(false);
            return Results.Json(report, jsonOptions);
        })
        .WithName("RunSecurityMasterQualityReport")
        .Produces<SecurityMasterQualityReportDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapGet(UiApiRoutes.SecurityMasterQualityReportLatest, async (
            [FromServices] ISecurityMasterDataQualityService qualityService,
            CancellationToken ct) =>
        {
            var report = await qualityService.GetLatestReportAsync(ct).ConfigureAwait(false);
            return report is null ? Results.NotFound() : Results.Json(report, jsonOptions);
        })
        .WithName("GetSecurityMasterQualityReportLatest")
        .Produces<SecurityMasterQualityReportDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);
    }

    // ── Exception Aging / SLA ────────────────────────────────────────────────

    private static void MapExceptionAgingEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(UiApiRoutes.SecurityMasterExceptionsAging, async (
            [FromServices] SecurityMasterExceptionCaseworkService caseworkService,
            CancellationToken ct) =>
        {
            var aging = await caseworkService.GetAgingExceptionsAsync(ct).ConfigureAwait(false);
            return Results.Json(aging, jsonOptions);
        })
        .WithName("GetSecurityMasterAgingExceptions")
        .Produces<IReadOnlyList<ReconciliationBreakQueueItem>>(StatusCodes.Status200OK);
    }
}
