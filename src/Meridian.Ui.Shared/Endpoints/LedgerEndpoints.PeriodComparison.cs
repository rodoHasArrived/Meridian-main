using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    private static void MapPostedPeriodComparisonEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var exactJson = new JsonSerializerOptions(jsonOptions);
        exactJson.Converters.Insert(0, new AccountOversightDecimalJsonConverter());
        app.MapGet("/api/ledger/books/{ledgerBookId:guid}/periods/{periodId:guid}/comparison", async (
            Guid ledgerBookId, Guid periodId, Guid? baselinePeriodId, DateTimeOffset? asOf,
            AccountingBasisKindDto? accountingBasis, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
                return EndpointHelpers.Forbidden();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (!HasAccountingPackageTenantScope(tenant) || !EndpointAuthorization.TryResolveActor(context, out _))
                return EndpointHelpers.Forbidden();
            var journalStore = ResolveJournalStore(context);
            var bookService = ResolveService(context);
            var tenancy = context.RequestServices.GetService<IFundProfileTenancyRegistry>();
            if (journalStore is null || bookService is null || tenancy is null)
                return Results.Problem("Authoritative posted ledger and retained ownership services are required.", statusCode: 503);
            try
            {
                if (!await CanAccessPreparationBookAsync(context, ledgerBookId).ConfigureAwait(false))
                    return EndpointHelpers.Forbidden();
                if (HasUnsupportedComparisonQuery(context.Request.Query))
                    return Results.BadRequest(new { error = "The comparison contains an unsupported or invalid dimension selector." });
                var filter = BuildDimensionReportFilter(context.Request.Query);
                var dimensions = ToLedgerDimensionSetDto(filter);
                var service = new PostedPeriodComparisonService(journalStore, bookService, tenancy);
                var comparison = await service.GetAsync(tenant.TenantId!, tenant.CompanyId!, ledgerBookId,
                    periodId, baselinePeriodId, asOf ?? DateTimeOffset.UtcNow, dimensions,
                    records => BuildJournalEntryDtos(records, _ => ledgerBookId, filter), context.RequestAborted,
                    accountingBasis: accountingBasis).ConfigureAwait(false);
                return Results.Json(comparison, exactJson);
            }
            catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
            catch (KeyNotFoundException) { return Results.NotFound(new { error = "The posted ledger period is unavailable." }); }
            catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or ArithmeticException or System.Data.Common.DbException)
            {
                return Results.Problem("The posted source population could not be safely compared. Refresh or restore the retained ledger source.", statusCode: 503);
            }
        }).WithName("GetPostedLedgerPeriodComparison")
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending,
                UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
            .Produces<PostedPeriodComparisonDto>(200).Produces(400).Produces(403).Produces(404).Produces(503);
    }

    private static bool HasUnsupportedComparisonQuery(IQueryCollection query)
    {
        string[] dimensions = ["fundId", "entityId", "sleeveId", "strategyId", "investorId", "capitalAccountId",
            "instrumentId", "positionId", "taxLotId", "costCenterId", "counterpartyId", "organizationId",
            "portfolioId", "bookId", "accountId", "customerId", "vendorId", "projectId"];
        var known = new HashSet<string>(dimensions, StringComparer.OrdinalIgnoreCase)
        {
            "baselinePeriodId", "asOf", "accountingBasis", "fundProfileId", "ledgerBookDimensionId", "externalGlDimensionKey", "externalGlDimensionValue"
        };
        foreach (var dimension in dimensions)
        {
            known.Add("dimension" + char.ToUpperInvariant(dimension[0]) + dimension[1..]);
            if (dimension is "instrumentId" or "positionId")
            {
                foreach (var key in new[] { dimension, "dimension" + char.ToUpperInvariant(dimension[0]) + dimension[1..] })
                    if (query.TryGetValue(key, out var value) && !Guid.TryParse(value.ToString(), out _))
                        return true;
            }
        }
        return query.Keys.Any(key => !known.Contains(key) && !key.StartsWith("externalGl.", StringComparison.OrdinalIgnoreCase));
    }
}
