using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapOperatorTriageEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet("/operator/triage", async (Guid? fundAccountId, HttpContext context) =>
        {
            if (!CanReadOperatorTriage(context) || !TryGetTriageContext(context, out var scope, out var actor))
                return EndpointHelpers.Forbidden();
            if (!await CanReadTradingAccountAsync(fundAccountId, context).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();
            var inbox = await BuildOperatorInboxAsync(fundAccountId, context).ConfigureAwait(false);
            var store = context.RequestServices.GetService<IOperatorTriageStore>();
            var available = store is not null;
            var unavailable = available ? null : "Durable operator triage is not configured.";
            var items = new List<OperatorTriageItemDto>();
            try
            {
                foreach (var work in inbox.Items)
                {
                    var source = await GetTriageReconciliationSourceAsync(work, context).ConfigureAwait(false);
                    var state = store is null ? OperatorTriageState.Empty
                        : await ObserveTriageSeverityAsync(store, scope, work, context.RequestAborted).ConfigureAwait(false);
                    items.Add(BuildTriageItem(work, source, state, actor, context, available));
                }
            }
            catch (Exception exception) when (IsTriagePersistenceFailure(exception))
            {
                // Do not publish empty personal state as a successful persistence read.
                available = false;
                unavailable = "Retained triage metadata is unavailable. The source queue remains actionable in its workspace.";
                items = inbox.Items.Select(work => BuildTriageItem(work, null, OperatorTriageState.Empty, actor, context, false)).ToList();
            }
            return Results.Json(new OperatorInboxTriageDto(inbox.AsOf, actor, items, available, unavailable), jsonOptions);
        }).WithName("GetOperatorInboxTriage").RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope();

        group.MapPost("/operator/triage/{workItemId}/actions", async (
            string workItemId, OperatorTriageCommand request, HttpContext context) =>
        {
            var result = await ApplyOperatorTriageAsync(workItemId, request, context).ConfigureAwait(false);
            return Results.Json(result, jsonOptions, statusCode: result.Status switch
            {
                "Applied" => 200, "Conflict" => 409, "NotFound" => 404,
                "Forbidden" => 403, "Unavailable" => 503, _ => 400
            });
        }).WithName("ApplyOperatorInboxTriage").RequireAuthenticatedSession()
            .RequireWorkstationTenantCompanyScope().RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost("/operator/triage/bulk", async (OperatorTriageBulkCommand request, HttpContext context) =>
        {
            if (request.Items is null || request.Items.Count is < 1 or > 100
                || request.Items.Select(item => item.WorkItemId).Distinct(StringComparer.Ordinal).Count() != request.Items.Count
                || request.Action != OperatorTriageAction.Assign || string.IsNullOrWhiteSpace(request.CommandId)
                || request.CommandId.Length > 64)
                return Results.BadRequest(new { error = "Bulk assign requires 1–100 distinct items and a command identity of at most 64 characters." });
            var results = new List<OperatorTriageResult>();
            for (var index = 0; index < request.Items.Count; index++)
            {
                var item = request.Items[index];
                results.Add(await ApplyOperatorTriageAsync(item.WorkItemId,
                    new OperatorTriageCommand($"{request.CommandId}:{index}", request.Action, item.ExpectedVersion,
                        request.Reason, request.AssigneeId, ExpectedSourceVersion: item.ExpectedSourceVersion), context).ConfigureAwait(false));
            }
            return Results.Json(new OperatorTriageBulkResult(results), jsonOptions);
        }).WithName("BulkAssignOperatorInboxTriage").RequireAuthenticatedSession()
            .RequireWorkstationTenantCompanyScope().RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static bool TryGetTriageContext(HttpContext context, out OperatorTriageScope scope, out string actor)
    {
        var current = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        actor = current.Actor ?? string.Empty;
        scope = new(current.TenantId ?? string.Empty, current.CompanyId ?? string.Empty);
        return current.HasTenantScope && !string.IsNullOrWhiteSpace(current.CompanyId) && actor.Length > 0;
    }

    private static bool CanReadOperatorTriage(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(context, UserPermission.ViewTrades, UserPermission.ViewStrategies,
            UserPermission.ManageStrategies, UserPermission.ViewDirectLending, UserPermission.ManageDirectLending,
            UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance);

    private static bool CanAssignTriage(OperatorWorkItemDto work, HttpContext context)
        => work.Kind switch
        {
            OperatorWorkItemKindDto.ReconciliationBreak => CanMutateReconciliationBreakQueue(context)
                && !string.IsNullOrWhiteSpace(work.AuditReference),
            OperatorWorkItemKindDto.PaperReplay or OperatorWorkItemKindDto.PromotionReview =>
                EndpointAuthorization.HasAnyPermission(context, UserPermission.ManageStrategies, UserPermission.AdminMaintenance),
            OperatorWorkItemKindDto.LedgerPeriodClose =>
                EndpointAuthorization.HasAnyPermission(context, UserPermission.ManageDirectLending, UserPermission.AdminMaintenance),
            OperatorWorkItemKindDto.SecurityMasterCoverage =>
                EndpointAuthorization.HasAnyPermission(context, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance),
            OperatorWorkItemKindDto.ReportPackApproval =>
                EndpointAuthorization.HasAnyPermission(context, UserPermission.ManageReporting, UserPermission.AdminMaintenance),
            OperatorWorkItemKindDto.BrokerageSync or OperatorWorkItemKindDto.ProviderTrustGate
                or OperatorWorkItemKindDto.ExecutionControl or OperatorWorkItemKindDto.BrokerExecutionReconciliation =>
                EndpointAuthorization.HasAnyPermission(context, UserPermission.ManageOrders, UserPermission.AdminMaintenance),
            _ => false
        };

    private static async Task<ReconciliationBreakQueueItem?> GetTriageReconciliationSourceAsync(OperatorWorkItemDto work, HttpContext context)
    {
        if (work.Kind != OperatorWorkItemKindDto.ReconciliationBreak || string.IsNullOrWhiteSpace(work.AuditReference)
            || !TryResolveReconciliationBreakQueueScope(context, out var scope)) return null;
        var repository = context.RequestServices.GetService<IReconciliationBreakQueueRepository>();
        return repository is null ? null
            : await repository.GetByIdAsync(scope, work.AuditReference, context.RequestAborted).ConfigureAwait(false);
    }

    private static OperatorTriageItemDto BuildTriageItem(OperatorWorkItemDto work, ReconciliationBreakQueueItem? source,
        OperatorTriageState state, string actor, HttpContext context, bool available)
    {
        var personal = state.Personal.FirstOrDefault(item => item.ActorId == actor);
        var snoozed = OperatorTriagePolicy.IsSnoozed(personal, work.Tone, DateTimeOffset.UtcNow);
        var canAssign = available && CanAssignTriage(work, context)
            && (work.Kind != OperatorWorkItemKindDto.ReconciliationBreak || source is not null);
        return new OperatorTriageItemDto(work.WorkItemId, work.Label, work.Detail, work.Workspace, work.TargetRoute,
            work.Tone, work.CreatedAt, source?.AssignedTo ?? state.AssigneeId, source?.SlaDueAt, state.Version, source?.Version,
            personal?.AcknowledgedAt, snoozed ? personal?.SnoozedUntil : null,
            new(canAssign, available, available && work.Tone != OperatorWorkItemToneDto.Critical,
                available && snoozed, available ? null : "Durable triage metadata is unavailable."),
            state.Activity.Where(activity => activity.Action == OperatorTriageAction.Assign || activity.ActorId == actor
                || activity.SubjectActorId == actor).ToArray(),
            work.FundAccountId ?? (Guid.TryParse(source?.FundAccountId, out var sourceAccountId) ? sourceAccountId : null),
            work.RunId, work.Scope, work.Kind, work.TargetPageTag);
    }

    private static async Task<OperatorTriageResult> ApplyOperatorTriageAsync(string workItemId, OperatorTriageCommand request,
        HttpContext context)
    {
        if (!CanReadOperatorTriage(context) || !TryGetTriageContext(context, out var scope, out var actor))
            return new("Forbidden", workItemId, Error: "An authorized operator session is required.");
        var store = context.RequestServices.GetService<IOperatorTriageStore>();
        if (store is null) return new("Unavailable", workItemId, Error: "Durable operator triage is not configured.");
        try
        {
            // Rebuild the authorized inbox on every command. A browser-supplied ID is not read authority.
            var inbox = await BuildOperatorInboxAsync(null, context).ConfigureAwait(false);
            var work = inbox.Items.FirstOrDefault(item => item.WorkItemId == workItemId);
            if (work is null) return new("NotFound", workItemId, Error: "The item is no longer in the authorized queue.");
            var source = await GetTriageReconciliationSourceAsync(work, context).ConfigureAwait(false);
            await ObserveTriageSeverityAsync(store, scope, work, context.RequestAborted).ConfigureAwait(false);
            var sourceOwned = work.Kind == OperatorWorkItemKindDto.ReconciliationBreak;
            if (request.Action == OperatorTriageAction.Assign)
            {
                if (!CanAssignTriage(work, context)) return new("Forbidden", workItemId, Error: "Assignment permission is required.");
                if (sourceOwned && source is null) return new("Unavailable", workItemId, Error: "Source assignment authority is unavailable.");
                if (!string.Equals(request.AssigneeId, actor, StringComparison.Ordinal))
                {
                    var assignee = context.RequestServices.GetService<UserProfileRegistry>()?.GetProfile(request.AssigneeId ?? string.Empty);
                    if (assignee is null || !string.Equals(assignee.CompanyId, scope.CompanyId, StringComparison.OrdinalIgnoreCase))
                        return new("Rejected", workItemId, Error: "Select an active operator in this company.");
                }
            }
            var replayed = false;
            var updated = await store.UpdateAsync(scope, workItemId, async (current, ct) =>
            {
                var next = OperatorTriagePolicy.Apply(current, actor, request, work.Tone, DateTimeOffset.UtcNow, sourceOwned, out replayed);
                if (!replayed && sourceOwned && request.Action == OperatorTriageAction.Assign)
                {
                    if (request.ExpectedSourceVersion is null)
                        throw new OperatorTriageException("Conflict", "Refresh the source case before assigning it.");
                    var handoff = context.RequestServices.GetService<IStatementReconciliationCaseworkHandoffService>()
                        ?? throw new OperatorTriageException("Unavailable", "Authoritative reconciliation assignment is unavailable.");
                    var sourceScope = new ReconciliationBreakQueueScope(scope.TenantId, scope.CompanyId);
                    var sourceCommandId = Meridian.Contracts.Integrity.Sha256Digest.ComputeUtf8(
                        JsonSerializer.Serialize(new[] { scope.TenantId, scope.CompanyId, actor, workItemId, request.CommandId }));
                    var command = new ReconciliationCaseworkCommand(source!.BreakId, ReconciliationCaseworkAction.Assign,
                        actor, $"triage:{sourceCommandId}", request.CommandId, "operator-inbox-triage", request.ExpectedSourceVersion.Value,
                        Reason: request.Reason, Assignee: request.AssigneeId,
                        ActionOrigin: EndpointAuthorization.DeriveActionOriginFromPrincipal(context));
                    var transition = await handoff.ApplyAsync(sourceScope, command, ct).ConfigureAwait(false);
                    if (transition.Status != ReconciliationBreakQueueTransitionStatus.Success)
                        throw new OperatorTriageException(transition.Status == ReconciliationBreakQueueTransitionStatus.Conflict
                            ? "Conflict" : "Rejected", transition.Error ?? "The source refused assignment.");
                }
                return next;
            }, context.RequestAborted).ConfigureAwait(false);
            source = await GetTriageReconciliationSourceAsync(work, context).ConfigureAwait(false);
            return new("Applied", workItemId, BuildTriageItem(work, source, updated, actor, context, true), Replayed: replayed);
        }
        catch (OperatorTriageException exception) { return new(exception.Status, workItemId, Error: exception.Message); }
        catch (UnauthorizedAccessException) { return new("Forbidden", workItemId, Error: "The source refused this operator's authority."); }
        catch (Exception exception) when (IsTriagePersistenceFailure(exception))
        {
            return new("Unavailable", workItemId, Error: "The retained triage outcome could not be confirmed. Refresh before retrying the same command.");
        }
    }

    private static Task<OperatorTriageState> ObserveTriageSeverityAsync(IOperatorTriageStore store, OperatorTriageScope scope,
        OperatorWorkItemDto work, CancellationToken ct)
        => store.UpdateAsync(scope, work.WorkItemId, (current, _) => Task.FromResult(
            OperatorTriagePolicy.ObserveSeverity(current, work.Tone, DateTimeOffset.UtcNow)), ct);

    private static bool IsTriagePersistenceFailure(Exception exception)
        => exception is IOException or JsonException or InvalidOperationException or System.Data.Common.DbException;
}
