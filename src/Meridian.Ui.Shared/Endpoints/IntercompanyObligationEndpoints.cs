using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static class IntercompanyObligationEndpoints
{
    public static void MapIntercompanyObligationEndpoints(this WebApplication app, JsonSerializerOptions options)
    {
        var monitorOptions = new JsonSerializerOptions(options);
        monitorOptions.Converters.Insert(0, new IntercompanyDecimalJsonConverter());
        var monitorContext = new IntercompanyObligationMonitorJsonContext(monitorOptions);
        app.MapGet("/api/ledger/intercompany/obligations", async (string? fundProfileId, Guid? ledgerBookId,
            DateOnly asOf, int? expiryHorizonDays, Guid? entityId, HttpContext http) =>
        {
            var scope = HttpContextWorkstationTenantContextAccessor.Resolve(http);
            var service = http.RequestServices.GetService<IIntercompanyObligationMonitorService>();
            var journals = http.RequestServices.GetService<ILedgerJournalStore>();
            var ownership = http.RequestServices.GetService<IFundProfileTenancyRegistry>();
            if (service is null || journals is null || ownership is null)
                return Results.Problem("Intercompany monitoring authority is unavailable.", statusCode: 503);
            if (ledgerBookId is not { } bookId || bookId == Guid.Empty)
                return Results.BadRequest(new { error = "Select a ledger book for obligation monitoring." });
            try
            {
                var book = await journals.GetLedgerBookAsync(bookId, http.RequestAborted);
                var owner = book is null ? null : await ownership.ResolveAsync(book.FundProfileId, http.RequestAborted);
                if (book is null || owner is null || !owner.IsHeldBy(scope.TenantId)
                    || !string.Equals(owner.CompanyId, scope.CompanyId, StringComparison.Ordinal)
                    || fundProfileId is not null && fundProfileId != book.FundProfileId
                    || entityId.HasValue && entityId != book.FundStructureNodeId)
                    return EndpointHelpers.Forbidden();
                using var reportAccess = IntercompanyLedgerReadPermissionScope.TryEnter(http);
                if (reportAccess is null)
                    return EndpointHelpers.Forbidden();
                var observation = await service.EvaluateAsync(new(scope.TenantId!, scope.Actor!), scope.CompanyId,
                    new(book.FundProfileId, bookId, asOf, expiryHorizonDays ?? 30, entityId), http.RequestAborted);
                return Results.Json(observation, monitorContext.IntercompanyObligationMonitorDto);
            }
            catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
            catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
            catch (IntercompanyAgreementForbiddenException) { return EndpointHelpers.Forbidden(); }
            catch (Npgsql.NpgsqlException) { return Results.Problem("Intercompany monitoring authority is unavailable.", statusCode: 503); }
        }).RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
            .RequireAnyPermission(UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);

        app.MapGet("/api/fund-structure/intercompany-agreements/{id:guid}/schedule", async (Guid id,
            DateOnly? asOf, int? version, Guid? proposalId, HttpContext http) =>
        {
            if (version.HasValue && proposalId.HasValue)
                return Results.BadRequest(new { error = "Select one approved version or proposal." });
            var legal = http.RequestServices.GetService<IIntercompanyAgreementService>();
            var preview = http.RequestServices.GetService<IIntercompanyStructuredTermsPreviewService>();
            if (legal is null || preview is null)
                return Results.Problem("Agreement date preview is unavailable.", statusCode: 503);
            var scope = HttpContextWorkstationTenantContextAccessor.Resolve(http);
            try
            {
                var detail = await legal.GetAsync(new(scope.TenantId!, scope.Actor!), id, asOf, ct: http.RequestAborted);
                if (detail is null)
                    return Results.NotFound();
                if (proposalId is { } selected)
                {
                    var proposal = detail.Proposals.SingleOrDefault(p => p.ProposalId == selected);
                    if (proposal is null)
                        return Results.NotFound();
                    var hash = proposal.SubmittedHash ?? Meridian.Application.FundStructure.IntercompanyAgreementRules.ReviewedHash(
                        new(proposal.Draft, proposal.BaseAgreementVersion, detail.Revisions.LastOrDefault()?.AgreementVersion, proposal.Rationale)
                        { AgreementId = id, ProposalId = selected });
                    return Results.Json(preview.Preview(proposal.Draft, hash), options);
                }
                var revision = version.HasValue ? detail.Revisions.SingleOrDefault(r => r.AgreementVersion == version.Value) : detail.ApplicableRevision;
                return revision is null ? Results.NotFound() : Results.Json(preview.Preview(revision.Terms, revision.PayloadHash), options);
            }
            catch (IntercompanyAgreementNotFoundException) { return Results.NotFound(); }
            catch (IntercompanyAgreementForbiddenException) { return EndpointHelpers.Forbidden(); }
            catch (IntercompanyAgreementValidationException error) { return Results.BadRequest(new { error = error.Message }); }
            catch (Npgsql.NpgsqlException) { return Results.Problem("Agreement preview authority is unavailable.", statusCode: 503); }
        }).RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
            .RequireAnyPermission(UserPermission.ManageFundStructure, UserPermission.ManageDirectLending,
                UserPermission.ManageCompliance, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);
    }
}
