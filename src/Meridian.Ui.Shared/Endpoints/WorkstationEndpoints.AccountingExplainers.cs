using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapAccountingExplainabilityEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationPortfolioOverview), async (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IPortfolioAccountingReadService>();
            if (service is null)
            {
                return Results.Problem("Portfolio accounting read service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var fundProfileId = context.Request.Query["fundProfileId"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(fundProfileId))
            {
                return Results.Problem("fundProfileId is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await service.GetPortfolioOverviewAsync(
                new PortfolioOverviewQuery(
                    FundProfileId: fundProfileId,
                    AsOf: ParseDateTimeOffset(context.Request.Query["asOf"].FirstOrDefault()),
                    Currency: context.Request.Query["currency"].FirstOrDefault()),
                context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("GetWorkstationPortfolioOverview")
        .Produces<PortfolioOverviewDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationAccountingLedgerDrillDown), async (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IPortfolioAccountingReadService>();
            if (service is null)
            {
                return Results.Problem("Portfolio accounting read service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var fundProfileId = context.Request.Query["fundProfileId"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(fundProfileId))
            {
                return Results.Problem("fundProfileId is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var scopeKindRaw = context.Request.Query["scopeKind"].FirstOrDefault();
            var scopeKind = Enum.TryParse<FundLedgerScope>(scopeKindRaw, ignoreCase: true, out var parsedScope)
                ? parsedScope
                : FundLedgerScope.Consolidated;
            var selectedLedgerIds = ParseSelectedLedgerIds(context.Request.Query["selectedLedgerIds"], context.Request.Query["selectedLedgerId"]);

            var result = await service.GetLedgerDrillDownAsync(
                new LedgerDrillDownQuery(
                    FundProfileId: fundProfileId,
                    AsOf: ParseDateTimeOffset(context.Request.Query["asOf"].FirstOrDefault()),
                    ScopeKind: scopeKind,
                    ScopeId: context.Request.Query["scopeId"].FirstOrDefault(),
                    SelectedLedgerIds: selectedLedgerIds),
                context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("GetWorkstationAccountingLedgerDrillDown")
        .Produces<LedgerDrillDownDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationAccountingFinancingAnalysis), async (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IPortfolioAccountingReadService>();
            if (service is null)
            {
                return Results.Problem("Portfolio accounting read service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var fundProfileId = context.Request.Query["fundProfileId"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(fundProfileId))
            {
                return Results.Problem("fundProfileId is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await service.GetFinancingAnalysisAsync(
                new FinancingAnalysisQuery(
                    FundProfileId: fundProfileId,
                    AsOf: ParseDateTimeOffset(context.Request.Query["asOf"].FirstOrDefault()),
                    Currency: context.Request.Query["currency"].FirstOrDefault()),
                context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("GetWorkstationAccountingFinancingAnalysis")
        .Produces<FinancingAnalysisDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationAccountingEquityChange), async (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IAccountingExplainabilityService>();
            if (service is null)
            {
                return Results.Problem("Accounting explainability service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var fundProfileId = context.Request.Query["fundProfileId"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(fundProfileId))
            {
                return Results.Problem("fundProfileId is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var from = ParseDateTimeOffset(context.Request.Query["from"].FirstOrDefault()) ?? DateTimeOffset.UtcNow.Date;
            var to = ParseDateTimeOffset(context.Request.Query["to"].FirstOrDefault()) ?? DateTimeOffset.UtcNow;
            var result = await service.ExplainEquityChangeAsync(
                new EquityChangeQuery(
                    FundProfileId: fundProfileId,
                    From: from,
                    To: to,
                    Currency: context.Request.Query["currency"].FirstOrDefault()),
                context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("GetWorkstationAccountingEquityChange")
        .Produces<EquityChangeExplanationDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationAccountingPnlReconciliation), async (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IAccountingExplainabilityService>();
            if (service is null)
            {
                return Results.Problem("Accounting explainability service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var fundProfileId = context.Request.Query["fundProfileId"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(fundProfileId))
            {
                return Results.Problem("fundProfileId is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var from = ParseDateTimeOffset(context.Request.Query["from"].FirstOrDefault()) ?? DateTimeOffset.UtcNow.Date;
            var to = ParseDateTimeOffset(context.Request.Query["to"].FirstOrDefault()) ?? DateTimeOffset.UtcNow;
            var result = await service.ReconcilePnlAsync(
                new PnlReconciliationQuery(
                    FundProfileId: fundProfileId,
                    From: from,
                    To: to,
                    Currency: context.Request.Query["currency"].FirstOrDefault()),
                context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("GetWorkstationAccountingPnlReconciliation")
        .Produces<PnlReconciliationDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static IReadOnlyList<string>? ParseSelectedLedgerIds(params StringValues[] valueSets)
    {
        var parsed = valueSets
            .SelectMany(static values => values)
            .Where(static value => value is not null)
            .SelectMany(static value => value!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return parsed.Length == 0 ? null : parsed;
    }
}
