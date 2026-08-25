using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.FinancialOperations.Ledger;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Identity.Auth;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    private const string ClosingEntryClientRejectionMessage =
        "Closing entries are produced by the governed period-close workflow and cannot be submitted as manual journal drafts.";

    public static void MapLedgerEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapGet(UiApiRoutes.LedgerBooks, async (
            string? fundProfileId,
            Guid? fundStructureNodeId,
            AccountingBasisKindDto? accountingBasis,
            HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var books = await service
                .ListBooksAsync(new LedgerBookQuery(fundProfileId, fundStructureNodeId, AccountingBasis: accountingBasis), context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(books, jsonOptions);
        })
        .WithName("ListLedgerBooks").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
        .Produces<IReadOnlyList<LedgerBookDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapGet(UiApiRoutes.LedgerBookById, async (Guid ledgerBookId, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var book = await service.GetBookAsync(ledgerBookId, context.RequestAborted).ConfigureAwait(false);
            return book is null
                ? Results.NotFound(new { error = $"Ledger book '{ledgerBookId}' was not found." })
                : Results.Json(book, jsonOptions);
        })
        .WithName("GetLedgerBook").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .Produces<LedgerBookDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapPost(UiApiRoutes.LedgerBooks, async (CreateLedgerBookRequest request, HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var book = await service.CreateBookAsync(request, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(book, jsonOptions, statusCode: StatusCodes.Status201Created);
            }
            catch (LedgerBookServiceException ex)
            {
                return MapServiceException(ex);
            }
        })
        .WithName("CreateLedgerBook").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
        .Produces<LedgerBookDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerBookRolloutAssessment, async (
            LedgerBookRolloutAssessmentRequest request,
            HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (!await IsBodyFundScopeAccessibleAsync(context, tenantContext, request.FundProfileId).ConfigureAwait(false))
            {
                return EndpointHelpers.Forbidden();
            }

            try
            {
                var assessment = await service.AssessRolloutAsync(request, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(assessment, jsonOptions);
            }
            catch (LedgerBookServiceException ex)
            {
                return MapServiceException(ex);
            }
            catch (NotSupportedException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status501NotImplemented);
            }
        })
        .WithName("AssessLedgerBookRollout").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
        .Produces<LedgerBookRolloutAssessmentDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapGet(UiApiRoutes.LedgerPeriods, async (
            Guid? ledgerBookId,
            string? fundProfileId,
            Guid? fundStructureNodeId,
            LedgerPeriodStatusDto? status,
            bool? openOnly,
            AccountingBasisKindDto? accountingBasis,
            HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var periods = await service
                .ListPeriodsAsync(
                    new LedgerPeriodQuery(
                        ledgerBookId,
                        fundProfileId,
                        fundStructureNodeId,
                        status,
                        OpenOnly: openOnly == true,
                        AccountingBasis: accountingBasis),
                    context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(periods, jsonOptions);
        })
        .WithName("ListLedgerPeriods").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
        .Produces<IReadOnlyList<LedgerPeriodDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapPost(UiApiRoutes.LedgerPeriods, async (CreateLedgerPeriodRequest request, HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var period = await service.CreatePeriodAsync(request, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(period, jsonOptions, statusCode: StatusCodes.Status201Created);
            }
            catch (LedgerBookServiceException ex)
            {
                return MapServiceException(ex);
            }
        })
        .WithName("CreateLedgerPeriod").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
        .Produces<LedgerPeriodDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerPeriodClose, async (
            Guid periodId,
            CloseLedgerPeriodRequest request,
            HttpContext context) =>
        {
            if (!TryGetLedgerCloseActor(context, out var actor))
            {
                return Results.Forbid();
            }

            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                if (request.CloseKind != LedgerPeriodCloseKindDto.SoftClose)
                {
                    return Results.BadRequest(new
                    {
                        error = "The generic ledger-period endpoint supports soft close only. Use the governed close-management period-lock workflow for hard close."
                    });
                }

                var result = await service
                    .ClosePeriodAsync(
                        periodId,
                        request with
                        {
                            ClosedBy = actor
                        },
                        context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (LedgerBookServiceException ex)
            {
                return MapServiceException(ex);
            }
        })
        .WithName("CloseLedgerPeriod").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
        .Produces<LedgerPeriodCloseResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapGet(UiApiRoutes.LedgerPeriodJournalEntries, async (Guid periodId, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var journalStore = ResolveJournalStore(context);
            if (journalStore is null)
            {
                return ServiceUnavailable();
            }

            var period = await journalStore.GetPeriodAsync(periodId, context.RequestAborted).ConfigureAwait(false);
            if (period is null)
            {
                return Results.NotFound(new { error = $"Ledger period '{periodId}' was not found." });
            }

            var ledgerBookId = period.LedgerBookId;
            if (ledgerBookId is not { } bookId)
            {
                return Results.BadRequest(new { error = $"Ledger period '{periodId}' is not scoped to a ledger book." });
            }

            var dimensionFilter = BuildDimensionReportFilter(context.Request.Query);
            var entries = await journalStore
                .QueryAsync(
                    new LedgerJournalEntryQuery(
                        LedgerBookId: bookId,
                        PeriodId: periodId,
                        LineDimensions: ToLineDimensionSet(dimensionFilter)),
                    context.RequestAborted)
                .ConfigureAwait(false);
            var result = BuildJournalEntryDtos(entries, _ => bookId, dimensionFilter);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetLedgerPeriodJournalEntries").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .Produces<IReadOnlyList<LedgerJournalEntryDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapGet(UiApiRoutes.LedgerAggregateJournalEntries, async (
            Guid aggregateId,
            Guid? ledgerBookId,
            HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var journalStore = ResolveJournalStore(context);
            if (journalStore is null)
            {
                return ServiceUnavailable();
            }

            var dimensionFilter = BuildDimensionReportFilter(context.Request.Query);
            var entries = await journalStore
                .QueryAsync(
                    new LedgerJournalEntryQuery(
                        LedgerBookId: ledgerBookId,
                        AggregateId: aggregateId,
                        LineDimensions: ToLineDimensionSet(dimensionFilter)),
                    context.RequestAborted)
                .ConfigureAwait(false);
            var periodBookIds = new Dictionary<Guid, Guid?>();
            foreach (var periodId in entries.Select(static entry => entry.PeriodId).Distinct())
            {
                var period = await journalStore.GetPeriodAsync(periodId, context.RequestAborted).ConfigureAwait(false);
                if (period is null)
                {
                    return Results.NotFound(new { error = $"Ledger period '{periodId}' was not found for aggregate '{aggregateId}'." });
                }

                periodBookIds[periodId] = period.LedgerBookId;
            }

            var result = BuildJournalEntryDtos(
                entries,
                entry => periodBookIds.GetValueOrDefault(entry.PeriodId),
                dimensionFilter);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetLedgerAggregateJournalEntries").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .Produces<IReadOnlyList<LedgerJournalEntryDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapGet(UiApiRoutes.LedgerPeriodTrialBalance, async (Guid periodId, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var dimensionFilter = BuildDimensionReportFilter(context.Request.Query);
            var summary = await service.GetPeriodSummaryAsync(periodId, context.RequestAborted).ConfigureAwait(false);
            return summary is null
                ? Results.NotFound(new { error = $"Ledger period '{periodId}' has no closed-period summary." })
                : Results.Json(ApplyDimensionFilter(summary, dimensionFilter).TrialBalance, jsonOptions);
        })
        .WithName("GetLedgerPeriodTrialBalance").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .Produces<IReadOnlyList<LedgerPeriodTrialBalanceLineDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapGet(UiApiRoutes.LedgerPeriodTrialBalanceReport, async (Guid periodId, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var dimensionFilter = BuildDimensionReportFilter(context.Request.Query);
            var summary = await service.GetPeriodSummaryAsync(periodId, context.RequestAborted).ConfigureAwait(false);
            return summary is null
                ? Results.NotFound()
                : Results.Json(BuildTrialBalanceReport(ApplyDimensionFilter(summary, dimensionFilter), context), jsonOptions);
        })
        .WithName("GetLedgerPeriodTrialBalanceReport").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .Produces<LedgerTrialBalanceReportDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapGet(UiApiRoutes.LedgerPeriodPnlSummary, async (Guid periodId, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var dimensionFilter = BuildDimensionReportFilter(context.Request.Query);
            var summary = await service.GetPeriodSummaryAsync(periodId, context.RequestAborted).ConfigureAwait(false);
            return summary is null
                ? Results.NotFound(new { error = $"Ledger period '{periodId}' has no closed-period summary." })
                : Results.Json(BuildPnlSummary(ApplyDimensionFilter(summary, dimensionFilter)), jsonOptions);
        })
        .WithName("GetLedgerPeriodPnlSummary").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .Produces<LedgerPeriodPnlSummaryDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapGet(UiApiRoutes.LedgerReportsTrialBalance, async (
            Guid? ledgerBookId,
            string? fundProfileId,
            Guid? fundStructureNodeId,
            AccountingBasisKindDto? accountingBasis,
            DateOnly? startDate,
            DateOnly? endDate,
            HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!IsValidDateRange(startDate, endDate))
            {
                return Results.BadRequest(new { error = "Report start date must be before or equal to the end date." });
            }

            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }
            var dimensionFilter = BuildDimensionReportFilter(context.Request.Query);

            var summaryLoad = await LoadClosedPeriodSummariesAsync(
                service,
                ledgerBookId,
                fundProfileId,
                fundStructureNodeId,
                accountingBasis,
                startDate,
                endDate,
                context.RequestAborted).ConfigureAwait(false);
            if (summaryLoad.Error is not null)
            {
                return summaryLoad.Error;
            }

            return Results.Json(
                BuildTrialBalanceReport(
                    summaryLoad.Summaries,
                    ledgerBookId,
                    fundProfileId,
                    fundStructureNodeId,
                    accountingBasis,
                    startDate,
                    endDate,
                    dimensionFilter),
                jsonOptions);
        })
        .WithName("GetLedgerCrossPeriodTrialBalanceReport").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
        .Produces<LedgerCrossPeriodTrialBalanceReportDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapGet(UiApiRoutes.LedgerReportsPnlSummary, async (
            Guid? ledgerBookId,
            string? fundProfileId,
            Guid? fundStructureNodeId,
            AccountingBasisKindDto? accountingBasis,
            DateOnly? startDate,
            DateOnly? endDate,
            HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!IsValidDateRange(startDate, endDate))
            {
                return Results.BadRequest(new { error = "Report start date must be before or equal to the end date." });
            }

            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }
            var dimensionFilter = BuildDimensionReportFilter(context.Request.Query);

            var summaryLoad = await LoadClosedPeriodSummariesAsync(
                service,
                ledgerBookId,
                fundProfileId,
                fundStructureNodeId,
                accountingBasis,
                startDate,
                endDate,
                context.RequestAborted).ConfigureAwait(false);
            if (summaryLoad.Error is not null)
            {
                return summaryLoad.Error;
            }

            return Results.Json(
                BuildPnlReport(
                    summaryLoad.Summaries,
                    ledgerBookId,
                    fundProfileId,
                    fundStructureNodeId,
                    accountingBasis,
                    startDate,
                    endDate,
                    dimensionFilter),
                jsonOptions);
        })
        .WithName("GetLedgerCrossPeriodPnlReport").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
        .Produces<LedgerCrossPeriodPnlReportDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented);

        MapAccountingConfigurationEndpoints(app, jsonOptions);

        app.MapGet(UiApiRoutes.LedgerCloseManagementPeriodPlan, async (
            Guid workflowId,
            HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingCloseManagementService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var scope = await ResolveCloseWorkflowTenantScopeAsync(context, service, workflowId).ConfigureAwait(false);
            if (!scope.IsAccessible)
            {
                return CloseWorkflowScopeDenied();
            }

            return scope.Plan is null
                ? Results.NotFound(new { error = $"Close workflow '{workflowId}' was not found." })
                : Results.Json(scope.Plan, jsonOptions);
        })
        .WithName("GetLedgerCloseManagementPeriodPlan").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .Produces<ClosePeriodPlanDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapPost(UiApiRoutes.LedgerCloseManagementPeriodPlanConfiguration, async (
            UpsertClosePeriodPlanConfigurationRequestDto request,
            HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context) || !CanConfigureCloseTaskApprovalRoles(context, request.TaskConfigurations))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingCloseManagementService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var scope = await ResolveCloseWorkflowTenantScopeAsync(context, service, request.WorkflowId).ConfigureAwait(false);
                if (!scope.IsAccessible)
                {
                    return CloseWorkflowScopeDenied();
                }
                if (scope.Plan is null)
                {
                    return Results.NotFound(new { error = $"Close workflow '{request.WorkflowId}' was not found." });
                }

                var actor = ResolveMutationActor(context, request.Actor ?? string.Empty);
                var result = await service
                    .ConfigurePeriodPlanScopedAsync(
                        request with { Actor = actor },
                        actor,
                        scope.TenantContext.TenantId,
                        scope.TenantContext.CompanyId,
                        context.RequestAborted)
                    .ConfigureAwait(false);
                return result is null
                    ? Results.NotFound(new { error = $"Close workflow '{request.WorkflowId}' was not found." })
                    : Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = [ex.Message]
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("ConfigureLedgerCloseManagementPeriodPlan").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
        .Produces<ClosePeriodPlanDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerCloseManagementLateAdjustments, async (
            CreateLateAdjustmentRequestDto request,
            HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingCloseManagementService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var scope = await ResolveCloseWorkflowTenantScopeAsync(context, service, request.WorkflowId).ConfigureAwait(false);
                if (!scope.IsAccessible)
                {
                    return CloseWorkflowScopeDenied();
                }
                if (scope.Plan is null)
                {
                    return Results.NotFound(new { error = $"Close workflow '{request.WorkflowId}' was not found." });
                }

                var actor = ResolveMutationActor(context, request.RequestedBy);
                var result = await service
                    .RequestLateAdjustmentScopedAsync(
                        request with { RequestedBy = actor },
                        actor,
                        scope.TenantContext.TenantId,
                        scope.TenantContext.CompanyId,
                        context.RequestAborted)
                    .ConfigureAwait(false);
                return result is null
                    ? Results.NotFound(new { error = $"Close workflow '{request.WorkflowId}' was not found." })
                    : Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = [ex.Message]
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("CreateLedgerCloseManagementLateAdjustment").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
        .Produces<ClosePeriodPlanDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerCloseManagementLateAdjustmentReview, async (
            ReviewLateAdjustmentRequestDto request,
            HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingCloseManagementService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var scope = await ResolveCloseWorkflowTenantScopeAsync(context, service, request.WorkflowId).ConfigureAwait(false);
                if (!scope.IsAccessible)
                {
                    return CloseWorkflowScopeDenied();
                }
                if (scope.Plan is null)
                {
                    return Results.NotFound(new { error = $"Close workflow '{request.WorkflowId}' was not found." });
                }

                var actor = ResolveMutationActor(context, request.Actor);
                var result = await service
                    .ReviewLateAdjustmentScopedAsync(
                        request with { Actor = actor },
                        actor,
                        scope.TenantContext.TenantId,
                        scope.TenantContext.CompanyId,
                        context.RequestAborted)
                    .ConfigureAwait(false);
                return result is null
                    ? Results.NotFound(new { error = $"Close workflow '{request.WorkflowId}' was not found." })
                    : Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = [ex.Message]
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("ReviewLedgerCloseManagementLateAdjustment").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
        .Produces<ClosePeriodPlanDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerCloseManagementTaskSignOffs, async (
            SignOffCloseTaskRequestDto request,
            HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context) || !HasCloseTaskSignOffRoleAuthority(context, request.Role))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingCloseManagementService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var scope = await ResolveCloseWorkflowTenantScopeAsync(context, service, request.WorkflowId).ConfigureAwait(false);
                if (!scope.IsAccessible)
                {
                    return CloseWorkflowScopeDenied();
                }
                if (scope.Plan is null)
                {
                    return Results.NotFound(new { error = $"Close workflow '{request.WorkflowId}' was not found." });
                }

                var actor = ResolveMutationActor(context, request.Actor);
                var result = await service
                    .SignOffCloseTaskScopedAsync(
                        request with { Actor = actor },
                        actor,
                        scope.TenantContext.TenantId,
                        scope.TenantContext.CompanyId,
                        context.RequestAborted)
                    .ConfigureAwait(false);
                return result is null
                    ? Results.NotFound(new { error = $"Close workflow '{request.WorkflowId}' was not found." })
                    : Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = [ex.Message]
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("SignOffLedgerCloseManagementTask").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
        .Produces<ClosePeriodPlanDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerCloseManagementEvidenceReview, async (
            ReviewCloseEvidenceRequestDto request,
            HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingCloseManagementService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var scope = await ResolveCloseWorkflowTenantScopeAsync(context, service, request.WorkflowId).ConfigureAwait(false);
                if (!scope.IsAccessible)
                {
                    return CloseWorkflowScopeDenied();
                }
                if (scope.Plan is null)
                {
                    return Results.NotFound(new { error = $"Close workflow '{request.WorkflowId}' was not found." });
                }

                var actor = ResolveMutationActor(context, request.Actor);
                var result = await service
                    .ReviewCloseEvidenceScopedAsync(
                        request with { Actor = actor },
                        actor,
                        scope.TenantContext.TenantId,
                        scope.TenantContext.CompanyId,
                        context.RequestAborted)
                    .ConfigureAwait(false);
                return result is null
                    ? Results.NotFound(new { error = $"Close workflow '{request.WorkflowId}' was not found." })
                    : Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = [ex.Message]
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("ReviewLedgerCloseManagementEvidence").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
        .Produces<ClosePeriodPlanDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerCloseManagementPeriodLock, async (
            LockClosePeriodRequestDto request,
            HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            string? controllerRole = null;
            if (!request.PrepareClosingEntriesOnly &&
                !TryResolveControllerRole(context, out controllerRole))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingCloseManagementService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var scope = await ResolveCloseWorkflowTenantScopeAsync(context, service, request.WorkflowId).ConfigureAwait(false);
                if (!scope.IsAccessible)
                {
                    return CloseWorkflowScopeDenied();
                }
                if (scope.Plan is null)
                {
                    return Results.NotFound(new { error = $"Close workflow '{request.WorkflowId}' was not found." });
                }

                var actor = ResolveMutationActor(context, request.Actor);
                var result = await service
                    .LockClosePeriodScopedAsync(
                        request with
                        {
                            Actor = actor,
                            ActionOrigin = OperationsActionOriginDto.HumanOperator,
                            ControllerRole = controllerRole
                        },
                        actor,
                        scope.TenantContext.TenantId,
                        scope.TenantContext.CompanyId,
                        context.RequestAborted)
                    .ConfigureAwait(false);
                return result is null
                    ? Results.NotFound(new { error = $"Close workflow '{request.WorkflowId}' was not found." })
                    : Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = [ex.Message]
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("LockLedgerCloseManagementPeriod").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
        .Produces<ClosePeriodLockResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerCloseManagementPeriodReopen, async (
            ReopenClosePeriodRequestDto request,
            HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context) ||
                !TryResolveControllerRole(context, out var controllerRole))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingCloseManagementService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var scope = await ResolveCloseWorkflowTenantScopeAsync(context, service, request.WorkflowId).ConfigureAwait(false);
                if (!scope.IsAccessible)
                {
                    return CloseWorkflowScopeDenied();
                }
                if (scope.Plan is null)
                {
                    return Results.NotFound(new { error = $"Close workflow '{request.WorkflowId}' was not found." });
                }

                var actor = ResolveMutationActor(context, request.Actor);
                var result = await service
                    .ReopenClosePeriodScopedAsync(
                        request with
                        {
                            Actor = actor,
                            Role = controllerRole
                        },
                        actor,
                        scope.TenantContext.TenantId,
                        scope.TenantContext.CompanyId,
                        context.RequestAborted)
                    .ConfigureAwait(false);
                return result is null
                    ? Results.NotFound(new { error = $"Close workflow '{request.WorkflowId}' was not found." })
                    : Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = [ex.Message]
                });
            }
            catch (NotSupportedException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status501NotImplemented);
            }
            catch (Exception ex) when (ex is InvalidOperationException or LedgerBookServiceException)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("ReopenLedgerCloseManagementPeriod").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
        .Produces<ClosePeriodReopenResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerReportsAccountingPackage, async (
            AccountingReportPackageRequestDto request,
            HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<IAccountingReportPackageService>();
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var actor = ResolveMutationActor(context, request.Actor);
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                if (!await IsAccountingPackageBuildScopeAccessibleAsync(context, tenantContext, request.FundProfileId).ConfigureAwait(false))
                {
                    return EndpointHelpers.Forbidden();
                }

                var result = await service
                    .BuildPackageAsync(request with
                    {
                        Actor = actor,
                        TenantId = tenantContext.TenantId,
                        CompanyId = tenantContext.CompanyId
                    }, context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = [ex.Message]
                });
            }
        })
        .WithName("BuildLedgerAccountingReportPackage").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
        .Produces<AccountingReportPackageBundleDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        // SEC-005 slice 4c-iii: this build route persists a fund-scoped report package but is not on the
        // MutationRateLimitPolicy set, so it needs the write-tenant gate explicitly (as certification does).
        .RequireFundScopedWriteTenant();

        app.MapPost(UiApiRoutes.LedgerReportsAccountingPackageCertification, async (
            CertifyAccountingReportPackageRequestDto request,
            HttpContext context) =>
        {
            if (!HasLedgerCertificationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<IAccountingReportPackageService>();
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var actor = ResolveMutationActor(context, request.Actor);
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                if (!HasAccountingPackageTenantScope(tenantContext))
                {
                    return EndpointHelpers.Forbidden();
                }

                var result = await service
                    .CertifyPackageAsync(request with
                    {
                        Actor = actor,
                        TenantId = tenantContext.TenantId,
                        CompanyId = tenantContext.CompanyId
                    }, context.RequestAborted)
                    .ConfigureAwait(false);
                return result is null
                    ? Results.NotFound(new { error = "Accounting report package was not found." })
                    : Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = [ex.Message]
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("CertifyLedgerAccountingReportPackage").RequirePermission(UserPermission.AdminMaintenance)
        .Produces<AccountingReportPackageBundleDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapGet(UiApiRoutes.LedgerReportsAccountingPackages, async (
            string? fundProfileId,
            string? periodId,
            Guid? ledgerBookId,
            HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<IAccountingReportPackageService>();
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var dimensionFilter = BuildDimensionReportFilter(context.Request.Query);
            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (!HasAccountingPackageTenantScope(tenantContext))
            {
                return EndpointHelpers.Forbidden();
            }

            var result = await service
                .ListPackagesAsync(
                    fundProfileId,
                    periodId,
                    ledgerBookId,
                    ToLedgerDimensionSetDto(dimensionFilter),
                    tenantContext.TenantId,
                    tenantContext.CompanyId,
                    context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("ListLedgerAccountingReportPackages").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
        .Produces<IReadOnlyList<AccountingReportPackageBundleDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapGet(UiApiRoutes.LedgerReportsAccountingPackageExport, async (
            string packageId,
            string artifactId,
            HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<IAccountingReportPackageService>();
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                if (!HasAccountingPackageTenantScope(tenantContext))
                {
                    return EndpointHelpers.Forbidden();
                }

                var result = await service
                    .GetExportArtifactManifestAsync(
                        packageId,
                        artifactId,
                        tenantContext.TenantId,
                        tenantContext.CompanyId,
                        context.RequestAborted)
                    .ConfigureAwait(false);
                return result is null
                    ? Results.NotFound(new { error = "Accounting report package export artifact was not found." })
                    : Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = [ex.Message]
                });
            }
        })
        .WithName("GetLedgerAccountingReportPackageExport").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .Produces<ReportExportArtifactManifestDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapGet(UiApiRoutes.LedgerManualJournalEntryWorkbench, async (
            string? fundProfileId,
            Guid? ledgerBookId,
            HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveManualJournalEntryWorkbenchService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var workbench = await service.GetWorkbenchAsync(fundProfileId, ledgerBookId, context.RequestAborted, tenantContext.TenantId, tenantContext.CompanyId).ConfigureAwait(false);
            return Results.Json(workbench, jsonOptions);
        })
        .WithName("GetManualJournalEntryWorkbench").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
        .Produces<ManualJournalEntryWorkbenchDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapGet(UiApiRoutes.LedgerPrivateCapitalActivity, async (
            string? fundProfileId,
            Guid? ledgerBookId,
            string? fundEventId,
            string? capitalAccountId,
            string? investorId,
            string? paymentIntentId,
            HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveManualJournalEntryWorkbenchService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var activity = await service.GetPrivateCapitalActivityAsync(fundProfileId, ledgerBookId, context.RequestAborted, tenantContext.TenantId, tenantContext.CompanyId).ConfigureAwait(false);
            return Results.Json(
                FilterPrivateCapitalActivity(activity, fundEventId, capitalAccountId, investorId, paymentIntentId),
                jsonOptions);
        })
        .WithName("GetLedgerPrivateCapitalActivity").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
        .Produces<PrivateCapitalActivityProjectionDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapGet(UiApiRoutes.LedgerPrivateCapitalFundEventRecord, async (
            string? fundProfileId,
            Guid? ledgerBookId,
            string? fundEventId,
            HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var normalizedFundEventId = NormalizeOptional(fundEventId);
            if (normalizedFundEventId is null)
            {
                return Results.BadRequest(new { error = "fundEventId is required." });
            }

            var service = ResolveManualJournalEntryWorkbenchService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var activity = await service.GetPrivateCapitalActivityAsync(fundProfileId, ledgerBookId, context.RequestAborted, tenantContext.TenantId, tenantContext.CompanyId).ConfigureAwait(false);
            var record = FilterPrivateCapitalActivity(activity, normalizedFundEventId, null, null, null)
                .FundEventRecords
                .FirstOrDefault(item => string.Equals(item.FundEventId, normalizedFundEventId, StringComparison.OrdinalIgnoreCase));
            if (record is null)
            {
                return Results.NotFound();
            }

            return Results.Json(record, jsonOptions);
        })
        .WithName("GetLedgerPrivateCapitalFundEventRecord").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
        .Produces<PrivateCapitalFundEventLedgerRecordDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapGet(UiApiRoutes.LedgerPrivateCapitalFundEventCommandCenter, async (
            string? fundProfileId,
            Guid? ledgerBookId,
            string? fundEventId,
            HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var normalizedFundEventId = NormalizeOptional(fundEventId);
            if (normalizedFundEventId is null)
            {
                return Results.BadRequest(new { error = "fundEventId is required." });
            }

            var service = ResolvePrivateCapitalFundEventCommandCenterService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var commandCenter = await service
                .GetCommandCenterAsync(fundProfileId, ledgerBookId, normalizedFundEventId, context.RequestAborted)
                .ConfigureAwait(false);
            if (commandCenter is null)
            {
                return Results.NotFound();
            }

            return Results.Json(commandCenter, jsonOptions);
        })
        .WithName("GetLedgerPrivateCapitalFundEventCommandCenter").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
        .Produces<PrivateCapitalFundEventCommandCenterDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapGet(UiApiRoutes.LedgerPrivateCapitalCapitalAccountSubledger, async (
            string? fundProfileId,
            Guid? ledgerBookId,
            string? capitalAccountId,
            string? investorId,
            string? currency,
            HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var normalizedCapitalAccountId = NormalizeOptional(capitalAccountId);
            if (normalizedCapitalAccountId is null)
            {
                return Results.BadRequest(new { error = "capitalAccountId is required." });
            }

            var service = ResolveManualJournalEntryWorkbenchService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var normalizedInvestorId = NormalizeOptional(investorId);
            var normalizedCurrency = NormalizeOptional(currency);
            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var activity = await service.GetPrivateCapitalActivityAsync(fundProfileId, ledgerBookId, context.RequestAborted, tenantContext.TenantId, tenantContext.CompanyId).ConfigureAwait(false);
            var filtered = FilterPrivateCapitalActivity(activity, null, normalizedCapitalAccountId, normalizedInvestorId, null);
            var subledgers = filtered.CapitalAccountSubledgers
                .Where(item =>
                    string.Equals(item.CapitalAccountId, normalizedCapitalAccountId, StringComparison.OrdinalIgnoreCase) &&
                    (normalizedInvestorId is null || string.Equals(item.InvestorId ?? string.Empty, normalizedInvestorId, StringComparison.OrdinalIgnoreCase)) &&
                    (normalizedCurrency is null || string.Equals(item.Currency, normalizedCurrency, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(static item => item.InvestorId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.Currency, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (subledgers.Length == 0)
            {
                return Results.NotFound();
            }

            if (subledgers.Length > 1)
            {
                return Results.BadRequest(new
                {
                    error = $"capitalAccountId '{normalizedCapitalAccountId}' matched {subledgers.Length} private-capital subledgers. Provide investorId and currency to select a single capital-account subledger."
                });
            }

            return Results.Json(subledgers[0], jsonOptions);
        })
        .WithName("GetLedgerPrivateCapitalCapitalAccountSubledger").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
        .Produces<PrivateCapitalCapitalAccountSubledgerDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapGet(UiApiRoutes.LedgerPrivateCapitalReportOutput, async (
            string? fundProfileId,
            Guid? ledgerBookId,
            string? reportOutputId,
            string? reportPackId,
            string? fundEventId,
            string? capitalAccountId,
            string? investorId,
            HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var normalizedReportOutputId = NormalizeOptional(reportOutputId);
            var normalizedReportPackId = NormalizeOptional(reportPackId);
            var normalizedFundEventId = NormalizeOptional(fundEventId);
            var normalizedCapitalAccountId = NormalizeOptional(capitalAccountId);
            var normalizedInvestorId = NormalizeOptional(investorId);
            if (normalizedReportOutputId is null &&
                normalizedReportPackId is null &&
                normalizedFundEventId is null)
            {
                return Results.BadRequest(new { error = "reportOutputId, reportPackId, or fundEventId is required." });
            }

            var service = ResolveManualJournalEntryWorkbenchService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var activity = await service.GetPrivateCapitalActivityAsync(fundProfileId, ledgerBookId, context.RequestAborted, tenantContext.TenantId, tenantContext.CompanyId).ConfigureAwait(false);
            var filtered = FilterPrivateCapitalActivity(activity, normalizedFundEventId, normalizedCapitalAccountId, normalizedInvestorId, null);
            var reportOutputs = filtered.ReportOutputs
                .Where(item =>
                    (normalizedReportOutputId is null || string.Equals(item.ReportOutputId, normalizedReportOutputId, StringComparison.OrdinalIgnoreCase)) &&
                    (normalizedReportPackId is null || string.Equals(item.ReportPackId ?? string.Empty, normalizedReportPackId, StringComparison.OrdinalIgnoreCase)) &&
                    MatchesPrivateCapitalFilter(item.FundEventId, normalizedFundEventId) &&
                    MatchesPrivateCapitalFilter(item.CapitalAccountId, normalizedCapitalAccountId) &&
                    MatchesPrivateCapitalFilter(item.InvestorId, normalizedInvestorId))
                .OrderByDescending(static item => item.IsPublished)
                .ThenByDescending(static item => item.IsReportReady)
                .ThenBy(static item => item.EffectiveDate)
                .ThenBy(static item => item.ReportOutputId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (reportOutputs.Length == 0)
            {
                return Results.NotFound();
            }

            if (reportOutputs.Length > 1)
            {
                return Results.BadRequest(new
                {
                    error = $"report output selector matched {reportOutputs.Length} private-capital report outputs. Provide reportOutputId or narrower fundEventId, reportPackId, capitalAccountId, and investorId filters to select one report output."
                });
            }

            return Results.Json(reportOutputs[0], jsonOptions);
        })
        .WithName("GetLedgerPrivateCapitalReportOutput").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
        .Produces<PrivateCapitalReportOutputDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapGet(UiApiRoutes.LedgerPrivateCapitalCapitalAccountWorkbench, async (
            string? fundProfileId,
            Guid? ledgerBookId,
            string? fundEventId,
            string? capitalAccountId,
            string? investorId,
            string? currency,
            HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveCapitalAccountWorkbenchService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var workbench = await service.GetWorkbenchAsync(
                    fundProfileId,
                    ledgerBookId,
                    fundEventId,
                    capitalAccountId,
                    investorId,
                    currency,
                    context.RequestAborted)
                .ConfigureAwait(false);
            if ((NormalizeOptional(fundEventId) is not null ||
                 NormalizeOptional(capitalAccountId) is not null ||
                 NormalizeOptional(investorId) is not null ||
                 NormalizeOptional(currency) is not null) &&
                workbench.InvestorAccounts.Count == 0)
            {
                return Results.NotFound();
            }

            return Results.Json(workbench, jsonOptions);
        })
        .WithName("GetLedgerPrivateCapitalCapitalAccountWorkbench").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
        .Produces<CapitalAccountWorkbenchDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapPost(UiApiRoutes.LedgerManualJournalEntryDrafts, async (SaveManualJournalEntryDraftRequest request, HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveManualJournalEntryWorkbenchService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            // Closing entries are the sanctioned exception to the closed-period posting bar; only the
            // in-process period-close automation may produce them. Reject client-submitted ClosingEntry
            // drafts so this HTTP boundary cannot be used to post to a closed period.
            if (request.Draft?.EntryType == ManualJournalEntryTypeDto.ClosingEntry)
            {
                return Results.BadRequest(new { error = ClosingEntryClientRejectionMessage });
            }

            try
            {
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var result = await service.SaveDraftAsync(request with
                {
                    Actor = ResolveMutationActor(context, request.Actor),
                    TenantId = tenantContext.TenantId,
                    CompanyId = tenantContext.CompanyId,
                    // SEC-005 slice 4a: scrub the nested draft's client-supplied tenant scope too — the
                    // service resolves the persisted scope as request.TenantId ?? request.Draft.TenantId,
                    // so the server-resolved tenant must overwrite the nested body value, not just the outer.
                    Draft = request.Draft with
                    {
                        TenantId = tenantContext.TenantId,
                        CompanyId = tenantContext.CompanyId
                    },
                    ReportGroupPrincipalIds = EndpointAuthorization.ResolveReportGroupPrincipalIds(context)
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
        .WithName("SaveManualJournalEntryDraft").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
        .Produces<ManualJournalEntryDraftDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerManualJournalEntryValidate, async (ValidateManualJournalEntryDraftRequest request, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveManualJournalEntryWorkbenchService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (request.Draft?.EntryType == ManualJournalEntryTypeDto.ClosingEntry)
            {
                return Results.BadRequest(new { error = ClosingEntryClientRejectionMessage });
            }

            try
            {
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                if (!await IsBodyFundScopeAccessibleAsync(context, tenantContext, request.Draft.FundProfileId).ConfigureAwait(false))
                {
                    return EndpointHelpers.Forbidden();
                }

                var result = await service.ValidateDraftAsync(request with
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
        })
        .WithName("ValidateManualJournalEntryDraft").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
        .Produces<ManualJournalEntryDraftDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant();

        app.MapPost(UiApiRoutes.LedgerManualJournalEntrySubmitApproval, async (SubmitManualJournalEntryApprovalRequest request, HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveManualJournalEntryWorkbenchService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var result = await service.SubmitApprovalAsync(request with
                {
                    Actor = ResolveMutationActor(context, request.Actor),
                    TenantId = tenantContext.TenantId,
                    CompanyId = tenantContext.CompanyId,
                    ReportGroupPrincipalIds = EndpointAuthorization.ResolveReportGroupPrincipalIds(context)
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
        .WithName("SubmitManualJournalEntryApproval").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
        .Produces<ManualJournalEntryDraftDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        MapJournalAutomationEndpoints(app, jsonOptions);

        app.MapPost(UiApiRoutes.LedgerManualJournalEntryEvidence, async (AttachManualJournalEntryEvidenceRequest request, HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveManualJournalEntryWorkbenchService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var result = await service.AttachEvidenceAsync(request with
                {
                    Actor = ResolveMutationActor(context, request.Actor),
                    TenantId = tenantContext.TenantId,
                    CompanyId = tenantContext.CompanyId,
                    ReportGroupPrincipalIds = EndpointAuthorization.ResolveReportGroupPrincipalIds(context)
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
        .WithName("AttachManualJournalEntryEvidence").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
        .Produces<ManualJournalEntryDraftDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerManualJournalEntryLifecycleAction, async (JournalEntryLifecycleActionRequestDto request, HttpContext context) =>
        {
            if (!HasManualJournalLifecycleActionPermission(context, request.Action))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveManualJournalEntryLifecycleService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var result = await service
                    .ApplyLifecycleActionAsync(request with
                    {
                        Actor = ResolveMutationActor(context, request.Actor),
                        TenantId = tenantContext.TenantId,
                        CompanyId = tenantContext.CompanyId,
                        ReportGroupPrincipalIds = EndpointAuthorization.ResolveReportGroupPrincipalIds(context)
                    }, context.RequestAborted)
                    .ConfigureAwait(false);
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
        .WithName("ApplyManualJournalEntryLifecycleAction").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
        .Produces<JournalEntryLifecycleActionResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static ILedgerBookService? ResolveService(HttpContext context)
        => context.RequestServices.GetService<ILedgerBookService>();

    private static ILedgerJournalStore? ResolveJournalStore(HttpContext context)
        => context.RequestServices.GetService<ILedgerJournalStore>();

    private static IAccountingCloseManagementService? ResolveAccountingCloseManagementService(HttpContext context)
        => context.RequestServices.GetService<IAccountingCloseManagementService>();

    private static IAccountingConfigurationService? ResolveAccountingConfigurationService(HttpContext context)
    {
        var service = context.RequestServices.GetService<IAccountingConfigurationService>();
        if (service is not null)
        {
            return service;
        }

        var store = context.RequestServices.GetService<IAccountingConfigurationStore>();
        var auditStore = context.RequestServices.GetService<IAccountingActionAuditStore>();
        return store is null || auditStore is null
            ? null
            : new AccountingConfigurationService(
                store,
                auditStore,
                context.RequestServices.GetService<ILedgerBookService>());
    }

    private static IAccountingPostingCandidateService? ResolveAccountingPostingCandidateService(HttpContext context)
        => context.RequestServices.GetService<IAccountingPostingCandidateService>();

    private static IAssetAccountingEventSpineService? ResolveAssetAccountingEventSpineService(HttpContext context)
        => context.RequestServices.GetService<IAssetAccountingEventSpineService>();

    private static IAccountingPostingCandidatePostService? ResolveAccountingPostingCandidatePostService(HttpContext context)
        => context.RequestServices.GetService<IAccountingPostingCandidatePostService>();

    private static IAccountingBasisProjectionSetService? ResolveAccountingBasisProjectionSetService(HttpContext context)
        => context.RequestServices.GetService<IAccountingBasisProjectionSetService>();

    private static IManualJournalEntryWorkbenchService? ResolveManualJournalEntryWorkbenchService(HttpContext context)
    {
        var service = context.RequestServices.GetService<IManualJournalEntryWorkbenchService>();
        if (service is not null)
        {
            return service;
        }

        var draftStore = context.RequestServices.GetService<IManualJournalEntryDraftStore>();
        var configurationService = ResolveAccountingConfigurationService(context);
        var auditStore = context.RequestServices.GetService<IAccountingActionAuditStore>();
        return draftStore is null || configurationService is null || auditStore is null
            ? null
            : new ManualJournalEntryWorkbenchService(
                draftStore,
                configurationService,
                auditStore,
                 context.RequestServices.GetService<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>(),
                 context.RequestServices.GetService<ILedgerJournalStore>(),
                 context.RequestServices.GetService<ReportPackWorkflowService>(),
                 postingTarget: context.RequestServices.GetService<IGovernedLedgerPostingTarget>());
    }

    private static IManualJournalEntryLifecycleService? ResolveManualJournalEntryLifecycleService(HttpContext context)
    {
        var service = context.RequestServices.GetService<IManualJournalEntryLifecycleService>();
        if (service is not null)
        {
            return service;
        }

        return ResolveManualJournalEntryWorkbenchService(context) as IManualJournalEntryLifecycleService;
    }

    private static ICapitalAccountWorkbenchService? ResolveCapitalAccountWorkbenchService(HttpContext context)
    {
        var service = context.RequestServices.GetService<ICapitalAccountWorkbenchService>();
        if (service is not null)
        {
            return service;
        }

        var manualJournalService = ResolveManualJournalEntryWorkbenchService(context);
        return manualJournalService is null
            ? null
            : new CapitalAccountWorkbenchService(
                manualJournalService,
                context.RequestServices.GetService<ReportPackWorkflowService>());
    }

    private static IPrivateCapitalFundEventCommandCenterService? ResolvePrivateCapitalFundEventCommandCenterService(HttpContext context)
    {
        var service = context.RequestServices.GetService<IPrivateCapitalFundEventCommandCenterService>();
        if (service is not null)
        {
            return service;
        }

        var manualJournalService = ResolveManualJournalEntryWorkbenchService(context);
        return manualJournalService is null
            ? null
            : new PrivateCapitalFundEventCommandCenterService(manualJournalService);
    }

    private static IResult ServiceUnavailable()
        => ServiceUnavailable("Ledger book service is not registered.");

    private static IResult ServiceUnavailable(string detail)
        => Results.Problem(detail, statusCode: StatusCodes.Status501NotImplemented);


    private static bool HasAccountingPackageTenantScope(WorkstationTenantContext tenantContext)
        => !string.IsNullOrWhiteSpace(tenantContext.TenantId) &&
           !string.IsNullOrWhiteSpace(tenantContext.CompanyId);

    /// <summary>
    /// Read authority over the governed ledger. <see cref="UserPermission.ViewLedgerReports"/> and
    /// <see cref="UserPermission.ManageLedgerReports"/> are the permissions this surface actually
    /// means; <see cref="UserPermission.ManageDirectLending"/> stays accepted so deployments that
    /// configured it as the de facto fund-accounting grant keep working through the transition.
    /// </summary>
    private static bool HasLedgerReadPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.AdminMaintenance,
            UserPermission.ManageDirectLending,
            UserPermission.ViewLedgerReports,
            UserPermission.ManageLedgerReports);

    private static async Task<CloseWorkflowTenantScope> ResolveCloseWorkflowTenantScopeAsync(
        HttpContext context,
        IAccountingCloseManagementService service,
        Guid workflowId)
    {
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        var plan = await service
            .GetPeriodPlanScopedAsync(workflowId, tenant.TenantId, tenant.CompanyId, context.RequestAborted)
            .ConfigureAwait(false);
        if (!tenant.HasTenantScope ||
            string.IsNullOrWhiteSpace(tenant.TenantId) ||
            string.IsNullOrWhiteSpace(tenant.CompanyId))
        {
            return new CloseWorkflowTenantScope(plan, tenant, IsAccessible: false);
        }

        if (plan is null)
        {
            return new CloseWorkflowTenantScope(plan, tenant, IsAccessible: true);
        }

        if (plan.LedgerBookId is not { } ledgerBookId || ledgerBookId == Guid.Empty)
        {
            return new CloseWorkflowTenantScope(plan, tenant, IsAccessible: false);
        }

        var ledgerBookService = context.RequestServices.GetService<ILedgerBookService>();
        if (ledgerBookService is null)
        {
            return new CloseWorkflowTenantScope(plan, tenant, IsAccessible: false);
        }

        var registry = context.RequestServices.GetService<IFundProfileTenancyRegistry>();
        var guard = context.RequestServices.GetService<IFundProfileTenantGuard>();
        if (registry is null)
        {
            return new CloseWorkflowTenantScope(plan, tenant, IsAccessible: false);
        }

        try
        {
            var book = await ledgerBookService.GetBookAsync(ledgerBookId, context.RequestAborted).ConfigureAwait(false);
            if (book is null ||
                !Guid.TryParse(plan.FundProfileId, out var fundAccountId) ||
                fundAccountId == Guid.Empty ||
                book.FundStructureNodeId != fundAccountId ||
                !string.Equals(book.BaseCurrency, plan.MaterialityPolicy.Currency, StringComparison.OrdinalIgnoreCase))
            {
                return new CloseWorkflowTenantScope(plan, tenant, IsAccessible: false);
            }

            var periods = await ledgerBookService
                .ListPeriodsAsync(new LedgerPeriodQuery(LedgerBookId: ledgerBookId), context.RequestAborted)
                .ConfigureAwait(false);
            var hasPeriodId = Guid.TryParse(plan.PeriodId, out var requestedPeriodId);
            if (!periods.Any(period =>
                    (hasPeriodId && period.PeriodId == requestedPeriodId) ||
                    string.Equals(period.Label, plan.PeriodId, StringComparison.OrdinalIgnoreCase)))
            {
                return new CloseWorkflowTenantScope(plan, tenant, IsAccessible: false);
            }

            // FundProfileId on the close plan identifies the operations fund account. The
            // authoritative tenant owner is the ledger book's fund profile, after proving that the
            // book belongs to that exact fund-account node.
            var owner = await registry.ResolveAsync(book.FundProfileId, context.RequestAborted).ConfigureAwait(false);
            if (owner is null || !CloseWorkflowOwnerMatches(owner, tenant))
            {
                return new CloseWorkflowTenantScope(plan, tenant, IsAccessible: false);
            }

            if (guard is not null)
            {
                var decision = await guard
                    .EvaluateAsync(tenant, book.FundProfileId, context.RequestAborted)
                    .ConfigureAwait(false);
                if (!decision.IsAllowed)
                {
                    return new CloseWorkflowTenantScope(plan, tenant, IsAccessible: false);
                }
            }

            return new CloseWorkflowTenantScope(plan, tenant, IsAccessible: true);
        }
        catch (LedgerBookServiceException)
        {
            return new CloseWorkflowTenantScope(plan, tenant, IsAccessible: false);
        }
    }

    private static bool CloseWorkflowOwnerMatches(
        FundProfileOwnership owner,
        WorkstationTenantContext tenant)
        => owner.IsHeldBy(tenant.TenantId) &&
           !string.IsNullOrWhiteSpace(owner.CompanyId) &&
           !string.IsNullOrWhiteSpace(tenant.CompanyId) &&
           string.Equals(owner.CompanyId.Trim(), tenant.CompanyId.Trim(), StringComparison.OrdinalIgnoreCase);

    private static IResult CloseWorkflowScopeDenied()
        => Results.Problem(
            "The requested close workflow is not accessible to the current tenant and company.",
            statusCode: StatusCodes.Status403Forbidden);

    private sealed record CloseWorkflowTenantScope(
        ClosePeriodPlanDto? Plan,
        WorkstationTenantContext TenantContext,
        bool IsAccessible);

    /// <summary>
    /// Tenant isolation (SEC-005 slice 3) for body-supplied fund scopes on POST read/preview routes the
    /// query-string <see cref="FundProfileScopeEndpointFilters"/> filter cannot see. Returns true (allow)
    /// for a blank fund or an unavailable guard (fail open); denies only a fund the registry positively
    /// attributes to another tenant. Call it after the route's permission check so an unauthorized caller
    /// never receives an ownership verdict.
    /// </summary>
    private static async Task<bool> IsBodyFundScopeAccessibleAsync(
        HttpContext context,
        WorkstationTenantContext tenant,
        string? fundProfileId)
    {
        if (string.IsNullOrWhiteSpace(fundProfileId))
        {
            return true;
        }

        var guard = context.RequestServices.GetService<IFundProfileTenantGuard>();
        if (guard is null)
        {
            return true;
        }

        var decision = await guard.EvaluateAsync(tenant, fundProfileId, context.RequestAborted).ConfigureAwait(false);
        return decision.IsAllowed;
    }

    /// <summary>
    /// Write authority over the governed ledger. Deliberately excludes
    /// <see cref="UserPermission.ViewLedgerReports"/>: reading the trial balance must never confer
    /// the authority to post to it.
    /// </summary>
    private static bool HasLedgerMutationPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.AdminMaintenance,
            UserPermission.ManageDirectLending,
            UserPermission.ManageLedgerReports);

    private static bool TryResolveControllerRole(HttpContext context, out string role)
    {
        if (context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserRoleKey, out var rawRole) &&
            rawRole is UserRole userRole &&
            userRole == UserRole.Controller)
        {
            role = "Controller";
            return true;
        }

        var profile = HttpContextWorkstationTenantContextAccessor.Resolve(context).RoleProfileName;
        if (string.Equals(profile, "Controller", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(profile, "Fund Controller", StringComparison.OrdinalIgnoreCase))
        {
            role = profile!.Trim();
            return true;
        }

        role = string.Empty;
        return false;
    }

    private static bool HasLedgerCertificationPermission(HttpContext context)
        => EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance);

    private static bool CanConfigureCloseTaskApprovalRoles(
        HttpContext context,
        IReadOnlyList<CloseTaskConfigurationDto> taskConfigurations)
        => taskConfigurations.All(configuration =>
            string.IsNullOrWhiteSpace(configuration.RequiredApprovalRole) ||
            HasCloseTaskSignOffRoleAuthority(context, configuration.RequiredApprovalRole));

    private static bool HasCloseTaskSignOffRoleAuthority(HttpContext context, string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        if (HasLedgerCertificationPermission(context))
        {
            return true;
        }

        var normalizedRole = role.Trim();
        if (context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserRoleKey, out var rawRole) &&
            rawRole is UserRole currentRole &&
            string.Equals(currentRole.ToString(), normalizedRole, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserRoleProfileNameKey, out var rawProfile) &&
            rawProfile is string profileName &&
            string.Equals(profileName.Trim(), normalizedRole, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasManualJournalLifecycleActionPermission(
        HttpContext context,
        JournalEntryLifecycleActionDto action)
        => action == JournalEntryLifecycleActionDto.Validate
            ? HasLedgerReadPermission(context)
            : HasLedgerCertificationPermission(context);

    private static bool HasLedgerClosePermission(HttpContext context)
        => HasLedgerMutationPermission(context);

    private static bool TryResolveActor(HttpContext context, out string actor)
        => EndpointAuthorization.TryResolveActor(context, out actor);

    private static string ResolveMutationActor(HttpContext context, string suppliedActor)
        => EndpointAuthorization.TryResolveActor(context, out var actor) && !string.IsNullOrWhiteSpace(actor)
            ? actor
            : suppliedActor;

    private static UpsertChartOfAccountsNodeRequest WithAccessContext(
        UpsertChartOfAccountsNodeRequest request,
        HttpContext context)
    {
        var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        return request with
        {
            Actor = ResolveMutationActor(context, request.Actor),
            TenantId = tenantContext.TenantId,
            CompanyId = tenantContext.CompanyId,
            ReportGroupPrincipalIds = EndpointAuthorization.ResolveReportGroupPrincipalIds(context)
        };
    }

    private static UpsertJournalEntryTemplateRequest WithAccessContext(
        UpsertJournalEntryTemplateRequest request,
        HttpContext context)
    {
        var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        return request with
        {
            Actor = ResolveMutationActor(context, request.Actor),
            TenantId = tenantContext.TenantId,
            CompanyId = tenantContext.CompanyId,
            ReportGroupPrincipalIds = EndpointAuthorization.ResolveReportGroupPrincipalIds(context)
        };
    }

    private static UpsertPostingRuleRequest WithAccessContext(
        UpsertPostingRuleRequest request,
        HttpContext context)
    {
        var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        return request with
        {
            Actor = ResolveMutationActor(context, request.Actor),
            TenantId = tenantContext.TenantId,
            CompanyId = tenantContext.CompanyId,
            ReportGroupPrincipalIds = EndpointAuthorization.ResolveReportGroupPrincipalIds(context)
        };
    }

    private static ApprovePostingRulePromotionRequest WithAccessContext(
        ApprovePostingRulePromotionRequest request,
        HttpContext context)
    {
        var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        return request with
        {
            Actor = ResolveMutationActor(context, request.Actor),
            TenantId = tenantContext.TenantId,
            CompanyId = tenantContext.CompanyId,
            ReportGroupPrincipalIds = EndpointAuthorization.ResolveReportGroupPrincipalIds(context)
        };
    }

    private static UpsertAccountingRuleTestCaseRequest WithAccessContext(
        UpsertAccountingRuleTestCaseRequest request,
        HttpContext context)
    {
        var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        return request with
        {
            Actor = ResolveMutationActor(context, request.Actor),
            TenantId = tenantContext.TenantId,
            CompanyId = tenantContext.CompanyId,
            ReportGroupPrincipalIds = EndpointAuthorization.ResolveReportGroupPrincipalIds(context)
        };
    }

    private static ActivateAccountingConfigurationRequest WithAccessContext(
        ActivateAccountingConfigurationRequest request,
        HttpContext context)
    {
        var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        return request with
        {
            Actor = ResolveMutationActor(context, request.Actor),
            TenantId = tenantContext.TenantId,
            CompanyId = tenantContext.CompanyId,
            ReportGroupPrincipalIds = EndpointAuthorization.ResolveReportGroupPrincipalIds(context)
        };
    }

    private static IResult MapServiceException(LedgerBookServiceException exception)
        => exception switch
        {
            LedgerBookNotFoundException => Results.NotFound(new { error = exception.Message }),
            LedgerBookValidationException or LedgerPeriodTransitionException => Results.BadRequest(new { error = exception.Message }),
            _ => Results.Problem(exception.Message)
        };

    private static LedgerPeriodPnlSummaryDto BuildPnlSummary(LedgerPeriodSummaryDto summary)
    {
        var revenueLines = summary.TrialBalance
            .Where(static row => string.Equals(row.AccountType, "Revenue", StringComparison.Ordinal))
            .ToArray();
        var expenseLines = summary.TrialBalance
            .Where(static row => string.Equals(row.AccountType, "Expense", StringComparison.Ordinal))
            .ToArray();
        var accrualAdjustmentLines = revenueLines
            .Concat(expenseLines)
            .Where(IsAccrualAdjustmentLine)
            .ToArray();
        var realizedRevenue = revenueLines
            .Where(static row => !IsAccrualAdjustmentLine(row))
            .Sum(static row => row.Balance);
        var realizedExpenses = expenseLines
            .Where(static row => !IsAccrualAdjustmentLine(row))
            .Sum(static row => row.Balance);
        var accrualAdjustmentRevenue = accrualAdjustmentLines
            .Where(static row => string.Equals(row.AccountType, "Revenue", StringComparison.Ordinal))
            .Sum(static row => row.Balance);
        var accrualAdjustmentExpenses = accrualAdjustmentLines
            .Where(static row => string.Equals(row.AccountType, "Expense", StringComparison.Ordinal))
            .Sum(static row => row.Balance);

        return new LedgerPeriodPnlSummaryDto(
            summary.PeriodId,
            summary.LedgerBookId,
            summary.FiscalYear,
            summary.PeriodNo,
            summary.Label,
            TotalRevenue: revenueLines.Sum(static row => row.Balance),
            TotalExpenses: expenseLines.Sum(static row => row.Balance),
            summary.NetIncome,
            summary.PeriodOnPeriodVariance,
            summary.OpenBreakCount,
            summary.SignoffStatus,
            summary.CompletedAt,
            revenueLines,
            expenseLines,
            summary.AccountingBasis,
            summary.AccountingPolicyId,
            summary.AccountingPolicyVersion,
            RealizedRevenue: realizedRevenue,
            RealizedExpenses: realizedExpenses,
            RealizedNetIncome: realizedRevenue - realizedExpenses,
            AccrualAdjustmentRevenue: accrualAdjustmentRevenue,
            AccrualAdjustmentExpenses: accrualAdjustmentExpenses,
            AccrualBasisAdjustmentNetImpact: accrualAdjustmentRevenue - accrualAdjustmentExpenses,
            AccrualAdjustmentLines: accrualAdjustmentLines);
    }

    private static bool IsAccrualAdjustmentLine(LedgerPeriodTrialBalanceLineDto row)
        => ContainsAccrualMarker(row.AccountName)
           || ContainsAccrualMarker(row.RuleId)
           || ContainsAccrualMarker(row.RuleVersion)
           || ContainsAccrualMarker(row.SourceEventId);

    private static LedgerTrialBalanceReportDto BuildTrialBalanceReport(LedgerPeriodSummaryDto summary, HttpContext context)
    {
        var signedAtUtc = DateTimeOffset.UtcNow;
        var actor = TryResolveActor(context, out var resolvedActor) ? resolvedActor : "system";
        var lines = summary.TrialBalance
            .Select(static row => row with { Dimensions = CanonicalizeDimensions(row.Dimensions) })
            .OrderBy(static row => row.AccountType, StringComparer.Ordinal)
            .ThenBy(static row => row.AccountName, StringComparer.Ordinal)
            .ThenBy(static row => row.Symbol, StringComparer.Ordinal)
            .ThenBy(static row => row.FinancialAccountId, StringComparer.Ordinal)
            .ThenBy(static row => BuildDimensionSignature(row.Dimensions), StringComparer.Ordinal)
            .ToArray();
        var reportSummary = summary with
        {
            TrialBalance = lines,
            TotalDebits = lines.Sum(static row => row.DebitTotal),
            TotalCredits = lines.Sum(static row => row.CreditTotal),
            NetIncome = CalculateNetIncome(lines)
        };
        var signature = new LedgerReportSignatureDto(
            "SHA256",
            ComputeTrialBalanceReportChecksum(reportSummary, lines, BuildDimensionReportFilter(context.Request.Query)),
            actor,
            signedAtUtc);

        return new LedgerTrialBalanceReportDto(
            reportSummary.PeriodId,
            reportSummary.LedgerBookId,
            reportSummary.FiscalYear,
            reportSummary.PeriodNo,
            reportSummary.Label,
            IsPeriodLocked: true,
            reportSummary.TotalDebits,
            reportSummary.TotalCredits,
            reportSummary.NetIncome,
            reportSummary.PeriodOnPeriodVariance,
            reportSummary.OpenBreakCount,
            reportSummary.SignoffStatus,
            reportSummary.CompletedAt,
            lines,
            signature,
            reportSummary.AccountingBasis,
            reportSummary.AccountingPolicyId,
            reportSummary.AccountingPolicyVersion);
    }

    private static string ComputeTrialBalanceReportChecksum(
        LedgerPeriodSummaryDto summary,
        IReadOnlyList<LedgerPeriodTrialBalanceLineDto> lines,
        LedgerDimensionReportFilter dimensionFilter)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ledger-trial-balance-report-v1");
        builder.AppendLine(CultureInfo.InvariantCulture, $"period-id,{summary.PeriodId:D}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"ledger-book-id,{summary.LedgerBookId:D}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"fiscal-year,{summary.FiscalYear}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"period-no,{summary.PeriodNo}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"label,{EscapeSignatureField(summary.Label)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"total-debits,{FormatSignatureDecimal(summary.TotalDebits)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"total-credits,{FormatSignatureDecimal(summary.TotalCredits)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"net-income,{FormatSignatureDecimal(summary.NetIncome)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"period-variance,{FormatSignatureDecimal(summary.PeriodOnPeriodVariance ?? 0m)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"open-break-count,{summary.OpenBreakCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"accounting-basis,{summary.AccountingBasis}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"accounting-policy-id,{EscapeSignatureField(summary.AccountingPolicyId)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"accounting-policy-version,{EscapeSignatureField(summary.AccountingPolicyVersion)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"dimension-filter,{EscapeSignatureField(BuildDimensionFilterSignature(dimensionFilter))}");
        builder.AppendLine("account-name,account-type,symbol,financial-account-id,debits,credits,balance,entry-count,rule-id,rule-version,source-event-id,source-journal-entry-id,dimensions");

        foreach (var line in lines)
        {
            builder.Append(EscapeSignatureField(line.AccountName));
            builder.Append(',');
            builder.Append(line.AccountType);
            builder.Append(',');
            builder.Append(EscapeSignatureField(line.Symbol ?? string.Empty));
            builder.Append(',');
            builder.Append(EscapeSignatureField(line.FinancialAccountId ?? string.Empty));
            builder.Append(',');
            builder.Append(FormatSignatureDecimal(line.DebitTotal));
            builder.Append(',');
            builder.Append(FormatSignatureDecimal(line.CreditTotal));
            builder.Append(',');
            builder.Append(FormatSignatureDecimal(line.Balance));
            builder.Append(',');
            builder.Append(line.EntryCount);
            builder.Append(',');
            builder.Append(EscapeSignatureField(line.RuleId ?? string.Empty));
            builder.Append(',');
            builder.Append(EscapeSignatureField(line.RuleVersion ?? string.Empty));
            builder.Append(',');
            builder.Append(EscapeSignatureField(line.SourceEventId ?? string.Empty));
            builder.Append(',');
            builder.Append(line.SourceJournalEntryId?.ToString("D") ?? string.Empty);
            builder.Append(',');
            builder.AppendLine(EscapeSignatureField(BuildDimensionSignature(line.Dimensions)));
        }

        return Sha256Digest.ComputeUtf8(builder.ToString());
    }

    private static LedgerLineDimensionSet? ToLineDimensionSet(LedgerDimensionReportFilter filter)
    {
        filter = CanonicalizeFilter(filter);
        if (!filter.HasCriteria)
        {
            return null;
        }

        var instrumentId = Guid.TryParse(filter.InstrumentId, out var parsedInstrumentId)
            ? parsedInstrumentId
            : (Guid?)null;
        var positionId = Guid.TryParse(filter.PositionId, out var parsedPositionId)
            ? parsedPositionId
            : (Guid?)null;
        return new LedgerLineDimensionSet(
            FundId: filter.FundId,
            EntityId: filter.EntityId,
            SleeveId: filter.SleeveId,
            StrategyId: filter.StrategyId,
            InvestorId: filter.InvestorId,
            CapitalAccountId: filter.CapitalAccountId,
            InstrumentId: instrumentId,
            TaxLotId: filter.TaxLotId,
            CostCenterId: filter.CostCenterId,
            CounterpartyId: filter.CounterpartyId,
            ExternalGlDimensions: filter.ExternalGlDimensions,
            OrganizationId: filter.OrganizationId,
            PortfolioId: filter.PortfolioId,
            BookId: filter.BookId,
            AccountId: filter.AccountId,
            CustomerId: filter.CustomerId,
            VendorId: filter.VendorId,
            ProjectId: filter.ProjectId)
        {
            PositionId = positionId
        };
    }

    private static LedgerDimensionSetDto? ToLedgerDimensionSetDto(LedgerDimensionReportFilter filter)
    {
        filter = CanonicalizeFilter(filter);
        if (!filter.HasCriteria)
        {
            return null;
        }

        var instrumentId = Guid.TryParse(filter.InstrumentId, out var parsedInstrumentId)
            ? parsedInstrumentId
            : (Guid?)null;
        var positionId = Guid.TryParse(filter.PositionId, out var parsedPositionId)
            ? parsedPositionId
            : (Guid?)null;
        return new LedgerDimensionSetDto(
            FundId: filter.FundId,
            EntityId: filter.EntityId,
            SleeveId: filter.SleeveId,
            StrategyId: filter.StrategyId,
            InvestorId: filter.InvestorId,
            CapitalAccountId: filter.CapitalAccountId,
            InstrumentId: instrumentId,
            TaxLotId: filter.TaxLotId,
            CostCenterId: filter.CostCenterId,
            CounterpartyId: filter.CounterpartyId,
            ExternalGlDimensions: filter.ExternalGlDimensions,
            OrganizationId: filter.OrganizationId,
            PortfolioId: filter.PortfolioId,
            BookId: filter.BookId,
            AccountId: filter.AccountId,
            CustomerId: filter.CustomerId,
            VendorId: filter.VendorId,
            ProjectId: filter.ProjectId)
        {
            PositionId = positionId
        };
    }

    private static string? GetQueryValue(IQueryCollection query, string key)
        => query.TryGetValue(key, out var value) ? value.ToString() : null;

    private static string? GetFirstQueryValue(IQueryCollection query, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = GetQueryValue(query, key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static LedgerPeriodSummaryDto ApplyDimensionFilter(
        LedgerPeriodSummaryDto summary,
        LedgerDimensionReportFilter filter)
    {
        filter = CanonicalizeFilter(filter);
        if (!filter.HasCriteria)
        {
            return summary;
        }

        var lines = summary.TrialBalance
            .Where(row => MatchesDimensionFilter(row.Dimensions, filter))
            .ToArray();
        return summary with
        {
            TrialBalance = lines,
            TotalDebits = lines.Sum(static row => row.DebitTotal),
            TotalCredits = lines.Sum(static row => row.CreditTotal),
            NetIncome = CalculateNetIncome(lines),
            PeriodOnPeriodVariance = null
        };
    }

    private static IReadOnlyList<LedgerJournalEntryDto> BuildJournalEntryDtos(
        IReadOnlyList<LedgerJournalEntryRecord> entries,
        Func<LedgerJournalEntryRecord, Guid?> resolveLedgerBookId,
        LedgerDimensionReportFilter filter)
    {
        filter = CanonicalizeFilter(filter);
        return entries
            .OrderBy(static entry => entry.GlobalSequence)
            .Select(entry => BuildJournalEntryDto(entry, resolveLedgerBookId(entry), filter))
            .Where(static entry => entry is not null)
            .Select(static entry => entry!)
            .ToArray();
    }

    private static LedgerJournalEntryDto? BuildJournalEntryDto(
        LedgerJournalEntryRecord record,
        Guid? ledgerBookId,
        LedgerDimensionReportFilter filter)
    {
        var entryDimensions = BuildDimensions(record.Entry.Metadata);
        var lines = record.Entry.Lines
            .Select(line => BuildJournalEntryLineDto(
                line,
                BuildDimensions(line.Dimensions)
                ?? BuildDimensions(record.Entry.Metadata, line.EntryId)
                ?? entryDimensions))
            .Where(line => MatchesDimensionFilter(line.Dimensions, filter))
            .ToArray();
        if (filter.HasCriteria && lines.Length == 0)
        {
            return null;
        }

        return new LedgerJournalEntryDto(
            record.Entry.JournalEntryId,
            record.PeriodId,
            ledgerBookId,
            record.AggregateId,
            record.CommandId,
            record.CorrelationId,
            record.GlobalSequence,
            record.CreatedAt,
            record.Entry.Timestamp,
            record.Entry.Description,
            lines.Sum(static line => line.Debit),
            lines.Sum(static line => line.Credit),
            Math.Abs(lines.Sum(static line => line.Debit) - lines.Sum(static line => line.Credit)) <= LedgerToleranceConstants.Balance,
            lines,
            record.AccountingBasis,
            record.AccountingPolicyId,
            record.AccountingPolicyVersion,
            record.RuleId,
            record.RuleVersion,
            record.SourceEventId,
            record.SourceJournalEntryId,
            record.PostingKind,
            record.AdjustmentApproval);
    }

    private static LedgerJournalEntryLineDto BuildJournalEntryLineDto(
        LedgerEntry line,
        LedgerDimensionSetDto? dimensions)
        => new(
            line.EntryId,
            line.JournalEntryId,
            line.Timestamp,
            line.Account.Name,
            line.Account.AccountType.ToString(),
            line.Account.Symbol,
            line.Account.FinancialAccountId,
            line.Debit,
            line.Credit,
            line.Description,
            dimensions);

    private static LedgerDimensionSetDto? BuildDimensions(JournalEntryMetadata metadata)
    {
        var tags = metadata.Tags;
        var positionId = Guid.TryParse(LedgerDimensionTags.FirstTag(tags, "positionId"), out var parsedPositionId)
            ? parsedPositionId
            : (Guid?)null;
        var dimensions = new LedgerDimensionSetDto(
            FundId: LedgerDimensionTags.FirstTag(tags, "fundId", "fundProfileId"),
            EntityId: LedgerDimensionTags.FirstTag(tags, "entityId", "legalEntityId"),
            SleeveId: LedgerDimensionTags.FirstTag(tags, "sleeveId"),
            StrategyId: metadata.StrategyId ?? LedgerDimensionTags.FirstTag(tags, "strategyId"),
            InvestorId: metadata.InvestorId ?? LedgerDimensionTags.FirstTag(tags, "investorId"),
            CapitalAccountId: metadata.CapitalAccountId ?? LedgerDimensionTags.FirstTag(tags, "capitalAccountId"),
            InstrumentId: metadata.SecurityId,
            TaxLotId: LedgerDimensionTags.FirstTag(tags, "taxLotId", "lotId"),
            CostCenterId: LedgerDimensionTags.FirstTag(tags, "costCenterId"),
            CounterpartyId: metadata.CounterpartyAccountId ?? LedgerDimensionTags.FirstTag(tags, "counterpartyId", "counterpartyAccountId"),
            ExternalGlDimensions: LedgerDimensionTags.ExtractExternalGlDimensions(tags),
            OrganizationId: LedgerDimensionTags.FirstTag(tags, "organizationId"),
            PortfolioId: LedgerDimensionTags.FirstTag(tags, "portfolioId"),
            BookId: metadata.LedgerBook ?? LedgerDimensionTags.FirstTag(tags, "bookId"),
            AccountId: metadata.FinancialAccountId ?? LedgerDimensionTags.FirstTag(tags, "accountId"),
            CustomerId: LedgerDimensionTags.FirstTag(tags, "customerId"),
            VendorId: LedgerDimensionTags.FirstTag(tags, "vendorId"),
            ProjectId: metadata.ProjectId ?? LedgerDimensionTags.FirstTag(tags, "projectId"))
        {
            PositionId = positionId
        };

        return CanonicalizeDimensions(dimensions);
    }

    private static LedgerDimensionSetDto? BuildDimensions(LedgerLineDimensionSet? dimensions)
    {
        if (dimensions is null)
        {
            return null;
        }

        var result = new LedgerDimensionSetDto(
            FundId: dimensions.FundId,
            EntityId: dimensions.EntityId,
            SleeveId: dimensions.SleeveId,
            StrategyId: dimensions.StrategyId,
            InvestorId: dimensions.InvestorId,
            CapitalAccountId: dimensions.CapitalAccountId,
            InstrumentId: dimensions.InstrumentId,
            TaxLotId: dimensions.TaxLotId,
            CostCenterId: dimensions.CostCenterId,
            CounterpartyId: dimensions.CounterpartyId,
            ExternalGlDimensions: dimensions.ExternalGlDimensions,
            OrganizationId: dimensions.OrganizationId,
            PortfolioId: dimensions.PortfolioId,
            BookId: dimensions.BookId,
            AccountId: dimensions.AccountId,
            CustomerId: dimensions.CustomerId,
            VendorId: dimensions.VendorId,
            ProjectId: dimensions.ProjectId)
        {
            PositionId = dimensions.PositionId
        };

        return CanonicalizeDimensions(result);
    }

    private static LedgerDimensionSetDto? BuildDimensions(JournalEntryMetadata metadata, Guid lineEntryId)
    {
        var tags = metadata.Tags;
        var prefix = $"lineDimensions.{lineEntryId:N}.";
        if (tags is null || tags.Keys.All(key => !key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var positionId = Guid.TryParse(LedgerDimensionTags.FirstTag(tags, prefix + "positionId"), out var parsedPositionId)
            ? parsedPositionId
            : (Guid?)null;
        var dimensions = new LedgerDimensionSetDto(
            FundId: LedgerDimensionTags.FirstTag(tags, prefix + "fundId"),
            EntityId: LedgerDimensionTags.FirstTag(tags, prefix + "entityId"),
            SleeveId: LedgerDimensionTags.FirstTag(tags, prefix + "sleeveId"),
            StrategyId: LedgerDimensionTags.FirstTag(tags, prefix + "strategyId"),
            InvestorId: LedgerDimensionTags.FirstTag(tags, prefix + "investorId"),
            CapitalAccountId: LedgerDimensionTags.FirstTag(tags, prefix + "capitalAccountId"),
            InstrumentId: Guid.TryParse(LedgerDimensionTags.FirstTag(tags, prefix + "instrumentId"), out var instrumentId) ? instrumentId : null,
            TaxLotId: LedgerDimensionTags.FirstTag(tags, prefix + "taxLotId"),
            CostCenterId: LedgerDimensionTags.FirstTag(tags, prefix + "costCenterId"),
            CounterpartyId: LedgerDimensionTags.FirstTag(tags, prefix + "counterpartyId"),
            ExternalGlDimensions: LedgerDimensionTags.ExtractExternalGlDimensions(tags, prefix),
            OrganizationId: LedgerDimensionTags.FirstTag(tags, prefix + "organizationId"),
            PortfolioId: LedgerDimensionTags.FirstTag(tags, prefix + "portfolioId"),
            BookId: LedgerDimensionTags.FirstTag(tags, prefix + "bookId"),
            AccountId: LedgerDimensionTags.FirstTag(tags, prefix + "accountId"),
            CustomerId: LedgerDimensionTags.FirstTag(tags, prefix + "customerId"),
            VendorId: LedgerDimensionTags.FirstTag(tags, prefix + "vendorId"),
            ProjectId: LedgerDimensionTags.FirstTag(tags, prefix + "projectId"))
        {
            PositionId = positionId
        };

        return CanonicalizeDimensions(dimensions);
    }

    private static decimal CalculateNetIncome(IReadOnlyList<LedgerPeriodTrialBalanceLineDto> lines)
        => lines.Sum(static row =>
            row.AccountType switch
            {
                "Revenue" => row.Balance,
                "Expense" => -row.Balance,
                _ => 0m
            });

    private static bool MatchesDimensionFilter(
        LedgerDimensionSetDto? dimensions,
        LedgerDimensionReportFilter filter)
    {
        filter = CanonicalizeFilter(filter);
        if (!filter.HasCriteria)
        {
            return true;
        }

        dimensions = CanonicalizeDimensions(dimensions);
        if (dimensions is null)
        {
            return false;
        }

        return Matches(filter.FundId, dimensions.FundId)
               && Matches(filter.EntityId, dimensions.EntityId)
               && Matches(filter.SleeveId, dimensions.SleeveId)
               && Matches(filter.StrategyId, dimensions.StrategyId)
               && Matches(filter.InvestorId, dimensions.InvestorId)
               && Matches(filter.CapitalAccountId, dimensions.CapitalAccountId)
               && Matches(filter.InstrumentId, dimensions.InstrumentId?.ToString("D"))
               && Matches(filter.PositionId, dimensions.PositionId?.ToString("D"))
               && Matches(filter.TaxLotId, dimensions.TaxLotId)
               && Matches(filter.CostCenterId, dimensions.CostCenterId)
               && Matches(filter.CounterpartyId, dimensions.CounterpartyId)
               && Matches(filter.OrganizationId, dimensions.OrganizationId)
               && Matches(filter.PortfolioId, dimensions.PortfolioId)
               && Matches(filter.BookId, dimensions.BookId)
               && Matches(filter.AccountId, dimensions.AccountId)
               && Matches(filter.CustomerId, dimensions.CustomerId)
               && Matches(filter.VendorId, dimensions.VendorId)
               && Matches(filter.ProjectId, dimensions.ProjectId)
               && MatchesExternalGlDimensions(filter.ExternalGlDimensions, dimensions.ExternalGlDimensions);
    }

    private static bool Matches(string? expected, string? actual)
        => expected is null || string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesExternalGlDimensions(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual)
    {
        foreach (var pair in expected)
        {
            if (!actual.TryGetValue(pair.Key, out var actualValue) ||
                !string.Equals(pair.Value, actualValue, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatSignatureDecimal(decimal value)
        => value.ToString("0.############################", CultureInfo.InvariantCulture);

    private static string EscapeSignatureField(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static bool IsValidDateRange(DateOnly? startDate, DateOnly? endDate)
        => !startDate.HasValue || !endDate.HasValue || startDate.Value <= endDate.Value;

    private static LedgerDimensionReportFilter CanonicalizeFilter(LedgerDimensionReportFilter filter)
        => new(
            FundId: NormalizeOptional(filter.FundId),
            EntityId: NormalizeOptional(filter.EntityId),
            SleeveId: NormalizeOptional(filter.SleeveId),
            StrategyId: NormalizeOptional(filter.StrategyId),
            InvestorId: NormalizeOptional(filter.InvestorId),
            CapitalAccountId: NormalizeOptional(filter.CapitalAccountId),
            InstrumentId: NormalizeOptional(filter.InstrumentId),
            PositionId: NormalizeOptional(filter.PositionId),
            TaxLotId: NormalizeOptional(filter.TaxLotId),
            CostCenterId: NormalizeOptional(filter.CostCenterId),
            CounterpartyId: NormalizeOptional(filter.CounterpartyId),
            OrganizationId: NormalizeOptional(filter.OrganizationId),
            PortfolioId: NormalizeOptional(filter.PortfolioId),
            BookId: NormalizeOptional(filter.BookId),
            AccountId: NormalizeOptional(filter.AccountId),
            CustomerId: NormalizeOptional(filter.CustomerId),
            VendorId: NormalizeOptional(filter.VendorId),
            ProjectId: NormalizeOptional(filter.ProjectId),
            ExternalGlDimensions: NormalizeExternalGlDimensions(filter.ExternalGlDimensions));

    private static LedgerDimensionSetDto? CanonicalizeDimensions(LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
        {
            return null;
        }

        var canonical = new LedgerDimensionSetDto(
            FundId: NormalizeOptional(dimensions.FundId),
            EntityId: NormalizeOptional(dimensions.EntityId),
            SleeveId: NormalizeOptional(dimensions.SleeveId),
            StrategyId: NormalizeOptional(dimensions.StrategyId),
            InvestorId: NormalizeOptional(dimensions.InvestorId),
            CapitalAccountId: NormalizeOptional(dimensions.CapitalAccountId),
            InstrumentId: dimensions.InstrumentId,
            TaxLotId: NormalizeOptional(dimensions.TaxLotId),
            CostCenterId: NormalizeOptional(dimensions.CostCenterId),
            CounterpartyId: NormalizeOptional(dimensions.CounterpartyId),
            ExternalGlDimensions: NormalizeExternalGlDimensions(dimensions.ExternalGlDimensions),
            OrganizationId: NormalizeOptional(dimensions.OrganizationId),
            PortfolioId: NormalizeOptional(dimensions.PortfolioId),
            BookId: NormalizeOptional(dimensions.BookId),
            AccountId: NormalizeOptional(dimensions.AccountId),
            CustomerId: NormalizeOptional(dimensions.CustomerId),
            VendorId: NormalizeOptional(dimensions.VendorId),
            ProjectId: NormalizeOptional(dimensions.ProjectId))
        {
            PositionId = dimensions.PositionId
        };

        return LedgerDimensionTags.HasAnyDimension(canonical) ? canonical : null;
    }

}
