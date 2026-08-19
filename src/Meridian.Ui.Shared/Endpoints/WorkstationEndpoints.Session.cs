using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Strategies.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Workstation session bootstrap payload for the API surface: builds the typed session DTO
/// (latest strategy-run digest, workspace summaries) and its display-name / role / environment /
/// workspace mapping helpers. Split out of the WorkstationEndpoints core partial as a
/// behavior-preserving relocation; the inline session route lambda and the shared BuildRunDigest
/// helper remain in core (reached across the partial).
/// </summary>
public static partial class WorkstationEndpoints
{
    // PR-03: returns typed DTO instead of anonymous object
    private static async Task<WorkstationSessionPayload> BuildSessionPayloadAsync(HttpContext context)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();

        // The shell bootstraps from this route unconditionally, so it stays open to any authenticated
        // session. The strategy-run digest it embeds is not universal, though -- run names, modes,
        // promotion states and counts belong to the run drill-ins -- so callers without the strategy
        // permission get the same neutral shell payload as a deployment with no run service at all.
        // FundAccountant, Controller and Compliance hold no strategy permission, and gating the whole
        // route would have failed their bootstrap and left them without a shell.
        var canReadRuns = EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.ViewStrategies,
            UserPermission.ManageStrategies);

        if (readService is null)
        {
            return new WorkstationSessionPayload(
                DisplayName: "Meridian Operator",
                Role: "Strategy Lead",
                Environment: "paper",
                ActiveWorkspace: "strategy",
                CommandCount: 6,
                LatestRun: null,
                WorkspaceSummary: new WorkstationSessionWorkspaceSummary(0, 0, 0, 0, 0));
        }

        var runs = (await readService.GetRunsAsync(ct: context.RequestAborted).ConfigureAwait(false)).ToArray();
        var latest = runs.FirstOrDefault();
        var latestDetail = latest is null
            ? null
            : await readService.GetRunDetailAsync(latest.RunId, context.RequestAborted).ConfigureAwait(false);
        var activeRuns = runs.Count(static run => run.Status is StrategyRunStatus.Running or StrategyRunStatus.Paused);
        var reviewRuns = runs.Count(static run => run.Promotion?.RequiresReview == true || run.Status is StrategyRunStatus.Failed or StrategyRunStatus.Cancelled);

        // Posture stays truthful for every operator: environment drives the masthead's live-money
        // warning, and reporting "paper" to a controller working against a live book would remove
        // that alarm. Only the identifying strategy detail is withheld -- the run digest, the run
        // counts, and the display name, which is built from the latest run's strategy name.
        return new WorkstationSessionPayload(
            DisplayName: canReadRuns ? BuildDisplayName(latest) : "Meridian Operator",
            Role: BuildRole(latest),
            Environment: MapEnvironment(latest),
            ActiveWorkspace: MapWorkspace(latest),
            CommandCount: canReadRuns ? Math.Max(6, runs.Length + activeRuns + reviewRuns) : 6,
            LatestRun: canReadRuns && latest is not null ? BuildRunDigest(latest, latestDetail) : null,
            WorkspaceSummary: canReadRuns
                ? new WorkstationSessionWorkspaceSummary(
                    TotalRuns: runs.Length,
                    ActiveRuns: activeRuns,
                    ReviewRuns: reviewRuns,
                    LedgerCoverage: runs.Count(static run => !string.IsNullOrWhiteSpace(run.LedgerReference)),
                    PortfolioCoverage: runs.Count(static run => !string.IsNullOrWhiteSpace(run.PortfolioId)))
                : new WorkstationSessionWorkspaceSummary(0, 0, 0, 0, 0));
    }

    private static string BuildDisplayName(StrategyRunSummary? latest)
        => latest is null ? "Meridian Operator" : $"{latest.StrategyName} Desk";

    private static string BuildRole(StrategyRunSummary? latest)
        => latest is null
            ? "Strategy Lead"
            : latest.Mode == StrategyRunMode.Live
                ? "Live Operations"
                : "Strategy Lead";

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
            StrategyRunPromotionState.LiveManaged => "accounting",
            StrategyRunPromotionState.CandidateForLive => "trading",
            StrategyRunPromotionState.CandidateForPaper => "strategy",
            _ => latest?.Mode == StrategyRunMode.Live ? "trading" : "strategy"
        };
}
