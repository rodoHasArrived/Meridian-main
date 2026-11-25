using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Tenancy;
using Meridian.Identity.Auth;
using Meridian.Storage.Export;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    private static void MapLedgerJournalExportEndpoints(WebApplication app)
    {
        app.MapPost(UiApiRoutes.LedgerPeriodJournalExport, async (Guid periodId, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context) || !EndpointAuthorization.HasPermission(context, UserPermission.ExportData))
                return EndpointHelpers.Forbidden();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (!HasAccountingPackageTenantScope(tenant) || !EndpointAuthorization.TryResolveActor(context, out _))
                return EndpointHelpers.Forbidden();
            var journalStore = ResolveJournalStore(context);
            if (journalStore is null || ResolveService(context) is null || context.RequestServices.GetService<IFundProfileTenancyRegistry>() is null)
                return ServiceUnavailable();
            var period = await journalStore.GetPeriodAsync(periodId, context.RequestAborted).ConfigureAwait(false);
            if (period is null)
                return Results.NotFound();
            if (period.PeriodId != periodId)
                return EndpointHelpers.Forbidden();
            if (period.LedgerBookId is not { } bookId || bookId == Guid.Empty)
                return Results.BadRequest("The accounting period must belong to a ledger book.");
            if (await ResolveAccessiblePreparationBookAsync(context, bookId).ConfigureAwait(false) is null)
                return EndpointHelpers.Forbidden();
            if (!context.Request.HasJsonContentType())
                return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
            RunLedgerExportRequest? request;
            try
            { request = await context.Request.ReadFromJsonAsync(RunLedgerQueryJsonContext.Default.RunLedgerExportRequest, context.RequestAborted).ConfigureAwait(false); }
            catch (JsonException) { return Results.BadRequest("Invalid ledger export request."); }
            var format = request?.Format?.Trim().ToLowerInvariant();
            if (format is not ("csv" or "parquet"))
                return Results.BadRequest("format must be csv or parquet.");
            var entries = await journalStore.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: bookId, PeriodId: periodId), context.RequestAborted).ConfigureAwait(false);
            if (entries.Any(record => record.PeriodId != periodId))
                return Results.Problem("Retained journal scope does not match the selected period and book.", statusCode: 503);
            try
            {
                var bytes = format == "csv" ? LedgerJournalExporter.ExportCsv(entries, request!.AccountMapping, bookId)
                    : await LedgerJournalExporter.ExportParquetAsync(entries, request!.AccountMapping, context.RequestAborted, bookId).ConfigureAwait(false);
                return Results.File(bytes, format == "csv" ? "text/csv; charset=utf-8" : "application/vnd.apache.parquet", $"journal-{periodId:D}.{format}");
            }
            catch (ArgumentException error) { return Results.BadRequest(error.Message); }
        }).WithName("ExportLedgerPeriodJournal")
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
            .Produces(200).Produces(400).Produces(403).Produces(404).Produces(501).Produces(503);
    }
}
