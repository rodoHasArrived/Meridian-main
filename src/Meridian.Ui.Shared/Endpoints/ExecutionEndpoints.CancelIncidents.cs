using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Tenancy;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.Accounts;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class ExecutionEndpoints
{
    private const string CancelIncidentCoverage = "Retained observations for the selected account only. Unattributed broker orders and other accounts are excluded. These observations do not establish current exposure; incomplete broker coverage remains unresolved.";

    private static void MapExecutionCancelIncidentEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet("/cancel-incidents", async (Guid? fundAccountId, int? take, HttpContext context) =>
        {
            if (fundAccountId is null || fundAccountId == Guid.Empty)
                return Results.BadRequest(new { error = "Select an execution fund account to read cancel-all evidence." });
            if (!await CanReadCancelIncidentAccountAsync(fundAccountId.Value, context).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();
            var audit = context.RequestServices.GetService<ExecutionAuditTrailService>();
            if (audit is null)
                return Results.Problem("Retained cancel-all evidence is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
            var limit = Math.Clamp(take ?? 50, 1, 100);
            var incidents = await audit.GetCancelAllIncidentsAsync(fundAccountId.Value, limit + 1, context.RequestAborted).ConfigureAwait(false);
            var summaries = incidents.Take(limit).Select(CancelAllIncidentSummaryDto.From).ToArray();
            return Results.Json(new CancelAllIncidentHistoryDto(DateTimeOffset.UtcNow, summaries.Length, limit,
                incidents.Count > limit, CancelIncidentCoverage, summaries), jsonOptions);
        })
        .WithName("GetExecutionCancelIncidents").RequirePermission(UserPermission.ViewTrades)
        .Produces<CancelAllIncidentHistoryDto>().Produces(400).Produces(403).Produces(503);

        group.MapGet("/cancel-incidents/{incidentId}", async (string incidentId, Guid? fundAccountId, HttpContext context) =>
        {
            if (fundAccountId is null || fundAccountId == Guid.Empty)
                return Results.BadRequest(new { error = "Select an execution fund account to read cancel-all evidence." });
            if (!await CanReadCancelIncidentAccountAsync(fundAccountId.Value, context).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();
            var audit = context.RequestServices.GetService<ExecutionAuditTrailService>();
            if (audit is null)
                return Results.Problem("Retained cancel-all evidence is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
            var incident = await audit.GetCancelAllIncidentAsync(incidentId, fundAccountId.Value, context.RequestAborted).ConfigureAwait(false);
            return incident is null ? Results.NotFound() : Results.Json(CancelAllIncidentDto.From(incident), jsonOptions);
        })
        .WithName("GetExecutionCancelIncident").RequirePermission(UserPermission.ViewTrades)
        .Produces<CancelAllIncidentDto>().Produces(400).Produces(403).Produces(404).Produces(503);
    }

    private static async Task<bool> CanReadCancelIncidentAccountAsync(Guid accountId, HttpContext context)
    {
        if (!await AuditTrailHttpAuthority.CanReadExecutionFeedAsync(context).ConfigureAwait(false)
            || !await FundAccountEndpoints.CanAccessFundAccountBrokerageSyncAsync(accountId, context).ConfigureAwait(false))
            return false;
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        var tenancy = context.RequestServices.GetService<IFundProfileTenancyRegistry>();
        if (!tenant.HasTenantScope || string.IsNullOrWhiteSpace(tenant.CompanyId) || tenancy is null)
            return false;
        var accounts = context.RequestServices.GetService<IAccountQueryService>()
            ?? context.RequestServices.GetService<IFundAccountService>() as IAccountQueryService;
        var account = accounts is null ? null : await accounts.GetAccountAsync(accountId, context.RequestAborted).ConfigureAwait(false);
        if (account?.AccountId != accountId || account.FundId is not { } fundId || fundId == Guid.Empty)
            return false;
        var owner = await tenancy.ResolveAsync(fundId.ToString("D"), context.RequestAborted).ConfigureAwait(false);
        return owner?.FundProfileId == fundId.ToString("D") && owner.TenantId == tenant.TenantId && owner.CompanyId == tenant.CompanyId;
    }
}

/// <summary>A bounded account projection; complete evidence and successful cancellation are separate states.</summary>
public sealed record CancelAllIncidentHistoryDto(DateTimeOffset AsOf, int Returned, int Limit, bool HasMore,
    string CoverageNote, IReadOnlyList<CancelAllIncidentSummaryDto> Incidents);

public sealed record CancelAllIncidentSummaryDto(string IncidentId, Guid? FundAccountId, DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt, string? Actor, string GatewayId, string ExecutionMode, string EvidenceStatus,
    string? EvidenceError, bool BrokerViewUnavailable, int OrderCount, int UnresolvedOrderCount)
{
    internal static CancelAllIncidentSummaryDto From(CancelAllIncident incident) => new(
        incident.IncidentId, incident.FundAccountId, incident.StartedAt, incident.CompletedAt, incident.Actor,
        incident.GatewayId, incident.ExecutionMode.ToString(), incident.EvidenceStatus,
        incident.EvidenceError is null ? null : "Some incident evidence could not be retained or its completion was not established. Reconcile the account before resuming execution.",
        incident.BrokerViewUnavailable, incident.Orders.Count,
        incident.Orders.Count(order => order.RequiresReconciliation || order.HasUnconfirmedReplacement
            || order.Disposition is CancelAllOrderDisposition.Unknown or CancelAllOrderDisposition.StillWorking));
}

public sealed record CancelAllIncidentDto(CancelAllIncidentSummaryDto Summary, string? CorrelationId,
    string? BrokerScopeIdentity, string? BrokerViewError, IReadOnlyList<CancelAllOrderOutcomeDto> Orders)
{
    internal static CancelAllIncidentDto From(CancelAllIncident incident) => new(CancelAllIncidentSummaryDto.From(incident),
        incident.CorrelationId, incident.BrokerScopeIdentity,
        incident.BrokerViewUnavailable ? "The complete broker open-order book was not established. Reconcile broker state before resuming execution." : null,
        incident.Orders.Select(CancelAllOrderOutcomeDto.From).ToArray());
}

/// <summary>Financial quantities are exact decimal text; unknown values remain null.</summary>
public sealed record CancelAllOrderOutcomeDto(string ObservationKey, string OrderId, string? BrokerOrderId,
    Guid? FundAccountId, string? BrokerAccountId, string? Symbol, string? Side, string Operation,
    string Disposition, string? InitialStatus, string? Status, string? InitialQuantity,
    string? InitialFilledQuantity, string? Quantity, string? FilledQuantity, string? RemainingQuantity,
    bool CancellationAttempted, bool RequiresReconciliation, bool HasUnconfirmedReplacement,
    string? CandidateBrokerOrderId, string? CandidateClientOrderId, string? ResidualNotional,
    string? Currency, string ValuationCoverage, string? ValuationPrice, DateTimeOffset? ValuationObservedAt,
    string? ValuationSource, DateTimeOffset ObservedAt, string? Detail)
{
    internal static CancelAllOrderOutcomeDto From(CancelAllOrderOutcome order) => new(order.ObservationKey,
        order.OrderId, order.BrokerOrderId, order.FundAccountId, order.BrokerAccountId, order.Symbol,
        order.Side?.ToString(), order.Operation, order.Disposition.ToString(), order.InitialStatus?.ToString(),
        order.Status?.ToString(), Exact(order.InitialQuantity), Exact(order.InitialFilledQuantity),
        Exact(order.Quantity), Exact(order.FilledQuantity), Exact(order.RemainingQuantity), order.CancellationAttempted,
        order.RequiresReconciliation, order.HasUnconfirmedReplacement, order.CandidateBrokerOrderId,
        order.CandidateClientOrderId, Exact(order.ResidualNotional), order.Currency, order.ValuationCoverage,
        Exact(order.ValuationReference?.Price), order.ValuationReference?.ObservedAt, order.ValuationReference?.Source,
        order.ObservedAt, order.Detail);

    private static string? Exact(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);
}
