using System.Text.Json;
using Meridian.Application.Accounts;
using Meridian.Application.FundAccounts;
using Meridian.Application.FundStructure;
using Meridian.Contracts.Auth;
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
        group.AddEndpointFilter(RequireFundAccountAccess);

        // ── Account CRUD ─────────────────────────────────────────────────────

        group.MapPost("/", async (JsonElement body, HttpContext context) =>
        {
            var service = ResolveManagementService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize(body.GetRawText(), FundStructureContractsJsonContext.Default.CreateAccountRequest);
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
            var queryService = ResolveQueryService(context);
            if (queryService is null)
                return ServiceUnavailable();

            var result = await queryService.GetAccountAsync(accountId, context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("GetFundAccount")
        .Produces<AccountSummaryDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/", async (HttpContext context) =>
        {
            var queryService = ResolveQueryService(context);
            if (queryService is null)
                return ServiceUnavailable();

            var q = context.Request.Query;
            var type = Enum.TryParse<AccountTypeDto>(q["accountType"], true, out var parsedType) ? parsedType : (AccountTypeDto?)null;
            var isActive = q.ContainsKey("activeOnly") ? q["activeOnly"] != "false" : true;
            var currency = q["currency"].FirstOrDefault();
            var accountId = TryParseGuidFilter(q["accountId"].FirstOrDefault());
            var fundId = TryParseGuidFilter(q["fundId"].FirstOrDefault());
            var entityId = TryParseGuidFilter(q["entityId"].FirstOrDefault());

            var results = await queryService.ListAccountsAsync(type, isActive, currency, context.RequestAborted).ConfigureAwait(false);
            if (accountId.HasValue)
            {
                results = results.Where(account => account.AccountId == accountId.Value).ToList();
            }

            if (fundId.HasValue)
            {
                results = results.Where(account => account.FundId == fundId.Value).ToList();
            }

            if (entityId.HasValue)
            {
                results = results.Where(account => account.EntityId == entityId.Value).ToList();
            }

            return Results.Json(results, jsonOptions);
        })
        .WithName("QueryFundAccounts")
        .Produces<IReadOnlyList<AccountSummaryDto>>(StatusCodes.Status200OK);

        group.MapGet("/fund/{fundId:guid}", async (Guid fundId, HttpContext context) =>
        {
            var queryService = ResolveQueryService(context);
            if (queryService is null)
                return ServiceUnavailable();

            var result = await queryService.GetFundAccountsAsync(fundId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetFundAccounts")
        .Produces<FundAccountsDto>(StatusCodes.Status200OK);

        app.MapGet("/api/funds/{fundId:guid}/accounts", async (Guid fundId, string? accountType, string? status, HttpContext context) =>
        {
            var query = context.RequestServices.GetService<IFundAccountTraversalQueryService>();
            if (query is null)
            {
                return ServiceUnavailable();
            }

            var parsedType = Enum.TryParse<AccountTypeDto>(accountType, ignoreCase: true, out var typeValue)
                ? typeValue
                : (AccountTypeDto?)null;
            var activeOnly = !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(status, "suspended", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(status, "closed", StringComparison.OrdinalIgnoreCase);

            var accounts = await query.GetFundAccountsAsync(fundId, parsedType, activeOnly, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(accounts, jsonOptions);
        })
        .WithName("GetFundAccountsByOwnershipTraversal")
        .Produces<IReadOnlyList<AccountSummaryDto>>(StatusCodes.Status200OK);

        group.MapPatch("/{accountId:guid}/custodian-details", async (Guid accountId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveManagementService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize(body.GetRawText(), FundStructureContractsJsonContext.Default.UpdateCustodianAccountDetailsRequest);
            if (request is null)
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);

                        try
            {
                var result = await service.UpdateCustodianDetailsAsync(accountId, request, context.RequestAborted).ConfigureAwait(false);
                return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
            }
            catch (AccountStatusPolicyException ex)
            {
                return StatusPolicyConflict(ex);
            }
        })
        .WithName("UpdateCustodianAccountDetails")
        .Produces<AccountSummaryDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPatch("/{accountId:guid}/bank-details", async (Guid accountId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveManagementService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize(body.GetRawText(), FundStructureContractsJsonContext.Default.UpdateBankAccountDetailsRequest);
            if (request is null)
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);

                        try
            {
                var result = await service.UpdateBankDetailsAsync(accountId, request, context.RequestAborted).ConfigureAwait(false);
                return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
            }
            catch (AccountStatusPolicyException ex)
            {
                return StatusPolicyConflict(ex);
            }
        })
        .WithName("UpdateBankAccountDetails")
        .Produces<AccountSummaryDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{accountId:guid}", async (Guid accountId, HttpContext context) =>
        {
            var service = ResolveManagementService(context);
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
            if (!HasBrokerageSyncAccess(context))
                return EndpointHelpers.Forbidden();

            var sync = ResolveBrokerageSyncService(context);
            if (sync is null)
                return BrokerageSyncUnavailable();

            var accounts = await sync.DiscoverAccountsAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(accounts, jsonOptions);
        })
        .WithName("DiscoverBrokerageSyncAccounts")
        .Produces<IReadOnlyList<WorkstationBrokerageAccountDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapGet("/api/portfolio/household", async (string? provider, HttpContext context) =>
        {
            if (!HasBrokerageSyncAccess(context))
                return EndpointHelpers.Forbidden();

            var sync = ResolveBrokerageSyncService(context);
            if (sync is null)
                return BrokerageSyncUnavailable();

            var household = await sync.GetHouseholdAsync(provider, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(household, jsonOptions);
        })
        .WithName("GetBrokerageHouseholdPortfolio")
        .Produces<BrokerageHouseholdPortfolioDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status501NotImplemented);

        group.MapGet("/{accountId:guid}/brokerage-sync/status", async (Guid accountId, HttpContext context) =>
        {
            if (!await CanAccessFundAccountBrokerageSyncAsync(accountId, context).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();

            var sync = ResolveBrokerageSyncService(context);
            if (sync is null)
                return BrokerageSyncUnavailable();

            var status = await sync.GetStatusAsync(accountId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(status, jsonOptions);
        })
        .WithName("GetAccountBrokerageSyncStatus")
        .Produces<WorkstationBrokerageSyncStatusDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status501NotImplemented);

        group.MapPost("/{accountId:guid}/brokerage-sync/link", async (Guid accountId, HttpContext context) =>
        {
            if (!await CanAccessFundAccountBrokerageSyncAsync(accountId, context, requireWriteAccess: true).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();

            var sync = ResolveBrokerageSyncService(context);
            if (sync is null)
                return BrokerageSyncUnavailable();

            var request = await ReadBrokerageLinkRequestAsync(context, jsonOptions).ConfigureAwait(false);
            if (request is null)
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);

            var link = await sync.LinkAccountAsync(accountId, request, context.RequestAborted).ConfigureAwait(false);
            return link is null
                ? Results.Problem("Fund account or brokerage account link is invalid.", statusCode: StatusCodes.Status400BadRequest)
                : Results.Json(link, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("LinkAccountBrokerageSync")
        .Accepts<BrokerageAccountLinkRequestDto>("application/json")
        .Produces<WorkstationBrokerageAccountLinkDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status501NotImplemented);

        group.MapPost("/{accountId:guid}/brokerage-sync/run", async (Guid accountId, HttpContext context) =>
        {
            if (!await CanAccessFundAccountBrokerageSyncAsync(accountId, context, requireWriteAccess: true).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();

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

#pragma warning disable CS0618 // Compatibility routes retain legacy brokerage projection payloads; readiness uses status DTOs.
        group.MapGet("/{accountId:guid}/brokerage-sync/positions", async (Guid accountId, HttpContext context) =>
        {
            if (!await CanAccessFundAccountBrokerageSyncAsync(accountId, context).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();

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
            if (!await CanAccessFundAccountBrokerageSyncAsync(accountId, context).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();

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
#pragma warning restore CS0618

        group.MapGet("/{accountId:guid}/performance", async (Guid accountId, HttpContext context) =>
        {
            if (!await CanAccessFundAccountBrokerageSyncAsync(accountId, context).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();

            var sync = ResolveBrokerageSyncService(context);
            if (sync is null)
                return BrokerageSyncUnavailable();

            var q = context.Request.Query;
            var from = DateOnly.TryParse(q["from"], out var parsedFrom) ? parsedFrom : (DateOnly?)null;
            var to = DateOnly.TryParse(q["to"], out var parsedTo) ? parsedTo : (DateOnly?)null;
            var performance = await sync.GetPerformanceAsync(accountId, from, to, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(performance, jsonOptions);
        })
        .WithName("GetAccountBrokeragePerformance")
        .Produces<BrokeragePortfolioPerformanceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status501NotImplemented);

        group.MapGet("/{accountId:guid}/cash-flow", async (Guid accountId, HttpContext context) =>
        {
            if (!await CanAccessFundAccountBrokerageSyncAsync(accountId, context).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();

            var sync = ResolveBrokerageSyncService(context);
            if (sync is null)
                return BrokerageSyncUnavailable();

            var q = context.Request.Query;
            var from = DateOnly.TryParse(q["from"], out var parsedFrom) ? parsedFrom : (DateOnly?)null;
            var to = DateOnly.TryParse(q["to"], out var parsedTo) ? parsedTo : (DateOnly?)null;
            var cashFlow = await sync.GetCashFlowAsync(accountId, from, to, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(cashFlow, jsonOptions);
        })
        .WithName("GetAccountBrokerageCashFlow")
        .Produces<BrokerageCashFlowSummaryDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status501NotImplemented);

        // ── Balance snapshots ─────────────────────────────────────────────────

        group.MapPost("/{accountId:guid}/balance-snapshots", async (Guid accountId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveManagementService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize(body.GetRawText(), FundStructureContractsJsonContext.Default.RecordAccountBalanceSnapshotRequest);
            if (request is null)
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);

                        try
            {
                var result = await service.RecordBalanceSnapshotAsync(request, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
            }
            catch (AccountStatusPolicyException ex)
            {
                return StatusPolicyConflict(ex);
            }
        })
        .WithName("RecordAccountBalanceSnapshot")
        .Produces<AccountBalanceSnapshotDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/{accountId:guid}/balance-snapshots", async (Guid accountId, HttpContext context) =>
        {
            var queryService = ResolveQueryService(context);
            if (queryService is null)
                return ServiceUnavailable();

            var q = context.Request.Query;
            DateOnly? from = DateOnly.TryParse(q["from"], out var f) ? f : null;
            DateOnly? to = DateOnly.TryParse(q["to"], out var t) ? t : null;

            var results = await queryService.GetBalanceTimelineAsync(accountId, from, to, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetAccountBalanceHistory")
        .Produces<IReadOnlyList<AccountBalanceSnapshotDto>>(StatusCodes.Status200OK);

        group.MapGet("/{accountId:guid}/balance-snapshots/latest", async (Guid accountId, HttpContext context) =>
        {
            var queryService = ResolveQueryService(context);
            if (queryService is null)
                return ServiceUnavailable();

            var result = await queryService.GetLatestBalanceSnapshotAsync(accountId, context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("GetLatestAccountBalanceSnapshot")
        .Produces<AccountBalanceSnapshotDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // ── Sync history and readiness ───────────────────────────────────────

        group.MapGet("/{accountId:guid}/sync-history", async (Guid accountId, HttpContext context) =>
        {
            var queryService = ResolveQueryService(context);
            if (queryService is null)
                return ServiceUnavailable();

            var capability = context.Request.Query["capability"].FirstOrDefault();
            var results = await queryService.GetSyncHistoryAsync(accountId, capability, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetAccountSyncHistory")
        .Produces<IReadOnlyList<AccountSyncHistoryEntryDto>>(StatusCodes.Status200OK);

        group.MapGet("/{accountId:guid}/readiness", async (Guid accountId, HttpContext context) =>
        {
            var queryService = ResolveQueryService(context);
            if (queryService is null)
                return ServiceUnavailable();

            var result = await queryService.GetReadinessAsync(accountId, context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("GetAccountReadiness")
        .Produces<AccountReadinessSnapshotDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // ── Statement ingestion ───────────────────────────────────────────────

        group.MapPost("/{accountId:guid}/custodian-statements", async (Guid accountId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveManagementService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize(body.GetRawText(), FundStructureContractsJsonContext.Default.IngestCustodianStatementRequest);
            if (request is null)
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);

                        try
            {
                var result = await service.IngestCustodianStatementAsync(request, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
            }
            catch (AccountStatusPolicyException ex)
            {
                return StatusPolicyConflict(ex);
            }
        })
        .WithName("IngestCustodianStatement")
        .Produces<CustodianStatementBatchDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/{accountId:guid}/custodian-positions", async (Guid accountId, HttpContext context) =>
        {
            var queryService = ResolveQueryService(context);
            if (queryService is null)
                return ServiceUnavailable();

            if (!DateOnly.TryParse(context.Request.Query["asOfDate"], out var asOfDate))
                return Results.Problem("'asOfDate' query parameter is required (YYYY-MM-DD).", statusCode: StatusCodes.Status400BadRequest);

            var results = await queryService.GetCustodianPositionsAsync(accountId, asOfDate, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetCustodianPositions")
        .Produces<IReadOnlyList<CustodianPositionLineDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/{accountId:guid}/bank-statements", async (Guid accountId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveManagementService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize(body.GetRawText(), FundStructureContractsJsonContext.Default.IngestBankStatementRequest);
            if (request is null)
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);

                        try
            {
                var result = await service.IngestBankStatementAsync(request, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
            }
            catch (AccountStatusPolicyException ex)
            {
                return StatusPolicyConflict(ex);
            }
        })
        .WithName("IngestBankStatement")
        .Produces<BankStatementBatchDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/{accountId:guid}/bank-statement-lines", async (Guid accountId, HttpContext context) =>
        {
            var queryService = ResolveQueryService(context);
            if (queryService is null)
                return ServiceUnavailable();

            var q = context.Request.Query;
            DateOnly? from = DateOnly.TryParse(q["from"], out var f) ? f : null;
            DateOnly? to = DateOnly.TryParse(q["to"], out var t) ? t : null;

            var results = await queryService.GetBankStatementLinesAsync(accountId, from, to, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetBankStatementLines")
        .Produces<IReadOnlyList<BankStatementLineDto>>(StatusCodes.Status200OK);

        // ── Reconciliation ────────────────────────────────────────────────────

        group.MapPost("/{accountId:guid}/reconcile", async (Guid accountId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveManagementService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize(body.GetRawText(), FundStructureContractsJsonContext.Default.ReconcileAccountRequest);
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
            var queryService = ResolveQueryService(context);
            if (queryService is null)
                return ServiceUnavailable();

            var results = await queryService.GetReconciliationRunsAsync(accountId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetAccountReconciliationRuns")
        .Produces<IReadOnlyList<AccountReconciliationRunDto>>(StatusCodes.Status200OK);

        group.MapGet("/reconciliation-runs/{runId:guid}/results", async (Guid runId, HttpContext context) =>
        {
            var queryService = ResolveQueryService(context);
            if (queryService is null)
                return ServiceUnavailable();

            var results = await queryService.GetReconciliationResultsAsync(runId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetAccountReconciliationResults")
        .Produces<IReadOnlyList<AccountReconciliationResultDto>>(StatusCodes.Status200OK);
    }

    private static IResult StatusPolicyConflict(AccountStatusPolicyException ex) => Results.Json(new
    {
        error = "account_status_policy_violation",
        status = ex.Status.ToString(),
        operation = ex.Operation,
        backfillAttempted = ex.BackfillAttempted,
        detail = ex.Message
    }, statusCode: StatusCodes.Status409Conflict);

    private static IAccountManagementService? ResolveManagementService(HttpContext context) =>
        context.RequestServices.GetService<IAccountManagementService>()
        ?? context.RequestServices.GetService<IFundAccountService>() as IAccountManagementService;

    private static IAccountQueryService? ResolveQueryService(HttpContext context) =>
        context.RequestServices.GetService<IAccountQueryService>()
        ?? context.RequestServices.GetService<IFundAccountService>() as IAccountQueryService;

    private static BrokeragePortfolioSyncService? ResolveBrokerageSyncService(HttpContext context) =>
        context.RequestServices.GetService<BrokeragePortfolioSyncService>();

    private static Guid? TryParseGuidFilter(string? raw)
    {
        return Guid.TryParse(raw, out var parsed) ? parsed : null;
    }

    private static ValueTask<object?> RequireFundAccountAccess(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!TryGetCurrentRole(context.HttpContext, out var role))
        {
            return ValueTask.FromResult<object?>(Results.Unauthorized());
        }

        if (role is UserRole.Admin or UserRole.Developer or UserRole.Accounting)
        {
            return next(context);
        }

        return ValueTask.FromResult<object?>(EndpointHelpers.Forbidden());
    }

    private static bool TryGetCurrentRole(HttpContext context, out UserRole role)
    {
        if (context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserRoleKey, out var item) && item is UserRole currentRole)
        {
            role = currentRole;
            return true;
        }

        role = default;
        return false;
    }

    private static IResult ServiceUnavailable() =>
        Results.Problem("Fund account service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

    private static IResult BrokerageSyncUnavailable() =>
        Results.Problem("Brokerage sync service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

    private static bool HasBrokerageSyncAccess(HttpContext context)
        => TryGetCurrentPermissions(context, out var permissions)
           && permissions.HasFlag(UserPermission.ViewTrades);

    private static async Task<bool> CanAccessFundAccountBrokerageSyncAsync(
        Guid accountId,
        HttpContext context,
        bool requireWriteAccess = false)
    {
        if (!TryGetCurrentPermissions(context, out var permissions)
            || !permissions.HasFlag(UserPermission.ViewTrades))
        {
            return false;
        }

        if (requireWriteAccess
            && !permissions.HasFlag(UserPermission.ExecuteTrades)
            && !permissions.HasFlag(UserPermission.ManageOrders))
        {
            return false;
        }

        var queryService = ResolveQueryService(context);
        if (queryService is null)
        {
            return false;
        }

        var account = await queryService.GetAccountAsync(accountId, context.RequestAborted).ConfigureAwait(false);
        return account is not null;
    }

    private static bool TryGetCurrentPermissions(HttpContext context, out UserPermission permissions)
    {
        permissions = UserPermission.None;
        if (context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] is UserPermission currentPermissions)
        {
            permissions = currentPermissions;
            return true;
        }

        return false;
    }

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

    private static async Task<BrokerageAccountLinkRequestDto?> ReadBrokerageLinkRequestAsync(
        HttpContext context,
        JsonSerializerOptions jsonOptions)
    {
        if (context.Request.ContentLength is null or 0)
        {
            return null;
        }

        return await JsonSerializer.DeserializeAsync<BrokerageAccountLinkRequestDto>(
                context.Request.Body,
                jsonOptions,
                context.RequestAborted)
            .ConfigureAwait(false);
    }
}
