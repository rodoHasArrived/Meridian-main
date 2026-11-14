using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Ledger;
using Meridian.Storage.Export;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapRunLedgerQueryEndpoints(RouteGroupBuilder group)
    {
        AddLedgerRead(group.MapGet(WorkstationSubroute(UiApiRoutes.RunsLedgerEntries),
            (string runId, DateTimeOffset? from, DateTimeOffset? to, string? symbol, int? offset, int? limit, HttpContext context) =>
                ReadRunLedgerEntriesAsync(runId, from, to, symbol, offset, limit, context)))
            .WithName("GetRunLedgerEntries");

        AddLedgerRead(group.MapGet(WorkstationSubroute(UiApiRoutes.RunsLedgerSymbol),
            (string runId, string symbol, DateTimeOffset? from, DateTimeOffset? to, int? offset, int? limit, HttpContext context) =>
                ReadRunLedgerEntriesAsync(runId, from, to, symbol, offset, limit, context)))
            .WithName("GetRunSymbolLedger");

        AddLedgerRead(group.MapGet(WorkstationSubroute(UiApiRoutes.RunsLedgerAccounts),
            (string runId, DateTimeOffset? asOf, string? financialAccountId, HttpContext context) =>
                ReadRunLedgerAccountsAsync(runId, asOf, financialAccountId, context)))
            .WithName("GetRunLedgerAccounts");

        AddLedgerRead(group.MapGet(WorkstationSubroute(UiApiRoutes.RunsLedgerTrialBalanceAsOf),
            (string runId, DateTimeOffset asOf, string? financialAccountId, HttpContext context) =>
                ReadRunLedgerAccountsAsync(runId, asOf, financialAccountId, context)))
            .WithName("GetRunLedgerTrialBalanceAsOf");

        AddLedgerRead(group.MapGet(WorkstationSubroute(UiApiRoutes.RunsLedgerRunningBalance), async (
            string runId, string accountName, string? accountType, string? symbol, string? financialAccountId,
            DateTimeOffset? from, DateTimeOffset? to, HttpContext context) =>
        {
            if (from > to)
                return Results.BadRequest("from must be on or before to.");
            var service = context.RequestServices.GetService<StrategyRunReadService>();
            if (service is null)
                return Results.Problem("Strategy run service is not registered.", statusCode: 501);
            var ledger = await service.GetLedgerSnapshotAsync(runId, ResolveStrategyRunReadScope(context), ct: context.RequestAborted).ConfigureAwait(false);
            if (ledger is null)
                return Results.NotFound();
            var accounts = ledger.Accounts.Where(account =>
                string.Equals(account.Name, accountName, StringComparison.Ordinal) &&
                (accountType is null || string.Equals(account.AccountType.ToString(), accountType, StringComparison.OrdinalIgnoreCase)) &&
                (symbol is null || string.Equals(account.Symbol ?? "", symbol, StringComparison.OrdinalIgnoreCase)) &&
                (financialAccountId is null || string.Equals(account.FinancialAccountId ?? "", financialAccountId, StringComparison.OrdinalIgnoreCase)))
                .Take(2).ToArray();
            if (accounts.Length == 0)
                return Results.NotFound();
            if (accounts.Length > 1)
                return Results.BadRequest("Account is ambiguous; specify accountType, symbol and financialAccountId (empty selects unscoped).");
            // DateTimeOffset.MinValue has no preceding tick; omitting the lower bound is equivalent.
            var points = ledger.GetRunningBalance(accounts[0], from == DateTimeOffset.MinValue ? null : from, to).ToArray();
            return Results.Json(points, RunLedgerQueryJsonContext.Default.LedgerBalancePointArray);
        })).WithName("GetRunLedgerRunningBalance");

        AddLedgerRead(group.MapPost(WorkstationSubroute(UiApiRoutes.RunsLedgerExport), async (
            string runId, DateTimeOffset? from, DateTimeOffset? to, HttpContext context) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ExportData))
                return EndpointHelpers.Forbidden();
            if (from > to)
                return Results.BadRequest("from must be on or before to.");
            if (!context.Request.HasJsonContentType())
                return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
            RunLedgerExportRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync(RunLedgerQueryJsonContext.Default.RunLedgerExportRequest, context.RequestAborted).ConfigureAwait(false);
            }
            catch (JsonException) { return Results.BadRequest("Invalid ledger export request."); }
            if (request is null)
                return Results.BadRequest("A ledger export request is required.");
            var format = request.Format?.Trim().ToLowerInvariant();
            if (format is not ("csv" or "parquet"))
                return Results.BadRequest("format must be csv or parquet.");
            var service = context.RequestServices.GetService<StrategyRunReadService>();
            if (service is null)
                return Results.Problem("Strategy run service is not registered.", statusCode: 501);
            var ledger = await service.GetLedgerSnapshotAsync(runId, ResolveStrategyRunReadScope(context), ct: context.RequestAborted).ConfigureAwait(false);
            if (ledger is null)
                return Results.NotFound();
            try
            {
                var query = new LedgerQuery(From: from, To: to);
                var bytes = format == "csv"
                    ? LedgerJournalExporter.ExportCsv(ledger, request.AccountMapping, query)
                    : await LedgerJournalExporter.ExportParquetAsync(ledger, request.AccountMapping, query, context.RequestAborted).ConfigureAwait(false);
                return Results.File(bytes, format == "csv" ? "text/csv; charset=utf-8" : "application/vnd.apache.parquet", $"journal.{format}");
            }
            catch (ArgumentException error) { return Results.BadRequest(error.Message); }
        })).WithName("ExportRunLedgerJournal");
    }

    private static RouteHandlerBuilder AddLedgerRead(RouteHandlerBuilder endpoint) => endpoint
        .RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .AddEndpointFilter(RequireStrategyRunReadAccessAsync)
        .Produces(200).Produces(400).Produces(404).Produces(501);

    private static async Task<IResult> ReadRunLedgerAccountsAsync(
        string runId, DateTimeOffset? asOf, string? financialAccountId, HttpContext context)
    {
        var service = context.RequestServices.GetService<StrategyRunReadService>();
        if (service is null)
            return Results.Problem("Strategy run service is not registered.", statusCode: 501);
        var ledger = await service.GetLedgerSnapshotAsync(runId, ResolveStrategyRunReadScope(context), asOf, context.RequestAborted).ConfigureAwait(false);
        return ledger is null ? Results.NotFound() : Results.Json(
            ledger.SummarizeAccounts(financialAccountId: financialAccountId).ToArray(),
            RunLedgerQueryJsonContext.Default.LedgerAccountSummaryArray);
    }

    private static async Task<IResult> ReadRunLedgerEntriesAsync(
        string runId, DateTimeOffset? from, DateTimeOffset? to, string? symbol, int? offset, int? limit, HttpContext context)
    {
        if (from > to || offset is < 0 || limit is < 1 or > 10000)
            return Results.BadRequest("Use from <= to, offset >= 0, and limit between 1 and 10000.");
        var service = context.RequestServices.GetService<StrategyRunReadService>();
        if (service is null)
            return Results.Problem("Strategy run service is not registered.", statusCode: 501);
        var ledger = await service.GetLedgerSnapshotAsync(runId, ResolveStrategyRunReadScope(context), ct: context.RequestAborted).ConfigureAwait(false);
        if (ledger is null)
            return Results.NotFound();
        var entries = ledger.GetJournalEntries(from, to).Where(entry => string.IsNullOrWhiteSpace(symbol) ||
            string.Equals(entry.Metadata.Symbol, symbol, StringComparison.OrdinalIgnoreCase) ||
            entry.Lines.Any(line => string.Equals(line.Account.Symbol, symbol, StringComparison.OrdinalIgnoreCase)))
            .Skip(offset ?? 0).Take(limit ?? 500).ToArray();
        return Results.Json(entries, RunLedgerQueryJsonContext.Default.JournalEntryArray);
    }
}
