using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
<<<<<<< Updated upstream
using Microsoft.Extensions.DependencyInjection;
=======
using Microsoft.AspNetCore.Mvc;
>>>>>>> Stashed changes
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

        /// <summary>
        /// Retrieves a security detail by its internal UUID. Returns full economic definition including terms, identifiers, and status.
        /// </summary>
        /// <remarks>
        /// <para>Returns 404 if the security does not exist.</para>
        /// </remarks>
        group.MapGet(UiApiRoutes.SecurityMasterById, async (
            Guid securityId,
            [FromServices] ISecurityMasterQueryService queryService,
            CancellationToken ct) =>
        {
            var detail = await queryService.GetByIdAsync(securityId, ct).ConfigureAwait(false);
            return detail is null
                ? Results.NotFound()
                : Results.Json(detail, jsonOptions);
        })
        .WithName("GetSecurityMasterById")
        .Produces<SecurityDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

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
                    ct)
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
        /// Full-text searches for securities by display name, issuer, or identifiers.
        /// Supports filtering by asset class, status, and provider.
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
            [FromServices] ISecurityMasterService service,
            CancellationToken ct) =>
        {
            var detail = await service.CreateAsync(request, ct).ConfigureAwait(false);
            return Results.Json(detail, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("CreateSecurityMaster")
        .Produces<SecurityDetailDto>(StatusCodes.Status201Created);

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
            [FromServices] ISecurityMasterService service,
            CancellationToken ct) =>
        {
            var detail = await service.AmendTermsAsync(request, ct).ConfigureAwait(false);
            return Results.Json(detail, jsonOptions);
        })
        .WithName("AmendSecurityMaster")
        .Produces<SecurityDetailDto>(StatusCodes.Status200OK);

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
            [FromServices] ISecurityMasterService service,
            CancellationToken ct) =>
        {
            await service.DeactivateAsync(request, ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithName("DeactivateSecurityMaster")
        .Produces(StatusCodes.Status204NoContent);

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
            [FromServices] ISecurityMasterService service,
            CancellationToken ct) =>
        {
            var alias = await service.UpsertAliasAsync(request, ct).ConfigureAwait(false);
            return Results.Json(alias, jsonOptions);
        })
        .WithName("UpsertSecurityMasterAlias")
        .Produces<SecurityAliasDto>(StatusCodes.Status200OK);

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
            [FromServices] ISecurityMasterEventStore eventStore,
            CancellationToken ct) =>
        {
            if (dto.SecurityId != securityId)
            {
                return Results.BadRequest("Corporate action SecurityId must match route parameter");
            }

            await eventStore.AppendCorporateActionAsync(dto, ct).ConfigureAwait(false);
            return Results.Ok();
        })
        .WithName("AppendSecurityMasterCorporateAction")
        .Accepts<CorporateActionDto>("application/json")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        // GET /api/security-master/conflicts
        group.MapGet(UiApiRoutes.SecurityMasterConflicts, async (
            [FromServices] AppSecurityMaster.ISecurityMasterConflictService conflictService,
            CancellationToken ct) =>
        {
            var conflicts = await conflictService.GetOpenConflictsAsync(ct).ConfigureAwait(false);
            return Results.Json(conflicts, jsonOptions);
        })
        .WithName("GetSecurityMasterConflicts")
        .Produces<IReadOnlyList<SecurityMasterConflict>>(StatusCodes.Status200OK);

        // POST /api/security-master/conflicts/{conflictId}/resolve
        group.MapPost(UiApiRoutes.SecurityMasterConflictResolve, async (
            Guid conflictId,
            ResolveConflictRequest request,
            [FromServices] AppSecurityMaster.ISecurityMasterConflictService conflictService,
            CancellationToken ct) =>
        {
            if (request.ConflictId != conflictId)
                return Results.BadRequest(ErrorResponse.Validation(
                    "ConflictId in body must match the route parameter."));

            var updated = await conflictService.ResolveAsync(request, ct).ConfigureAwait(false);
            return updated is null
                ? Results.NotFound()
                : Results.Json(updated, jsonOptions);
        })
        .WithName("ResolveSecurityMasterConflict")
        .Produces<SecurityMasterConflict>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        // POST /api/security-master/import
        group.MapPost(UiApiRoutes.SecurityMasterImport, async (
            SecurityMasterImportRequest request,
            [FromServices] AppSecurityMaster.ISecurityMasterImportService importService,
            CancellationToken ct) =>
        {
            var result = await importService.ImportAsync(
                request.FileContent,
                request.FileExtension,
                progress: null,
                ct: ct).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("ImportSecurityMaster")
        .Produces<AppSecurityMaster.SecurityMasterImportResult>(StatusCodes.Status200OK);

        // GET /api/security-master/ingest/status
        group.MapGet(UiApiRoutes.SecurityMasterIngestStatus, async (
            [FromServices] AppSecurityMaster.ISecurityMasterConflictService conflictService,
            [FromServices] ISecurityMasterQueryService queryService,
            CancellationToken ct) =>
        {
            var openConflicts = await conflictService.GetOpenConflictsAsync(ct).ConfigureAwait(false);
            return Results.Json(new
            {
                OpenConflicts = openConflicts.Count,
                RetrievedAt = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("SecurityMasterIngestStatus")
        .Produces(StatusCodes.Status200OK);
    }
}
