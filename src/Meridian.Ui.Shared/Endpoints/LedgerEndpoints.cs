using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Api;
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
        .WithName("ListLedgerBooks")
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
        .WithName("GetLedgerBook")
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
        .WithName("CreateLedgerBook")
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
        .WithName("AssessLedgerBookRollout")
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
        .WithName("ListLedgerPeriods")
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
        .WithName("CreateLedgerPeriod")
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
        .WithName("CloseLedgerPeriod")
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
        .WithName("GetLedgerPeriodJournalEntries")
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
        .WithName("GetLedgerAggregateJournalEntries")
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
        .WithName("GetLedgerPeriodTrialBalance")
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
        .WithName("GetLedgerPeriodTrialBalanceReport")
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
        .WithName("GetLedgerPeriodPnlSummary")
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
        .WithName("GetLedgerCrossPeriodTrialBalanceReport")
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
        .WithName("GetLedgerCrossPeriodPnlReport")
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
        .Produces<LedgerCrossPeriodPnlReportDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapGet(UiApiRoutes.LedgerAccountingConfiguration, async (
            string? fundProfileId,
            Guid? ledgerBookId,
            HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingConfigurationService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var workspace = await service
                .GetWorkspaceAsync(fundProfileId, ledgerBookId, context.RequestAborted, tenantContext.TenantId, tenantContext.CompanyId)
                .ConfigureAwait(false);
            return Results.Json(workspace, jsonOptions);
        })
        .WithName("GetAccountingConfiguration")
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
        .Produces<AccountingConfigurationWorkspaceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationChart, async (UpsertChartOfAccountsNodeRequest request, HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingConfigurationService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var result = await service.UpsertChartNodeAsync(WithAccessContext(request, context), context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("UpsertAccountingConfigurationChartNode")
        .Produces<AccountingConfigurationWorkspaceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationTemplates, async (UpsertJournalEntryTemplateRequest request, HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingConfigurationService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var result = await service.UpsertTemplateAsync(WithAccessContext(request, context), context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("UpsertAccountingConfigurationTemplate")
        .Produces<AccountingConfigurationWorkspaceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationPostingRules, async (UpsertPostingRuleRequest request, HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingConfigurationService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var result = await service.UpsertPostingRuleAsync(WithAccessContext(request, context), context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("UpsertAccountingConfigurationPostingRule")
        .Produces<AccountingConfigurationWorkspaceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationPostingRulePromotionApprovals, async (ApprovePostingRulePromotionRequest request, HttpContext context) =>
        {
            if (!HasLedgerCertificationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingConfigurationService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var result = await service.ApprovePostingRulePromotionAsync(WithAccessContext(request, context), context.RequestAborted).ConfigureAwait(false);
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
        .WithName("ApproveAccountingConfigurationPostingRulePromotion")
        .Produces<AccountingConfigurationWorkspaceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationPostingRuleTestCases, async (UpsertAccountingRuleTestCaseRequest request, HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingConfigurationService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var result = await service.UpsertRuleTestCaseAsync(WithAccessContext(request, context), context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("UpsertAccountingConfigurationPostingRuleTestCase")
        .Produces<AccountingConfigurationWorkspaceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationPreview, async (PreviewJournalTemplateRequest request, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingConfigurationService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (!await IsBodyFundScopeAccessibleAsync(context, tenantContext, request.FundProfileId).ConfigureAwait(false))
            {
                return EndpointHelpers.Forbidden();
            }

            var result = await service.PreviewTemplateAsync(request with
            {
                Actor = ResolveMutationActor(context, request.Actor),
                TenantId = tenantContext.TenantId,
                CompanyId = tenantContext.CompanyId
            }, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("PreviewAccountingConfigurationTemplate")
        .Produces<AccountingJournalTemplatePreviewDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        // SEC-005 slice 4c-iii: body-scoped fund EVALUATION (read-permission POST). The fund-scope read
        // gate above is fail-open; this adds the write/evaluate tenant gate so enforcement fails closed.
        .RequireFundScopedWriteTenant();

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationPostingRuleDryRun, async (RuleDryRunRequestDto request, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingConfigurationService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                if (!await IsBodyFundScopeAccessibleAsync(context, tenantContext, request.FundProfileId).ConfigureAwait(false))
                {
                    return EndpointHelpers.Forbidden();
                }

                var result = await service.DryRunPostingRuleAsync(request with
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
        .WithName("DryRunAccountingConfigurationPostingRule")
        .Produces<RuleDryRunResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant();

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationPostingRuleCandidates, async (PostingRuleJournalCandidateRequestDto request, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingPostingCandidateService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                if (!await IsBodyFundScopeAccessibleAsync(context, tenantContext, request.FundProfileId).ConfigureAwait(false))
                {
                    return EndpointHelpers.Forbidden();
                }

                var result = await service
                    .BuildCandidateAsync(request with
                    {
                        Actor = ResolveMutationActor(context, request.Actor),
                        TenantId = tenantContext.TenantId,
                        CompanyId = tenantContext.CompanyId
                    }, context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("BuildAccountingConfigurationPostingRuleCandidate")
        .Produces<PostingRuleJournalCandidateResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant();

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationPostingRuleCandidatePosts, async (PostPostingRuleJournalCandidateRequestDto request, HttpContext context) =>
        {
            if (!HasLedgerCertificationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingPostingCandidatePostService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var result = await service
                    .PostCandidateAsync(request with
                    {
                        Actor = ResolveMutationActor(context, request.Actor),
                        TenantId = tenantContext.TenantId,
                        CompanyId = tenantContext.CompanyId
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
        .WithName("PostAccountingConfigurationPostingRuleCandidate")
        .Produces<PostedPostingRuleJournalCandidateResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationPostingRuleProjectionSets, async (AccountingBasisProjectionSetRequestDto request, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingBasisProjectionSetService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                if (!await IsBodyFundScopeAccessibleAsync(context, tenantContext, request.FundProfileId).ConfigureAwait(false))
                {
                    return EndpointHelpers.Forbidden();
                }

                var result = await service
                    .BuildProjectionSetAsync(request with
                    {
                        Actor = ResolveMutationActor(context, request.Actor),
                        TenantId = tenantContext.TenantId,
                        CompanyId = tenantContext.CompanyId
                    }, context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("BuildAccountingConfigurationPostingRuleProjectionSet")
        .Produces<AccountingBasisProjectionSetDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant();

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationPostingRuleTests, async (ExecuteAccountingRuleTestCasesRequestDto request, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingConfigurationService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                if (!await IsBodyFundScopeAccessibleAsync(context, tenantContext, request.FundProfileId).ConfigureAwait(false))
                {
                    return EndpointHelpers.Forbidden();
                }

                var result = await service.ExecuteRuleTestCasesAsync(request with
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
        .WithName("ExecuteAccountingConfigurationPostingRuleTests")
        .Produces<AccountingRuleTestSuiteResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant();

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationActivate, async (ActivateAccountingConfigurationRequest request, HttpContext context) =>
        {
            if (!HasLedgerCertificationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingConfigurationService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var result = await service.ActivateAsync(WithAccessContext(request, context), context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("ActivateAccountingConfiguration")
        .Produces<AccountingConfigurationWorkspaceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapGet(UiApiRoutes.LedgerAccountingConfigurationAudit, async (
            string? fundProfileId,
            Guid? ledgerBookId,
            HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingConfigurationService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var audit = await service
                .ListAuditAsync(fundProfileId, ledgerBookId, context.RequestAborted, tenantContext.TenantId, tenantContext.CompanyId)
                .ConfigureAwait(false);
            return Results.Json(audit, jsonOptions);
        })
        .WithName("ListAccountingConfigurationAudit")
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
        .Produces<IReadOnlyList<AccountingActionAuditEventDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented);

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

            var result = await service.GetPeriodPlanAsync(workflowId, context.RequestAborted).ConfigureAwait(false);
            return result is null
                ? Results.NotFound(new { error = $"Close workflow '{workflowId}' was not found." })
                : Results.Json(result, jsonOptions);
        })
        .WithName("GetLedgerCloseManagementPeriodPlan")
        .Produces<ClosePeriodPlanDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapPost(UiApiRoutes.LedgerCloseManagementPeriodPlanConfiguration, async (
            UpsertClosePeriodPlanConfigurationRequestDto request,
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
                var actor = ResolveMutationActor(context, request.Actor ?? string.Empty);
                var result = await service
                    .ConfigurePeriodPlanAsync(
                        request with { Actor = actor },
                        actor,
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
        .WithName("ConfigureLedgerCloseManagementPeriodPlan")
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
                var actor = ResolveMutationActor(context, request.RequestedBy);
                var result = await service
                    .RequestLateAdjustmentAsync(
                        request with { RequestedBy = actor },
                        actor,
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
        .WithName("CreateLedgerCloseManagementLateAdjustment")
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
                var actor = ResolveMutationActor(context, request.Actor);
                var result = await service
                    .ReviewLateAdjustmentAsync(
                        request with { Actor = actor },
                        actor,
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
        .WithName("ReviewLedgerCloseManagementLateAdjustment")
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
                var actor = ResolveMutationActor(context, request.Actor);
                var result = await service
                    .SignOffCloseTaskAsync(
                        request with { Actor = actor },
                        actor,
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
        .WithName("SignOffLedgerCloseManagementTask")
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
                var actor = ResolveMutationActor(context, request.Actor);
                var result = await service
                    .ReviewCloseEvidenceAsync(
                        request with { Actor = actor },
                        actor,
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
        .WithName("ReviewLedgerCloseManagementEvidence")
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

            var service = ResolveAccountingCloseManagementService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var actor = ResolveMutationActor(context, request.Actor);
                var result = await service
                    .LockClosePeriodAsync(
                        request with
                        {
                            Actor = actor,
                            ActionOrigin = OperationsActionOriginDto.HumanOperator
                        },
                        actor,
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
        .WithName("LockLedgerCloseManagementPeriod")
        .Produces<ClosePeriodLockResultDto>(StatusCodes.Status200OK)
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
                if (!await IsBodyFundScopeAccessibleAsync(context, tenantContext, request.FundProfileId).ConfigureAwait(false))
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
        .WithName("BuildLedgerAccountingReportPackage")
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
        .WithName("CertifyLedgerAccountingReportPackage")
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
        .WithName("ListLedgerAccountingReportPackages")
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
        .WithName("GetLedgerAccountingReportPackageExport")
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
        .WithName("GetManualJournalEntryWorkbench")
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
        .WithName("GetLedgerPrivateCapitalActivity")
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
        .WithName("GetLedgerPrivateCapitalFundEventRecord")
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
        .WithName("GetLedgerPrivateCapitalFundEventCommandCenter")
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
        .WithName("GetLedgerPrivateCapitalCapitalAccountSubledger")
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
        .WithName("GetLedgerPrivateCapitalReportOutput")
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
        .WithName("GetLedgerPrivateCapitalCapitalAccountWorkbench")
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
        .WithName("SaveManualJournalEntryDraft")
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
        .WithName("ValidateManualJournalEntryDraft")
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
        .WithName("SubmitManualJournalEntryApproval")
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
        .WithName("AttachManualJournalEntryEvidence")
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
        .WithName("ApplyManualJournalEntryLifecycleAction")
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
                context.RequestServices.GetService<ReportPackWorkflowService>());
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
        => Results.Problem("Ledger book service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

    private static bool HasLedgerReadPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.AdminMaintenance,
            UserPermission.ManageDirectLending);

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

    private static bool HasLedgerMutationPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.AdminMaintenance,
            UserPermission.ManageDirectLending);

    private static bool HasLedgerCertificationPermission(HttpContext context)
        => EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance);

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

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static string BuildDimensionFilterSignature(LedgerDimensionReportFilter filter)
    {
        filter = CanonicalizeFilter(filter);
        if (!filter.HasCriteria)
        {
            return string.Empty;
        }

        var externalGl = filter.ExternalGlDimensions
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => $"{pair.Key.Trim()}={pair.Value.Trim()}");

        var signature = string.Join(
            "|",
            filter.FundId ?? string.Empty,
            filter.EntityId ?? string.Empty,
            filter.SleeveId ?? string.Empty,
            filter.StrategyId ?? string.Empty,
            filter.InvestorId ?? string.Empty,
            filter.CapitalAccountId ?? string.Empty,
            filter.InstrumentId ?? string.Empty,
            filter.TaxLotId ?? string.Empty,
            filter.CostCenterId ?? string.Empty,
            filter.CounterpartyId ?? string.Empty,
            filter.OrganizationId ?? string.Empty,
            filter.PortfolioId ?? string.Empty,
            filter.BookId ?? string.Empty,
            filter.AccountId ?? string.Empty,
            filter.CustomerId ?? string.Empty,
            filter.VendorId ?? string.Empty,
            filter.ProjectId ?? string.Empty,
            string.Join(";", externalGl));

        return filter.PositionId is not null
            ? $"{signature}|positionId={filter.PositionId}"
            : signature;
    }

    private static string BuildDimensionSignature(LedgerDimensionSetDto? dimensions)
    {
        dimensions = CanonicalizeDimensions(dimensions);
        if (dimensions is null)
        {
            return string.Empty;
        }

        var externalGl = dimensions.ExternalGlDimensions
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => $"{pair.Key.Trim()}={pair.Value.Trim()}");
        var signature = string.Join(
            "|",
            dimensions.FundId ?? string.Empty,
            dimensions.EntityId ?? string.Empty,
            dimensions.SleeveId ?? string.Empty,
            dimensions.StrategyId ?? string.Empty,
            dimensions.InvestorId ?? string.Empty,
            dimensions.CapitalAccountId ?? string.Empty,
            dimensions.InstrumentId?.ToString("D") ?? string.Empty,
            dimensions.TaxLotId ?? string.Empty,
            dimensions.CostCenterId ?? string.Empty,
            dimensions.CounterpartyId ?? string.Empty,
            dimensions.OrganizationId ?? string.Empty,
            dimensions.PortfolioId ?? string.Empty,
            dimensions.BookId ?? string.Empty,
            dimensions.AccountId ?? string.Empty,
            dimensions.CustomerId ?? string.Empty,
            dimensions.VendorId ?? string.Empty,
            dimensions.ProjectId ?? string.Empty,
            string.Join(";", externalGl));

        return dimensions.PositionId.HasValue
            ? $"{signature}|positionId={dimensions.PositionId.Value:D}"
            : signature;
    }

    private static LedgerDimensionReportFilter BuildDimensionReportFilter(IQueryCollection query)
    {
        var externalGlDimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query)
        {
            const string prefix = "externalGl.";
            if (!pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var dimensionName = NormalizeOptional(pair.Key[prefix.Length..]);
            var dimensionValue = NormalizeOptional(pair.Value.ToString());
            if (dimensionName is not null && dimensionValue is not null)
            {
                externalGlDimensions[dimensionName] = dimensionValue;
            }
        }

        var externalGlDimensionKey = NormalizeOptional(GetQueryValue(query, "externalGlDimensionKey"));
        var externalGlDimensionValue = NormalizeOptional(GetQueryValue(query, "externalGlDimensionValue"));
        if (externalGlDimensionKey is not null && externalGlDimensionValue is not null)
        {
            externalGlDimensions[externalGlDimensionKey] = externalGlDimensionValue;
        }

        return CanonicalizeFilter(new LedgerDimensionReportFilter(
            FundId: NormalizeOptional(GetFirstQueryValue(query, "dimensionFundId", "fundId", "fundProfileId")),
            EntityId: NormalizeOptional(GetFirstQueryValue(query, "dimensionEntityId", "entityId")),
            SleeveId: NormalizeOptional(GetFirstQueryValue(query, "dimensionSleeveId", "sleeveId")),
            StrategyId: NormalizeOptional(GetFirstQueryValue(query, "dimensionStrategyId", "strategyId")),
            InvestorId: NormalizeOptional(GetFirstQueryValue(query, "dimensionInvestorId", "investorId")),
            CapitalAccountId: NormalizeOptional(GetFirstQueryValue(query, "dimensionCapitalAccountId", "capitalAccountId")),
            InstrumentId: NormalizeOptional(GetFirstQueryValue(query, "dimensionInstrumentId", "instrumentId")),
            PositionId: NormalizeOptional(GetFirstQueryValue(query, "dimensionPositionId", "positionId")),
            TaxLotId: NormalizeOptional(GetFirstQueryValue(query, "dimensionTaxLotId", "taxLotId")),
            CostCenterId: NormalizeOptional(GetFirstQueryValue(query, "dimensionCostCenterId", "costCenterId")),
            CounterpartyId: NormalizeOptional(GetFirstQueryValue(query, "dimensionCounterpartyId", "counterpartyId")),
            OrganizationId: NormalizeOptional(GetFirstQueryValue(query, "dimensionOrganizationId", "organizationId")),
            PortfolioId: NormalizeOptional(GetFirstQueryValue(query, "dimensionPortfolioId", "portfolioId")),
            BookId: NormalizeOptional(GetFirstQueryValue(query, "bookId", "ledgerBookDimensionId", "dimensionBookId")),
            AccountId: NormalizeOptional(GetFirstQueryValue(query, "dimensionAccountId", "accountId")),
            CustomerId: NormalizeOptional(GetFirstQueryValue(query, "dimensionCustomerId", "customerId")),
            VendorId: NormalizeOptional(GetFirstQueryValue(query, "dimensionVendorId", "vendorId")),
            ProjectId: NormalizeOptional(GetFirstQueryValue(query, "dimensionProjectId", "projectId")),
            ExternalGlDimensions: externalGlDimensions));
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
            Math.Abs(lines.Sum(static line => line.Debit) - lines.Sum(static line => line.Credit)) <= 0.000001m,
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
        var positionId = Guid.TryParse(FirstTag(tags, "positionId"), out var parsedPositionId)
            ? parsedPositionId
            : (Guid?)null;
        var dimensions = new LedgerDimensionSetDto(
            FundId: FirstTag(tags, "fundId", "fundProfileId"),
            EntityId: FirstTag(tags, "entityId", "legalEntityId"),
            SleeveId: FirstTag(tags, "sleeveId"),
            StrategyId: metadata.StrategyId ?? FirstTag(tags, "strategyId"),
            InvestorId: metadata.InvestorId ?? FirstTag(tags, "investorId"),
            CapitalAccountId: metadata.CapitalAccountId ?? FirstTag(tags, "capitalAccountId"),
            InstrumentId: metadata.SecurityId,
            TaxLotId: FirstTag(tags, "taxLotId", "lotId"),
            CostCenterId: FirstTag(tags, "costCenterId"),
            CounterpartyId: metadata.CounterpartyAccountId ?? FirstTag(tags, "counterpartyId", "counterpartyAccountId"),
            ExternalGlDimensions: ExtractExternalGlDimensions(tags),
            OrganizationId: FirstTag(tags, "organizationId"),
            PortfolioId: FirstTag(tags, "portfolioId"),
            BookId: metadata.LedgerBook ?? FirstTag(tags, "bookId"),
            AccountId: metadata.FinancialAccountId ?? FirstTag(tags, "accountId"),
            CustomerId: FirstTag(tags, "customerId"),
            VendorId: FirstTag(tags, "vendorId"),
            ProjectId: metadata.ProjectId ?? FirstTag(tags, "projectId"))
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

        var positionId = Guid.TryParse(FirstTag(tags, prefix + "positionId"), out var parsedPositionId)
            ? parsedPositionId
            : (Guid?)null;
        var dimensions = new LedgerDimensionSetDto(
            FundId: FirstTag(tags, prefix + "fundId"),
            EntityId: FirstTag(tags, prefix + "entityId"),
            SleeveId: FirstTag(tags, prefix + "sleeveId"),
            StrategyId: FirstTag(tags, prefix + "strategyId"),
            InvestorId: FirstTag(tags, prefix + "investorId"),
            CapitalAccountId: FirstTag(tags, prefix + "capitalAccountId"),
            InstrumentId: Guid.TryParse(FirstTag(tags, prefix + "instrumentId"), out var instrumentId) ? instrumentId : null,
            TaxLotId: FirstTag(tags, prefix + "taxLotId"),
            CostCenterId: FirstTag(tags, prefix + "costCenterId"),
            CounterpartyId: FirstTag(tags, prefix + "counterpartyId"),
            ExternalGlDimensions: ExtractExternalGlDimensions(tags, prefix),
            OrganizationId: FirstTag(tags, prefix + "organizationId"),
            PortfolioId: FirstTag(tags, prefix + "portfolioId"),
            BookId: FirstTag(tags, prefix + "bookId"),
            AccountId: FirstTag(tags, prefix + "accountId"),
            CustomerId: FirstTag(tags, prefix + "customerId"),
            VendorId: FirstTag(tags, prefix + "vendorId"),
            ProjectId: FirstTag(tags, prefix + "projectId"))
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

        return HasAnyCanonicalDimension(canonical) ? canonical : null;
    }

    private static IReadOnlyDictionary<string, string> NormalizeExternalGlDimensions(
        IReadOnlyDictionary<string, string>? dimensions)
    {
        if (dimensions is null || dimensions.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in dimensions.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var key = NormalizeOptional(pair.Key);
            var value = NormalizeOptional(pair.Value);
            if (key is not null && value is not null && !normalized.ContainsKey(key))
            {
                normalized[key] = value;
            }
        }

        return normalized;
    }

    private static bool HasAnyCanonicalDimension(LedgerDimensionSetDto dimensions)
        => dimensions.FundId is not null
           || dimensions.EntityId is not null
           || dimensions.SleeveId is not null
           || dimensions.StrategyId is not null
           || dimensions.InvestorId is not null
           || dimensions.CapitalAccountId is not null
           || dimensions.InstrumentId.HasValue
           || dimensions.PositionId.HasValue
           || dimensions.TaxLotId is not null
           || dimensions.CostCenterId is not null
           || dimensions.CounterpartyId is not null
           || dimensions.ExternalGlDimensions.Count > 0
           || dimensions.OrganizationId is not null
           || dimensions.PortfolioId is not null
           || dimensions.BookId is not null
           || dimensions.AccountId is not null
           || dimensions.CustomerId is not null
           || dimensions.VendorId is not null
           || dimensions.ProjectId is not null;

    private static IReadOnlyDictionary<string, string> ExtractExternalGlDimensions(
        IReadOnlyDictionary<string, string>? tags)
        => ExtractExternalGlDimensions(tags, prefix: null);

    private static IReadOnlyDictionary<string, string> ExtractExternalGlDimensions(
        IReadOnlyDictionary<string, string>? tags,
        string? prefix)
    {
        if (tags is null || tags.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in tags)
        {
            var key = NormalizeOptional(pair.Key);
            var value = NormalizeOptional(pair.Value);
            if (key is null || value is null)
            {
                continue;
            }

            var scopedKey = prefix is null
                ? key
                : StripPrefix(key, prefix);
            if (scopedKey is null)
            {
                continue;
            }

            var dimensionKey = StripPrefix(scopedKey, "externalGl.")
                               ?? StripPrefix(scopedKey, "externalGl:")
                               ?? StripPrefix(scopedKey, "gl.")
                               ?? StripPrefix(scopedKey, "gl:");
            if (!string.IsNullOrWhiteSpace(dimensionKey))
            {
                result[dimensionKey] = value;
            }
        }

        return result;
    }

    private static string? StripPrefix(string value, string prefix)
        => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? NormalizeOptional(value[prefix.Length..])
            : null;

    private static string? FirstTag(
        IReadOnlyDictionary<string, string>? tags,
        params string[] keys)
    {
        if (tags is null || tags.Count == 0)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (tags.TryGetValue(key, out var value))
            {
                return NormalizeOptional(value);
            }
        }

        return null;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static PrivateCapitalActivityProjectionDto FilterPrivateCapitalActivity(
        PrivateCapitalActivityProjectionDto activity,
        string? fundEventId,
        string? capitalAccountId,
        string? investorId,
        string? paymentIntentId)
    {
        var normalizedFundEventId = NormalizeOptional(fundEventId);
        var normalizedCapitalAccountId = NormalizeOptional(capitalAccountId);
        var normalizedInvestorId = NormalizeOptional(investorId);
        var normalizedPaymentIntentId = NormalizeOptional(paymentIntentId);
        if (normalizedFundEventId is null &&
            normalizedCapitalAccountId is null &&
            normalizedInvestorId is null &&
            normalizedPaymentIntentId is null)
        {
            return activity;
        }

        var paymentIntentFundEventIds = normalizedPaymentIntentId is null
            ? null
            : activity.FundEvents
                .Where(item => MatchesPrivateCapitalFilter(item.PaymentIntentId, normalizedPaymentIntentId))
                .Select(static item => item.FundEventId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matchingFundEvents = activity.FundEvents
            .Where(item =>
                MatchesPrivateCapitalFilter(item.FundEventId, normalizedFundEventId) &&
                MatchesPrivateCapitalFilter(item.CapitalAccountId, normalizedCapitalAccountId) &&
                MatchesPrivateCapitalFilter(item.InvestorId, normalizedInvestorId) &&
                MatchesPrivateCapitalFilter(item.PaymentIntentId, normalizedPaymentIntentId))
            .ToArray();
        var matchingSubledgerEntries = activity.CapitalAccountSubledgerEntries
            .Where(item =>
                MatchesPrivateCapitalFilter(item.FundEventId, normalizedFundEventId) &&
                MatchesPrivateCapitalFilter(item.CapitalAccountId, normalizedCapitalAccountId) &&
                MatchesPrivateCapitalFilter(item.InvestorId, normalizedInvestorId) &&
                MatchesPrivateCapitalFundEventSet(item.FundEventId, paymentIntentFundEventIds))
            .ToArray();
        var matchingLedgerImpacts = activity.LedgerImpacts
            .Where(item =>
                MatchesPrivateCapitalFilter(item.FundEventId, normalizedFundEventId) &&
                MatchesPrivateCapitalFilter(item.CapitalAccountId, normalizedCapitalAccountId) &&
                MatchesPrivateCapitalFilter(item.InvestorId, normalizedInvestorId) &&
                MatchesPrivateCapitalFundEventSet(item.FundEventId, paymentIntentFundEventIds))
            .ToArray();
        var matchingReportOutputs = activity.ReportOutputs
            .Where(item =>
                MatchesPrivateCapitalFilter(item.FundEventId, normalizedFundEventId) &&
                MatchesPrivateCapitalFilter(item.CapitalAccountId, normalizedCapitalAccountId) &&
                MatchesPrivateCapitalFilter(item.InvestorId, normalizedInvestorId) &&
                MatchesPrivateCapitalFundEventSet(item.FundEventId, paymentIntentFundEventIds))
            .ToArray();
        var matchedFundEventIds = matchingFundEvents
            .Select(static item => item.FundEventId)
            .Concat(matchingSubledgerEntries.Select(static item => item.FundEventId))
            .Concat(matchingLedgerImpacts.Select(static item => item.FundEventId))
            .Concat(matchingReportOutputs.Select(static item => item.FundEventId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fundEvents = activity.FundEvents
            .Where(item => matchedFundEventIds.Contains(item.FundEventId))
            .ToArray();
        var retainedFundEventIds = fundEvents
            .Select(static item => item.FundEventId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retainedPaymentIntentIds = fundEvents
            .Select(static item => item.PaymentIntentId)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var capitalAccountSubledgerEntries = activity.CapitalAccountSubledgerEntries
            .Where(item =>
                MatchesPrivateCapitalFilter(item.FundEventId, normalizedFundEventId) &&
                MatchesPrivateCapitalFilter(item.CapitalAccountId, normalizedCapitalAccountId) &&
                MatchesPrivateCapitalFilter(item.InvestorId, normalizedInvestorId) &&
                MatchesPrivateCapitalFundEventSet(item.FundEventId, paymentIntentFundEventIds) &&
                retainedFundEventIds.Contains(item.FundEventId))
            .ToArray();
        var ledgerImpacts = activity.LedgerImpacts
            .Where(item =>
                MatchesPrivateCapitalFilter(item.FundEventId, normalizedFundEventId) &&
                MatchesPrivateCapitalFilter(item.CapitalAccountId, normalizedCapitalAccountId) &&
                MatchesPrivateCapitalFilter(item.InvestorId, normalizedInvestorId) &&
                MatchesPrivateCapitalFundEventSet(item.FundEventId, paymentIntentFundEventIds) &&
                retainedFundEventIds.Contains(item.FundEventId))
            .ToArray();
        var reportOutputs = activity.ReportOutputs
            .Where(item =>
                MatchesPrivateCapitalFilter(item.FundEventId, normalizedFundEventId) &&
                (MatchesPrivateCapitalFilter(item.CapitalAccountId, normalizedCapitalAccountId) &&
                 MatchesPrivateCapitalFilter(item.InvestorId, normalizedInvestorId) ||
                 ((normalizedCapitalAccountId is not null || normalizedInvestorId is not null) &&
                  capitalAccountSubledgerEntries.Any(entry => string.Equals(entry.FundEventId, item.FundEventId, StringComparison.OrdinalIgnoreCase)))) &&
                MatchesPrivateCapitalFundEventSet(item.FundEventId, paymentIntentFundEventIds) &&
                retainedFundEventIds.Contains(item.FundEventId))
            .ToArray();
        var paymentIntents = activity.PaymentIntents
            .Where(item =>
                MatchesPrivateCapitalFilter(item.PaymentIntentId, normalizedPaymentIntentId) &&
                (retainedPaymentIntentIds.Contains(item.PaymentIntentId) ||
                 (!string.IsNullOrWhiteSpace(item.FundEventId) && retainedFundEventIds.Contains(item.FundEventId)) ||
                 MatchesPrivateCapitalFilter(item.ExpectedCashMovement.CapitalAccountId, normalizedCapitalAccountId) &&
                 MatchesPrivateCapitalFilter(item.ExpectedCashMovement.InvestorId, normalizedInvestorId) &&
                 MatchesPrivateCapitalFilter(item.FundEventId, normalizedFundEventId)))
            .ToArray();
        var fundEventRecords = PrivateCapitalFundEventLedgerRecordBuilder.Build(
            activity.FundProfileId,
            fundEvents,
            capitalAccountSubledgerEntries,
            ledgerImpacts,
            reportOutputs);
        var capitalAccounts = BuildFilteredCapitalAccounts(capitalAccountSubledgerEntries, fundEvents);
        var retainedCapitalAccountIds = capitalAccounts
            .Select(static item => item.CapitalAccountId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retainedJournalEntryIds = fundEvents
            .Select(static item => item.JournalEntryId.ToString("D"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var validationIssues = activity.ValidationIssues
            .Where(issue => MatchesFilteredPrivateCapitalIssue(
                issue,
                retainedFundEventIds,
                retainedCapitalAccountIds,
                retainedJournalEntryIds))
            .ToArray();
        var currency = fundEvents
            .Select(static item => item.Currency)
            .Concat(capitalAccountSubledgerEntries.Select(static item => item.Currency))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? activity.Currency;
        var netCapitalActivity = capitalAccountSubledgerEntries.Length > 0
            ? capitalAccountSubledgerEntries.Sum(static item => item.NetCapitalActivity)
            : fundEvents.Sum(static item => item.NetCapitalActivity);
        var capitalAccountSubledgers = PrivateCapitalCapitalAccountSubledgerBuilder.Build(
            activity.FundProfileId,
            activity.LedgerBookId,
            activity.ProjectedAtUtc,
            capitalAccounts,
            fundEventRecords,
            capitalAccountSubledgerEntries,
            ledgerImpacts,
            reportOutputs,
            validationIssues);

        return new PrivateCapitalActivityProjectionDto(
            activity.FundProfileId,
            activity.LedgerBookId,
            activity.ProjectedAtUtc,
            fundEvents.Length,
            capitalAccounts.Count,
            fundEvents.Count(static item => item.JournalStatus is ManualJournalEntryStatusDto.Submitted or ManualJournalEntryStatusDto.Approved),
            fundEvents.Count(static item => item.JournalStatus == ManualJournalEntryStatusDto.Submitted),
            fundEvents.Count(static item => item.IsPosted),
            reportOutputs.Count(static item => item.IsPublished),
            netCapitalActivity,
            currency,
            fundEvents,
            capitalAccounts,
            capitalAccountSubledgerEntries,
            ledgerImpacts,
            reportOutputs,
            validationIssues,
            fundEventRecords,
            capitalAccountSubledgers,
            paymentIntents);
    }

    private static IReadOnlyList<PrivateCapitalCapitalAccountActivityDto> BuildFilteredCapitalAccounts(
        IReadOnlyList<PrivateCapitalCapitalAccountSubledgerEntryDto> subledgerEntries,
        IReadOnlyList<PrivateCapitalFundEventDto> fundEvents)
    {
        if (subledgerEntries.Count > 0)
        {
            return subledgerEntries
                .GroupBy(static item => new { item.CapitalAccountId, item.InvestorId, item.Currency })
                .Select(group =>
                {
                    var ordered = group
                        .OrderByDescending(static item => item.EffectiveDate)
                        .ThenByDescending(static item => item.UpdatedAtUtc)
                        .ToArray();
                    return new PrivateCapitalCapitalAccountActivityDto(
                        group.Key.CapitalAccountId,
                        group.Key.InvestorId,
                        group.Key.Currency,
                        group.Where(static item => item.EntryType == ManualJournalEntryTypeDto.CapitalCall).Sum(static item => Math.Abs(item.NetCapitalActivity)),
                        group.Where(static item => item.EntryType == ManualJournalEntryTypeDto.Distribution).Sum(static item => Math.Abs(item.NetCapitalActivity)),
                        group.Where(static item => item.EntryType == ManualJournalEntryTypeDto.Subscription).Sum(static item => Math.Abs(item.NetCapitalActivity)),
                        group.Where(static item => item.EntryType == ManualJournalEntryTypeDto.Redemption).Sum(static item => Math.Abs(item.NetCapitalActivity)),
                        group.Where(static item => item.EntryType == ManualJournalEntryTypeDto.ManagementFee).Sum(static item => Math.Abs(item.NetCapitalActivity)),
                        group.Sum(static item => item.NetCapitalActivity),
                        group.Select(static item => item.FundEventId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                        ordered.Length == 0 ? null : ordered[0].EffectiveDate,
                        ordered.Length == 0 ? null : ordered[0].FundEventType,
                        group
                            .Select(static item => item.FundEventId)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Order(StringComparer.OrdinalIgnoreCase)
                            .ToArray());
                })
                .OrderBy(static item => item.CapitalAccountId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.InvestorId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.Currency, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return fundEvents
            .GroupBy(item => new { item.CapitalAccountId, item.InvestorId, item.Currency })
            .Select(group =>
            {
                var ordered = group
                    .OrderByDescending(static item => item.EffectiveDate)
                    .ThenByDescending(static item => item.UpdatedAtUtc)
                    .ToArray();
                return new PrivateCapitalCapitalAccountActivityDto(
                    group.Key.CapitalAccountId,
                    group.Key.InvestorId,
                    group.Key.Currency,
                    group.Where(static item => item.EntryType == ManualJournalEntryTypeDto.CapitalCall).Sum(static item => Math.Abs(item.NetCapitalActivity)),
                    group.Where(static item => item.EntryType == ManualJournalEntryTypeDto.Distribution).Sum(static item => Math.Abs(item.NetCapitalActivity)),
                    group.Where(static item => item.EntryType == ManualJournalEntryTypeDto.Subscription).Sum(static item => Math.Abs(item.NetCapitalActivity)),
                    group.Where(static item => item.EntryType == ManualJournalEntryTypeDto.Redemption).Sum(static item => Math.Abs(item.NetCapitalActivity)),
                    group.Where(static item => item.EntryType == ManualJournalEntryTypeDto.ManagementFee).Sum(static item => Math.Abs(item.NetCapitalActivity)),
                    group.Sum(static item => item.NetCapitalActivity),
                    group.Count(),
                    ordered.Length == 0 ? null : ordered[0].EffectiveDate,
                    ordered.Length == 0 ? null : ordered[0].FundEventType,
                    group
                        .Select(static item => item.FundEventId)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray());
            })
            .OrderBy(static item => item.CapitalAccountId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.InvestorId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Currency, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool MatchesPrivateCapitalFilter(string? value, string? filter)
        => filter is null || string.Equals(value?.Trim(), filter, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesPrivateCapitalFundEventSet(
        string fundEventId,
        IReadOnlySet<string>? fundEventIds)
        => fundEventIds is null || fundEventIds.Contains(fundEventId);

    private static bool MatchesFilteredPrivateCapitalIssue(
        AccountingConfigurationValidationIssueDto issue,
        IReadOnlySet<string> retainedFundEventIds,
        IReadOnlySet<string> retainedCapitalAccountIds,
        IReadOnlySet<string> retainedJournalEntryIds)
    {
        if (retainedFundEventIds.Count == 0 &&
            retainedCapitalAccountIds.Count == 0 &&
            retainedJournalEntryIds.Count == 0)
        {
            return false;
        }

        var targetId = NormalizeOptional(issue.TargetId);
        return targetId is null ||
               retainedFundEventIds.Contains(targetId) ||
               retainedCapitalAccountIds.Contains(targetId) ||
               retainedJournalEntryIds.Contains(targetId);
    }

    private static async Task<LedgerPeriodSummaryLoadResult> LoadClosedPeriodSummariesAsync(
        ILedgerBookService service,
        Guid? ledgerBookId,
        string? fundProfileId,
        Guid? fundStructureNodeId,
        AccountingBasisKindDto? accountingBasis,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken)
    {
        var periods = await service
            .ListPeriodsAsync(
                new LedgerPeriodQuery(
                    ledgerBookId,
                    fundProfileId,
                    fundStructureNodeId,
                    Status: null,
                    OpenOnly: false,
                    accountingBasis),
                cancellationToken)
            .ConfigureAwait(false);

        var closedPeriods = periods
            .Where(period => period.Status != LedgerPeriodStatusDto.Open)
            .Where(period => !startDate.HasValue || period.EndDate >= startDate.Value)
            .Where(period => !endDate.HasValue || period.StartDate <= endDate.Value)
            .OrderBy(static period => period.StartDate)
            .ThenBy(static period => period.PeriodNo)
            .ToArray();

        var summaries = new List<(LedgerPeriodDto period, LedgerPeriodSummaryDto summary)>(closedPeriods.Length);
        foreach (var period in closedPeriods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ledgerBookId.HasValue && period.LedgerBookId != ledgerBookId.Value)
            {
                return LedgerPeriodSummaryLoadResult.Conflict(
                    $"Ledger period '{period.PeriodId:D}' belongs to ledger book '{period.LedgerBookId:D}', not requested ledger book '{ledgerBookId.Value:D}'.");
            }

            var summary = await service.GetPeriodSummaryAsync(period.PeriodId, cancellationToken).ConfigureAwait(false);
            if (summary is not null)
            {
                if (summary.LedgerBookId != period.LedgerBookId)
                {
                    return LedgerPeriodSummaryLoadResult.Conflict(
                        $"Closed-period summary for period '{period.PeriodId:D}' belongs to ledger book '{summary.LedgerBookId:D}', but the period belongs to ledger book '{period.LedgerBookId:D}'.");
                }

                summaries.Add((period, summary));
            }
        }

        return LedgerPeriodSummaryLoadResult.Success(summaries);
    }

    private sealed record LedgerPeriodSummaryLoadResult(
        IReadOnlyList<(LedgerPeriodDto period, LedgerPeriodSummaryDto summary)> Summaries,
        IResult? Error)
    {
        public static LedgerPeriodSummaryLoadResult Success(
            IReadOnlyList<(LedgerPeriodDto period, LedgerPeriodSummaryDto summary)> summaries)
            => new(summaries, Error: null);

        public static LedgerPeriodSummaryLoadResult Conflict(string message)
            => new([], Results.Conflict(new { error = message }));
    }

    private static LedgerCrossPeriodTrialBalanceReportDto BuildTrialBalanceReport(
        IReadOnlyList<(LedgerPeriodDto period, LedgerPeriodSummaryDto summary)> summaries,
        Guid? ledgerBookId,
        string? fundProfileId,
        Guid? fundStructureNodeId,
        AccountingBasisKindDto? accountingBasis,
        DateOnly? startDate,
        DateOnly? endDate,
        LedgerDimensionReportFilter dimensionFilter)
    {
        var filteredSummaries = summaries
            .Select(item => (item.period, summary: ApplyDimensionFilter(item.summary, dimensionFilter)))
            .ToArray();
        var lines = filteredSummaries
            .SelectMany(static item => item.summary.TrialBalance.Select(line => new LedgerCrossPeriodTrialBalanceLineDto(
                item.summary.PeriodId,
                item.summary.LedgerBookId,
                item.summary.FiscalYear,
                item.summary.PeriodNo,
                item.summary.Label,
                line.AccountName,
                line.AccountType,
                line.Symbol,
                line.FinancialAccountId,
                line.DebitTotal,
                line.CreditTotal,
                line.Balance,
                line.EntryCount,
                line.AccountingBasis,
                line.AccountingPolicyId,
                line.AccountingPolicyVersion,
                line.RuleId,
                line.RuleVersion,
                line.SourceEventId,
                line.SourceJournalEntryId,
                CanonicalizeDimensions(line.Dimensions))))
            .ToArray();

        return new LedgerCrossPeriodTrialBalanceReportDto(
            DateTimeOffset.UtcNow,
            ledgerBookId,
            NormalizeOptional(fundProfileId),
            fundStructureNodeId,
            accountingBasis,
            startDate,
            endDate,
            filteredSummaries.Select(static item => item.period).ToArray(),
            lines,
            filteredSummaries.Sum(static item => item.summary.TotalDebits),
            filteredSummaries.Sum(static item => item.summary.TotalCredits),
            filteredSummaries.Sum(static item => item.summary.NetIncome));
    }

    private static LedgerCrossPeriodPnlReportDto BuildPnlReport(
        IReadOnlyList<(LedgerPeriodDto period, LedgerPeriodSummaryDto summary)> summaries,
        Guid? ledgerBookId,
        string? fundProfileId,
        Guid? fundStructureNodeId,
        AccountingBasisKindDto? accountingBasis,
        DateOnly? startDate,
        DateOnly? endDate,
        LedgerDimensionReportFilter dimensionFilter)
    {
        var periods = summaries
            .Select(item => BuildPnlSummary(ApplyDimensionFilter(item.summary, dimensionFilter)))
            .ToArray();

        return new LedgerCrossPeriodPnlReportDto(
            DateTimeOffset.UtcNow,
            ledgerBookId,
            NormalizeOptional(fundProfileId),
            fundStructureNodeId,
            accountingBasis,
            startDate,
            endDate,
            periods,
            periods.Sum(static period => period.TotalRevenue),
            periods.Sum(static period => period.TotalExpenses),
            periods.Sum(static period => period.NetIncome),
            periods.Sum(static period => period.RealizedNetIncome),
            periods.Sum(static period => period.AccrualBasisAdjustmentNetImpact));
    }

    private sealed record LedgerDimensionReportFilter(
        string? FundId,
        string? EntityId,
        string? SleeveId,
        string? StrategyId,
        string? InvestorId,
        string? CapitalAccountId,
        string? InstrumentId,
        string? PositionId,
        string? TaxLotId,
        string? CostCenterId,
        string? CounterpartyId,
        string? OrganizationId,
        string? PortfolioId,
        string? BookId,
        string? AccountId,
        string? CustomerId,
        string? VendorId,
        string? ProjectId,
        IReadOnlyDictionary<string, string> ExternalGlDimensions)
    {
        public bool HasCriteria
            => FundId is not null
               || EntityId is not null
               || SleeveId is not null
               || StrategyId is not null
               || InvestorId is not null
               || CapitalAccountId is not null
               || InstrumentId is not null
               || PositionId is not null
               || TaxLotId is not null
               || CostCenterId is not null
               || CounterpartyId is not null
               || OrganizationId is not null
               || PortfolioId is not null
               || BookId is not null
               || AccountId is not null
               || CustomerId is not null
               || VendorId is not null
               || ProjectId is not null
               || ExternalGlDimensions.Count > 0;
    }

    private static bool ContainsAccrualMarker(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Contains("accru", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetLedgerCloseActor(HttpContext context, out string actor)
    {
        actor = string.Empty;
        if (context.Items[LoginSessionMiddleware.CurrentUserKey] is not string username ||
            string.IsNullOrWhiteSpace(username))
        {
            return false;
        }

        if (context.Items[LoginSessionMiddleware.CurrentUserRoleKey] is not UserRole role)
        {
            return false;
        }

        if (role is not UserRole.Admin and not UserRole.Accounting)
        {
            return false;
        }

        actor = username.Trim();
        return true;
    }
}
