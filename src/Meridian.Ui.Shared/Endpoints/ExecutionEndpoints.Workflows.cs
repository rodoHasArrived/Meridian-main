using System.Text.Json;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Serialization;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Shared.Streaming;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class ExecutionEndpoints
{
    private static void MapExecutionWorkflowEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapPost("/api/execution/orders/preview", async (OrderRequest request, HttpContext context) =>
        {
            if (!HasExecutionTradingPermission(context, UserPermission.ManageOrders))
                return EndpointHelpers.Forbidden();
            if (request.FundAccountId is not { } account || account == Guid.Empty)
                return Results.BadRequest("Select an execution account before previewing.");
            if (await RequireExecutionFundAccountAccessAsync(account, UserPermission.ManageOrders, context, jsonOptions).ConfigureAwait(false) is { } failure)
                return failure;
            if (TryRejectClientControlledExecutionMetadata(request, jsonOptions) is { } metadataFailure)
                return metadataFailure;
            var preview = context.RequestServices.GetService<IOrderPreviewService>();
            if (preview is null)
                return Results.StatusCode(503);
            var result = await preview.PreviewAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, ExecutionWorkflowJsonContext.Default.OrderPreviewResult);
        }).RequirePermission(UserPermission.ManageOrders).RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        var group = app.MapGroup("/api/execution/journal");
        app.MapGet("/api/execution/strategy-parity", async (Guid accountId, string runId, HttpContext context) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ViewStrategies))
                return EndpointHelpers.Forbidden();
            if (await AuthorizeWorkflowRead(accountId, context, jsonOptions).ConfigureAwait(false) is { } failure)
                return failure;
            var report = await context.RequestServices.GetRequiredService<StrategyRunParityService>().GetAsync(accountId, runId, context.RequestAborted).ConfigureAwait(false);
            return report is null ? Results.NotFound() : Results.Json(report, ExecutionWorkflowJsonContext.Default.StrategyRunParityReport);
        }).RequirePermission(UserPermission.ViewStrategies);
        group.MapGet("/orders", async (Guid accountId, string? symbol, string? strategy, string? tag,
            DateTimeOffset? from, DateTimeOffset? to, string? status, int? take, HttpContext context) =>
        {
            if (await AuthorizeWorkflowRead(accountId, context, jsonOptions).ConfigureAwait(false) is { } failure)
                return failure;
            var journal = context.RequestServices.GetRequiredService<ExecutionJournal>();
            var orders = await journal.OrdersAsync(accountId, symbol, strategy, tag, from, to, Math.Clamp(take ?? 100, 1, 500), context.RequestAborted,
                scope: WorkflowScope(context), status: status).ConfigureAwait(false);
            return Results.Json(orders, ExecutionWorkflowJsonContext.Default.IReadOnlyListJournalOrder);
        }).RequirePermission(UserPermission.ViewTrades);
        group.MapGet("/orders/{orderId}", async (string orderId, Guid accountId, HttpContext context) =>
        {
            if (await AuthorizeWorkflowRead(accountId, context, jsonOptions).ConfigureAwait(false) is { } failure)
                return failure;
            var journal = context.RequestServices.GetRequiredService<ExecutionJournal>();
            var orders = await journal.OrdersAsync(accountId, take: 1, ct: context.RequestAborted,
                scope: WorkflowScope(context), orderId: orderId).ConfigureAwait(false);
            var order = orders.FirstOrDefault(entry => entry.OrderId == orderId);
            return order is null ? Results.NotFound() : Results.Json(order, ExecutionWorkflowJsonContext.Default.JournalOrder);
        }).RequirePermission(UserPermission.ViewTrades);
        group.MapGet("/summary", async (Guid accountId, DateTimeOffset? from, DateTimeOffset? to, HttpContext context) =>
        {
            if (await AuthorizeWorkflowRead(accountId, context, jsonOptions).ConfigureAwait(false) is { } failure)
                return failure;
            var summary = await context.RequestServices.GetRequiredService<ExecutionJournal>().SummaryAsync(accountId, context.RequestAborted,
                scope: WorkflowScope(context), from: from, to: to).ConfigureAwait(false);
            return Results.Json(summary, ExecutionWorkflowJsonContext.Default.JournalSummary);
        }).RequirePermission(UserPermission.ViewTrades);

        app.MapGet("/api/execution/notifications", async (Guid accountId, HttpContext context) =>
        {
            if (await AuthorizeWorkflowRead(accountId, context, jsonOptions).ConfigureAwait(false) is { } failure)
                return failure;
            var scope = WorkflowScope(context);
            var notifications = await context.RequestServices.GetRequiredService<ExecutionAlertService>()
                .NotificationsAsync(accountId, scope, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(notifications, ExecutionWorkflowJsonContext.Default.IReadOnlyListExecutionNotification);
        }).RequirePermission(UserPermission.ViewTrades);

        app.MapGet("/api/execution/notifications/stream", async (Guid accountId, HttpContext context) =>
        {
            if (await AuthorizeWorkflowRead(accountId, context, jsonOptions).ConfigureAwait(false) is { } failure)
            { await failure.ExecuteAsync(context).ConfigureAwait(false); return; }
            var worker = context.RequestServices.GetRequiredService<ExecutionWorkflowWorker>();
            var alerts = context.RequestServices.GetRequiredService<ExecutionAlertService>();
            var initialScope = WorkflowScope(context);
            var registry = context.RequestServices.GetRequiredService<StreamConnectionRegistry>();
            var sessionId = StreamEndpointHelpers.ResolveStreamSessionId(context);
            if (!registry.TryReserve(sessionId))
            { context.Response.StatusCode = 429; return; }
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            try
            {
                do
                {
                    if (await AuthorizeWorkflowRead(accountId, context, jsonOptions).ConfigureAwait(false) is not null)
                        break;
                    if (worker.Scope is not { } scope || scope != initialScope)
                        break;
                    var notifications = await alerts.NotificationsAsync(accountId, scope, context.RequestAborted).ConfigureAwait(false);
                    var data = JsonSerializer.Serialize(notifications, ExecutionWorkflowJsonContext.Default.IReadOnlyListExecutionNotification);
                    await context.Response.WriteAsync($"event: execution-notifications\ndata: {data}\n\n", context.RequestAborted).ConfigureAwait(false);
                    await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
                } while (await timer.WaitForNextTickAsync(context.RequestAborted).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            finally { registry.Release(sessionId); }
        }).RequirePermission(UserPermission.ViewTrades);

        app.MapGet("/api/execution/alert-settings", async (Guid accountId, HttpContext context) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ModifyConfig))
                return EndpointHelpers.Forbidden();
            if (await AuthorizeWorkflowRead(accountId, context, jsonOptions).ConfigureAwait(false) is { } failure)
                return failure;
            var scope = WorkflowScope(context);
            var settings = await context.RequestServices.GetRequiredService<ExecutionAlertService>().SettingsAsync(accountId, scope, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(settings, ExecutionWorkflowJsonContext.Default.ExecutionAlertSettings);
        }).RequirePermission(UserPermission.ModifyConfig);
        app.MapPost("/api/execution/alert-settings", async (ExecutionAlertSettings settings, HttpContext context) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ModifyConfig))
                return EndpointHelpers.Forbidden();
            if (!HasExecutionTradingPermission(context, UserPermission.ManageOrders))
                return EndpointHelpers.Forbidden();
            if (await RequireExecutionFundAccountAccessAsync(settings.AccountId, UserPermission.ManageOrders, context, jsonOptions).ConfigureAwait(false) is { } failure)
                return failure;
            var scope = context.RequestServices.GetRequiredService<ExecutionWorkflowWorker>().Scope;
            if (scope is null)
                return Results.StatusCode(503);
            try
            { await context.RequestServices.GetRequiredService<ExecutionAlertService>().SaveSettingsAsync(settings, scope, context.RequestAborted).ConfigureAwait(false); }
            catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
            return Results.NoContent();
        }).RequirePermission(UserPermission.ModifyConfig).RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private const string WorkflowScopeKey = "Meridian.ExecutionWorkflowScope";
    private static string WorkflowScope(HttpContext context) => (string)context.Items[WorkflowScopeKey]!;

    private static async Task<IResult?> AuthorizeWorkflowRead(Guid accountId, HttpContext context, JsonSerializerOptions options)
    {
        if (!HasExecutionTradingPermission(context, UserPermission.ViewTrades))
            return EndpointHelpers.Forbidden();
        var failure = await RequireExecutionFundAccountAccessAsync(accountId, UserPermission.ViewTrades, context, options).ConfigureAwait(false);
        if (failure is not null)
            return failure;
        var scope = context.RequestServices.GetRequiredService<ExecutionWorkflowWorker>().Scope;
        if (scope is null)
            return Results.StatusCode(503);
        context.Items[WorkflowScopeKey] = scope;
        return null;
    }
}
