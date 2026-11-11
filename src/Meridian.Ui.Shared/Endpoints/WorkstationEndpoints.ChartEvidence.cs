using System.Text.Json;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.PortfolioRecords.Accounts;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapWorkstationChartEvidenceEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationProviderVerificationHistory), async (string connectionId, int? limit, HttpContext context) =>
        {
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (!tenant.HasTenantScope || !EndpointAuthorization.HasPermission(context, UserPermission.ManageCredentials))
                return EndpointHelpers.Forbidden();
            if (limit is < 1 or > 500) return Results.BadRequest(new { error = "History limit must be between 1 and 500." });
            var service = context.RequestServices.GetService<ProviderConnectionLifecycleService>();
            if (service is null) return Results.Problem("Provider history is not configured.", statusCode: 503);
            try
            {
                var history = await service.GetVerificationHistoryForTenantAsync(connectionId, tenant.TenantId!, limit ?? 100, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(history, jsonOptions);
            }
            catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
            catch (ArgumentException) { return Results.NotFound(); }
            catch (Exception error) when (error is IOException or JsonException or NotSupportedException)
            {
                return Results.Problem("Retained provider verification history is unavailable. No historical readiness is inferred.", statusCode: 503);
            }
        }).WithName("GetWorkstationProviderVerificationHistory")
            .RequirePermission(UserPermission.ManageCredentials)
            .Produces<ProviderVerificationHistoryDto>(200).Produces(403).Produces(503);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationAccountBalanceHistory), async (Guid accountId, HttpContext context) =>
        {
            // Use the existing unconditional account read guard; the trading-shell guard only
            // applies while live brokerage sync is active and is insufficient for retained history.
            if (!await FundAccountEndpoints.CanAccessFundAccountBrokerageSyncAsync(accountId, context).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();
            var query = context.RequestServices.GetService<IAccountQueryService>()
                ?? context.RequestServices.GetService<IFundAccountService>() as IAccountQueryService;
            if (query is null) return Results.Problem("Account history is not configured.", statusCode: 503);
            var snapshots = await query.GetBalanceTimelineAsync(accountId, ct: context.RequestAborted).ConfigureAwait(false);
            if (snapshots.Any(snapshot => snapshot.AccountId != accountId))
                return Results.Problem("Account history ownership could not be established.", statusCode: 503);
            var ordered = snapshots.OrderBy(snapshot => snapshot.AsOfDate).ThenBy(snapshot => snapshot.RecordedAt).ToArray();
            var points = ordered.TakeLast(500).Select(snapshot => new AccountBalanceHistoryPointDto(
                snapshot.SnapshotId, snapshot.AccountId, snapshot.AsOfDate, snapshot.RecordedAt, snapshot.Currency,
                snapshot.Source, snapshot.CashBalance.ToString(System.Globalization.CultureInfo.InvariantCulture),
                snapshot.SecuritiesMarketValue?.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
            return Results.Json(new AccountBalanceHistoryDto(accountId, DateTimeOffset.UtcNow, points, ordered.Length > 500), jsonOptions);
        }).WithName("GetWorkstationAccountBalanceHistory").RequirePermission(UserPermission.ViewTrades)
            .Produces<AccountBalanceHistoryDto>(200).Produces(403).Produces(503);
    }
}
