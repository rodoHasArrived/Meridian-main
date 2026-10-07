using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    private static void MapDisposalTaxEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapGet(UiApiRoutes.LedgerJournalEntryTaxResults, async (
            Guid periodId, Guid journalEntryId, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
                return EndpointHelpers.Forbidden();
            var store = ResolveJournalStore(context);
            if (store is null)
                return ServiceUnavailable();
            var ct = context.RequestAborted;
            var period = await store.GetPeriodAsync(periodId, ct).ConfigureAwait(false);
            if (period?.LedgerBookId is not { } bookId)
                return Results.NotFound();
            var book = await store.GetLedgerBookAsync(bookId, ct).ConfigureAwait(false);
            if (book is null)
                return Results.NotFound();
            var entries = await store.QueryAsync(new LedgerJournalEntryQuery(
                    LedgerBookId: bookId, PeriodId: periodId, JournalEntryId: journalEntryId), ct)
                .ConfigureAwait(false);
            var journal = entries.SingleOrDefault(record => record.Entry.JournalEntryId == journalEntryId && record.PeriodId == periodId);
            if (journal is null)
                return Results.NotFound();
            var history = context.RequestServices.GetService<ILedgerTaxLotDisposalHistory>() ?? store as ILedgerTaxLotDisposalHistory;
            var result = await new LedgerDisposalTaxReadService(history).ReadAsync(bookId, periodId,
                journal.Entry, book.BaseCurrency, ct).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetLedgerJournalEntryTaxResults")
        .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .Produces<LedgerJournalTaxResultsDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented);
    }
}
