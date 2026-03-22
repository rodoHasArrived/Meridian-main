using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Workstation;
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

        app.MapGet("/workstation", (IWebHostEnvironment environment) => ServeWorkstationIndex(environment))
            .ExcludeFromDescription();

        app.MapGet("/workstation/{*path}", (string? path, IWebHostEnvironment environment) =>
            string.IsNullOrWhiteSpace(path) || !Path.HasExtension(path)
                ? ServeWorkstationIndex(environment)
                : Results.NotFound())
            .ExcludeFromDescription();
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
        var activeRuns = runs.Count(static run => run.Status is StrategyRunStatus.Running or StrategyRunStatus.Paused);
        var reviewRuns = runs.Count(static run => run.Promotion?.RequiresReview == true || run.Status is StrategyRunStatus.Failed or StrategyRunStatus.Cancelled);

        return new
        {
            displayName = BuildDisplayName(latest),
            role = BuildRole(latest),
            environment = MapEnvironment(latest),
            activeWorkspace = MapWorkspace(latest),
            commandCount = Math.Max(6, runs.Length + activeRuns + reviewRuns),
            latestRun = latest is null ? null : BuildRunDigest(latest),
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
            runs = runs.Select(BuildResearchRunCard).ToArray(),
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
                    notes = "Primary paper candidate with stable fill quality and healthy depth coverage."
                }
            }
        };
    }

    private static object BuildRunDigest(StrategyRunSummary run)
    {
        return new
        {
            runId = run.RunId,
            strategyName = run.StrategyName,
            mode = run.Mode.ToString().ToLowerInvariant(),
            status = run.Status.ToString(),
            lastUpdated = FormatRelativeTime(run.LastUpdatedAt),
            hasLedger = !string.IsNullOrWhiteSpace(run.LedgerReference),
            hasPortfolio = !string.IsNullOrWhiteSpace(run.PortfolioId)
        };
    }

    private static object BuildResearchRunCard(StrategyRunSummary run)
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
            finalEquity = run.FinalEquity
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
}
