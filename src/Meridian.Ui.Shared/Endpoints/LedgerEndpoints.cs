using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Auth;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;
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
    }

    private static ILedgerBookService? ResolveService(HttpContext context)
        => context.RequestServices.GetService<ILedgerBookService>();

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
            summary.AccountingPolicyVersion);
    }

    private static bool IsValidDateRange(DateOnly? startDate, DateOnly? endDate)
        => !startDate.HasValue || !endDate.HasValue || startDate.Value <= endDate.Value;

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
            periods.Sum(static period => period.NetIncome));
    }

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
