using System.Text.Json;
using Meridian.Application.FundAccounts;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static class FundAccountEndpoints
{
    public static void MapFundAccountEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/fund-accounts").WithTags("Fund Accounts");

        // ── Account CRUD ─────────────────────────────────────────────────────

        group.MapPost("/", async (JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize<CreateAccountRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);

            var result = await service.CreateAccountAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("CreateFundAccount")
        .Produces<AccountSummaryDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/{accountId:guid}", async (Guid accountId, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var result = await service.GetAccountAsync(accountId, context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("GetFundAccount")
        .Produces<AccountSummaryDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/", async (HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var q = context.Request.Query;
            var query = new AccountStructureQuery(
                AccountId: Guid.TryParse(q["accountId"], out var aid) ? aid : null,
                FundId: Guid.TryParse(q["fundId"], out var fid) ? fid : null,
                EntityId: Guid.TryParse(q["entityId"], out var eid) ? eid : null,
                ActiveOnly: q["activeOnly"] != "false");

            var results = await service.QueryAccountsAsync(query, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("QueryFundAccounts")
        .Produces<IReadOnlyList<AccountSummaryDto>>(StatusCodes.Status200OK);

        group.MapGet("/fund/{fundId:guid}", async (Guid fundId, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var result = await service.GetFundAccountsAsync(fundId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetFundAccounts")
        .Produces<FundAccountsDto>(StatusCodes.Status200OK);

        group.MapPatch("/{accountId:guid}/custodian-details", async (Guid accountId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize<UpdateCustodianAccountDetailsRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);

            var result = await service.UpdateCustodianDetailsAsync(accountId, request, context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("UpdateCustodianAccountDetails")
        .Produces<AccountSummaryDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPatch("/{accountId:guid}/bank-details", async (Guid accountId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize<UpdateBankAccountDetailsRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);

            var result = await service.UpdateBankDetailsAsync(accountId, request, context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("UpdateBankAccountDetails")
        .Produces<AccountSummaryDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{accountId:guid}", async (Guid accountId, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var deactivatedBy = context.Request.Query["deactivatedBy"].FirstOrDefault() ?? "system";
            var result = await service.DeactivateAccountAsync(accountId, deactivatedBy, context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("DeactivateFundAccount")
        .Produces<AccountSummaryDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // ── Brokerage read-side sync ──────────────────────────────────────────

        group.MapGet("/brokerage-sync/accounts", async (HttpContext context) =>
        {
            var sync = ResolveBrokerageSyncService(context);
            if (sync is null)
                return BrokerageSyncUnavailable();

            var accounts = await sync.DiscoverAccountsAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(accounts, jsonOptions);
        })
        .WithName("DiscoverBrokerageSyncAccounts")
        .Produces<IReadOnlyList<WorkstationBrokerageAccountDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status501NotImplemented);

        group.MapGet("/{accountId:guid}/brokerage-sync/status", async (Guid accountId, HttpContext context) =>
        {
            var sync = ResolveBrokerageSyncService(context);
            if (sync is null)
                return BrokerageSyncUnavailable();

            var status = await sync.GetStatusAsync(accountId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(status, jsonOptions);
        })
        .WithName("GetAccountBrokerageSyncStatus")
        .Produces<WorkstationBrokerageSyncStatusDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status501NotImplemented);

        group.MapPost("/{accountId:guid}/brokerage-sync/run", async (Guid accountId, HttpContext context) =>
        {
            var sync = ResolveBrokerageSyncService(context);
            if (sync is null)
                return BrokerageSyncUnavailable();

            var request = await ReadBrokerageSyncRequestAsync(context, jsonOptions).ConfigureAwait(false)
                ?? new WorkstationBrokerageSyncRunRequestDto();
            var status = await sync.RunSyncAsync(accountId, request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(status, jsonOptions);
        })
        .WithName("RunAccountBrokerageSync")
        .Accepts<WorkstationBrokerageSyncRunRequestDto>("application/json")
        .Produces<WorkstationBrokerageSyncStatusDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status501NotImplemented);

        group.MapGet("/{accountId:guid}/brokerage-sync/positions", async (Guid accountId, HttpContext context) =>
        {
            var sync = ResolveBrokerageSyncService(context);
            if (sync is null)
                return BrokerageSyncUnavailable();

            var positions = await sync.GetPositionsAsync(accountId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(positions, jsonOptions);
        })
        .WithName("GetAccountBrokerageSyncPositions")
        .Produces<IReadOnlyList<WorkstationBrokeragePositionDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status501NotImplemented);

        group.MapGet("/{accountId:guid}/brokerage-sync/activity", async (Guid accountId, HttpContext context) =>
        {
            var sync = ResolveBrokerageSyncService(context);
            if (sync is null)
                return BrokerageSyncUnavailable();

            var view = await sync.GetActivityAsync(accountId, context.RequestAborted).ConfigureAwait(false);
            return view is null
                ? Results.NotFound()
                : Results.Json(view, jsonOptions);
        })
        .WithName("GetAccountBrokerageSyncActivity")
        .Produces<WorkstationBrokerageSyncViewDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented);

        // ── Balance snapshots ─────────────────────────────────────────────────

        group.MapPost("/{accountId:guid}/balance-snapshots", async (Guid accountId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize<RecordAccountBalanceSnapshotRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);

            var result = await service.RecordBalanceSnapshotAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("RecordAccountBalanceSnapshot")
        .Produces<AccountBalanceSnapshotDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/{accountId:guid}/balance-snapshots", async (Guid accountId, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var q = context.Request.Query;
            DateOnly? from = DateOnly.TryParse(q["from"], out var f) ? f : null;
            DateOnly? to = DateOnly.TryParse(q["to"], out var t) ? t : null;

            var results = await service.GetBalanceHistoryAsync(accountId, from, to, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetAccountBalanceHistory")
        .Produces<IReadOnlyList<AccountBalanceSnapshotDto>>(StatusCodes.Status200OK);

        group.MapGet("/{accountId:guid}/balance-snapshots/latest", async (Guid accountId, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var result = await service.GetLatestBalanceSnapshotAsync(accountId, context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("GetLatestAccountBalanceSnapshot")
        .Produces<AccountBalanceSnapshotDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // ── Statement ingestion ───────────────────────────────────────────────

        group.MapPost("/{accountId:guid}/custodian-statements", async (Guid accountId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize<IngestCustodianStatementRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);

            var result = await service.IngestCustodianStatementAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("IngestCustodianStatement")
        .Produces<CustodianStatementBatchDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/{accountId:guid}/custodian-positions", async (Guid accountId, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            if (!DateOnly.TryParse(context.Request.Query["asOfDate"], out var asOfDate))
                return Results.Problem("'asOfDate' query parameter is required (YYYY-MM-DD).", statusCode: StatusCodes.Status400BadRequest);

            var results = await service.GetCustodianPositionsAsync(accountId, asOfDate, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetCustodianPositions")
        .Produces<IReadOnlyList<CustodianPositionLineDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/{accountId:guid}/bank-statements", async (Guid accountId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize<IngestBankStatementRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);

            var result = await service.IngestBankStatementAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("IngestBankStatement")
        .Produces<BankStatementBatchDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/{accountId:guid}/bank-statement-lines", async (Guid accountId, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var q = context.Request.Query;
            DateOnly? from = DateOnly.TryParse(q["from"], out var f) ? f : null;
            DateOnly? to = DateOnly.TryParse(q["to"], out var t) ? t : null;

            var results = await service.GetBankStatementLinesAsync(accountId, from, to, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetBankStatementLines")
        .Produces<IReadOnlyList<BankStatementLineDto>>(StatusCodes.Status200OK);

        // ── Reconciliation ────────────────────────────────────────────────────

        group.MapPost("/{accountId:guid}/reconcile", async (Guid accountId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize<ReconcileAccountRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);

            var result = await service.ReconcileAccountAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("ReconcileFundAccount")
        .Produces<AccountReconciliationRunDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/{accountId:guid}/reconciliation-runs", async (Guid accountId, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var results = await service.GetReconciliationRunsAsync(accountId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetAccountReconciliationRuns")
        .Produces<IReadOnlyList<AccountReconciliationRunDto>>(StatusCodes.Status200OK);

        group.MapGet("/reconciliation-runs/{runId:guid}/results", async (Guid runId, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var results = await service.GetReconciliationResultsAsync(runId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetAccountReconciliationResults")
        .Produces<IReadOnlyList<AccountReconciliationResultDto>>(StatusCodes.Status200OK);
    }

    private static IFundAccountService? ResolveService(HttpContext context) =>
        context.RequestServices.GetService<IFundAccountService>();

    private static BrokeragePortfolioSyncService? ResolveBrokerageSyncService(HttpContext context) =>
        context.RequestServices.GetService<BrokeragePortfolioSyncService>();

    private static IResult ServiceUnavailable() =>
        Results.Problem("Fund account service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

    private static IResult BrokerageSyncUnavailable() =>
        Results.Problem("Brokerage sync service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

    private static async Task<WorkstationBrokerageSyncRunRequestDto?> ReadBrokerageSyncRequestAsync(
        HttpContext context,
        JsonSerializerOptions jsonOptions)
    {
        if (context.Request.ContentLength is null or 0)
        {
            return null;
        }

        return await JsonSerializer.DeserializeAsync<WorkstationBrokerageSyncRunRequestDto>(
                context.Request.Body,
                jsonOptions,
                context.RequestAborted)
            .ConfigureAwait(false);
    }
}
