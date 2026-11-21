using System.Text.Json;
using Meridian.Application.SecurityMaster.Valuation;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Identity.Auth;
using Meridian.Storage.AssetOperations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapAssetMarketValuationEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationAssetMarketValuation), async (
            Guid securityId, Guid positionId, Guid ledgerBookId, string accountId, DateOnly valuationDate,
            Guid economicStateId, long economicStateVersion, DateTimeOffset? knownAt, HttpContext context) =>
        {
            // Resolve current authenticated ownership before any position or pricing observation is read.
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (!tenant.HasTenantScope || string.IsNullOrWhiteSpace(tenant.CompanyId))
                return EndpointHelpers.Forbidden();
            if (securityId == Guid.Empty || positionId == Guid.Empty || ledgerBookId == Guid.Empty
                || economicStateId == Guid.Empty || economicStateVersion <= 0 || string.IsNullOrWhiteSpace(accountId)
                || accountId.Length > 256 || valuationDate == default || valuationDate == DateOnly.MaxValue)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                { ["scope"] = ["Exact security, position, book, account, date and positive economic-state identity/version are required."] });
            var books = context.RequestServices.GetService<ILedgerBookService>();
            var tenancy = context.RequestServices.GetService<IFundProfileTenancyRegistry>();
            var positions = context.RequestServices.GetService<IInstrumentPositionProjectionStore>();
            var securities = context.RequestServices.GetService<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>();
            var pricing = context.RequestServices.GetService<ISecurityMasterPricingService>();
            var models = context.RequestServices.GetServices<ISecurityValuationModel>().ToArray();
            if (books is null || tenancy is null || positions is null || securities is null || pricing is null
                || pricing is Meridian.Application.SecurityMaster.NullSecurityMasterPricingService || models.Length == 0)
                return Results.Problem("Owned ledger books, retained position projections and valuation/pricing composition are required.", statusCode: StatusCodes.Status503ServiceUnavailable);
            LedgerBookDto? book;
            try
            { book = await books.GetBookAsync(ledgerBookId, context.RequestAborted).ConfigureAwait(false); }
            catch (KeyNotFoundException) { return EndpointHelpers.Forbidden(); }
            if (book is null || book.LedgerBookId != ledgerBookId || string.IsNullOrWhiteSpace(book.FundProfileId))
                return EndpointHelpers.Forbidden();
            var owner = await tenancy.ResolveAsync(book.FundProfileId, context.RequestAborted).ConfigureAwait(false);
            if (owner is null || owner.FundProfileId != book.FundProfileId || owner.TenantId != tenant.TenantId || owner.CompanyId != tenant.CompanyId)
                return EndpointHelpers.Forbidden();
            var scopeKindName = book.FundStructureNodeKind == FundStructureNodeKindDto.Entity ? "LegalEntity" : book.FundStructureNodeKind.ToString();
            if (book.FundStructureNodeId == Guid.Empty || !Enum.TryParse<AccessScopeKindDto>(scopeKindName, out var scopeKind))
                return EndpointHelpers.Forbidden();
            var allowed = false;
            foreach (var permission in new[] { UserPermission.ViewTrades, UserPermission.ViewSecurityMaster,
                UserPermission.ModifySecurityMaster, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports,
                UserPermission.AdminMaintenance })
            {
                if (EndpointAuthorization.HasPermission(context, permission) && await EndpointAuthorization.HasScopedPermissionAsync(
                    context, permission, scopeKind, book.FundStructureNodeId, context.RequestAborted).ConfigureAwait(false))
                { allowed = true; break; }
            }
            if (!allowed)
                return EndpointHelpers.Forbidden();
            var request = new AssetPositionMarketValuationRequest(securityId, positionId, ledgerBookId,
                accountId, valuationDate, economicStateId, economicStateVersion, knownAt);
            var service = new AssetPositionMarketValuationService(positions, securities, models, pricing);
            var result = await service.ValueAsync(request, book, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetWorkstationAssetMarketValuation")
        .RequireAnyPermission(UserPermission.ViewTrades, UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster,
            UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance)
        .RequireAuthenticatedSession()
        .RequireWorkstationTenantCompanyScope()
        // The existing golden-copy model retains selection receipts, as the Security Master valuation read does.
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces<AssetPositionMarketValuationDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status429TooManyRequests)
        .Produces(StatusCodes.Status503ServiceUnavailable);
    }
}
