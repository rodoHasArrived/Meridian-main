using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Storage.Export;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Endpoints for the React workstation shell and its bootstrap placeholder data.
/// </summary>
public static class WorkstationEndpoints
{
    private const int SecurityCoveragePreviewLimit = 5;

    public static void MapWorkstationEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/workstation").WithTags("Workstation");

        group.MapGet("/session", async (HttpContext context) =>
        {
            var payload = await BuildSessionPayloadAsync(context).ConfigureAwait(false);
            return Results.Json(payload, jsonOptions);
        })
        .WithName("GetWorkstationSession")
        .Produces(200);

        group.MapGet("/research", async (HttpContext context) =>
        {
            var payload = await BuildResearchPayloadAsync(context).ConfigureAwait(false);
            return Results.Json(payload, jsonOptions);
        })
        .WithName("GetResearchWorkspace")
        .Produces(200);

        group.MapGet("/trading", async (HttpContext context) =>
        {
            var payload = await BuildTradingPayloadAsync(context).ConfigureAwait(false);
            return Results.Json(payload, jsonOptions);
        })
        .WithName("GetTradingWorkspace")
        .Produces(200);

        group.MapGet("/data-operations", async (HttpContext context) =>
        {
            var payload = await BuildDataOperationsPayloadAsync(context).ConfigureAwait(false);
            return Results.Json(payload, jsonOptions);
        })
        .WithName("GetDataOperationsWorkspace")
        .Produces(200);

        group.MapGet("/governance", async (HttpContext context) =>
        {
            var payload = await BuildGovernancePayloadAsync(context).ConfigureAwait(false);
            return Results.Json(payload, jsonOptions);
        })
        .WithName("GetGovernanceWorkspace")
        .Produces(200);

        group.MapPost("/reconciliation/runs", async (ReconciliationRunRequest request, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IReconciliationRunService>();
            if (service is null)
            {
                return Results.Problem("Reconciliation service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var detail = await service.RunAsync(request, context.RequestAborted).ConfigureAwait(false);
            return detail is null
                ? Results.NotFound()
                : Results.Json(detail, jsonOptions);
        })
        .WithName("CreateReconciliationRun")
        .Produces<ReconciliationRunDetail>(200)
        .Produces(404);

        group.MapGet("/reconciliation/runs/{reconciliationRunId}", async (string reconciliationRunId, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IReconciliationRunService>();
            if (service is null)
            {
                return Results.Problem("Reconciliation service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var detail = await service.GetByIdAsync(reconciliationRunId, context.RequestAborted).ConfigureAwait(false);
            return detail is null
                ? Results.NotFound()
                : Results.Json(detail, jsonOptions);
        })
        .WithName("GetReconciliationRun")
        .Produces<ReconciliationRunDetail>(200)
        .Produces(404);

        group.MapGet("/runs/{runId}/reconciliation", async (string runId, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IReconciliationRunService>();
            if (service is null)
            {
                return Results.Problem("Reconciliation service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var detail = await service.GetLatestForRunAsync(runId, context.RequestAborted).ConfigureAwait(false);
            return detail is null
                ? Results.NotFound()
                : Results.Json(detail, jsonOptions);
        })
        .WithName("GetLatestRunReconciliation")
        .Produces<ReconciliationRunDetail>(200)
        .Produces(404);

        group.MapGet("/runs/{runId}/reconciliation/history", async (string runId, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IReconciliationRunService>();
            if (service is null)
            {
                return Results.Problem("Reconciliation service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var history = await service.GetHistoryForRunAsync(runId, context.RequestAborted).ConfigureAwait(false);
            return history.Count == 0
                ? Results.NotFound()
                : Results.Json(history, jsonOptions);
        })
        .WithName("GetRunReconciliationHistory")
        .Produces<IReadOnlyList<ReconciliationRunSummary>>(200)
        .Produces(404);

        group.MapGet("/runs/{runId}/ledger", async (string runId, HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var summary = await readService.GetLedgerSummaryAsync(runId, context.RequestAborted).ConfigureAwait(false);
            return summary is null
                ? Results.NotFound()
                : Results.Json(summary, jsonOptions);
        })
        .WithName("GetRunLedger")
        .Produces<LedgerSummary>(200)
        .Produces(404);

        group.MapGet("/runs/{runId}/equity-curve", async (string runId, HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var curve = await readService.GetEquityCurveAsync(runId, context.RequestAborted).ConfigureAwait(false);
            return curve is null
                ? Results.NotFound()
                : Results.Json(curve, jsonOptions);
        })
        .WithName("GetRunEquityCurve")
        .Produces<EquityCurveSummary>(200)
        .Produces(404)
        .Produces(501);

        group.MapGet("/runs/{runId}/fills", async (string runId, string? symbol, HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var summary = await readService.GetFillsAsync(runId, context.RequestAborted).ConfigureAwait(false);
            if (summary is null)
            {
                return Results.NotFound();
            }

            if (!string.IsNullOrWhiteSpace(symbol))
            {
                var filtered = summary with
                {
                    Fills = summary.Fills
                        .Where(f => string.Equals(f.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                        .ToArray(),
                    TotalFills = summary.Fills
                        .Count(f => string.Equals(f.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                };
                return Results.Json(filtered, jsonOptions);
            }

            return Results.Json(summary, jsonOptions);
        })
        .WithName("GetRunFills")
        .Produces<RunFillSummary>(200)
        .Produces(404)
        .Produces(501);

        group.MapGet("/runs/{runId}/attribution", async (string runId, HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var attribution = await readService.GetAttributionAsync(runId, context.RequestAborted).ConfigureAwait(false);
            return attribution is null
                ? Results.NotFound()
                : Results.Json(attribution, jsonOptions);
        })
        .WithName("GetRunAttribution")
        .Produces<RunAttributionSummary>(200)
        .Produces(404)
        .Produces(501);

        group.MapGet("/runs/{runId}/ledger/trial-balance", async (string runId, string? accountType, HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var summary = await readService.GetLedgerSummaryAsync(runId, context.RequestAborted).ConfigureAwait(false);
            if (summary is null)
            {
                return Results.NotFound();
            }

            var lines = string.IsNullOrWhiteSpace(accountType)
                ? summary.TrialBalance
                : summary.TrialBalance
                    .Where(l => string.Equals(l.AccountType, accountType, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            return Results.Json(lines, jsonOptions);
        })
        .WithName("GetRunLedgerTrialBalance")
        .Produces<IReadOnlyList<LedgerTrialBalanceLine>>(200)
        .Produces(404);

        group.MapGet("/runs/{runId}/ledger/journal", async (
            string runId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var summary = await readService.GetLedgerSummaryAsync(runId, context.RequestAborted).ConfigureAwait(false);
            if (summary is null)
            {
                return Results.NotFound();
            }

            IEnumerable<LedgerJournalLine> entries = summary.Journal;
            if (from.HasValue)
            {
                entries = entries.Where(e => e.Timestamp >= from.Value);
            }

            if (to.HasValue)
            {
                entries = entries.Where(e => e.Timestamp <= to.Value);
            }

            return Results.Json(entries.ToArray(), jsonOptions);
        })
        .WithName("GetRunLedgerJournal")
        .Produces<IReadOnlyList<LedgerJournalLine>>(200)
        .Produces(404);

        group.MapGet("/security-master/securities", async (
            string? query,
            int? take,
            bool activeOnly,
            HttpContext context) =>
        {
            var queryService = context.RequestServices.GetService<ISecurityMasterQueryService>();
            if (queryService is null)
            {
                return Results.Problem("Security Master query service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return Results.BadRequest(new { error = "Query is required." });
            }

            var request = new SecuritySearchRequest(
                Query: query.Trim(),
                Take: Math.Clamp(take ?? 25, 1, 100),
                ActiveOnly: activeOnly);
            var results = await queryService.SearchAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(results.Select(MapToWorkstationSecurity).ToArray(), jsonOptions);
        })
        .WithName("SearchSecurityMasterWorkstation")
        .Produces<IReadOnlyList<SecurityMasterWorkstationDto>>(200)
        .Produces(400)
        .Produces(501);

        group.MapGet("/security-master/securities/{securityId:guid}", async (Guid securityId, HttpContext context) =>
        {
            var queryService = context.RequestServices.GetService<ISecurityMasterQueryService>();
            if (queryService is null)
            {
                return Results.Problem("Security Master query service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var detail = await queryService.GetByIdAsync(securityId, context.RequestAborted).ConfigureAwait(false);
            return detail is null
                ? Results.NotFound()
                : Results.Json(MapToWorkstationSecurity(detail), jsonOptions);
        })
        .WithName("GetSecurityMasterWorkstationSecurity")
        .Produces<SecurityMasterWorkstationDto>(200)
        .Produces(404)
        .Produces(501);

        group.MapGet("/security-master/securities/{securityId:guid}/history", async (
            Guid securityId,
            int? take,
            HttpContext context) =>
        {
            var queryService = context.RequestServices.GetService<ISecurityMasterQueryService>();
            if (queryService is null)
            {
                return Results.Problem("Security Master query service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var history = await queryService.GetHistoryAsync(
                    new SecurityHistoryRequest(
                        SecurityId: securityId,
                        Take: Math.Clamp(take ?? 50, 1, 500)),
                    context.RequestAborted)
                .ConfigureAwait(false);

            return history.Count == 0
                ? Results.NotFound()
                : Results.Json(history, jsonOptions);
        })
        .WithName("GetSecurityMasterWorkstationSecurityHistory")
        .Produces<IReadOnlyList<SecurityMasterEventEnvelope>>(200)
        .Produces(404)
        .Produces(501);

        group.MapGet("/security-master/securities/{securityId:guid}/identity", async (Guid securityId, HttpContext context) =>
        {
            var queryService = context.RequestServices.GetService<ISecurityMasterQueryService>();
            if (queryService is null)
            {
                return Results.Problem("Security Master query service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var detail = await queryService.GetByIdAsync(securityId, context.RequestAborted).ConfigureAwait(false);
            return detail is null
                ? Results.NotFound()
                : Results.Json(MapToIdentityDrillIn(detail), jsonOptions);
        })
        .WithName("GetSecurityMasterWorkstationIdentityDrillIn")
        .Produces<SecurityIdentityDrillInDto>(200)
        .Produces(404)
        .Produces(501);

        group.MapGet("/security-master/securities/{securityId:guid}/economic-definition", async (Guid securityId, HttpContext context) =>
        {
            var queryService = context.RequestServices.GetService<ISecurityMasterQueryService>();
            if (queryService is null)
            {
                return Results.Problem("Security Master query service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var record = await queryService.GetEconomicDefinitionByIdAsync(securityId, context.RequestAborted).ConfigureAwait(false);
            return record is null
                ? Results.NotFound()
                : Results.Json(MapToEconomicDefinitionSummary(record), jsonOptions);
        })
        .WithName("GetSecurityMasterWorkstationEconomicDefinition")
        .Produces<SecurityEconomicDefinitionSummaryDto>(200)
        .Produces(404)
        .Produces(501);

        // --- Multi-run comparison and diff ---

        group.MapPost("/runs/compare", async (RunComparisonRequest request, HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            if (request.RunIds is not { Count: >= 2 })
            {
                return Results.BadRequest(new { error = "At least two run IDs are required for comparison." });
            }

            var comparison = await readService.CompareRunsAsync(request.RunIds, context.RequestAborted).ConfigureAwait(false);
            if (request.Modes is { Count: > 0 })
            {
                var modeFilter = new HashSet<StrategyRunMode>(request.Modes);
                comparison = comparison.Where(row => modeFilter.Contains(row.Mode)).ToArray();
            }

            return Results.Json(comparison, jsonOptions);
        })
        .WithName("CompareRuns")
        .Produces<IReadOnlyList<StrategyRunComparison>>(200)
        .Produces(400)
        .Produces(501);

        group.MapPost("/runs/diff", async (RunDiffRequest request, HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var baseDetail = await readService.GetRunDetailAsync(request.BaseRunId, context.RequestAborted).ConfigureAwait(false);
            var targetDetail = await readService.GetRunDetailAsync(request.TargetRunId, context.RequestAborted).ConfigureAwait(false);

            if (baseDetail is null || targetDetail is null)
            {
                return Results.NotFound(new { error = "One or both run IDs not found." });
            }

            var diff = BuildRunDiff(baseDetail, targetDetail);
            return Results.Json(diff, jsonOptions);
        })
        .WithName("DiffRuns")
        .Produces<StrategyRunDiff>(200)
        .Produces(404)
        .Produces(501);

        app.MapGet("/api/strategies/{strategyId}/runs", async (string strategyId, string? type, HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            RunType? runType = null;
            if (!string.IsNullOrWhiteSpace(type) &&
                Enum.TryParse<RunType>(type, ignoreCase: true, out var parsed))
            {
                runType = parsed;
            }

            var runs = await readService.GetRunsAsync(strategyId, runType, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(runs, jsonOptions);
        })
        .WithName("GetStrategyRuns")
        .WithTags("Strategies")
        .Produces<IReadOnlyList<StrategyRunSummary>>(200);

        group.MapGet("/runs/history", async (
            string? mode,
            StrategyRunStatus? status,
            string? strategyId,
            int? limit,
            HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var modes = ParseModes(mode);
            var runs = await readService.GetRunsAsync(
                    new StrategyRunHistoryQuery(
                        Modes: modes,
                        Status: status,
                        StrategyId: strategyId,
                        Limit: Math.Clamp(limit ?? 50, 1, 500)),
                    context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(runs, jsonOptions);
        })
        .WithName("GetWorkstationRunHistory")
        .Produces<IReadOnlyList<StrategyRunSummary>>(200)
        .Produces(501);

        group.MapGet("/runs/timeline", async (
            string? mode,
            StrategyRunStatus? status,
            string? strategyId,
            int? limit,
            HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var modes = ParseModes(mode);
            var timeline = await readService.GetMergedTimelineAsync(
                    new StrategyRunHistoryQuery(
                        Modes: modes,
                        Status: status,
                        StrategyId: strategyId,
                        Limit: Math.Clamp(limit ?? 100, 1, 500)),
                    context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(timeline, jsonOptions);
        })
        .WithName("GetWorkstationMergedRunTimeline")
        .Produces<IReadOnlyList<StrategyRunTimelineEntry>>(200)
        .Produces(501);

        // --- Portfolio cash-flow projections ---
        var portfolioGroup = app.MapGroup("/api/portfolio").WithTags("Portfolio");

        portfolioGroup.MapGet("/{runId}/cash-flows", async (
            string runId,
            DateTimeOffset? asOf,
            string? currency,
            int? bucketDays,
            HttpContext context) =>
        {
            var projectionService = context.RequestServices.GetService<CashFlowProjectionService>();
            if (projectionService is null)
            {
                return Results.Problem(
                    "Cash flow projection service is not registered.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            var summary = await projectionService
                .GetAsync(runId, asOf, currency, bucketDays, context.RequestAborted)
                .ConfigureAwait(false);

            return summary is null
                ? Results.NotFound()
                : Results.Json(summary, jsonOptions);
        })
        .WithName("GetPortfolioCashFlows")
        .Produces<RunCashFlowSummary>(200)
        .Produces(404)
        .Produces(501);

        app.MapGet("/workstation", (IWebHostEnvironment environment) => ServeWorkstationIndex(environment))
            .ExcludeFromDescription();

        app.MapGet("/workstation/{*path}", (string? path, IWebHostEnvironment environment) =>
            string.IsNullOrWhiteSpace(path) || !Path.HasExtension(path)
                ? ServeWorkstationIndex(environment)
                : Results.NotFound())
            .ExcludeFromDescription();
    }

    private static StrategyRunDiff BuildRunDiff(StrategyRunDetail baseRun, StrategyRunDetail targetRun)
    {
        var basePositions = baseRun.Portfolio?.Positions ?? [];
        var targetPositions = targetRun.Portfolio?.Positions ?? [];

        var baseSymbols = new HashSet<string>(basePositions.Select(static p => p.Symbol), StringComparer.OrdinalIgnoreCase);
        var targetSymbols = new HashSet<string>(targetPositions.Select(static p => p.Symbol), StringComparer.OrdinalIgnoreCase);

        var added = targetPositions
            .Where(p => !baseSymbols.Contains(p.Symbol))
            .Select(static p => new PositionDiffEntry(p.Symbol, 0, p.Quantity, 0m, p.RealizedPnl + p.UnrealizedPnl, "Added"))
            .ToList();

        var removed = basePositions
            .Where(p => !targetSymbols.Contains(p.Symbol))
            .Select(static p => new PositionDiffEntry(p.Symbol, p.Quantity, 0, p.RealizedPnl + p.UnrealizedPnl, 0m, "Removed"))
            .ToList();

        var modified = new List<PositionDiffEntry>();
        foreach (var basePos in basePositions.Where(p => targetSymbols.Contains(p.Symbol)))
        {
            var targetPos = targetPositions.First(p =>
                string.Equals(p.Symbol, basePos.Symbol, StringComparison.OrdinalIgnoreCase));
            if (basePos.Quantity != targetPos.Quantity ||
                basePos.AverageCostBasis != targetPos.AverageCostBasis)
            {
                modified.Add(new PositionDiffEntry(
                    basePos.Symbol,
                    basePos.Quantity,
                    targetPos.Quantity,
                    basePos.RealizedPnl + basePos.UnrealizedPnl,
                    targetPos.RealizedPnl + targetPos.UnrealizedPnl,
                    "Modified"));
            }
        }

        var paramDiffs = BuildParameterDiff(baseRun.Parameters, targetRun.Parameters);

        var metricsDiff = new MetricsDiff(
            NetPnlDelta: (targetRun.Summary.NetPnl ?? 0m) - (baseRun.Summary.NetPnl ?? 0m),
            TotalReturnDelta: (targetRun.Summary.TotalReturn ?? 0m) - (baseRun.Summary.TotalReturn ?? 0m),
            FillCountDelta: targetRun.Summary.FillCount - baseRun.Summary.FillCount,
            BaseNetPnl: baseRun.Summary.NetPnl,
            TargetNetPnl: targetRun.Summary.NetPnl,
            BaseTotalReturn: baseRun.Summary.TotalReturn,
            TargetTotalReturn: targetRun.Summary.TotalReturn);

        return new StrategyRunDiff(
            BaseRunId: baseRun.Summary.RunId,
            TargetRunId: targetRun.Summary.RunId,
            BaseStrategyName: baseRun.Summary.StrategyName,
            TargetStrategyName: targetRun.Summary.StrategyName,
            AddedPositions: added,
            RemovedPositions: removed,
            ModifiedPositions: modified,
            ParameterChanges: paramDiffs,
            Metrics: metricsDiff);
    }

    private static IReadOnlyList<ParameterDiff> BuildParameterDiff(
        IReadOnlyDictionary<string, string> baseParams,
        IReadOnlyDictionary<string, string> targetParams)
    {
        var diffs = new List<ParameterDiff>();
        var allKeys = new HashSet<string>(baseParams.Keys.Concat(targetParams.Keys), StringComparer.Ordinal);

        foreach (var key in allKeys.Order())
        {
            baseParams.TryGetValue(key, out var baseVal);
            targetParams.TryGetValue(key, out var targetVal);

            if (!string.Equals(baseVal, targetVal, StringComparison.Ordinal))
            {
                diffs.Add(new ParameterDiff(key, baseVal, targetVal));
            }
        }

        return diffs;
    }

    private static async Task<object> BuildSessionPayloadAsync(HttpContext context)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        if (readService is null)
        {
            return new
            {
                displayName = "Meridian Operator",
                role = "Research Lead",
                environment = "paper",
                activeWorkspace = "research",
                commandCount = 6
            };
        }

        var runs = (await readService.GetRunsAsync(ct: context.RequestAborted).ConfigureAwait(false)).ToArray();
        var latest = runs.FirstOrDefault();
        var latestDetail = latest is null
            ? null
            : await readService.GetRunDetailAsync(latest.RunId, context.RequestAborted).ConfigureAwait(false);
        var activeRuns = runs.Count(static run => run.Status is StrategyRunStatus.Running or StrategyRunStatus.Paused);
        var reviewRuns = runs.Count(static run => run.Promotion?.RequiresReview == true || run.Status is StrategyRunStatus.Failed or StrategyRunStatus.Cancelled);

        return new
        {
            displayName = BuildDisplayName(latest),
            role = BuildRole(latest),
            environment = MapEnvironment(latest),
            activeWorkspace = MapWorkspace(latest),
            commandCount = Math.Max(6, runs.Length + activeRuns + reviewRuns),
            latestRun = latest is null ? null : BuildRunDigest(latest, latestDetail),
            workspaceSummary = new
            {
                totalRuns = runs.Length,
                activeRuns,
                reviewRuns,
                ledgerCoverage = runs.Count(static run => !string.IsNullOrWhiteSpace(run.LedgerReference)),
                portfolioCoverage = runs.Count(static run => !string.IsNullOrWhiteSpace(run.PortfolioId))
            }
        };
    }

    private static async Task<object> BuildResearchPayloadAsync(HttpContext context)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        if (readService is null)
        {
            return BuildResearchFallbackPayload();
        }

        var runs = (await readService.GetRunsAsync(ct: context.RequestAborted).ConfigureAwait(false))
            .Take(6)
            .ToArray();
        var runDetails = await Task.WhenAll(
                runs.Select(run => readService.GetRunDetailAsync(run.RunId, context.RequestAborted)))
            .ConfigureAwait(false);

        if (runs.Length == 0)
        {
            return new
            {
                metrics = new[]
                {
                    new { id = "active-runs", label = "Active Runs", value = "0", delta = "0%", tone = "success" },
                    new { id = "queued-runs", label = "Queued Promotions", value = "0", delta = "0%", tone = "default" },
                    new { id = "review-runs", label = "Needs Review", value = "0", delta = "0%", tone = "warning" },
                    new { id = "winning-runs", label = "Positive P&L", value = "0", delta = "0%", tone = "default" }
                },
                runs = Array.Empty<object>(),
                workspace = new { totalRuns = 0, latestRunId = (string?)null, hasLedgerCoverage = false, hasPortfolioCoverage = false }
            };
        }

        var activeRuns = runs.Count(static run => run.Status is StrategyRunStatus.Running or StrategyRunStatus.Paused);
        var queuedPromotions = runs.Count(static run => run.Promotion is { RequiresReview: true } &&
            run.Promotion.State is StrategyRunPromotionState.CandidateForPaper or StrategyRunPromotionState.CandidateForLive);
        var reviewRuns = runs.Count(static run => run.Promotion?.RequiresReview == true || run.Status is StrategyRunStatus.Failed or StrategyRunStatus.Cancelled);
        var winningRuns = runs.Count(static run => (run.NetPnl ?? 0m) > 0m);
        var latestRun = runs[0];

        return new
        {
            metrics = new[]
            {
                new { id = "active-runs", label = "Active Runs", value = activeRuns.ToString(CultureInfo.InvariantCulture), delta = activeRuns == 0 ? "0%" : $"+{activeRuns}", tone = "success" },
                new { id = "queued-runs", label = "Queued Promotions", value = queuedPromotions.ToString(CultureInfo.InvariantCulture), delta = queuedPromotions == 0 ? "0%" : $"+{queuedPromotions}", tone = "default" },
                new { id = "review-runs", label = "Needs Review", value = reviewRuns.ToString(CultureInfo.InvariantCulture), delta = reviewRuns == 0 ? "0%" : $"-{reviewRuns}", tone = "warning" },
                new { id = "winning-runs", label = "Positive P&L", value = winningRuns.ToString(CultureInfo.InvariantCulture), delta = winningRuns == 0 ? "0%" : $"+{winningRuns}", tone = "default" }
            },
            runs = runs
                .Zip(runDetails, static (run, detail) => BuildResearchRunCard(run, detail))
                .ToArray(),
            comparisons = BuildModeComparisons(runs),
            timeline = runs.Select(BuildTimelineCard).ToArray(),
            workspace = new
            {
                totalRuns = runs.Length,
                latestRunId = latestRun.RunId,
                latestStrategyName = latestRun.StrategyName,
                hasLedgerCoverage = runs.Any(static run => !string.IsNullOrWhiteSpace(run.LedgerReference)),
                hasPortfolioCoverage = runs.Any(static run => !string.IsNullOrWhiteSpace(run.PortfolioId)),
                promotionCandidates = queuedPromotions
            }
        };
    }

    private static object BuildResearchFallbackPayload()
    {
        return new
        {
            metrics = new[]
            {
                new { id = "active-runs", label = "Active Runs", value = "24", delta = "+8%", tone = "success" },
                new { id = "queued-runs", label = "Queued Promotions", value = "3", delta = "0%", tone = "default" },
                new { id = "review-runs", label = "Needs Review", value = "2", delta = "-1%", tone = "warning" },
                new { id = "winning-runs", label = "Positive P&L", value = "17", delta = "+4%", tone = "default" }
            },
            runs = new[]
            {
                new
                {
                    id = "run-research-001",
                    strategyName = "Mean Reversion FX",
                    engine = "Meridian Native",
                    mode = "paper",
                    status = "Running",
                    dataset = "FX Majors",
                    window = "90d",
                    pnl = "+4.2%",
                    sharpe = "1.41",
                    lastUpdated = "2m ago",
                    notes = "Primary paper candidate with stable fill quality and healthy depth coverage.",
                    securityCoverage = new
                    {
                        portfolioResolved = 0,
                        portfolioMissing = 0,
                        ledgerResolved = 0,
                        ledgerMissing = 0,
                        hasIssues = false,
                        tone = "default",
                        summary = "Security Master coverage not yet evaluated.",
                        resolvedReferences = Array.Empty<SecurityCoverageReferencePayload>(),
                        missingReferences = Array.Empty<SecurityCoverageGapPayload>()
                    }
                }
            }
        };
    }

    private static async Task<object> BuildTradingPayloadAsync(HttpContext context)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        var portfolio = context.RequestServices.GetService<IPortfolioState>();
        var oms = context.RequestServices.GetService<IOrderManager>();

        // When neither execution layer nor strategy run service is active, use fixture data
        if (portfolio is null && oms is null && readService is null)
        {
            return BuildTradingFallbackPayload();
        }

        // Resolve the most relevant paper run (for run-level metadata)
        StrategyRunSummary? run = null;
        if (readService is not null)
        {
            var runs = (await readService.GetRunsAsync(ct: context.RequestAborted).ConfigureAwait(false)).ToArray();
            run = runs.FirstOrDefault(static candidate => candidate.Mode == StrategyRunMode.Paper) ?? runs.FirstOrDefault();
        }

        // --- Metrics (prefer live data, fall back to run-level metrics) ---
        var realisedPnl = portfolio?.RealisedPnl ?? run?.NetPnl ?? 0m;
        var unrealisedPnl = portfolio?.UnrealisedPnl ?? 0m;
        var totalPnl = realisedPnl + unrealisedPnl;
        var openOrderCount = oms?.GetOpenOrders().Count ?? 0;
        var pnlTone = totalPnl >= 0m ? "success" : "warning";

        // --- Positions (live execution layer when available) ---
        object[] positions;
        if (portfolio is not null && portfolio.Positions.Count > 0)
        {
            positions = portfolio.Positions.Values.Select(pos => (object)new
            {
                symbol = pos.Symbol,
                side = pos.Quantity >= 0 ? "Long" : "Short",
                quantity = Math.Abs(pos.Quantity).ToString(CultureInfo.InvariantCulture),
                averagePrice = pos.AverageCostBasis.ToString("F2", CultureInfo.InvariantCulture),
                markPrice = "—",
                dayPnl = "—",
                unrealizedPnl = FormatCurrency(pos.UnrealisedPnl),
                exposure = "—"
            }).ToArray();
        }
        else
        {
            // No live positions yet — show an informational placeholder row
            positions =
            [
                new { symbol = "—", side = "—", quantity = "—", averagePrice = "—", markPrice = "—", dayPnl = "—", unrealizedPnl = "—", exposure = "No open positions" }
            ];
        }

        // --- Open orders (live OMS when available) ---
        object[] openOrders;
        if (oms is not null)
        {
            openOrders = oms.GetOpenOrders().Select(static order => (object)new
            {
                orderId = order.OrderId,
                symbol = order.Symbol,
                side = order.Side.ToString(),
                type = order.Type.ToString(),
                quantity = order.Quantity.ToString(CultureInfo.InvariantCulture),
                limitPrice = order.LimitPrice.HasValue ? order.LimitPrice.Value.ToString("F2", CultureInfo.InvariantCulture) : "—",
                status = order.Status.ToString(),
                submittedAt = order.CreatedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " UTC"
            }).ToArray();
        }
        else
        {
            openOrders = [];
        }

        // --- Risk state (derived from live portfolio when available) ---
        var riskState = "Healthy";
        var riskSummary = "Portfolio and order-book exposure are within configured paper thresholds.";
        var grossExposure = 0m;
        var netExposureValue = 0m;

        if (portfolio is not null)
        {
            grossExposure = portfolio.Positions.Values.Sum(static pos => Math.Abs(pos.AverageCostBasis * pos.Quantity));
            netExposureValue = portfolio.Positions.Values.Sum(pos => pos.AverageCostBasis * pos.Quantity);
            var drawdownPct = portfolio.PortfolioValue > 0m
                ? totalPnl / portfolio.PortfolioValue
                : 0m;

            if (drawdownPct < -0.05m)
            {
                riskState = "Constrained";
                riskSummary = "Portfolio has breached the 5% drawdown threshold. Promotion to live is blocked.";
            }
            else if (drawdownPct < -0.02m)
            {
                riskState = "Observe";
                riskSummary = "Exposure nearing guardrail limits. Monitoring intraday drawdown closely.";
            }
        }
        else if (run is not null && run.NetPnl.HasValue && run.NetPnl < 0m)
        {
            riskState = "Observe";
            riskSummary = "Strategy is running at a loss. Monitoring active.";
        }

        var maxDrawdownDisplay = portfolio is not null && portfolio.PortfolioValue > 0m
            ? FormatPercent(totalPnl / portfolio.PortfolioValue)
            : "—";

        // --- Fills (completed orders from OMS) ---
        object[] fills;
        if (oms is not null)
        {
            fills = oms.GetCompletedOrders(20).Select(static order => (object)new
            {
                fillId = order.OrderId,
                orderId = order.OrderId,
                symbol = order.Symbol,
                side = order.Side.ToString(),
                quantity = order.FilledQuantity.ToString(CultureInfo.InvariantCulture),
                price = order.AverageFillPrice.HasValue
                    ? order.AverageFillPrice.Value.ToString("F2", CultureInfo.InvariantCulture)
                    : "—",
                venue = "Paper",
                timestamp = (order.LastUpdatedAt ?? order.CreatedAt).ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " UTC"
            }).ToArray();
        }
        else
        {
            fills = Array.Empty<object>();
        }

        return new
        {
            metrics = new[]
            {
                new { id = "trading-net-pnl", label = "Net P&L", value = FormatCurrency(totalPnl), delta = totalPnl >= 0m ? "+session" : "-session", tone = pnlTone },
                new { id = "trading-open-orders", label = "Open Orders", value = openOrderCount.ToString(CultureInfo.InvariantCulture), delta = openOrderCount == 0 ? "0" : $"+{openOrderCount}", tone = "default" },
                new { id = "trading-cash", label = "Cash", value = portfolio is not null ? FormatCurrency(portfolio.Cash) : "—", delta = "0%", tone = "default" },
                new { id = "trading-portfolio-value", label = "Portfolio Value", value = portfolio is not null ? FormatCurrency(portfolio.PortfolioValue) : "—", delta = "0%", tone = "default" }
            },
            positions,
            openOrders,
            fills,
            risk = new
            {
                state = riskState,
                summary = riskSummary,
                netExposure = portfolio is not null ? FormatCurrency(netExposureValue) : "—",
                grossExposure = portfolio is not null ? FormatCurrency(grossExposure) : "—",
                var95 = "—",
                maxDrawdown = maxDrawdownDisplay,
                buyingPowerUsed = "—",
                activeGuardrails = new[]
                {
                    "Single-name concentration cap set at 30% notional.",
                    "Auto-throttle activates above 70% intraday buying power.",
                    "Strategy promotion to live blocked while state is Observe or Constrained."
                }
            },
            brokerage = new
            {
                provider = "Interactive Brokers",
                account = run is not null && !string.IsNullOrWhiteSpace(run.PortfolioId) ? run.PortfolioId : "—",
                environment = "paper",
                connection = portfolio is not null ? "Connected" : "Disconnected",
                lastHeartbeat = portfolio is not null ? "live" : "—",
                orderIngress = oms is not null ? "healthy" : "—",
                fillFeed = portfolio is not null ? "healthy" : "—",
                notes = portfolio is not null
                    ? "Live execution state from PaperTradingPortfolio and OrderManagementSystem."
                    : "Paper gateway not active. Start a paper session to see live position and order data."
            },
            comparisons = run is null ? Array.Empty<object>() : BuildModeComparisons([run]),
            drillIn = run is null ? null : BuildRunDrillInLinks(run)
        };
    }

    private static object BuildTradingFallbackPayload()
    {
        return new
        {
            metrics = new[]
            {
                new { id = "trading-net-pnl", label = "Net P&L", value = "+$3,918", delta = "+2.4%", tone = "success" },
                new { id = "trading-open-orders", label = "Open Orders", value = "5", delta = "+1", tone = "default" },
                new { id = "trading-fills", label = "Fills Today", value = "27", delta = "+7", tone = "success" },
                new { id = "trading-risk-state", label = "Risk State", value = "Healthy", delta = "0%", tone = "success" }
            },
            positions = new[]
            {
                new { symbol = "AAPL", side = "Long", quantity = "300", averagePrice = "188.22", markPrice = "189.30", dayPnl = "+$324", unrealizedPnl = "+$1,126", exposure = "$56,790" },
                new { symbol = "MSFT", side = "Long", quantity = "150", averagePrice = "416.10", markPrice = "414.80", dayPnl = "-$195", unrealizedPnl = "-$195", exposure = "$62,220" }
            },
            openOrders = new[]
            {
                new { orderId = "PO-24812", symbol = "AMZN", side = "Buy", type = "Limit", quantity = "100", limitPrice = "184.00", status = "Working", submittedAt = "09:35:12 ET" },
                new { orderId = "PO-24814", symbol = "QQQ", side = "Sell", type = "Stop", quantity = "40", limitPrice = "442.30", status = "Pending Routing", submittedAt = "09:36:48 ET" }
            },
            fills = new[]
            {
                new { fillId = "FL-90071", orderId = "PO-24810", symbol = "AAPL", side = "Buy", quantity = "50", price = "188.12", venue = "NASDAQ", timestamp = "09:33:04 ET" },
                new { fillId = "FL-90077", orderId = "PO-24811", symbol = "MSFT", side = "Sell", quantity = "25", price = "414.88", venue = "IEX", timestamp = "09:34:26 ET" }
            },
            risk = new
            {
                state = "Healthy",
                summary = "Portfolio and order-book exposure are within configured paper thresholds.",
                netExposure = "$119,010",
                grossExposure = "$156,432",
                var95 = "$9,874",
                maxDrawdown = "-0.9%",
                buyingPowerUsed = "44%",
                activeGuardrails = new[]
                {
                    "Daily loss guard set to -$12,000.",
                    "Max position notional guard set to $120,000.",
                    "Kill-switch can be engaged manually from governance lane."
                }
            },
            brokerage = new
            {
                provider = "Interactive Brokers",
                account = "DU1009034",
                environment = "paper",
                connection = "Connected",
                lastHeartbeat = "1s ago",
                orderIngress = "healthy (p50 19ms)",
                fillFeed = "healthy (p50 31ms)",
                notes = "Paper execution routing is synchronized with run-level reconciliation wiring."
            }
        };
    }

    private static async Task<object> BuildDataOperationsPayloadAsync(HttpContext context)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        var configStore = context.RequestServices.GetService<Meridian.Application.UI.ConfigStore>();

        if (readService is null && configStore is null)
        {
            return BuildDataOperationsFallbackPayload();
        }

        var runs = readService is not null
            ? (await readService.GetRunsAsync(ct: context.RequestAborted).ConfigureAwait(false)).ToArray()
            : [];
        var activeRuns = runs.Count(static run => run.Status is StrategyRunStatus.Running or StrategyRunStatus.Paused);
        var reviewRuns = runs.Count(static run => run.Promotion?.RequiresReview == true || run.Status is StrategyRunStatus.Failed or StrategyRunStatus.Cancelled);

        // --- Providers (real data from metrics store when available) ---
        var metricsStatus = configStore?.TryLoadProviderMetrics();
        var healthyProviderCount = metricsStatus?.HealthyProviders ?? 0;
        object[] providers = metricsStatus is { Providers.Length: > 0 }
            ? metricsStatus.Providers.Select(static p => (object)new
            {
                provider = p.ProviderId,
                status = p.IsConnected ? "Healthy" : "Offline",
                capability = p.ProviderType,
                latency = $"{p.AverageLatencyMs:F0}ms p50",
                note = p.IsConnected
                    ? $"Active subscriptions: {p.ActiveSubscriptions}. Quality score: {p.DataQualityScore:P0}."
                    : $"Provider disconnected. Last seen: {p.Timestamp:HH:mm} UTC."
            }).ToArray()
            : [];

        // --- Backfills (last known backfill result from status file) ---
        var lastBackfill = configStore?.TryLoadBackfillStatus();
        object[] backfills;
        if (lastBackfill is not null)
        {
            var symbolSummary = lastBackfill.Symbols.Length > 0
                ? string.Join(", ", lastBackfill.Symbols.Take(3)) + (lastBackfill.Symbols.Length > 3 ? " …" : "")
                : "unknown";
            var days = (lastBackfill.To != null && lastBackfill.From != null)
                ? (lastBackfill.To.Value.DayNumber - lastBackfill.From.Value.DayNumber).ToString(CultureInfo.InvariantCulture) + "d"
                : "—";
            var age = DateTimeOffset.UtcNow - lastBackfill.CompletedUtc;
            var updatedAt = age.TotalMinutes < 60
                ? $"{(int)age.TotalMinutes}m ago"
                : $"{(int)age.TotalHours}h ago";
            backfills =
            [
                new
                {
                    jobId = $"BF-{Math.Abs(lastBackfill.GetHashCode()) % 10000:D4}",
                    scope = $"{symbolSummary} / {days}",
                    provider = lastBackfill.Provider,
                    status = lastBackfill.Success ? "Completed" : "Failed",
                    progress = lastBackfill.Success ? "100%" : "Error",
                    updatedAt
                }
            ];
        }
        else
        {
            backfills = [];
        }

        return new
        {
            metrics = new[]
            {
                new { id = "providers-healthy", label = "Providers Healthy", value = healthyProviderCount.ToString(CultureInfo.InvariantCulture), delta = "0", tone = healthyProviderCount > 0 ? "success" : "default" },
                new { id = "backfills-running", label = "Backfills Running", value = activeRuns.ToString(CultureInfo.InvariantCulture), delta = activeRuns == 0 ? "0" : $"+{activeRuns}", tone = activeRuns > 0 ? "default" : "success" },
                new { id = "exports-ready", label = "Exports Ready", value = "0", delta = "0", tone = "default" },
                new { id = "ops-review", label = "Needs Review", value = reviewRuns.ToString(CultureInfo.InvariantCulture), delta = reviewRuns == 0 ? "0" : $"+{reviewRuns}", tone = reviewRuns == 0 ? "default" : "warning" }
            },
            providers,
            backfills,
            exports = Array.Empty<object>()
        };
    }

    private static object BuildDataOperationsFallbackPayload()
    {
        return new
        {
            metrics = new[]
            {
                new { id = "providers-healthy", label = "Providers Healthy", value = "4", delta = "0", tone = "success" },
                new { id = "backfills-running", label = "Backfills Running", value = "2", delta = "+1", tone = "default" },
                new { id = "exports-ready", label = "Exports Ready", value = "3", delta = "+1", tone = "success" },
                new { id = "ops-review", label = "Needs Review", value = "1", delta = "+1", tone = "warning" }
            },
            providers = new[]
            {
                new { provider = "Interactive Brokers", status = "Healthy", capability = "Execution + fills", latency = "21ms p50", note = "Paper adapter routing is available." },
                new { provider = "Polygon", status = "Healthy", capability = "Streaming equities", latency = "16ms p50", note = "Realtime subscriptions are steady." },
                new { provider = "Databento", status = "Warning", capability = "Historical replay", latency = "69ms p50", note = "Replay queue is elevated but within tolerance." }
            },
            backfills = new[]
            {
                new { jobId = "BF-1038", scope = "US equities / 30d", provider = "Databento", status = "Running", progress = "58%", updatedAt = "3m ago" },
                new { jobId = "BF-1040", scope = "FX majors / 14d", provider = "Polygon", status = "Queued", progress = "0%", updatedAt = "6m ago" }
            },
            exports = new[]
            {
                new { exportId = "EX-2196", profile = "python-pandas", target = "research pack", status = "Ready", rows = "118k", updatedAt = "7m ago" },
                new { exportId = "EX-2198", profile = "postgresql", target = "ops warehouse", status = "Attention", rows = "42k", updatedAt = "9m ago" }
            }
        };
    }

    private static async Task<object> BuildGovernancePayloadAsync(HttpContext context)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        if (readService is null)
        {
            return BuildGovernanceFallbackPayload();
        }

        var allRuns = (await readService.GetRunsAsync(ct: context.RequestAborted).ConfigureAwait(false)).ToArray();
        var runs = allRuns.Take(6).ToArray();
        if (runs.Length == 0)
        {
            return new
            {
                metrics = new[]
                {
                    new { id = "open-breaks", label = "Open Breaks", value = "0", tone = "success" },
                    new { id = "timing-drift", label = "Timing Drift", value = "0", tone = "default" },
                    new { id = "security-gaps", label = "Security Gaps", value = "0", tone = "success" },
                    new { id = "audit-ready", label = "Audit Ready", value = "0", tone = "default" }
                },
                reconciliationQueue = Array.Empty<object>(),
                workspace = new
                {
                    totalRuns = 0,
                    reconciledRuns = 0,
                    ledgerReadyRuns = 0,
                    openBreaks = 0,
                    securityIssues = 0
                },
                cashFlow = BuildGovernanceWorkspaceCashFlowSummary(Array.Empty<StrategyRunDetail?>()),
                reporting = BuildGovernanceReportingPayload()
            };
        }

        var reconciliationService = context.RequestServices.GetService<IReconciliationRunService>();
        var detailTasks = runs.Select(run => readService.GetRunDetailAsync(run.RunId, context.RequestAborted));
        var reconciliationTasks = reconciliationService is null
            ? runs.Select(_ => Task.FromResult<ReconciliationRunDetail?>(null))
            : runs.Select(run => reconciliationService.GetLatestForRunAsync(run.RunId, context.RequestAborted));

        var details = await Task.WhenAll(detailTasks).ConfigureAwait(false);
        var reconciliations = await Task.WhenAll(reconciliationTasks).ConfigureAwait(false);

        var openBreaks = reconciliations.Sum(static detail => detail?.Summary.OpenBreakCount ?? 0);
        var timingDriftRuns = reconciliations.Count(static detail => detail?.Summary.HasTimingDrift == true);
        var runsWithBreaks = reconciliations.Count(static detail => (detail?.Summary.BreakCount ?? 0) > 0);
        var runsWithSecurityIssues = details.Count(static detail =>
            (detail?.Portfolio?.SecurityMissingCount ?? 0) > 0 ||
            (detail?.Ledger?.SecurityMissingCount ?? 0) > 0);
        var auditReadyRuns = runs.Count(static run => !string.IsNullOrWhiteSpace(run.AuditReference)) - runsWithBreaks;

        return new
        {
            metrics = new[]
            {
                new { id = "open-breaks", label = "Open Breaks", value = openBreaks.ToString(CultureInfo.InvariantCulture), tone = openBreaks == 0 ? "success" : "warning" },
                new { id = "timing-drift", label = "Timing Drift", value = timingDriftRuns.ToString(CultureInfo.InvariantCulture), tone = timingDriftRuns == 0 ? "default" : "warning" },
                new { id = "security-gaps", label = "Security Gaps", value = runsWithSecurityIssues.ToString(CultureInfo.InvariantCulture), tone = runsWithSecurityIssues == 0 ? "success" : "warning" },
                new { id = "audit-ready", label = "Audit Ready", value = Math.Max(0, auditReadyRuns).ToString(CultureInfo.InvariantCulture), tone = auditReadyRuns > 0 ? "success" : "default" }
            },
            reconciliationQueue = runs
                .Zip(details, static (run, detail) => (run, detail))
                .Zip(reconciliations, static (pair, reconciliation) => BuildGovernanceRunCard(pair.run, pair.detail, reconciliation))
                .ToArray(),
            workspace = new
            {
                totalRuns = allRuns.Length,
                reconciledRuns = reconciliations.Count(static detail => detail is not null),
                ledgerReadyRuns = runs.Count(static run => !string.IsNullOrWhiteSpace(run.LedgerReference)),
                openBreaks,
                securityIssues = runsWithSecurityIssues
            },
            cashFlow = BuildGovernanceWorkspaceCashFlowSummary(details),
            reporting = BuildGovernanceReportingPayload()
        };
    }

    private static object BuildGovernanceFallbackPayload()
    {
        return new
        {
            metrics = new[]
            {
                new { id = "open-breaks", label = "Open Breaks", value = "4", tone = "warning" },
                new { id = "timing-drift", label = "Timing Drift", value = "1", tone = "warning" },
                new { id = "security-gaps", label = "Security Gaps", value = "2", tone = "warning" },
                new { id = "audit-ready", label = "Audit Ready", value = "9", tone = "success" }
            },
            reconciliationQueue = new[]
            {
                new
                {
                    runId = "gov-run-001",
                    strategyName = "Global Macro Overlay",
                    mode = "paper",
                    status = "Completed",
                    lastUpdated = "12m ago",
                    auditReference = "audit-gov-run-001",
                    breakCount = 2,
                    openBreakCount = 2,
                    reconciliationStatus = "BreaksOpen",
                    latestReconciliation = new
                    {
                        breakCount = 2,
                        openBreakCount = 2,
                        hasTimingDrift = false,
                        securityIssueCount = 2,
                        hasSecurityCoverageIssues = true,
                        lastUpdated = "15m ago",
                        tone = "warning"
                    },
                    securityCoverage = new
                    {
                        portfolioResolved = 14,
                        portfolioMissing = 1,
                        ledgerResolved = 12,
                        ledgerMissing = 1,
                        hasIssues = true,
                        tone = "warning",
                        summary = "26 references mapped, 2 unresolved.",
                        resolvedReferences = new[]
                        {
                            new SecurityCoverageReferencePayload(
                                Source: "portfolio",
                                Symbol: "AAPL",
                                AccountName: null,
                                SecurityId: "security-aapl",
                                DisplayName: "Apple Inc.",
                                AssetClass: "Equity",
                                Currency: "USD",
                                Status: "Active",
                                PrimaryIdentifier: "AAPL")
                        },
                        missingReferences = new[]
                        {
                            new SecurityCoverageGapPayload(
                                Source: "portfolio",
                                Symbol: "XYZ",
                                AccountName: null,
                                Reason: "Portfolio position is missing a Security Master match."),
                            new SecurityCoverageGapPayload(
                                Source: "ledger",
                                Symbol: "XYZ",
                                AccountName: "Securities",
                                Reason: "Ledger coverage is missing a Security Master match.")
                        }
                    },
                    cashFlow = new
                    {
                        cashBalance = 1_250_000m,
                        ledgerCashBalance = 1_247_500m,
                        cashVariance = -2_500m,
                        financing = 12_500m,
                        realizedPnl = 42_000m,
                        unrealizedPnl = 18_000m,
                        journalEntryCount = 24,
                        tone = "warning",
                        summary = "Cash and ledger balances diverge and should be reviewed."
                    }
                }
            },
            workspace = new
            {
                totalRuns = 12,
                reconciledRuns = 9,
                ledgerReadyRuns = 10,
                openBreaks = 4,
                securityIssues = 2
            },
            cashFlow = new
            {
                totalCash = 2_450_000m,
                totalLedgerCash = 2_447_500m,
                netVariance = -2_500m,
                totalFinancing = 12_500m,
                runsWithCashSignals = 9,
                runsWithCashVariance = 1,
                tone = "warning",
                summary = "Cash-flow coverage is available for 9 runs; 1 run needs variance review."
            },
            reporting = BuildGovernanceReportingPayload()
        };
    }

    private static object BuildRunDigest(StrategyRunSummary run, StrategyRunDetail? detail)
    {
        return new
        {
            runId = run.RunId,
            strategyName = run.StrategyName,
            mode = run.Mode.ToString().ToLowerInvariant(),
            status = run.Status.ToString(),
            lastUpdated = FormatRelativeTime(run.LastUpdatedAt),
            hasLedger = !string.IsNullOrWhiteSpace(run.LedgerReference),
            hasPortfolio = !string.IsNullOrWhiteSpace(run.PortfolioId),
            securityCoverage = BuildSecurityCoverage(detail)
        };
    }

    private static object BuildResearchRunCard(StrategyRunSummary run, StrategyRunDetail? detail)
    {
        return new
        {
            id = run.RunId,
            strategyName = run.StrategyName,
            engine = run.Engine.ToString(),
            mode = run.Mode.ToString().ToLowerInvariant(),
            status = run.Status.ToString(),
            dataset = run.DatasetReference ?? run.FeedReference ?? "Unassigned",
            window = FormatWindow(run.StartedAt, run.CompletedAt),
            pnl = FormatReturn(run.TotalReturn, run.NetPnl),
            sharpe = FormatSharpeProxy(run),
            lastUpdated = FormatRelativeTime(run.LastUpdatedAt),
            notes = BuildRunNotes(run),
            promotionState = run.Promotion?.State.ToString(),
            ledgerReference = run.LedgerReference,
            portfolioId = run.PortfolioId,
            netPnl = run.NetPnl,
            totalReturn = run.TotalReturn,
            finalEquity = run.FinalEquity,
            securityCoverage = BuildSecurityCoverage(detail),
            drillIn = BuildRunDrillInLinks(run)
        };
    }

    private static object BuildTimelineCard(StrategyRunSummary run) => new
    {
        runId = run.RunId,
        strategyName = run.StrategyName,
        mode = run.Mode.ToString().ToLowerInvariant(),
        status = run.Status.ToString(),
        startedAt = run.StartedAt,
        completedAt = run.CompletedAt,
        lastUpdatedAt = run.LastUpdatedAt,
        totalReturn = run.TotalReturn
    };

    private static object[] BuildModeComparisons(IReadOnlyList<StrategyRunSummary> runs)
    {
        return runs
            .GroupBy(static run => run.StrategyName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                strategyName = group.Key,
                modes = group
                    .OrderBy(static run => run.Mode)
                    .Select(static run => new
                    {
                        runId = run.RunId,
                        mode = run.Mode.ToString().ToLowerInvariant(),
                        status = run.Status.ToString(),
                        netPnl = run.NetPnl,
                        totalReturn = run.TotalReturn,
                        drillIn = BuildRunDrillInLinks(run)
                    })
                    .ToArray()
            })
            .Where(static comparison => comparison.modes.Length > 0)
            .ToArray<object>();
    }

    private static object BuildRunDrillInLinks(StrategyRunSummary run) => new
    {
        equityCurve = $"/api/workstation/runs/{run.RunId}/equity-curve",
        fills = $"/api/workstation/runs/{run.RunId}/fills",
        attribution = $"/api/workstation/runs/{run.RunId}/attribution",
        ledger = string.IsNullOrWhiteSpace(run.LedgerReference) ? null : $"/api/workstation/runs/{run.RunId}/ledger",
        cashFlows = $"/api/portfolio/{run.RunId}/cash-flows",
        comparison = "/api/workstation/runs/compare"
    };

    private static IReadOnlyList<StrategyRunMode>? ParseModes(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return null;
        }

        var parsed = mode
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static token => Enum.TryParse<StrategyRunMode>(token, true, out var modeValue)
                ? (StrategyRunMode?)modeValue
                : null)
            .Where(static item => item.HasValue)
            .Select(static item => item!.Value)
            .Distinct()
            .ToArray();

        return parsed.Length == 0 ? null : parsed;
    }

    private static object BuildGovernanceRunCard(
        StrategyRunSummary run,
        StrategyRunDetail? detail,
        ReconciliationRunDetail? reconciliation)
    {
        return new
        {
            runId = run.RunId,
            strategyName = run.StrategyName,
            mode = run.Mode.ToString().ToLowerInvariant(),
            status = run.Status.ToString(),
            lastUpdated = FormatRelativeTime(run.LastUpdatedAt),
            auditReference = run.AuditReference,
            ledgerReference = run.LedgerReference,
            portfolioId = run.PortfolioId,
            breakCount = reconciliation?.Summary.BreakCount ?? 0,
            openBreakCount = reconciliation?.Summary.OpenBreakCount ?? 0,
            reconciliationStatus = MapReconciliationStatus(reconciliation),
            governance = new
            {
                hasAuditTrail = run.Governance?.HasAuditTrail ?? false,
                hasPortfolio = run.Governance?.HasPortfolio ?? false,
                hasLedger = run.Governance?.HasLedger ?? false,
                datasetReference = run.Governance?.DatasetReference,
                feedReference = run.Governance?.FeedReference
            },
            securityCoverage = BuildSecurityCoverage(detail),
            cashFlow = BuildGovernanceRunCashFlowSummary(detail),
            latestReconciliation = reconciliation is null
                ? null
                : new
                {
                    reconciliationRunId = reconciliation.Summary.ReconciliationRunId,
                    breakCount = reconciliation.Summary.BreakCount,
                    openBreakCount = reconciliation.Summary.OpenBreakCount,
                    matchCount = reconciliation.Summary.MatchCount,
                    hasTimingDrift = reconciliation.Summary.HasTimingDrift,
                    securityIssueCount = reconciliation.Summary.SecurityIssueCount,
                    hasSecurityCoverageIssues = reconciliation.Summary.HasSecurityCoverageIssues,
                    lastUpdated = FormatRelativeTime(reconciliation.Summary.CreatedAt),
                    tone = reconciliation.Summary.BreakCount == 0 && !reconciliation.Summary.HasSecurityCoverageIssues ? "success" : "warning"
                }
        };
    }

    private static string MapReconciliationStatus(ReconciliationRunDetail? reconciliation)
    {
        if (reconciliation is null)
        {
            return "NotStarted";
        }

        if (reconciliation.Summary.OpenBreakCount > 0)
        {
            return "BreaksOpen";
        }

        if (reconciliation.Summary.HasSecurityCoverageIssues)
        {
            return "SecurityCoverageOpen";
        }

        if (reconciliation.Summary.BreakCount > 0)
        {
            return "Resolved";
        }

        return "Balanced";
    }

    private static object BuildSecurityCoverage(StrategyRunDetail? detail)
    {
        var portfolio = detail?.Portfolio;
        var ledger = detail?.Ledger;
        var portfolioResolved = portfolio?.SecurityResolvedCount ?? 0;
        var portfolioMissing = portfolio?.SecurityMissingCount ?? 0;
        var ledgerResolved = ledger?.SecurityResolvedCount ?? 0;
        var ledgerMissing = ledger?.SecurityMissingCount ?? 0;
        var hasIssues = portfolioMissing > 0 || ledgerMissing > 0;
        var resolvedReferences = BuildResolvedSecurityReferences(detail);
        var missingReferences = BuildMissingSecurityReferences(detail);
        var resolvedCount = portfolioResolved + ledgerResolved;
        var missingCount = portfolioMissing + ledgerMissing;

        return new
        {
            portfolioResolved,
            portfolioMissing,
            ledgerResolved,
            ledgerMissing,
            hasIssues,
            tone = hasIssues ? "warning" : resolvedCount > 0 ? "success" : "default",
            summary = missingCount > 0
                ? $"{resolvedCount} references mapped, {missingCount} unresolved."
                : resolvedCount > 0
                    ? $"{resolvedCount} references mapped with no unresolved symbols."
                    : "Security Master coverage not yet evaluated.",
            resolvedReferences,
            missingReferences
        };
    }

    private static SecurityCoverageReferencePayload[] BuildResolvedSecurityReferences(StrategyRunDetail? detail)
    {
        if (detail is null)
        {
            return [];
        }

        var results = new List<SecurityCoverageReferencePayload>();

        if (detail.Portfolio is not null)
        {
            results.AddRange(
                detail.Portfolio.Positions
                    .Where(static position => position.Security is not null)
                    .Select(static position => new SecurityCoverageReferencePayload(
                        Source: "portfolio",
                        Symbol: position.Symbol,
                        AccountName: null,
                        SecurityId: position.Security!.SecurityId.ToString("N"),
                        DisplayName: position.Security.DisplayName,
                        AssetClass: position.Security.AssetClass,
                        Currency: position.Security.Currency,
                        Status: position.Security.Status.ToString(),
                        PrimaryIdentifier: position.Security.PrimaryIdentifier)));
        }

        if (detail.Ledger is not null)
        {
            results.AddRange(
                detail.Ledger.TrialBalance
                    .Where(static line => line.Security is not null && !string.IsNullOrWhiteSpace(line.Symbol))
                    .Select(static line => new SecurityCoverageReferencePayload(
                        Source: "ledger",
                        Symbol: line.Symbol!,
                        AccountName: line.AccountName,
                        SecurityId: line.Security!.SecurityId.ToString("N"),
                        DisplayName: line.Security.DisplayName,
                        AssetClass: line.Security.AssetClass,
                        Currency: line.Security.Currency,
                        Status: line.Security.Status.ToString(),
                        PrimaryIdentifier: line.Security.PrimaryIdentifier)));
        }

        return results
            .DistinctBy(static item => $"{item.Source}|{item.Symbol}|{item.AccountName}|{item.SecurityId}", StringComparer.OrdinalIgnoreCase)
            .Take(SecurityCoveragePreviewLimit)
            .ToArray();
    }

    private static SecurityCoverageGapPayload[] BuildMissingSecurityReferences(StrategyRunDetail? detail)
    {
        if (detail is null)
        {
            return [];
        }

        var results = new List<SecurityCoverageGapPayload>();

        if (detail.Portfolio is not null)
        {
            results.AddRange(
                detail.Portfolio.Positions
                    .Where(static position => position.Security is null && !string.IsNullOrWhiteSpace(position.Symbol))
                    .Select(static position => new SecurityCoverageGapPayload(
                        Source: "portfolio",
                        Symbol: position.Symbol,
                        AccountName: null,
                        Reason: "Portfolio position is missing a Security Master match.")));
        }

        if (detail.Ledger is not null)
        {
            results.AddRange(
                detail.Ledger.TrialBalance
                    .Where(static line => line.Security is null && !string.IsNullOrWhiteSpace(line.Symbol))
                    .Select(static line => new SecurityCoverageGapPayload(
                        Source: "ledger",
                        Symbol: line.Symbol!,
                        AccountName: line.AccountName,
                        Reason: "Ledger coverage is missing a Security Master match.")));
        }

        return results
            .DistinctBy(static item => $"{item.Source}|{item.Symbol}|{item.AccountName}", StringComparer.OrdinalIgnoreCase)
            .Take(SecurityCoveragePreviewLimit)
            .ToArray();
    }

    private static object BuildGovernanceWorkspaceCashFlowSummary(IReadOnlyList<StrategyRunDetail?> details)
    {
        var totalCash = details.Sum(static detail => detail?.Portfolio?.Cash ?? 0m);
        var totalLedgerCash = details.Sum(static detail => GetLedgerCashBalance(detail?.Ledger) ?? 0m);
        var totalFinancing = details.Sum(static detail => detail?.Portfolio?.Financing ?? 0m);
        var runsWithCashSignals = details.Count(static detail => detail?.Portfolio is not null || detail?.Ledger is not null);
        var runsWithCashVariance = details.Count(static detail => Math.Abs(GetCashVariance(detail)) > 0.01m);
        var netVariance = totalLedgerCash - totalCash;

        return new
        {
            totalCash,
            totalLedgerCash,
            netVariance,
            totalFinancing,
            runsWithCashSignals,
            runsWithCashVariance,
            tone = runsWithCashVariance > 0 ? "warning" : runsWithCashSignals > 0 ? "success" : "default",
            summary = runsWithCashSignals == 0
                ? "Cash-flow coverage is not yet available."
                : runsWithCashVariance > 0
                    ? $"Cash-flow coverage is available for {runsWithCashSignals} runs; {runsWithCashVariance} run needs variance review."
                    : $"Cash-flow coverage is aligned across {runsWithCashSignals} runs."
        };
    }

    private static object BuildGovernanceRunCashFlowSummary(StrategyRunDetail? detail)
    {
        var cashBalance = detail?.Portfolio?.Cash ?? 0m;
        var ledgerCashBalance = GetLedgerCashBalance(detail?.Ledger) ?? 0m;
        var cashVariance = ledgerCashBalance - cashBalance;
        var financing = detail?.Portfolio?.Financing ?? 0m;
        var realizedPnl = detail?.Portfolio?.RealizedPnl ?? 0m;
        var unrealizedPnl = detail?.Portfolio?.UnrealizedPnl ?? 0m;
        var journalEntryCount = detail?.Ledger?.JournalEntryCount ?? 0;
        var hasSignals = detail?.Portfolio is not null || detail?.Ledger is not null;

        return new
        {
            cashBalance,
            ledgerCashBalance,
            cashVariance,
            financing,
            realizedPnl,
            unrealizedPnl,
            journalEntryCount,
            tone = !hasSignals ? "default" : Math.Abs(cashVariance) > 0.01m ? "warning" : "success",
            summary = !hasSignals
                ? "Cash-flow coverage is not yet available."
                : Math.Abs(cashVariance) > 0.01m
                    ? "Cash and ledger balances diverge and should be reviewed."
                    : "Cash and ledger balances are aligned."
        };
    }

    private static decimal? GetLedgerCashBalance(LedgerSummary? ledger)
        => ledger?.TrialBalance.FirstOrDefault(static line =>
            string.Equals(line.AccountName, "Cash", StringComparison.OrdinalIgnoreCase))?.Balance;

    private static decimal GetCashVariance(StrategyRunDetail? detail)
    {
        var portfolioCash = detail?.Portfolio?.Cash;
        var ledgerCash = GetLedgerCashBalance(detail?.Ledger);
        if (!portfolioCash.HasValue || !ledgerCash.HasValue)
        {
            return 0m;
        }

        return ledgerCash.Value - portfolioCash.Value;
    }

    private static object BuildGovernanceReportingPayload()
    {
        var profiles = ExportProfile.GetBuiltInProfiles()
            .Select(static profile => new GovernanceReportingProfilePayload(
                Id: profile.Id,
                Name: profile.Name,
                TargetTool: profile.TargetTool,
                Format: profile.Format.ToString(),
                Description: profile.Description ?? string.Empty,
                LoaderScript: profile.IncludeLoaderScript,
                DataDictionary: profile.IncludeDataDictionary))
            .OrderBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var recommended = profiles
            .Where(static profile => profile.Id is "excel" or "python-pandas" or "postgresql" or "arrow-feather")
            .Select(static profile => profile.Id)
            .ToArray();

        return new
        {
            profileCount = profiles.Length,
            recommendedProfiles = recommended,
            profiles,
            reportPackTargets = new[] { "board", "investor", "compliance", "fund-ops" },
            summary = $"{profiles.Length} export/reporting profiles are available for governance workflows."
        };
    }

    private static string BuildDisplayName(StrategyRunSummary? latest)
        => latest is null ? "Meridian Operator" : $"{latest.StrategyName} Desk";

    private static string BuildRole(StrategyRunSummary? latest)
        => latest is null
            ? "Research Lead"
            : latest.Mode == StrategyRunMode.Live
                ? "Live Operations"
                : "Research Lead";

    private static string MapEnvironment(StrategyRunSummary? latest)
        => latest?.Mode switch
        {
            StrategyRunMode.Live => "live",
            StrategyRunMode.Paper => "paper",
            StrategyRunMode.Backtest => "research",
            _ => "paper"
        };

    private static string MapWorkspace(StrategyRunSummary? latest)
        => latest?.Promotion?.State switch
        {
            StrategyRunPromotionState.LiveManaged => "governance",
            StrategyRunPromotionState.CandidateForLive => "operations",
            StrategyRunPromotionState.CandidateForPaper => "research",
            _ => latest?.Mode == StrategyRunMode.Live ? "operations" : "research"
        };

    private static string BuildRunNotes(StrategyRunSummary run)
    {
        if (run.Promotion?.RequiresReview == true)
        {
            return run.Promotion.State switch
            {
                StrategyRunPromotionState.CandidateForPaper => "Completed backtest awaiting paper review.",
                StrategyRunPromotionState.CandidateForLive => "Paper run pending live promotion review.",
                StrategyRunPromotionState.RequiresCompletion => "Run must complete before promotion review can proceed.",
                _ => "Run is flagged for governance review."
            };
        }

        if (!string.IsNullOrWhiteSpace(run.LedgerReference) && !string.IsNullOrWhiteSpace(run.PortfolioId))
        {
            return "Run has portfolio and ledger drill-in coverage.";
        }

        if (!string.IsNullOrWhiteSpace(run.LedgerReference))
        {
            return "Run includes ledger drill-in coverage.";
        }

        if (!string.IsNullOrWhiteSpace(run.PortfolioId))
        {
            return "Run includes portfolio drill-in coverage.";
        }

        return run.Status switch
        {
            StrategyRunStatus.Running => "Active run with live workspace telemetry.",
            StrategyRunStatus.Completed => "Completed run available for comparison and export.",
            StrategyRunStatus.Failed => "Run completed with errors requiring review.",
            _ => "Run is available for workstation review."
        };
    }

    private static string FormatWindow(DateTimeOffset startedAt, DateTimeOffset? completedAt)
    {
        var end = completedAt ?? DateTimeOffset.UtcNow;
        var span = end - startedAt;

        if (span.TotalDays >= 1)
        {
            return $"{(int)Math.Round(span.TotalDays)}d";
        }

        if (span.TotalHours >= 1)
        {
            return $"{(int)Math.Round(span.TotalHours)}h";
        }

        if (span.TotalMinutes >= 1)
        {
            return $"{(int)Math.Round(span.TotalMinutes)}m";
        }

        return "0m";
    }

    private static string FormatRelativeTime(DateTimeOffset timestamp)
    {
        var span = DateTimeOffset.UtcNow - timestamp;

        if (span.TotalMinutes < 1)
        {
            return "just now";
        }

        if (span.TotalHours < 1)
        {
            return $"{(int)Math.Round(span.TotalMinutes)}m ago";
        }

        if (span.TotalDays < 1)
        {
            return $"{(int)Math.Round(span.TotalHours)}h ago";
        }

        return $"{(int)Math.Round(span.TotalDays)}d ago";
    }

    private static string FormatReturn(decimal? totalReturn, decimal? netPnl)
    {
        if (totalReturn is not null)
        {
            return FormatPercent(totalReturn.Value);
        }

        if (netPnl is not null)
        {
            return FormatCurrency(netPnl.Value);
        }

        return "n/a";
    }

    private static string FormatSharpeProxy(StrategyRunSummary run)
    {
        if (run.TotalReturn is null && run.NetPnl is null)
        {
            return "n/a";
        }

        var proxy = (run.TotalReturn ?? 0m) * 12m;
        if (run.NetPnl is not null)
        {
            proxy += Math.Sign(run.NetPnl.Value) * 0.25m;
        }

        return proxy.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string FormatPercent(decimal value)
        => $"{(value >= 0 ? "+" : string.Empty)}{(value * 100m).ToString("0.0", CultureInfo.InvariantCulture)}%";

    private static string FormatCurrency(decimal value)
    {
        var sign = value >= 0 ? "+" : "-";
        var absolute = Math.Abs(value);
        var scaled = absolute;
        var suffix = string.Empty;

        if (absolute >= 1_000_000m)
        {
            scaled = absolute / 1_000_000m;
            suffix = "M";
        }
        else if (absolute >= 1_000m)
        {
            scaled = absolute / 1_000m;
            suffix = "K";
        }

        return $"{sign}${scaled.ToString("0.##", CultureInfo.InvariantCulture)}{suffix}";
    }

    private static SecurityMasterWorkstationDto MapToWorkstationSecurity(SecuritySummaryDto summary)
        => new(
            SecurityId: summary.SecurityId,
            DisplayName: summary.DisplayName,
            Status: summary.Status,
            Classification: new SecurityClassificationSummaryDto(
                AssetClass: summary.AssetClass,
                SubType: DeriveSubType(summary.AssetClass),
                PrimaryIdentifierKind: null,
                PrimaryIdentifierValue: summary.PrimaryIdentifier),
            EconomicDefinition: new SecurityEconomicDefinitionSummaryDto(
                Currency: summary.Currency,
                Version: summary.Version,
                EffectiveFrom: null,
                EffectiveTo: null));

    private static SecurityMasterWorkstationDto MapToWorkstationSecurity(SecurityDetailDto detail)
    {
        var primaryIdentifier = detail.Identifiers
            .FirstOrDefault(static identifier => identifier.IsPrimary)
            ?? detail.Identifiers.FirstOrDefault();

        return new SecurityMasterWorkstationDto(
            SecurityId: detail.SecurityId,
            DisplayName: detail.DisplayName,
            Status: detail.Status,
            Classification: new SecurityClassificationSummaryDto(
                AssetClass: detail.AssetClass,
                SubType: DeriveSubType(detail.AssetClass),
                PrimaryIdentifierKind: primaryIdentifier?.Kind.ToString(),
                PrimaryIdentifierValue: primaryIdentifier?.Value),
            EconomicDefinition: new SecurityEconomicDefinitionSummaryDto(
                Currency: detail.Currency,
                Version: detail.Version,
                EffectiveFrom: detail.EffectiveFrom,
                EffectiveTo: detail.EffectiveTo));
    }

    private static SecurityIdentityDrillInDto MapToIdentityDrillIn(SecurityDetailDto detail)
        => new(
            SecurityId: detail.SecurityId,
            DisplayName: detail.DisplayName,
            AssetClass: detail.AssetClass,
            Status: detail.Status,
            Version: detail.Version,
            EffectiveFrom: detail.EffectiveFrom,
            EffectiveTo: detail.EffectiveTo,
            Identifiers: detail.Identifiers,
            Aliases: detail.Aliases);

    private static SecurityEconomicDefinitionSummaryDto MapToEconomicDefinitionSummary(SecurityEconomicDefinitionRecord record)
        => new(
            Currency: record.Currency,
            Version: record.Version,
            EffectiveFrom: record.EffectiveFrom,
            EffectiveTo: record.EffectiveTo,
            SubType: record.SubType,
            AssetFamily: record.AssetFamily,
            IssuerType: record.IssuerType);

    /// <summary>
    /// Derives the most specific sub-type available from the asset-class string without requiring
    /// a full aggregate rebuild. Returns null for asset classes that may map to multiple sub-types.
    /// </summary>
    private static string? DeriveSubType(string? assetClass) => assetClass switch
    {
        "Bond" => "Bond",
        "TreasuryBill" => "TreasuryBill",
        "Option" => "OptionContract",
        "Future" => "FutureContract",
        "Swap" => "SwapContract",
        "DirectLoan" => "DirectLoan",
        "Deposit" => "Deposit",
        "MoneyMarketFund" => "MoneyMarket",
        "CertificateOfDeposit" => "CertificateOfDeposit",
        "CommercialPaper" => "CommercialPaper",
        "Repo" => "Repo",
        _ => null
    };

    private static IResult ServeWorkstationIndex(IWebHostEnvironment environment)
    {
        var root = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var indexPath = Path.Combine(root, "workstation", "index.html");

        return File.Exists(indexPath)
            ? Results.File(indexPath, "text/html")
            : Results.NotFound(new
            {
                error = "Workstation bundle not found.",
                message = "Build src/Meridian.Ui/dashboard before opening /workstation."
            });
    }

    private sealed record SecurityCoverageReferencePayload(
        string Source,
        string Symbol,
        string? AccountName,
        string SecurityId,
        string DisplayName,
        string AssetClass,
        string Currency,
        string Status,
        string? PrimaryIdentifier);

    private sealed record SecurityCoverageGapPayload(
        string Source,
        string Symbol,
        string? AccountName,
        string Reason);

    private sealed record GovernanceReportingProfilePayload(
        string Id,
        string Name,
        string TargetTool,
        string Format,
        string Description,
        bool LoaderScript,
        bool DataDictionary);
}

/// <summary>Request to compare multiple strategy runs side by side.</summary>
public sealed record RunComparisonRequest(
    IReadOnlyList<string> RunIds,
    IReadOnlyList<StrategyRunMode>? Modes = null);

/// <summary>Request to diff two strategy runs.</summary>
public sealed record RunDiffRequest(string BaseRunId, string TargetRunId);

/// <summary>Result of a run-vs-run diff showing position, parameter, and metric changes.</summary>
public sealed record StrategyRunDiff(
    string BaseRunId,
    string TargetRunId,
    string BaseStrategyName,
    string TargetStrategyName,
    IReadOnlyList<PositionDiffEntry> AddedPositions,
    IReadOnlyList<PositionDiffEntry> RemovedPositions,
    IReadOnlyList<PositionDiffEntry> ModifiedPositions,
    IReadOnlyList<ParameterDiff> ParameterChanges,
    MetricsDiff Metrics);

/// <summary>A single position change between two runs.</summary>
public sealed record PositionDiffEntry(
    string Symbol,
    long BaseQuantity,
    long TargetQuantity,
    decimal BasePnl,
    decimal TargetPnl,
    string ChangeType);

/// <summary>A single parameter change between two runs.</summary>
public sealed record ParameterDiff(
    string Key,
    string? BaseValue,
    string? TargetValue);

/// <summary>High-level metrics delta between two runs.</summary>
public sealed record MetricsDiff(
    decimal NetPnlDelta,
    decimal TotalReturnDelta,
    int FillCountDelta,
    decimal? BaseNetPnl,
    decimal? TargetNetPnl,
    decimal? BaseTotalReturn,
    decimal? TargetTotalReturn);
