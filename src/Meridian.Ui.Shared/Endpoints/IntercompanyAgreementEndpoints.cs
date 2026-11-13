using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>Tenant-scoped agreement authoring and independent review shared by both workstations.</summary>
public static class IntercompanyAgreementEndpoints
{
    public static void MapIntercompanyAgreementEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(UiApiRoutes.IntercompanyAgreements).WithTags("Intercompany Agreements")
            .RequireWorkstationTenantScope();
        group.AddEndpointFilter(async (invocation, next) =>
        {
            var http = invocation.HttpContext;
            var scope = HttpContextWorkstationTenantContextAccessor.Resolve(http);
            if (string.IsNullOrWhiteSpace(scope.Actor))
                return Results.Unauthorized();
            if (!scope.HasTenantScope || !TenantReadPredicate.IsResolvedTenant(scope.CompanyId))
                return EndpointHelpers.Forbidden();
            if (http.RequestServices.GetService<IIntercompanyAgreementService>() is null)
                return Results.Problem("The authoritative agreement registry is not configured.", statusCode: 503);
            try
            {
                return await next(invocation).ConfigureAwait(false);
            }
            catch (IntercompanyAgreementNotFoundException) { return Results.NotFound(); }
            catch (IntercompanyAgreementConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (IntercompanyAgreementForbiddenException ex) { return Results.Json(new { error = ex.Message }, statusCode: 403); }
            catch (HumanOperatorRequiredException ex) { return Results.Json(new { error = ex.Message }, statusCode: 403); }
            catch (IntercompanyAgreementValidationException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        group.MapGet("", async (HttpContext http, Guid? entityId, IntercompanyAgreementTypeDto? agreementType,
            IntercompanyAgreementStatusDto? status, DateOnly? asOf, DateTimeOffset? knownAtUtc, string? cursor, int? pageSize) =>
        {
            var query = new IntercompanyAgreementQueryDto(entityId, agreementType, status, asOf, knownAtUtc, cursor, pageSize ?? 50);
            var result = await Service(http).ListAsync(Context(http), query, http.RequestAborted).ConfigureAwait(false);
            var reviewEntities = await AttestationEntitiesAsync(http, result.Items).ConfigureAwait(false);
            var canCreate = CanMutate(http);
            if (canCreate && entityId.HasValue)
            {
                var access = http.RequestServices.GetService<IIntercompanyAgreementAccessAuthority>();
                canCreate = access is not null && await access.CanAccessEntitiesAsync(Context(http), [entityId.Value], http.RequestAborted).ConfigureAwait(false);
            }
            return Results.Json(result with
            {
                Items = result.Items.Select(detail => ReadCapabilities(http, detail, reviewEntities)).ToArray(),
                CanCreate = canCreate,
                ReferencePostingSupported = http.RequestServices.GetService<IIntercompanyAgreementPostingAuthority>() is not null
                    && string.Equals(http.RequestServices.GetService<IConfiguration>()?["MERIDIAN_INTERCOMPANY_REFERENCE_AUTHORING_ENABLED"]
                        ?? Environment.GetEnvironmentVariable("MERIDIAN_INTERCOMPANY_REFERENCE_AUTHORING_ENABLED"),
                        "true", StringComparison.OrdinalIgnoreCase)
            }, jsonOptions);
        }).WithName("ListIntercompanyAgreements")
            .RequireAnyPermission(UserPermission.ManageFundStructure, UserPermission.ManageDirectLending, UserPermission.ManageCompliance,
                UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);

        group.MapGet("/{id:guid}", async (Guid id, HttpContext http, DateOnly? asOf, DateTimeOffset? knownAtUtc) =>
        {
            var detail = await Service(http).GetAsync(Context(http), id, asOf, knownAtUtc, http.RequestAborted).ConfigureAwait(false);
            return detail is null ? Results.NotFound() : Results.Json(ReadCapabilities(http, detail,
                await AttestationEntitiesAsync(http, [detail]).ConfigureAwait(false)), jsonOptions);
        }).WithName("GetIntercompanyAgreement")
            .RequireAnyPermission(UserPermission.ManageFundStructure, UserPermission.ManageDirectLending, UserPermission.ManageCompliance,
                UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);

        group.MapGet("/{id:guid}/versions/{version:int}", async (Guid id, int version, HttpContext http) =>
        {
            var detail = await Service(http).GetAsync(Context(http), id, ct: http.RequestAborted).ConfigureAwait(false);
            var revision = detail?.Revisions.SingleOrDefault(item => item.AgreementVersion == version);
            return revision is null ? Results.NotFound() : Results.Json(revision, jsonOptions);
        }).WithName("GetIntercompanyAgreementVersion")
            .RequireAnyPermission(UserPermission.ManageFundStructure, UserPermission.ManageDirectLending, UserPermission.ManageCompliance,
                UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);

        group.MapGet("/{id:guid}/history", async (Guid id, HttpContext http) =>
        {
            var detail = await Service(http).GetAsync(Context(http), id, ct: http.RequestAborted).ConfigureAwait(false);
            return detail is null ? Results.NotFound() : Results.Json(detail.History, jsonOptions);
        }).WithName("GetIntercompanyAgreementHistory")
            .RequireAnyPermission(UserPermission.ManageFundStructure, UserPermission.ManageDirectLending, UserPermission.ManageCompliance,
                UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);

        group.MapGet("/{id:guid}/journal-lines", async (Guid id, Guid ledgerBookId, DateOnly? asOf,
            string? cursor, int? pageSize, HttpContext http) =>
        {
            var ledger = http.RequestServices.GetService<ILedgerJournalStore>();
            var tenancy = http.RequestServices.GetService<IFundProfileTenancyRegistry>();
            if (ledger is null || tenancy is null)
                return Results.Problem("Authoritative ledger coverage is unavailable.", statusCode: 503);
            var date = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var detail = await Service(http).GetAsync(Context(http), id, date, ct: http.RequestAborted).ConfigureAwait(false);
            if (detail is null)
                return Results.NotFound();
            var book = await ledger.GetLedgerBookAsync(ledgerBookId, http.RequestAborted).ConfigureAwait(false);
            if (book is null || book.FundStructureNodeKind != FundStructureNodeKindDto.Entity
                || book.FundStructureNodeId != detail.PartyOneId && book.FundStructureNodeId != detail.PartyTwoId)
                return Results.NotFound();
            var scope = HttpContextWorkstationTenantContextAccessor.Resolve(http);
            var owner = await tenancy.ResolveAsync(book.FundProfileId, http.RequestAborted).ConfigureAwait(false);
            if (owner is null || !owner.IsHeldBy(scope.TenantId)
                || !string.Equals(owner.FundProfileId, book.FundProfileId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(owner.CompanyId, scope.CompanyId, StringComparison.Ordinal))
                return Results.NotFound();
            var permission = EndpointAuthorization.HasPermission(http, UserPermission.ManageLedgerReports)
                ? UserPermission.ManageLedgerReports : UserPermission.ViewLedgerReports;
            if (!EndpointAuthorization.HasPermission(http, UserPermission.AdminMaintenance)
                && !await EndpointAuthorization.HasScopedPermissionAsync(http, permission,
                    AccessScopeKindDto.LegalEntity, book.FundStructureNodeId, http.RequestAborted).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();
            var records = await ledger.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: ledgerBookId, EffectiveTo: date),
                http.RequestAborted).ConfigureAwait(false);
            return Results.Json(IntercompanyAgreementJournalProjection.Project(detail, book, records, date, cursor, pageSize ?? 100), jsonOptions);
        }).WithName("GetIntercompanyAgreementJournalLines")
            .RequireAnyPermission(UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance)
            .RequireAnyPermission(UserPermission.ManageFundStructure, UserPermission.ManageDirectLending, UserPermission.AdminMaintenance);

        group.MapPost("", async (IntercompanyAgreementCommandDto command, HttpContext http) =>
            Results.Json(await ReadResultAsync(http, await Service(http).CreateAsync(Context(http), command, http.RequestAborted).ConfigureAwait(false)), jsonOptions))
            .WithName("CreateIntercompanyAgreement")
            .RequireAnyPermission(UserPermission.ManageFundStructure, UserPermission.AdminMaintenance);
        group.MapPost("/{id:guid}/proposals", async (Guid id, IntercompanyAgreementCommandDto command, HttpContext http) =>
            Results.Json(await ReadResultAsync(http, await Service(http).CreateProposalAsync(Context(http), RouteCommand(command, id), http.RequestAborted).ConfigureAwait(false)), jsonOptions))
            .WithName("CreateIntercompanyAgreementProposal")
            .RequireAnyPermission(UserPermission.ManageFundStructure, UserPermission.AdminMaintenance);
        group.MapPut("/{id:guid}/proposals/{proposalId:guid}", async (Guid id, Guid proposalId, IntercompanyAgreementCommandDto command, HttpContext http) =>
            Results.Json(await ReadResultAsync(http, await Service(http).SaveDraftAsync(Context(http), RouteCommand(command, id, proposalId), http.RequestAborted).ConfigureAwait(false)), jsonOptions))
            .WithName("SaveIntercompanyAgreementDraft")
            .RequireAnyPermission(UserPermission.ManageFundStructure, UserPermission.AdminMaintenance);

        foreach (var action in new[] { "submit", "approve", "reject", "withdraw" })
        {
            var transition = action;
            group.MapPost("/{id:guid}/proposals/{proposalId:guid}/" + transition,
                async (Guid id, Guid proposalId, IntercompanyAgreementCommandDto command, HttpContext http) =>
                {
                    if (transition is "approve" or "reject")
                        OperationsOriginGuard.RequireHumanOperator(
                            EndpointAuthorization.ResolveTrustedActionOrigin(http, OperationsActionOriginDto.HumanOperator),
                            "review intercompany agreement terms");
                    if (command.ExternalReview is not null && !EndpointAuthorization.HasPermission(http, UserPermission.ManageCompliance))
                        return EndpointHelpers.Forbidden();
                    if (command.ExternalReview is not null)
                        http.Items[HttpContextIntercompanyAgreementAccessAuthority.RequiredPermissionKey] = UserPermission.ManageCompliance;
                    var service = Service(http);
                    var trusted = RouteCommand(command, id, proposalId);
                    var context = Context(http);
                    var task = transition switch
                    {
                        "submit" => service.SubmitAsync(context, trusted, http.RequestAborted),
                        "approve" => service.ApproveAsync(context, trusted, http.RequestAborted),
                        "reject" => service.RejectAsync(context, trusted, http.RequestAborted),
                        _ => service.WithdrawAsync(context, trusted, http.RequestAborted)
                    };
                    return Results.Json(await ReadResultAsync(http, await task.ConfigureAwait(false)), jsonOptions);
                }).WithName("IntercompanyAgreement" + transition)
                .RequireAnyPermission(transition is "approve" or "reject"
                    ? [UserPermission.ManageFundStructure, UserPermission.ManageCompliance, UserPermission.AdminMaintenance]
                    : [UserPermission.ManageFundStructure, UserPermission.AdminMaintenance]);
        }
    }

    private static IIntercompanyAgreementService Service(HttpContext http)
        => http.RequestServices.GetRequiredService<IIntercompanyAgreementService>();

    private static IntercompanyAgreementCallContext Context(HttpContext http)
    {
        var scope = HttpContextWorkstationTenantContextAccessor.Resolve(http);
        return new(scope.TenantId!, scope.Actor!);
    }

    private static bool CanMutate(HttpContext http)
        => EndpointAuthorization.HasAnyPermission(http, UserPermission.ManageFundStructure, UserPermission.AdminMaintenance);

    private static IntercompanyAgreementDetailDto ReadCapabilities(HttpContext http, IntercompanyAgreementDetailDto detail,
        IReadOnlySet<Guid> reviewEntities)
    {
        var canAttest = !EndpointAuthorization.IsNonSessionPrincipal(http)
            && EndpointAuthorization.HasPermission(http, UserPermission.ManageCompliance)
            && detail.Capabilities.CanAttestExternalReview && reviewEntities.Contains(detail.PartyOneId) && reviewEntities.Contains(detail.PartyTwoId);
        return CanMutate(http) ? detail with { Capabilities = detail.Capabilities with { CanAttestExternalReview = canAttest } }
            : detail with
            {
                Capabilities = new(false, false, false, false, false, false, false,
                    "Fund-structure management permission is required to change agreement terms.")
                { CanAttestExternalReview = canAttest }
            };
    }

    private static async Task<IntercompanyAgreementCommandResultDto> ReadResultAsync(HttpContext http, IntercompanyAgreementCommandResultDto result)
    {
        var entities = await AttestationEntitiesAsync(http, [result.Detail, result.Receipt.Detail]).ConfigureAwait(false);
        return result with
        {
            Detail = ReadCapabilities(http, result.Detail, entities),
            Receipt = result.Receipt with { Detail = ReadCapabilities(http, result.Receipt.Detail, entities) }
        };
    }

    private static Task<IReadOnlySet<Guid>> AttestationEntitiesAsync(HttpContext http, IEnumerable<IntercompanyAgreementDetailDto> details)
        => EndpointAuthorization.HasPermission(http, UserPermission.ManageCompliance)
            ? EndpointAuthorization.AuthorizeScopedManyAsync(http, UserPermission.ManageCompliance, AccessScopeKindDto.LegalEntity,
                details.SelectMany(detail => new[] { detail.PartyOneId, detail.PartyTwoId }).Distinct().ToArray(), http.RequestAborted)
            : Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());

    private static IntercompanyAgreementCommandDto RouteCommand(IntercompanyAgreementCommandDto command, Guid agreementId, Guid? proposalId = null)
    {
        if ((command.AgreementId != Guid.Empty && command.AgreementId != agreementId)
            || (proposalId.HasValue && command.ProposalId != Guid.Empty && command.ProposalId != proposalId.Value))
            throw new IntercompanyAgreementValidationException("Command identity does not match the selected route.");
        return command with { AgreementId = agreementId, ProposalId = proposalId ?? command.ProposalId };
    }
}
