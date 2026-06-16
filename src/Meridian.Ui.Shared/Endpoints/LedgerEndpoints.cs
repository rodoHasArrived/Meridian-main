using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static class LedgerEndpoints
{
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
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

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
                var trustedRequest = request with { ClosedBy = actor };
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
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

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

            var summary = await service.GetPeriodSummaryAsync(periodId, context.RequestAborted).ConfigureAwait(false);
            return summary is null
                ? Results.NotFound(new { error = $"Ledger period '{periodId}' has no closed-period summary." })
                : Results.Json(summary.TrialBalance, jsonOptions);
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

            var summary = await service.GetPeriodSummaryAsync(periodId, context.RequestAborted).ConfigureAwait(false);
            return summary is null
                ? Results.NotFound()
                : Results.Json(BuildTrialBalanceReport(summary, context), jsonOptions);
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

            var summary = await service.GetPeriodSummaryAsync(periodId, context.RequestAborted).ConfigureAwait(false);
            return summary is null
                ? Results.NotFound(new { error = $"Ledger period '{periodId}' has no closed-period summary." })
                : Results.Json(BuildPnlSummary(summary), jsonOptions);
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

            var summaries = await LoadClosedPeriodSummariesAsync(
                service,
                ledgerBookId,
                fundProfileId,
                fundStructureNodeId,
                accountingBasis,
                startDate,
                endDate,
                context.RequestAborted).ConfigureAwait(false);

            return Results.Json(
                BuildTrialBalanceReport(
                    summaries,
                    ledgerBookId,
                    fundProfileId,
                    fundStructureNodeId,
                    accountingBasis,
                    startDate,
                    endDate),
                jsonOptions);
        })
        .WithName("GetLedgerCrossPeriodTrialBalanceReport")
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

            var summaries = await LoadClosedPeriodSummariesAsync(
                service,
                ledgerBookId,
                fundProfileId,
                fundStructureNodeId,
                accountingBasis,
                startDate,
                endDate,
                context.RequestAborted).ConfigureAwait(false);

            return Results.Json(
                BuildPnlReport(
                    summaries,
                    ledgerBookId,
                    fundProfileId,
                    fundStructureNodeId,
                    accountingBasis,
                    startDate,
                    endDate),
                jsonOptions);
        })
        .WithName("GetLedgerCrossPeriodPnlReport")
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

            var workspace = await service.GetWorkspaceAsync(fundProfileId, ledgerBookId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(workspace, jsonOptions);
        })
        .WithName("GetAccountingConfiguration")
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

            var result = await service.PreviewTemplateAsync(request with { Actor = ResolveMutationActor(context, request.Actor) }, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("PreviewAccountingConfigurationTemplate")
        .Produces<AccountingJournalTemplatePreviewDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationActivate, async (ActivateAccountingConfigurationRequest request, HttpContext context) =>
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

            var audit = await service.ListAuditAsync(fundProfileId, ledgerBookId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(audit, jsonOptions);
        })
        .WithName("ListAccountingConfigurationAudit")
        .Produces<IReadOnlyList<AccountingActionAuditEventDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
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

            var workbench = await service.GetWorkbenchAsync(fundProfileId, ledgerBookId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(workbench, jsonOptions);
        })
        .WithName("GetManualJournalEntryWorkbench")
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

            var activity = await service.GetPrivateCapitalActivityAsync(fundProfileId, ledgerBookId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(
                FilterPrivateCapitalActivity(activity, fundEventId, capitalAccountId, investorId, paymentIntentId),
                jsonOptions);
        })
        .WithName("GetLedgerPrivateCapitalActivity")
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

            var activity = await service.GetPrivateCapitalActivityAsync(fundProfileId, ledgerBookId, context.RequestAborted).ConfigureAwait(false);
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
            var activity = await service.GetPrivateCapitalActivityAsync(fundProfileId, ledgerBookId, context.RequestAborted).ConfigureAwait(false);
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

            var activity = await service.GetPrivateCapitalActivityAsync(fundProfileId, ledgerBookId, context.RequestAborted).ConfigureAwait(false);
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

            try
            {
                var result = await service.SaveDraftAsync(request with { Actor = ResolveMutationActor(context, request.Actor) }, context.RequestAborted).ConfigureAwait(false);
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

            try
            {
                var result = await service.ValidateDraftAsync(request with { Actor = ResolveMutationActor(context, request.Actor) }, context.RequestAborted).ConfigureAwait(false);
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
        .Produces(StatusCodes.Status501NotImplemented);

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
                var result = await service.SubmitApprovalAsync(request with { Actor = ResolveMutationActor(context, request.Actor) }, context.RequestAborted).ConfigureAwait(false);
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
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static ILedgerBookService? ResolveService(HttpContext context)
        => context.RequestServices.GetService<ILedgerBookService>();

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

    private static bool HasLedgerMutationPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.AdminMaintenance,
            UserPermission.ManageDirectLending);

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
        => request with
        {
            Actor = ResolveMutationActor(context, request.Actor),
            CompanyId = EndpointAuthorization.ResolveCompanyId(context),
            ReportGroupPrincipalIds = EndpointAuthorization.ResolveReportGroupPrincipalIds(context)
        };

    private static UpsertJournalEntryTemplateRequest WithAccessContext(
        UpsertJournalEntryTemplateRequest request,
        HttpContext context)
        => request with
        {
            Actor = ResolveMutationActor(context, request.Actor),
            CompanyId = EndpointAuthorization.ResolveCompanyId(context),
            ReportGroupPrincipalIds = EndpointAuthorization.ResolveReportGroupPrincipalIds(context)
        };

    private static UpsertPostingRuleRequest WithAccessContext(
        UpsertPostingRuleRequest request,
        HttpContext context)
        => request with
        {
            Actor = ResolveMutationActor(context, request.Actor),
            CompanyId = EndpointAuthorization.ResolveCompanyId(context),
            ReportGroupPrincipalIds = EndpointAuthorization.ResolveReportGroupPrincipalIds(context)
        };

    private static ActivateAccountingConfigurationRequest WithAccessContext(
        ActivateAccountingConfigurationRequest request,
        HttpContext context)
        => request with
        {
            Actor = ResolveMutationActor(context, request.Actor),
            CompanyId = EndpointAuthorization.ResolveCompanyId(context),
            ReportGroupPrincipalIds = EndpointAuthorization.ResolveReportGroupPrincipalIds(context)
        };

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
            .OrderBy(static row => row.AccountType, StringComparer.Ordinal)
            .ThenBy(static row => row.AccountName, StringComparer.Ordinal)
            .ThenBy(static row => row.Symbol, StringComparer.Ordinal)
            .ThenBy(static row => row.FinancialAccountId, StringComparer.Ordinal)
            .ToArray();
        var signature = new LedgerReportSignatureDto(
            "SHA256",
            ComputeTrialBalanceReportChecksum(summary, lines),
            actor,
            signedAtUtc);

        return new LedgerTrialBalanceReportDto(
            summary.PeriodId,
            summary.LedgerBookId,
            summary.FiscalYear,
            summary.PeriodNo,
            summary.Label,
            IsPeriodLocked: true,
            summary.TotalDebits,
            summary.TotalCredits,
            summary.NetIncome,
            summary.PeriodOnPeriodVariance,
            summary.OpenBreakCount,
            summary.SignoffStatus,
            summary.CompletedAt,
            lines,
            signature,
            summary.AccountingBasis,
            summary.AccountingPolicyId,
            summary.AccountingPolicyVersion);
    }

    private static string ComputeTrialBalanceReportChecksum(
        LedgerPeriodSummaryDto summary,
        IReadOnlyList<LedgerPeriodTrialBalanceLineDto> lines)
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
        builder.AppendLine("account-name,account-type,symbol,financial-account-id,debits,credits,balance,entry-count,rule-id,rule-version,source-event-id,source-journal-entry-id");

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
            builder.AppendLine(line.SourceJournalEntryId?.ToString("D") ?? string.Empty);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
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

    private static async Task<IReadOnlyList<(LedgerPeriodDto period, LedgerPeriodSummaryDto summary)>> LoadClosedPeriodSummariesAsync(
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
            var summary = await service.GetPeriodSummaryAsync(period.PeriodId, cancellationToken).ConfigureAwait(false);
            if (summary is not null)
            {
                summaries.Add((period, summary));
            }
        }

        return summaries;
    }

    private static LedgerCrossPeriodTrialBalanceReportDto BuildTrialBalanceReport(
        IReadOnlyList<(LedgerPeriodDto period, LedgerPeriodSummaryDto summary)> summaries,
        Guid? ledgerBookId,
        string? fundProfileId,
        Guid? fundStructureNodeId,
        AccountingBasisKindDto? accountingBasis,
        DateOnly? startDate,
        DateOnly? endDate)
    {
        var lines = summaries
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
                line.SourceJournalEntryId)))
            .ToArray();

        return new LedgerCrossPeriodTrialBalanceReportDto(
            DateTimeOffset.UtcNow,
            ledgerBookId,
            NormalizeOptional(fundProfileId),
            fundStructureNodeId,
            accountingBasis,
            startDate,
            endDate,
            summaries.Select(static item => item.period).ToArray(),
            lines,
            summaries.Sum(static item => item.summary.TotalDebits),
            summaries.Sum(static item => item.summary.TotalCredits),
            summaries.Sum(static item => item.summary.NetIncome));
    }

    private static LedgerCrossPeriodPnlReportDto BuildPnlReport(
        IReadOnlyList<(LedgerPeriodDto period, LedgerPeriodSummaryDto summary)> summaries,
        Guid? ledgerBookId,
        string? fundProfileId,
        Guid? fundStructureNodeId,
        AccountingBasisKindDto? accountingBasis,
        DateOnly? startDate,
        DateOnly? endDate)
    {
        var periods = summaries
            .Select(static item => BuildPnlSummary(item.summary))
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
