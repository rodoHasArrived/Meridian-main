using System.Text.Json;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>Flat projection of a parked risk escalation for API and workstation consumption.</summary>
public sealed record RiskEscalationDto(
    string EscalationId,
    string Symbol,
    string Side,
    string Type,
    decimal Quantity,
    decimal? LimitPrice,
    string Reason,
    string? RuleName,
    string Status,
    DateTimeOffset ParkedAt,
    string? ResolvedBy,
    string? ResolutionReason,
    DateTimeOffset? ResolvedAt,
    // Everything else that determines what the approver is actually authorizing. Without
    // these, a stop, trailing, option, multi-leg, or dollar-sized order is reviewed from a
    // symbol and a placeholder quantity — a native $500k order can look like quantity 1.
    decimal? StopPrice = null,
    decimal? TrailPrice = null,
    decimal? TrailPercent = null,
    string? TimeInForce = null,
    decimal? RoutedNotional = null,
    Guid? FundAccountId = null,
    string? StrategyId = null,
    string? PositionIntent = null,
    OptionContractIdentity? OptionContract = null,
    IReadOnlyList<OrderLeg>? Legs = null);

/// <summary>Operator resolution request for a parked escalation.</summary>
/// <param name="Reason">Operator-supplied rationale, audited with the decision.</param>
/// <param name="Release">Approve only: when true (default) the approved order is immediately resubmitted through the full pre-trade gate.</param>
public sealed record RiskEscalationResolutionRequest(string? Reason = null, bool Release = true);

/// <summary>Approve response: the resolved escalation plus the release order result when resubmitted.</summary>
public sealed record RiskEscalationApprovalResponse(RiskEscalationDto Escalation, OrderResult? ReleaseResult);

/// <summary>
/// Exposes runtime risk rule status, operator-managed rule configuration, and the
/// governed-approval queue for escalated orders.
/// </summary>
public static class RiskEndpoints
{
    public static void MapRiskEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        MapRiskRoutes(app.MapGroup("/api/risk").WithTags("Risk"), jsonOptions);
        // Versioning scaffold for future contract evolution.
        MapRiskRoutes(app.MapGroup("/api/v1/risk").WithTags("Risk"), jsonOptions);
    }

    private static void MapRiskRoutes(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet("/rules", async (HttpContext context) =>
        {
            var runtime = context.RequestServices.GetService<RiskRuleRuntimeService>();
            if (runtime is null)
            {
                return Results.Problem("Risk runtime service is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var statuses = await runtime.GetAllStatusesAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(statuses, jsonOptions);
        })
        .Produces<IReadOnlyList<RiskRuleStatusDto>>(200)
        .Produces(503);

        group.MapGet("/rules/{ruleName}/status", async (string ruleName, HttpContext context) =>
        {
            var runtime = context.RequestServices.GetService<RiskRuleRuntimeService>();
            if (runtime is null)
            {
                return Results.Problem("Risk runtime service is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var status = await runtime.GetStatusAsync(ruleName, context.RequestAborted).ConfigureAwait(false);
            return status is null ? Results.NotFound() : Results.Json(status, jsonOptions);
        })
        .Produces<RiskRuleStatusDto>(200)
        .Produces(404)
        .Produces(503);

        group.MapGet("/rules/{ruleName}/config", (string ruleName, HttpContext context) =>
        {
            var runtime = context.RequestServices.GetService<RiskRuleRuntimeService>();
            if (runtime is null)
            {
                return Results.Problem("Risk runtime service is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var config = runtime.GetConfig(ruleName);
            return config is null ? Results.NotFound() : Results.Json(config, jsonOptions);
        })
        .Produces<RiskRuleConfigDto>(200)
        .Produces(404)
        .Produces(503);

        group.MapPut("/rules/{ruleName}/config", async (
            string ruleName,
            RiskRuleConfigUpdateRequest request,
            HttpContext context) =>
        {
            if (!HasRiskConfigPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var runtime = context.RequestServices.GetService<RiskRuleRuntimeService>();
            if (runtime is null)
            {
                return Results.Problem("Risk runtime service is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            try
            {
                var updated = await runtime
                    .UpdateConfigAsync(ruleName, request, ResolveActor(context), context.RequestAborted)
                    .ConfigureAwait(false);
                return updated is null ? Results.NotFound() : Results.Json(updated, jsonOptions);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .Produces<RiskRuleConfigDto>(200)
        .Produces(400)
        .Produces(404)
        .Produces(503);

        group.MapGet("/escalations", async (HttpContext context) =>
        {
            // The queue reveals retained order details (symbol, size, price), so listing
            // requires the same order-management permission as acting on an entry.
            if (!HasRiskConfigPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var queue = context.RequestServices.GetService<RiskEscalationQueueService>();
            if (queue is null)
            {
                return Results.Problem("Risk escalation queue is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var entries = queue.GetRecent();
            var isAdmin = EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance);
            var visible = new List<RiskEscalationDto>(entries.Count);
            // Fund-scoped entries are visible only within the caller's scoped account
            // authority; one scope check per distinct account rather than per entry.
            Dictionary<Guid, bool>? scopeCache = null;
            foreach (var entry in entries)
            {
                if (!isAdmin && entry.Request.FundAccountId is { } fundAccountId)
                {
                    scopeCache ??= [];
                    if (!scopeCache.TryGetValue(fundAccountId, out var authorized))
                    {
                        authorized = await EndpointAuthorization.HasScopedPermissionAsync(
                            context,
                            UserPermission.ManageOrders,
                            AccessScopeKindDto.Account,
                            fundAccountId,
                            context.RequestAborted).ConfigureAwait(false);
                        scopeCache[fundAccountId] = authorized;
                    }

                    if (!authorized)
                    {
                        continue;
                    }
                }

                visible.Add(ToDto(entry));
            }

            return Results.Json(visible, jsonOptions);
        })
        .Produces<IReadOnlyList<RiskEscalationDto>>(200)
        .Produces(403)
        .Produces(503);

        group.MapPost("/escalations/{escalationId}/approve", async (
            string escalationId,
            RiskEscalationResolutionRequest? request,
            HttpContext context) =>
        {
            if (!HasRiskConfigPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var queue = context.RequestServices.GetService<RiskEscalationQueueService>();
            if (queue is null)
            {
                return Results.Problem("Risk escalation queue is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var entry = queue.TryGet(escalationId);
            if (entry is null)
            {
                return Results.NotFound();
            }

            var actor = ResolveActor(context);

            // Segregation of duties: the operator who submitted the escalated order can
            // never approve their own risk exception.
            if (!string.IsNullOrWhiteSpace(entry.Actor) &&
                string.Equals(entry.Actor.Trim(), actor, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(
                    new { error = "The submitting operator cannot approve their own escalation; a distinct approver is required." },
                    jsonOptions,
                    statusCode: StatusCodes.Status403Forbidden);
            }

            // The release bypasses /orders/submit, so re-check the approver's scoped
            // fund-account authority against the retained order before transitioning.
            if (entry.Request.FundAccountId is { } fundAccountId &&
                !EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance) &&
                !await EndpointAuthorization.HasScopedPermissionAsync(
                    context,
                    UserPermission.ManageOrders,
                    AccessScopeKindDto.Account,
                    fundAccountId,
                    context.RequestAborted).ConfigureAwait(false))
            {
                return EndpointHelpers.Forbidden();
            }

            // Approve when still pending; an entry already Approved (e.g. an earlier
            // release attempt failed downstream) stays retryable through this endpoint.
            var approved = entry.Status switch
            {
                RiskEscalationStatus.PendingApproval => queue.Approve(escalationId, actor, request?.Reason),
                RiskEscalationStatus.Approved => entry,
                _ => null
            };
            if (approved is null)
            {
                return Results.Conflict(new { error = $"Escalation is already {entry.Status}." });
            }

            // Release the parked order back through the full pre-trade gate. The approval
            // token satisfies exactly this escalation once; every other rule still enforces.
            OrderResult? releaseResult = null;
            if (request?.Release != false &&
                context.RequestServices.GetService<IOrderManager>() is { } oms)
            {
                // Carry every approval this order has already been granted, not just the
                // newest: an order breaching several escalation-capable rules needs all of
                // its tokens present at once, or each release satisfies one rule while
                // re-parking another and the order can never route.
                var carriedTokens = new List<string>();
                if (approved.Request.Metadata is not null &&
                    approved.Request.Metadata.TryGetValue(RiskEscalationQueueService.ApprovalMetadataKey, out var existingTokens))
                {
                    carriedTokens.AddRange(RiskEscalationQueueService.SplitTokens(existingTokens));
                }

                carriedTokens.Add(approved.EscalationId);

                var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [RiskEscalationQueueService.ApprovalMetadataKey] = RiskEscalationQueueService.JoinTokens(carriedTokens),
                    ["actor"] = actor,
                    ["correlationId"] = $"risk-escalation-{approved.EscalationId}"
                };
                if (approved.Request.Metadata is not null)
                {
                    foreach (var (key, value) in approved.Request.Metadata)
                    {
                        metadata.TryAdd(key, value);
                    }
                }

                var releaseRequest = approved.Request with { Metadata = metadata };
                releaseResult = await oms.PlaceOrderAsync(releaseRequest, context.RequestAborted).ConfigureAwait(false);
            }

            var latest = queue.TryGet(escalationId) ?? approved;
            return Results.Json(
                new RiskEscalationApprovalResponse(ToDto(latest), releaseResult),
                jsonOptions);
        })
        .Produces<RiskEscalationApprovalResponse>(200)
        .Produces(403)
        .Produces(404)
        .Produces(409)
        .Produces(503);

        group.MapPost("/escalations/{escalationId}/deny", async (
            string escalationId,
            RiskEscalationResolutionRequest? request,
            HttpContext context) =>
        {
            if (!HasRiskConfigPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var queue = context.RequestServices.GetService<RiskEscalationQueueService>();
            if (queue is null)
            {
                return Results.Problem("Risk escalation queue is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            // Denial transitions the retained order too: the same scoped fund-account
            // authority applies as for approval.
            if (queue.TryGet(escalationId) is { } entry &&
                entry.Request.FundAccountId is { } fundAccountId &&
                !EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance) &&
                !await EndpointAuthorization.HasScopedPermissionAsync(
                    context,
                    UserPermission.ManageOrders,
                    AccessScopeKindDto.Account,
                    fundAccountId,
                    context.RequestAborted).ConfigureAwait(false))
            {
                return EndpointHelpers.Forbidden();
            }

            try
            {
                var denied = queue.Deny(escalationId, ResolveActor(context), request?.Reason);
                return denied is null ? Results.NotFound() : Results.Json(ToDto(denied), jsonOptions);
            }
            catch (InvalidOperationException exception)
            {
                // A denial that cannot be durably committed is refused rather than left
                // resurrectable; the entry remains pending for a retry.
                return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .Produces<RiskEscalationDto>(200)
        .Produces(403)
        .Produces(404)
        .Produces(503);
    }

    private static RiskEscalationDto ToDto(RiskEscalationEntry entry) => new(
        EscalationId: entry.EscalationId,
        Symbol: entry.Request.Symbol,
        Side: entry.Request.Side.ToString(),
        Type: entry.Request.Type.ToString(),
        Quantity: entry.Request.Quantity,
        LimitPrice: entry.Request.LimitPrice,
        Reason: entry.Reason,
        RuleName: entry.RuleName,
        Status: entry.Status.ToString(),
        ParkedAt: entry.ParkedAt,
        ResolvedBy: entry.ResolvedBy,
        ResolutionReason: entry.ResolutionReason,
        ResolvedAt: entry.ResolvedAt,
        StopPrice: entry.Request.StopPrice,
        TrailPrice: entry.Request.TrailPrice,
        TrailPercent: entry.Request.TrailPercent,
        TimeInForce: entry.Request.TimeInForce.ToString(),
        RoutedNotional: BrokerNotionalMetadata.TryRead(entry.Request.Metadata, entry.Request.Quantity),
        FundAccountId: entry.Request.FundAccountId,
        StrategyId: entry.Request.StrategyId,
        PositionIntent: entry.Request.PositionIntent?.ToString(),
        OptionContract: entry.Request.OptionContract,
        Legs: entry.Request.Legs);

    private static string ResolveActor(HttpContext context)
    {
        if (context.Items[LoginSessionMiddleware.CurrentUserKey] is string userName &&
            !string.IsNullOrWhiteSpace(userName))
        {
            return userName.Trim();
        }

        if (context.User.Identity?.IsAuthenticated == true &&
            !string.IsNullOrWhiteSpace(context.User.Identity.Name))
        {
            return context.User.Identity.Name!;
        }

        return "operator";
    }

    private static bool HasRiskConfigPermission(HttpContext context)
    {
        if (!context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserPermissionsKey, out var value))
        {
            return false;
        }

        var permissions = value is UserPermission userPermission
            ? userPermission
            : UserPermission.None;

        return permissions.HasFlag(UserPermission.ManageOrders);
    }
}
