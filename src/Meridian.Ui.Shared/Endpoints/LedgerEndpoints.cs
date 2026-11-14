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
        MapDisposalTaxEndpoints(app, jsonOptions);
        MapConsolidationEndpoints(app, jsonOptions);
        MapConsolidationFxEndpoints(app, jsonOptions);
        MapConsolidationTransitionEndpoints(app, jsonOptions);
        MapIntercompanyAccountBindingEndpoints(app, jsonOptions);
        MapLegacyIntercompanyAttributionEndpoints(app, jsonOptions);
        app.MapIntercompanyObligationEndpoints(jsonOptions);
        MapEntityControlEndpoints(app, jsonOptions);
        MapAccountOversightEndpoints(app, jsonOptions);
        app.MapLedgerBondWorkflowEndpoints();
        MapPrivateCapitalEndpoints(app, jsonOptions);
        MapPostedPeriodComparisonEndpoints(app, jsonOptions);
        MapCloseSnapshotEndpoints(app);
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
            // Served in the canonical order rather than the store's: the first book is the scope a
            // freshly opened surface takes. LedgerBookOrdering says why clients cannot re-derive it.
            return Results.Json(LedgerBookOrdering.Sort(books), jsonOptions);
        })
        .WithName("ListLedgerBooks").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
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
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .Produces<IReadOnlyList<LedgerPeriodDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapPost(UiApiRoutes.LedgerPeriods, async (CreateLedgerPeriodRequest request, HttpContext context) =>
        {
            if (!TryGetLedgerCloseActor(context, out var actor))
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
                var period = await service.CreatePeriodAsync(request with { CreatedBy = actor }, context.RequestAborted).ConfigureAwait(false);
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
                            ClosedBy = actor,
                            ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin)
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

        MapAccountRunningBalanceEndpoint(app, jsonOptions);
        MapLedgerJournalExportEndpoints(app);
        MapLedgerCompletenessEndpoints(app);

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
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
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
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .Produces<LedgerCrossPeriodPnlReportDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented);

        MapAccountingConfigurationEndpoints(app, jsonOptions);
        MapClosePreparationEndpoints(app, jsonOptions);

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
                        request with { Actor = actor, ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin) },
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
                        request with { RequestedBy = actor, ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin) },
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

                var adjustment = scope.Plan.LateAdjustments.FirstOrDefault(row =>
                    string.Equals(row.RequestId, request.RequestId?.Trim(), StringComparison.OrdinalIgnoreCase));
                var reviewRole = adjustment?.MaterialityPolicy.ReviewRole ?? scope.Plan.MaterialityPolicy.ReviewRole;
                if (!TryResolveActor(context, out var actor) || string.IsNullOrWhiteSpace(reviewRole) ||
                    !EndpointAuthorization.ResolveReportGroupPrincipalIds(context)
                        .Contains(reviewRole.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    return EndpointHelpers.Forbidden();
                }

                var result = await service
                    .ReviewLateAdjustmentScopedAsync(
                        request with { Actor = actor, ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin) },
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
                        request with { Actor = actor, ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin) },
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
                        request with { Actor = actor, ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin) },
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

        app.MapPost(UiApiRoutes.LedgerCloseManagementPeriodLock, (
            LockClosePeriodRequestDto request,
            HttpContext context) => ExecuteClosePeriodLockAsync(request, context, jsonOptions))
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
                        new ReopenClosePeriodRequestDto(
                            WorkflowId: request.WorkflowId,
                            ExpectedWorkflowVersion: request.ExpectedWorkflowVersion,
                            Actor: actor,
                            Role: controllerRole,
                            Rationale: request.Rationale,
                            IncidentId: request.IncidentId,
                            Justification: request.Justification,
                            ApprovalReference: request.ApprovalReference,
                            ImpactSummary: request.ImpactSummary,
                            EvidenceLinks: request.EvidenceLinks,
                            CorrelationId: request.CorrelationId,
                            ActionOrigin: EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin)),
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
                        CompanyId = tenantContext.CompanyId,
                        ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin)
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
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
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
        .WithName("GetManualJournalEntryWorkbench").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
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
        .WithName("GetLedgerPrivateCapitalActivity").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
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
        .WithName("GetLedgerPrivateCapitalFundEventRecord").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
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
        .WithName("GetLedgerPrivateCapitalFundEventCommandCenter").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
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
        .WithName("GetLedgerPrivateCapitalCapitalAccountSubledger").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
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
        .WithName("GetLedgerPrivateCapitalReportOutput").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
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
        .WithName("GetLedgerPrivateCapitalCapitalAccountWorkbench").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
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
                    ReportGroupPrincipalIds = EndpointAuthorization.ResolveReportGroupPrincipalIds(context),
                    ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin)
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
        MapOpenLotBackfillEndpoints(app, jsonOptions);

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
                    ReportGroupPrincipalIds = EndpointAuthorization.ResolveReportGroupPrincipalIds(context),
                    ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin)
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
            var action = ResolveAuthorizedManualJournalLifecycleAction(context, request.Action);
            if (action is null ||
                !EndpointAuthorization.TryResolveActor(context, out var actor) || string.IsNullOrWhiteSpace(actor))
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
                    .ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
                        JournalEntryId: request.JournalEntryId,
                        FundProfileId: request.FundProfileId,
                        Action: action.Value,
                        Actor: actor,
                        Version: request.Version,
                        Notes: request.Notes,
                        CorrelationId: request.CorrelationId,
                        EvidenceLinks: request.EvidenceLinks,
                        ActionOrigin: EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin),
                        PeriodIsLocked: request.PeriodIsLocked,
                        RebookLines: request.RebookLines,
                        LedgerBookId: request.LedgerBookId,
                        TenantId: tenantContext.TenantId,
                        CompanyId: tenantContext.CompanyId,
                        ReportGroupPrincipalIds: EndpointAuthorization.ResolveReportGroupPrincipalIds(context))
                    {
                        AuthenticatedActorAssertion = request.Actor ?? string.Empty
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
        .AddEndpointFilter(async (invocation, next) =>
        {
            try
            { return await next(invocation).ConfigureAwait(false); }
            catch (LedgerValidationException ex)
            { return Results.Conflict(new { error = ex.Message }); }
            catch (Npgsql.PostgresException ex) when (ex.SqlState is "40001" or "40P01" or "23505")
            {
                return Results.Conflict(new { error = "Posting authority changed or a retained financial identity is already in use. Reload retained records and review the current inputs before retrying." });
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
}
