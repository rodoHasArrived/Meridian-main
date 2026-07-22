using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    /// <summary>
    /// Maps the automated journal-intake routes (daily valuation, corporate-action dividends,
    /// fee-schedule accruals, and period-close closing entries) that project source evidence or
    /// closed-period trial balances into governed manual journal workbench drafts.
    /// </summary>
    private static void MapJournalAutomationEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapGet(UiApiRoutes.LedgerJournalAutomationMonthlySchedules, async (HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var store = context.RequestServices.GetService<IAutomatedJournalScheduleStore>();
            if (store is null)
            {
                return ServiceUnavailable();
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var scheduleId = context.Request.Query["scheduleId"].ToString();
            var schedules = (await store.ListAsync(context.RequestAborted).ConfigureAwait(false))
                .Where(item => string.IsNullOrWhiteSpace(scheduleId) || string.Equals(
                    item.ScheduleId,
                    scheduleId.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                .Where(item => string.Equals(
                    item.TenantId,
                    tenantContext.TenantId,
                    StringComparison.OrdinalIgnoreCase))
                .Where(item => string.Equals(
                    item.CompanyId,
                    tenantContext.CompanyId,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return Results.Json(schedules, jsonOptions);
        })
        .WithName("ListLedgerJournalAutomationMonthlySchedules")
        .Produces<IReadOnlyList<AutomatedJournalScheduleWorkItem>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapPost(UiApiRoutes.LedgerJournalAutomationMonthlySchedules, async (AutomatedJournalScheduleWorkItem request, HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var store = context.RequestServices.GetService<IAutomatedJournalScheduleStore>();
            if (store is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var existing = await store.GetAsync(request.ScheduleId, context.RequestAborted).ConfigureAwait(false);
                if (existing is not null &&
                    (!string.Equals(existing.TenantId, tenantContext.TenantId, StringComparison.OrdinalIgnoreCase) ||
                     !string.Equals(existing.CompanyId, tenantContext.CompanyId, StringComparison.OrdinalIgnoreCase)))
                {
                    return EndpointHelpers.Forbidden();
                }

                if (existing?.State == AutomatedJournalScheduleStateDto.Running)
                {
                    return Results.Conflict(new
                    {
                        error = $"Automated journal schedule '{existing.ScheduleId}' is Running and cannot be reconfigured until its durable claim completes or resumes."
                    });
                }

                if (existing is not null && request.Version != existing.Version)
                {
                    return Results.Conflict(new
                    {
                        error = $"Automated journal schedule '{existing.ScheduleId}' version is stale. Expected {request.Version}, current {existing.Version}."
                    });
                }

                if (existing is not null && existing.JournalEntryIds.Count > 0)
                {
                    var draftStore = context.RequestServices.GetService<IManualJournalEntryDraftStore>();
                    if (draftStore is null)
                    {
                        return ServiceUnavailable();
                    }

                    foreach (var journalEntryId in existing.JournalEntryIds)
                    {
                        var draft = await draftStore.GetAsync(
                            existing.FundProfileId,
                            journalEntryId,
                            context.RequestAborted,
                            existing.TenantId,
                            existing.CompanyId).ConfigureAwait(false);
                        if (draft is null || draft.Status is ManualJournalEntryStatusDto.Draft or
                            ManualJournalEntryStatusDto.NeedsFix or
                            ManualJournalEntryStatusDto.Submitted or
                            ManualJournalEntryStatusDto.Approved)
                        {
                            return Results.Conflict(new
                            {
                                error = $"Automated journal schedule '{existing.ScheduleId}' cannot be re-armed while retained draft '{journalEntryId:D}' is pending. Post or reject the current draft before rearming."
                            });
                        }
                    }
                }

                var actor = ResolveMutationActor(context, request.Actor);
                var now = (context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System)
                    .GetUtcNow()
                    .ToUniversalTime();
                var runHistory = existing?.RunHistory ?? [];
                if (existing is not null)
                {
                    var summary = $"Monthly {request.Kind} work was deliberately re-armed by {actor} after configuration review.";
                    var rearm = new AutomatedJournalScheduleRunHistory(
                        RunKey: FormattableString.Invariant(
                            $"rearm|{existing.ScheduleId.Trim().ToLowerInvariant()}|v{existing.Version + 1}|{request.PeriodId.Trim().ToLowerInvariant()}"),
                        ScheduledForUtc: existing.ScheduledForUtc ?? now,
                        StartedAtUtc: now,
                        CompletedAtUtc: now,
                        State: AutomatedJournalScheduleStateDto.Scheduled,
                        Summary: summary,
                        PeriodId: request.PeriodId,
                        PeriodStart: request.PeriodStart,
                        PeriodEnd: request.PeriodEnd,
                        HistoryKind: AutomatedJournalScheduleHistoryKind.Rearm,
                        Actor: actor,
                        PreviousVersion: existing.Version,
                        ResultVersion: checked(existing.Version + 1));
                    runHistory = runHistory.Append(rearm).ToArray();
                }

                var saved = await store.SaveAsync(request with
                {
                    Actor = actor,
                    CreatedBy = existing?.CreatedBy ?? existing?.Actor ?? actor,
                    LastConfiguredBy = actor,
                    TenantId = tenantContext.TenantId,
                    CompanyId = tenantContext.CompanyId,
                    State = AutomatedJournalScheduleStateDto.Scheduled,
                    LastRunAtUtc = existing?.LastRunAtUtc,
                    LastScheduledForUtc = null,
                    JournalEntryIds = [],
                    LastSummary = existing is null
                        ? $"Monthly {request.Kind} work is scheduled."
                        : $"Monthly {request.Kind} work was deliberately re-armed by {actor} after configuration review.",
                    EvidenceLinks = [],
                    Blockers = [],
                    RunHistory = runHistory,
                    CapitalAccountReconciliation = null
                }, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(saved, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (AutomatedJournalScheduleConcurrencyException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (InvalidOperationException)
            {
                return EndpointHelpers.Forbidden();
            }
        })
        .WithName("ConfigureLedgerJournalAutomationMonthlySchedule")
        .Produces<AutomatedJournalScheduleWorkItem>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireWorkstationTenantCompanyScope()
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerJournalAutomationMonthlyRunDue, async (HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var worker = context.RequestServices.GetService<AutomatedJournalScheduledWorker>();
            if (worker is null)
            {
                return ServiceUnavailable();
            }

            var timeProvider = context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var result = await worker.RunDueForScopeAsync(
                timeProvider.GetUtcNow(),
                tenantContext.TenantId,
                tenantContext.CompanyId,
                context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("RunDueLedgerJournalAutomationMonthlySchedules")
        .Produces<AutomatedJournalScheduledBatchResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireWorkstationTenantCompanyScope()
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapGet(UiApiRoutes.LedgerJournalAutomationDailyMarkToMarketSchedules, async (HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var source = context.RequestServices.GetService<IDailyValuationPortfolioSource>();
            if (source is null)
            {
                return ServiceUnavailable();
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var schedules = (await source.ListAsync(context.RequestAborted).ConfigureAwait(false))
                .Where(item => string.Equals(
                    item.TenantId,
                    tenantContext.TenantId,
                    StringComparison.OrdinalIgnoreCase))
                .Where(item => string.Equals(
                    item.CompanyId,
                    tenantContext.CompanyId,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return Results.Json(schedules, jsonOptions);
        })
        .WithName("ListLedgerJournalAutomationDailyMarkToMarketSchedules")
        .Produces<IReadOnlyList<DailyValuationScheduleWorkItem>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapPost(UiApiRoutes.LedgerJournalAutomationDailyMarkToMarketSchedules, async (DailyValuationScheduleWorkItem request, HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var source = context.RequestServices.GetService<IDailyValuationPortfolioSource>();
            if (source is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var existing = await source.GetAsync(request.ScheduleId, context.RequestAborted).ConfigureAwait(false);
                if (existing is not null &&
                    (!string.Equals(existing.TenantId, tenantContext.TenantId, StringComparison.OrdinalIgnoreCase) ||
                     !string.Equals(existing.CompanyId, tenantContext.CompanyId, StringComparison.OrdinalIgnoreCase)))
                {
                    return EndpointHelpers.Forbidden();
                }

                if (existing is not null && existing.JournalEntryIds.Count > 0)
                {
                    var draftStore = context.RequestServices.GetService<IManualJournalEntryDraftStore>();
                    if (draftStore is null)
                    {
                        return ServiceUnavailable();
                    }

                    foreach (var journalEntryId in existing.JournalEntryIds)
                    {
                        var draft = await draftStore.GetAsync(
                            existing.FundProfileId,
                            journalEntryId,
                            context.RequestAborted,
                            existing.TenantId,
                            existing.CompanyId).ConfigureAwait(false);
                        if (draft is null || draft.Status is ManualJournalEntryStatusDto.Draft or
                            ManualJournalEntryStatusDto.NeedsFix or
                            ManualJournalEntryStatusDto.Submitted or
                            ManualJournalEntryStatusDto.Approved)
                        {
                            return Results.Conflict(new
                            {
                                error = $"Daily valuation schedule '{existing.ScheduleId}' cannot be reconfigured while retained batch draft '{journalEntryId:D}' is pending. Post or reject the current batch first."
                            });
                        }
                    }
                }

                var saved = await source.SaveAsync(request with
                {
                    Actor = ResolveMutationActor(context, request.Actor),
                    CreatedBy = existing?.CreatedBy ?? existing?.Actor ?? ResolveMutationActor(context, request.Actor),
                    LastConfiguredBy = ResolveMutationActor(context, request.Actor),
                    TenantId = tenantContext.TenantId,
                    CompanyId = tenantContext.CompanyId,
                    State = DailyValuationScheduleStateDto.Scheduled,
                    LastRunAtUtc = existing?.LastRunAtUtc,
                    LastScheduledForUtc = null,
                    JournalEntryId = null,
                    JournalEntryIds = [],
                    BatchCorrelationId = null,
                    LastSummary = $"Daily valuation is scheduled for {request.NextRunAtUtc:O}.",
                    EvidenceLinks = [],
                    Blockers = []
                }, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(saved, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException)
            {
                return EndpointHelpers.Forbidden();
            }
        })
        .WithName("ConfigureLedgerJournalAutomationDailyMarkToMarketSchedule")
        .Produces<DailyValuationScheduleWorkItem>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireWorkstationTenantCompanyScope()
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerJournalAutomationDailyMarkToMarketRunDue, async (HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var worker = context.RequestServices.GetService<DailyValuationScheduledWorker>();
            if (worker is null)
            {
                return ServiceUnavailable();
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var timeProvider = context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
            var result = await worker.RunDueForScopeAsync(
                timeProvider.GetUtcNow(),
                tenantContext.TenantId,
                tenantContext.CompanyId,
                context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("RunDueLedgerJournalAutomationDailyMarkToMarketSchedules")
        .Produces<DailyValuationScheduledBatchResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireWorkstationTenantCompanyScope()
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerJournalAutomationDailyMarkToMarketBatchLifecycle, async (
            DailyValuationBatchLifecycleRequestDto request,
            HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<DailyValuationBatchLifecycleService>();
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var result = await service.ApproveAndPostAsync(request with
                {
                    Actor = ResolveMutationActor(context, request.Actor),
                    TenantId = tenantContext.TenantId,
                    CompanyId = tenantContext.CompanyId
                }, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (UnauthorizedAccessException)
            {
                return EndpointHelpers.Forbidden();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("ApproveAndPostLedgerJournalAutomationDailyMarkToMarketBatch")
        .Produces<DailyValuationBatchLifecycleResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireWorkstationTenantCompanyScope()
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerJournalAutomationDailyMarkToMarketIntake, async (RunDailyMarkToMarketDraftIntakeRequest request, HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var runner = context.RequestServices.GetService<AutomatedJournalIntakeRunner>();
            if (runner is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var result = await runner.RunDailyMarkToMarketIntakeAsync(request with
                {
                    Actor = ResolveMutationActor(context, request.Actor),
                    TenantId = tenantContext.TenantId,
                    CompanyId = tenantContext.CompanyId
                }, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("RunLedgerJournalAutomationDailyMarkToMarketIntake")
        .Produces<DailyMarkToMarketIntakeRunResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireWorkstationTenantCompanyScope()
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerJournalAutomationDividendIntake, async (RunDividendDraftIntakeRequest request, HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var runner = context.RequestServices.GetService<AutomatedJournalIntakeRunner>();
            if (runner is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var result = await runner.RunDividendIntakeAsync(request with
                {
                    Actor = ResolveMutationActor(context, request.Actor),
                    TenantId = tenantContext.TenantId,
                    CompanyId = tenantContext.CompanyId
                }, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("RunLedgerJournalAutomationDividendIntake")
        .Produces<AutomatedJournalIntakeRunResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireWorkstationTenantCompanyScope()
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerJournalAutomationFeeAccrualIntake, async (RunFeeAccrualDraftIntakeRequest request, HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var runner = context.RequestServices.GetService<AutomatedJournalIntakeRunner>();
            if (runner is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var result = await runner.RunFeeAccrualIntakeAsync(request with
                {
                    Actor = ResolveMutationActor(context, request.Actor),
                    TenantId = tenantContext.TenantId,
                    CompanyId = tenantContext.CompanyId,
                    EvidenceRetainedAtUtc = null,
                    CapitalAccountReconciliation = null
                }, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("RunLedgerJournalAutomationFeeAccrualIntake")
        .Produces<AutomatedJournalIntakeRunResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireWorkstationTenantCompanyScope()
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerJournalAutomationPeriodCloseIntake, async (RunPeriodCloseDraftIntakeRequest request, HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var runner = context.RequestServices.GetService<AutomatedJournalIntakeRunner>();
            if (runner is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var result = await runner.RunPeriodCloseIntakeAsync(request with
                {
                    Actor = ResolveMutationActor(context, request.Actor),
                    TenantId = tenantContext.TenantId,
                    CompanyId = tenantContext.CompanyId
                }, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("RunLedgerJournalAutomationPeriodCloseIntake")
        .Produces<AutomatedJournalIntakeRunResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireWorkstationTenantCompanyScope()
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }
}
