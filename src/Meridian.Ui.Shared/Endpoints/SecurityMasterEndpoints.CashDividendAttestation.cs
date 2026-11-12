using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Storage.SecurityMaster;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class SecurityMasterEndpoints
{
    private const string CashDividendCaseRoute = "/api/security-master/corporate-actions/cases/{caseId:guid}/cash-dividend-attestation";

    private static void MapCashDividendAttestationEndpoints(RouteGroupBuilder group, JsonSerializerOptions json)
    {
        CashDividendReadRoute(group.MapGet(CashDividendCaseRoute, (Guid caseId, HttpContext context) =>
            CashDividendResultAsync(context, async () =>
            {
                var trusted = RequireCashDividendScope(context);
                var processingCase = await RequireCashDividendCaseAsync(caseId, trusted, context).ConfigureAwait(false);
                var store = ResolveCashDividendStore(context);
                var service = context.RequestServices.GetService<ICorporateActionCashDividendAccountingService>();
                var blockers = new List<string>();
                CashDividendAttestationDeclarationStateDto? declarationState = null;
                CorporateActionCashDividendPreviewDto? preview = null;
                CorporateActionCashDividendAttestationDto? attestation = null;
                CorporateActionCashDividendPostingDto? posting = null;
                if (store is null)
                    blockers.Add("The governed cash-dividend attestation persistence is unavailable.");
                else
                {
                    declarationState = await store.GetCashDividendDeclarationStateAsync(trusted.TenantId,
                        trusted.CompanyId, context.RequestAborted).ConfigureAwait(false);
                    attestation = await store.GetCashDividendAttestationAsync(caseId, trusted.TenantId,
                        trusted.CompanyId, context.RequestAborted).ConfigureAwait(false);
                    posting = await store.GetCashDividendPostingAsync(caseId, trusted.TenantId,
                        trusted.CompanyId, context.RequestAborted).ConfigureAwait(false);
                    if (service is null)
                        blockers.Add("The governed cash-dividend readiness service is unavailable; retained history and declaration withdrawal remain readable.");
                    else if (posting is null)
                    {
                        try
                        {
                            preview = await service.PreviewCashDividendAsync(caseId, trusted.TenantId,
                                trusted.CompanyId, context.RequestAborted).ConfigureAwait(false);
                        }
                        catch (CorporateActionOperationException exception)
                        {
                            blockers.Add(exception.Message);
                        }
                    }
                }
                var authority = CashDividendAuthority(context);
                var declaration = declarationState?.Declaration;
                var bound = declaration is { Enabled: true }
                    && Guid.TryParse(processingCase.Scope.LedgerBookId, out var bookId)
                    && declaration.LedgerBookId == bookId
                    && declaration.PolicyVersion == CorporateActionCashDividendAttestationPolicy.Version
                    && declaration.PolicyHash == CorporateActionCashDividendAttestationPolicy.PolicyHash
                    && string.Equals(declaration.PrincipalId, trusted.Actor, StringComparison.OrdinalIgnoreCase);
                var policyEnabled = declarationState?.PolicyEnabled == true;
                if (!policyEnabled)
                    blockers.Add("The economic attestation policy is disabled on this host.");
                if (!bound)
                    blockers.Add("A current declaration for this principal and the case's selected USD book is required.");
                var canAttest = policyEnabled && bound && authority.CanAttestAccounting && preview is not null
                    && preview.Eligibility.AccountRoleBinding == declaration!.AccountRoleBinding
                    && posting is null && string.Equals(preview.Projection.PreparedBy, trusted.Actor, StringComparison.OrdinalIgnoreCase);
                var canPost = policyEnabled && bound && authority.CanPostAccounting && posting is null
                    && preview is not null && processingCase.State == CorporateActionCaseStates.PreparerAttested
                    && attestation is { IsActive: true }
                    && attestation.DeclarationRevision == declaration!.Revision
                    && attestation.DeclarationId == declaration.DeclarationId
                    && attestation.ProjectionId == preview.Projection.ProjectionId
                    && attestation.EntitlementSnapshotHash == preview.Eligibility.EntitlementSnapshotHash
                    && attestation.AccountRoleBinding == preview.Eligibility.AccountRoleBinding
                    && attestation.AccountRoleBinding == declaration.AccountRoleBinding
                    && string.Equals(attestation.AttestedBy, trusted.Actor, StringComparison.OrdinalIgnoreCase);
                var hash = preview?.Eligibility.EntitlementSnapshotHash;
                return Results.Json(new CorporateActionCashDividendWorkspaceDto(1,
                    store is null ? "Unavailable" : "Available",
                    CorporateActionCashDividendAttestationPolicy.Version, CorporateActionCashDividendAttestationPolicy.PolicyHash,
                    policyEnabled, trusted.Actor, processingCase.Scope, processingCase,
                    store is not null && EndpointAuthorization.HasPermission(context, UserPermission.ManageSinglePrincipalDeclarations),
                    canAttest, canPost, declaration, declarationState?.History ?? [], preview, attestation, posting,
                    hash is null ? null : "cash-dividend-eligibility:" + hash, hash,
                    CorporateActionCashDividendAttestationPolicy.Disclosure, CorporateActionCashDividendAttestationPolicy.Confirmation,
                    CorporateActionCashDividendAttestationPolicy.RelatedEventGrossLimitUsd,
                    CorporateActionCashDividendAttestationPolicy.BookCalendarMonthGrossLimitUsd,
                    blockers.Distinct(StringComparer.Ordinal).ToArray(),
                    EndpointAuthorization.HasPermission(context, UserPermission.ManageLedgerReports)), json);
            })))
            .WithName("GetCorporateActionCashDividendAttestationWorkspace")
            .Produces<CorporateActionCashDividendWorkspaceDto>()
            .Produces(StatusCodes.Status404NotFound);

        CashDividendReadRoute(group.MapPost(CashDividendCaseRoute + "/declaration",
            (Guid caseId, ChangeCashDividendAttestationDeclarationRequestDto request, HttpContext context) =>
                CashDividendResultAsync(context, async () =>
                {
                    var trusted = RequireCashDividendScope(context);
                    RequireCashDividendRequestScope(request.TenantId, request.CompanyId, request.Actor, trusted);
                    var processingCase = await RequireCashDividendCaseAsync(caseId, trusted, context).ConfigureAwait(false);
                    if (!Guid.TryParse(processingCase.Scope.LedgerBookId, out var bookId)
                        || (request.LedgerBookId != Guid.Empty && bookId != request.LedgerBookId))
                        throw new CorporateActionScopeMismatchException("The declaration must select this case's exact accounting book.");
                    var store = ResolveCashDividendStore(context)
                        ?? throw new CorporateActionPersistenceUnavailableException("The governed cash-dividend declaration store is unavailable.");
                    var authority = new CashDividendDeclarationAuthorityDto(
                        EndpointAuthorization.HasPermission(context, UserPermission.ManageSinglePrincipalDeclarations),
                        RequireCashDividendHumanOrigin(context, request.Authority?.Origin ?? OperationsActionOriginDto.HumanOperator),
                        EndpointAuthorization.HasPermission(context, UserPermission.ManageLedgerReports));
                    var command = request with
                    {
                        TenantId = trusted.TenantId, CompanyId = trusted.CompanyId, Actor = trusted.Actor,
                        LedgerBookId = bookId, Authority = authority
                    };
                    if (!authority.CanAdministerDeclaration)
                        throw new CorporateActionPermissionDeniedException("CashDividendDeclaration", "ManageSinglePrincipalDeclarations");
                    var replay = await store.GetCashDividendDeclarationReceiptAsync(command, context.RequestAborted).ConfigureAwait(false);
                    if (replay is not null)
                        return Results.Json(replay, json);
                    var current = await store.GetCashDividendDeclarationAsync(trusted.TenantId, trusted.CompanyId,
                        context.RequestAborted).ConfigureAwait(false);
                    if (command.Enabled || command.AccountRoleBinding is not null && command.AccountRoleBinding != current?.AccountRoleBinding)
                    {
                        if (!authority.CanBindAccountingRoles)
                            throw new CorporateActionPermissionDeniedException("CashDividendAccountRoles", "ManageLedgerReports");
                        var service = context.RequestServices.GetService<ICorporateActionCashDividendAccountingService>()
                            ?? throw new CorporateActionPersistenceUnavailableException("The governed accounting-role validation service is unavailable.");
                        var preview = await service.PreviewCashDividendAsync(caseId, trusted.TenantId,
                            trusted.CompanyId, context.RequestAborted).ConfigureAwait(false);
                        CorporateActionCashDividendAttestationValidation.EnsureAccountRoleBinding(command.AccountRoleBinding, preview.Projection);
                        CorporateActionCashDividendAttestationValidation.EnsureSameAccountRoleBinding(
                            preview.Eligibility.AccountRoleBinding, command.AccountRoleBinding);
                    }
                    var result = await store.ChangeCashDividendDeclarationAsync(command, context.RequestAborted).ConfigureAwait(false);
                    return Results.Json(result, json);
                })))
            .WithName("ChangeCorporateActionCashDividendDeclaration")
            .Produces<CashDividendAttestationDeclarationResultDto>()
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        CashDividendReadRoute(group.MapPost(CashDividendCaseRoute + "/attest",
            (Guid caseId, AttestCorporateActionCashDividendRequestDto request, HttpContext context) =>
                CashDividendResultAsync(context, async () =>
                {
                    var trusted = RequireCashDividendScope(context);
                    RequireCashDividendRequestScope(request.TenantId, request.CompanyId, request.Actor, trusted);
                    if (request.CaseId != Guid.Empty && request.CaseId != caseId)
                        throw new CorporateActionValidationException("Route and request case identities must match.");
                    var processingCase = await RequireCashDividendCaseAsync(caseId, trusted, context, request.ScopeAssertion).ConfigureAwait(false);
                    var service = context.RequestServices.GetService<ICorporateActionCashDividendAccountingService>()
                        ?? throw new CorporateActionPersistenceUnavailableException("The governed cash-dividend accounting service is unavailable.");
                    return Results.Json(await service.AttestCashDividendAsync(request with
                    {
                        CaseId = caseId, TenantId = trusted.TenantId, CompanyId = trusted.CompanyId, Actor = trusted.Actor,
                        ScopeAssertion = processingCase.Scope,
                        CorrelationId = context.TraceIdentifier,
                        Authority = CashDividendAuthority(context, request.Authority?.Origin ?? OperationsActionOriginDto.HumanOperator)
                    }, context.RequestAborted).ConfigureAwait(false), json);
                })))
            .WithName("AttestCorporateActionCashDividendAccounting")
            .Produces<CorporateActionCashDividendAttestationResultDto>()
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        CashDividendReadRoute(group.MapPost(CashDividendCaseRoute + "/post",
            (Guid caseId, PostAttestedCorporateActionCashDividendRequestDto request, HttpContext context) =>
                CashDividendResultAsync(context, async () =>
                {
                    var trusted = RequireCashDividendScope(context);
                    RequireCashDividendRequestScope(request.TenantId, request.CompanyId, request.Actor, trusted);
                    if (request.CaseId != Guid.Empty && request.CaseId != caseId)
                        throw new CorporateActionValidationException("Route and request case identities must match.");
                    var processingCase = await RequireCashDividendCaseAsync(caseId, trusted, context, request.ScopeAssertion).ConfigureAwait(false);
                    var service = context.RequestServices.GetService<ICorporateActionCashDividendAccountingService>()
                        ?? throw new CorporateActionPersistenceUnavailableException("The governed cash-dividend accounting service is unavailable.");
                    return Results.Json(await service.PostAttestedCashDividendAsync(request with
                    {
                        CaseId = caseId, TenantId = trusted.TenantId, CompanyId = trusted.CompanyId, Actor = trusted.Actor,
                        ScopeAssertion = processingCase.Scope,
                        CorrelationId = context.TraceIdentifier,
                        Authority = CashDividendAuthority(context, request.Authority?.Origin ?? OperationsActionOriginDto.HumanOperator)
                    }, context.RequestAborted).ConfigureAwait(false), json);
                })))
            .WithName("PostPreparerAttestedCorporateActionCashDividend")
            .Produces<CorporateActionCashDividendPostingResultDto>()
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static ICorporateActionCashDividendAccountingStore? ResolveCashDividendStore(HttpContext context) =>
        context.RequestServices.GetService<ICorporateActionCashDividendAccountingStore>()
        ?? context.RequestServices.GetService<ICorporateActionOperationsStore>() as ICorporateActionCashDividendAccountingStore;

    private static CorporateActionCashDividendDecisionAuthorityDto CashDividendAuthority(HttpContext context,
        OperationsActionOriginDto origin = OperationsActionOriginDto.HumanOperator) => new(
            EndpointAuthorization.HasPermission(context, UserPermission.RecordPreparerAttestations)
                && EndpointAuthorization.HasPermission(context, UserPermission.ApproveCorporateActionAccounting),
            EndpointAuthorization.HasPermission(context, UserPermission.PostCorporateActionAccounting),
            RequireCashDividendHumanOrigin(context, origin));

    private static OperationsActionOriginDto RequireCashDividendHumanOrigin(HttpContext context, OperationsActionOriginDto origin)
    {
        var trusted = EndpointAuthorization.ResolveTrustedActionOrigin(context, origin);
        if (trusted != OperationsActionOriginDto.HumanOperator)
            throw new CorporateActionPermissionDeniedException("CashDividendAttestation", "An authenticated interactive human is required.");
        return trusted;
    }

    private static async Task<CorporateActionProcessingCaseDto> RequireCashDividendCaseAsync(Guid caseId,
        TrustedCorporateActionCommandScope trusted, HttpContext context, CorporateActionCaseScopeDto? assertion = null)
    {
        var processingCase = await context.RequestServices.GetRequiredService<ICorporateActionOperationsStore>()
            .GetCaseAsync(caseId, trusted.TenantId, trusted.CompanyId, context.RequestAborted).ConfigureAwait(false)
            ?? throw new CorporateActionNotFoundException("Corporate-action processing case", caseId);
        if (processingCase.CaseId != caseId || processingCase.Scope.TenantId != trusted.TenantId
            || processingCase.Scope.CompanyId != trusted.CompanyId || assertion is not null && assertion != processingCase.Scope)
            throw new CorporateActionScopeMismatchException("The case or asserted accounting scope does not match the authenticated case scope.");
        return processingCase;
    }

    private static TrustedCorporateActionCommandScope RequireCashDividendScope(HttpContext context)
    {
        if (!TryResolveCorporateActionScope(context, out var scope))
            throw new CorporateActionScopeMismatchException("An authenticated tenant and company scope is required.");
        return new(scope.TenantId, scope.CompanyId, ResolveActor(context), context.TraceIdentifier);
    }

    private static void RequireCashDividendRequestScope(string? tenantId, string? companyId, string? actor,
        TrustedCorporateActionCommandScope scope)
    {
        if (tenantId is not null && tenantId != scope.TenantId || companyId is not null && companyId != scope.CompanyId)
            throw new CorporateActionScopeMismatchException("The request scope does not match the authenticated tenant and company.");
        if (actor is not null && !string.Equals(actor, scope.Actor, StringComparison.OrdinalIgnoreCase))
            throw new CorporateActionPermissionDeniedException("CashDividendAttestation", "The supplied actor does not match the authenticated principal.");
    }

    private static RouteHandlerBuilder CashDividendReadRoute(RouteHandlerBuilder route) => route
        .RequireAuthenticatedSession()
        .RequireAllPermissions(UserPermission.ViewSecurityMaster, UserPermission.ViewCorporateActions)
        .RequireWorkstationTenantCompanyScope()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    private static async Task<IResult> CashDividendResultAsync(HttpContext context, Func<Task<IResult>> operation)
    {
        try { return await operation().ConfigureAwait(false); }
        catch (CorporateActionOperationException exception) { return CorporateActionProblem(context, exception); }
        catch (Exception exception) when (exception is IOException or Npgsql.NpgsqlException)
        {
            return CorporateActionProblem(context, new CorporateActionPersistenceUnavailableException(
                "Governed cash-dividend persistence is unavailable; no new authority was granted."));
        }
    }
}
