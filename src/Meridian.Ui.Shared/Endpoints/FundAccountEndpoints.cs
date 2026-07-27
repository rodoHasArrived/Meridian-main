using System.Text.Json;
using Meridian.PortfolioRecords.Accounts;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Application.FundStructure;
using Meridian.Identity.Auth;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Shared.Contracts.Reconciliation;
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

            var scopeGuard = await RequireScopedCreateAccountAccessAsync(request, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

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

            var scopeGuard = await RequireScopedAccountAccessAsync(accountId, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

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

            var scopeGuard = await RequireScopedAccountQueryAccessAsync(accountId, fundId, entityId, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

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

            var scopeGuard = await RequireScopedFundAccessAsync(fundId, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

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

            var scopeGuard = await RequireScopedFundAccessAsync(fundId, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

            var accounts = await query.GetFundAccountsAsync(fundId, parsedType, activeOnly, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(accounts, jsonOptions);
        })
        .WithName("GetFundAccountsByOwnershipTraversal")
        .Produces<IReadOnlyList<AccountSummaryDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .AddEndpointFilter(RequireFundAccountAccess);

        group.MapPatch("/{accountId:guid}/custodian-details", async (Guid accountId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveManagementService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize(body.GetRawText(), FundStructureContractsJsonContext.Default.UpdateCustodianAccountDetailsRequest);
            if (request is null)
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);

            var scopeGuard = await RequireScopedAccountAccessAsync(accountId, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

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

            var scopeGuard = await RequireScopedAccountAccessAsync(accountId, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

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

            var scopeGuard = await RequireScopedAccountAccessAsync(accountId, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

            if (!EndpointAuthorization.TryResolveActor(context, out var deactivatedBy))
            {
                return Results.Unauthorized();
            }

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
        .Produces<IReadOnlyList<FundAccountBrokeragePositionDto>>(StatusCodes.Status200OK)
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
        .Produces<FundAccountBrokerageSyncActivityDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented);

        group.MapPost("/{accountId:guid}/brokerage-sync/reconcile-ledger", async (Guid accountId, HttpContext context) =>
        {
            if (!await CanAccessFundAccountBrokerageSyncAsync(accountId, context, requireWriteAccess: true).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();

            var reconciliation = ResolveProviderLedgerReconciliationService(context);
            if (reconciliation is null)
                return ProviderLedgerReconciliationUnavailable();

            var request = await ReadProviderLedgerReconciliationRequestAsync(context, jsonOptions).ConfigureAwait(false)
                ?? new ProviderLedgerReconciliationRequestDto();
            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }
            if (request.SignedOffBreakKeys is { Count: > 0 } || !string.IsNullOrWhiteSpace(request.SignedOffBy))
            {
                return Results.Problem(
                    "Reconciliation sign-off cannot be asserted by this request. Use the governed reconciliation casework action with retained approval evidence.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var serverBoundRequest = request with
            {
                RequestedBy = actor.Trim(),
                SignedOffBreakKeys = [],
                SignedOffBy = null
            };
            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var accessScope = new ReconciliationBreakQueueScope(
                tenantContext.TenantId!,
                tenantContext.CompanyId!);
            var detail = await reconciliation
                .RunAsync(accountId, accessScope, serverBoundRequest, context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(detail, jsonOptions);
        })
        .WithName("RunProviderLedgerReconciliation")
        .Accepts<ProviderLedgerReconciliationRequestDto>("application/json")
        .Produces<ProviderLedgerReconciliationDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireWorkstationTenantCompanyScope();

        group.MapGet("/{accountId:guid}/brokerage-sync/reconciliation/latest", async (Guid accountId, HttpContext context) =>
        {
            if (!await CanAccessFundAccountBrokerageSyncAsync(accountId, context).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();

            var reconciliation = ResolveProviderLedgerReconciliationService(context);
            if (reconciliation is null)
                return ProviderLedgerReconciliationUnavailable();

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var accessScope = new ReconciliationBreakQueueScope(
                tenantContext.TenantId!,
                tenantContext.CompanyId!);
            var detail = await reconciliation
                .GetLatestAsync(accountId, accessScope, context.RequestAborted)
                .ConfigureAwait(false);
            return detail is null
                ? Results.NotFound()
                : Results.Json(detail, jsonOptions);
        })
        .WithName("GetLatestProviderLedgerReconciliation")
        .Produces<ProviderLedgerReconciliationDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireWorkstationTenantCompanyScope();

        group.MapGet("/{accountId:guid}/close-readiness", async (Guid accountId, HttpContext context) =>
        {
            var scopeGuard = await RequireScopedAccountAccessAsync(accountId, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

            var closeReadiness = ResolveFundAccountCloseReadinessService(context);
            if (closeReadiness is null)
                return FundAccountCloseReadinessUnavailable();

            var result = await closeReadiness.GetAsync(accountId, context.RequestAborted).ConfigureAwait(false);
            return result is null
                ? Results.NotFound()
                : Results.Json(result, jsonOptions);
        })
        .WithName("GetFundAccountCloseReadiness")
        .Produces<FundAccountCloseReadinessDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented);

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
            var scopeGuard = await RequireScopedAccountAccessAsync(accountId, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

            var service = ResolveManagementService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize(body.GetRawText(), FundStructureContractsJsonContext.Default.RecordAccountBalanceSnapshotRequest);
            if (request is null)
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
            if (request.AccountId != accountId)
                return AccountRouteMismatch();

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
            var scopeGuard = await RequireScopedAccountAccessAsync(accountId, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

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
            var scopeGuard = await RequireScopedAccountAccessAsync(accountId, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

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
            var scopeGuard = await RequireScopedAccountAccessAsync(accountId, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

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
            var scopeGuard = await RequireScopedAccountAccessAsync(accountId, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

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
            var scopeGuard = await RequireScopedAccountAccessAsync(accountId, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

            var service = ResolveManagementService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize(body.GetRawText(), FundStructureContractsJsonContext.Default.IngestCustodianStatementRequest);
            if (request is null)
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
            if (request.AccountId != accountId || request.Lines.Any(line => line.AccountId != accountId))
                return AccountRouteMismatch();

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
            var scopeGuard = await RequireScopedAccountAccessAsync(accountId, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

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
            var scopeGuard = await RequireScopedAccountAccessAsync(accountId, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

            var service = ResolveManagementService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize(body.GetRawText(), FundStructureContractsJsonContext.Default.IngestBankStatementRequest);
            if (request is null)
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
            if (request.AccountId != accountId || request.Lines.Any(line => line.AccountId != accountId))
                return AccountRouteMismatch();

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
            var scopeGuard = await RequireScopedAccountAccessAsync(accountId, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

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
            var scopeGuard = await RequireScopedAccountAccessAsync(accountId, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

            var service = ResolveManagementService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize(body.GetRawText(), FundStructureContractsJsonContext.Default.ReconcileAccountRequest);
            if (request is null)
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
            if (request.AccountId != accountId)
                return AccountRouteMismatch();

            var result = await service.ReconcileAccountAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("ReconcileFundAccount")
        .Produces<AccountReconciliationRunDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/{accountId:guid}/reconciliation-runs", async (Guid accountId, HttpContext context) =>
        {
            var scopeGuard = await RequireScopedAccountAccessAsync(accountId, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

            var queryService = ResolveQueryService(context);
            if (queryService is null)
                return ServiceUnavailable();

            var results = await queryService.GetReconciliationRunsAsync(accountId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetAccountReconciliationRuns")
        .Produces<IReadOnlyList<AccountReconciliationRunDto>>(StatusCodes.Status200OK);


        group.MapGet("/{accountId:guid}/reconciliation-queue-status", async (Guid accountId, HttpContext context) =>
        {
            var scopeGuard = await RequireScopedAccountAccessAsync(accountId, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

            var queryService = ResolveQueryService(context);
            if (queryService is null)
                return ServiceUnavailable();

            var runs = await queryService.GetReconciliationRunsAsync(accountId, context.RequestAborted).ConfigureAwait(false);
            var latestRun = runs.OrderByDescending(run => run.AsOfDate).FirstOrDefault();
            var unresolvedBreaks = latestRun is null
                ? 0
                : (await queryService.GetReconciliationResultsAsync(latestRun.ReconciliationRunId, context.RequestAborted).ConfigureAwait(false))
                    .Count(result => !string.Equals(result.Status, "Matched", StringComparison.OrdinalIgnoreCase));

            var payload = new ReconciliationQueueAccountStatusDto(
                AccountId: accountId,
                AccountCode: latestRun?.AccountId.ToString("D") ?? "unknown",
                QueueState: unresolvedBreaks == 0 ? "Ready" : "Blocked",
                UnresolvedBreakCount: unresolvedBreaks,
                SignOffReady: unresolvedBreaks == 0 && latestRun is not null,
                NextBestAction: unresolvedBreaks == 0 ? "Proceed with operator sign-off." : "Resolve unresolved breaks before sign-off.",
                BlockerReason: unresolvedBreaks == 0 ? "None" : "One or more unresolved reconciliation breaks remain.",
                EvidenceLinks: latestRun is null
                    ? Array.Empty<string>()
                    : [$"/api/fund-accounts/reconciliation-runs/{latestRun.ReconciliationRunId}/results"]);

            return Results.Json(payload, jsonOptions);
        })
        .WithName("GetAccountReconciliationQueueStatus")
        .Produces<ReconciliationQueueAccountStatusDto>(StatusCodes.Status200OK);

        group.MapGet("/reconciliation-runs/{runId:guid}/results", async (Guid runId, HttpContext context) =>
        {
            var queryService = ResolveQueryService(context);
            if (queryService is null)
                return ServiceUnavailable();

            var scopeGuard = await RequireScopedReconciliationRunAccessAsync(runId, queryService, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

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

    private static IResult AccountRouteMismatch() =>
        Results.Problem(
            "The accountId in the request body must match the accountId route value.",
            statusCode: StatusCodes.Status400BadRequest);

    private static IAccountManagementService? ResolveManagementService(HttpContext context) =>
        context.RequestServices.GetService<IAccountManagementService>()
        ?? context.RequestServices.GetService<IFundAccountService>() as IAccountManagementService;

    private static IAccountQueryService? ResolveQueryService(HttpContext context) =>
        context.RequestServices.GetService<IAccountQueryService>()
        ?? context.RequestServices.GetService<IFundAccountService>() as IAccountQueryService;

    private static BrokeragePortfolioSyncService? ResolveBrokerageSyncService(HttpContext context) =>
        context.RequestServices.GetService<BrokeragePortfolioSyncService>();

    private static ProviderLedgerReconciliationService? ResolveProviderLedgerReconciliationService(HttpContext context) =>
        context.RequestServices.GetService<ProviderLedgerReconciliationService>();

    private static FundAccountCloseReadinessService? ResolveFundAccountCloseReadinessService(HttpContext context) =>
        context.RequestServices.GetService<FundAccountCloseReadinessService>();

    private static Guid? TryParseGuidFilter(string? raw)
    {
        return Guid.TryParse(raw, out var parsed) ? parsed : null;
    }

    private static ValueTask<object?> RequireFundAccountAccess(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!EndpointAuthorization.TryGetPermissions(context.HttpContext, out _))
        {
            return ValueTask.FromResult<object?>(Results.Unauthorized());
        }

        if (EndpointAuthorization.HasAnyPermission(
                context.HttpContext,
                UserPermission.AdminMaintenance,
                UserPermission.ManageDirectLending))
        {
            return next(context);
        }

        return ValueTask.FromResult<object?>(EndpointHelpers.Forbidden());
    }

    private static async Task<IResult?> RequireScopedCreateAccountAccessAsync(
        CreateAccountRequest request,
        HttpContext context)
    {
        var scopes = new List<(AccessScopeKindDto Kind, Guid Id)>();
        if (request.FundId.HasValue)
        {
            scopes.Add((AccessScopeKindDto.Fund, request.FundId.Value));
        }

        if (request.SleeveId.HasValue)
        {
            scopes.Add((AccessScopeKindDto.Sleeve, request.SleeveId.Value));
        }

        if (request.VehicleId.HasValue)
        {
            scopes.Add((AccessScopeKindDto.Vehicle, request.VehicleId.Value));
        }

        if (request.EntityId.HasValue)
        {
            scopes.Add((AccessScopeKindDto.LegalEntity, request.EntityId.Value));
        }

        if (scopes.Count == 0)
        {
            return EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance)
                ? null
                : EndpointHelpers.Forbidden();
        }

        foreach (var scope in scopes)
        {
            var guard = await RequireScopedFundAccountAccessAsync(scope.Kind, scope.Id, context).ConfigureAwait(false);
            if (guard is not null)
            {
                return guard;
            }
        }

        return null;
    }

    private static async Task<IResult?> RequireScopedAccountQueryAccessAsync(
        Guid? accountId,
        Guid? fundId,
        Guid? entityId,
        HttpContext context)
    {
        if (accountId.HasValue)
        {
            return await RequireScopedAccountAccessAsync(accountId.Value, context).ConfigureAwait(false);
        }

        if (fundId.HasValue)
        {
            return await RequireScopedFundAccessAsync(fundId.Value, context).ConfigureAwait(false);
        }

        if (entityId.HasValue)
        {
            return await RequireScopedFundAccountAccessAsync(
                AccessScopeKindDto.LegalEntity,
                entityId.Value,
                context).ConfigureAwait(false);
        }

        return EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance)
            ? null
            : EndpointHelpers.Forbidden();
    }

    private static Task<IResult?> RequireScopedFundAccessAsync(Guid fundId, HttpContext context)
        => RequireScopedFundAccountAccessAsync(AccessScopeKindDto.Fund, fundId, context);

    private static Task<IResult?> RequireScopedAccountAccessAsync(Guid accountId, HttpContext context)
        => RequireScopedFundAccountAccessAsync(AccessScopeKindDto.Account, accountId, context);

    private static async Task<IResult?> RequireScopedReconciliationRunAccessAsync(
        Guid runId,
        IAccountQueryService queryService,
        HttpContext context)
    {
        var accounts = await queryService.ListAccountsAsync(null, null, null, context.RequestAborted).ConfigureAwait(false);
        foreach (var account in accounts)
        {
            var runs = await queryService.GetReconciliationRunsAsync(account.AccountId, context.RequestAborted).ConfigureAwait(false);
            if (runs.Any(run => run.ReconciliationRunId == runId))
            {
                return await RequireScopedAccountAccessAsync(account.AccountId, context).ConfigureAwait(false);
            }
        }

        return Results.NotFound();
    }

    private static async Task<IResult?> RequireScopedFundAccountAccessAsync(
        AccessScopeKindDto scopeKind,
        Guid scopeId,
        HttpContext context)
    {
        if (!EndpointAuthorization.TryGetPermissions(context, out _))
        {
            return Results.Unauthorized();
        }

        if (EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance))
        {
            return null;
        }

        var allowed = await EndpointAuthorization.HasScopedPermissionAsync(
            context,
            UserPermission.ManageDirectLending,
            scopeKind,
            scopeId,
            context.RequestAborted).ConfigureAwait(false);

        return allowed ? null : EndpointHelpers.Forbidden();
    }

    private static IResult ServiceUnavailable() =>
        Results.Problem("Fund account service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

    private static IResult BrokerageSyncUnavailable() =>
        Results.Problem("Brokerage sync service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

    private static IResult ProviderLedgerReconciliationUnavailable() =>
        Results.Problem("Provider-ledger reconciliation service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

    private static IResult FundAccountCloseReadinessUnavailable() =>
        Results.Problem("Fund account close readiness service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

    private static bool HasBrokerageSyncAccess(HttpContext context)
        => EndpointAuthorization.HasPermission(context, UserPermission.ViewTrades);

    private static async Task<bool> CanAccessFundAccountBrokerageSyncAsync(
        Guid accountId,
        HttpContext context,
        bool requireWriteAccess = false)
    {
        if (!EndpointAuthorization.TryGetPermissions(context, out var permissions)
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

        if (!EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance))
        {
            var requiredScopedPermission = requireWriteAccess
                ? (permissions.HasFlag(UserPermission.ExecuteTrades)
                    ? UserPermission.ExecuteTrades
                    : UserPermission.ManageOrders)
                : UserPermission.ViewTrades;
            if (!await EndpointAuthorization.HasScopedPermissionAsync(
                    context,
                    requiredScopedPermission,
                    AccessScopeKindDto.Account,
                    accountId,
                    context.RequestAborted).ConfigureAwait(false))
            {
                return false;
            }
        }

        var queryService = ResolveQueryService(context);
        if (queryService is null)
        {
            return false;
        }

        var account = await queryService.GetAccountAsync(accountId, context.RequestAborted).ConfigureAwait(false);
        return account is not null;
    }

    private static async Task<WorkstationBrokerageSyncRunRequestDto?> ReadBrokerageSyncRequestAsync(
        HttpContext context,
        JsonSerializerOptions jsonOptions)
    {
        // A null Content-Length means the transport did not declare a length (for example,
        // chunked HTTP or TestServer), not that the request has no body. Only an explicit zero
        // is empty; otherwise deserialize so the operation's server-side guards see every field.
        if (context.Request.ContentLength == 0)
        {
            return null;
        }

        return await JsonSerializer.DeserializeAsync<WorkstationBrokerageSyncRunRequestDto>(
                context.Request.Body,
                jsonOptions,
                context.RequestAborted)
            .ConfigureAwait(false);
    }

    private static async Task<ProviderLedgerReconciliationRequestDto?> ReadProviderLedgerReconciliationRequestAsync(
        HttpContext context,
        JsonSerializerOptions jsonOptions)
    {
        if (context.Request.ContentLength == 0)
        {
            return null;
        }

        return await JsonSerializer.DeserializeAsync<ProviderLedgerReconciliationRequestDto>(
                context.Request.Body,
                jsonOptions,
                context.RequestAborted)
            .ConfigureAwait(false);
    }

    private static async Task<BrokerageAccountLinkRequestDto?> ReadBrokerageLinkRequestAsync(
        HttpContext context,
        JsonSerializerOptions jsonOptions)
    {
        if (context.Request.ContentLength == 0)
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
