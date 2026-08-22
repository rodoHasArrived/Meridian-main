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
                Role: ResolveRoleLabel(context, latest: null),
                Environment: "paper",
                ActiveWorkspace: NeutralWorkspace,
                CommandCount: 6,
                LatestRun: null,
                WorkspaceSummary: new WorkstationSessionWorkspaceSummary(0, 0, 0, 0, 0));
        }

        var runs = (await readService.GetRunsAsync(ct: context.RequestAborted).ConfigureAwait(false)).ToArray();
        var latest = runs.FirstOrDefault();
        // Only fetched when it will actually be returned: this route is the shell's bootstrap, and a
        // slow or failing detail store must not delay or fail it for callers whose payload omits the
        // detail anyway.
        var latestDetail = latest is null || !canReadRuns
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
            Role: ResolveRoleLabel(context, latest),
            Environment: MapEnvironment(latest),
            ActiveWorkspace: canReadRuns ? MapWorkspace(latest) : NeutralWorkspace,
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

    /// <summary>
    /// The caller's own authority, not the latest run's posture. The browser prints this in the
    /// masthead and matches it against the role catalog to name the active authority profile, and the
    /// run-derived labels are not role names at all -- so the match failed for every operator, not
    /// only the ones whose run digest is withheld. The role profile name is preferred when the
    /// deployment defines one, since that is what a custom profile is called in the catalog.
    /// The run-derived label survives only where no principal is resolvable, which keeps the payload
    /// populated for a deployment without an authorization context rather than showing nothing.
    /// </summary>
    private static string ResolveRoleLabel(HttpContext context, StrategyRunSummary? latest)
    {
        if (context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserRoleProfileNameKey, out var profile) &&
            profile is string profileName &&
            !string.IsNullOrWhiteSpace(profileName))
        {
            return profileName;
        }

        return context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserRoleKey, out var role) && role is UserRole userRole
            ? userRole.ToString()
            : BuildRole(latest);
    }

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

    /// <summary>
    /// Landing workspace for a payload carrying no run data. <see cref="MapWorkspace"/> is derived
    /// from the latest run's promotion state -- LiveManaged reads as accounting, CandidateForLive as
    /// trading, CandidateForPaper as strategy -- so returning it to a caller without strategy
    /// permission would hand back the very promotion state the rest of the payload withholds, one
    /// field over. This is the same value a deployment with no run service returns, so it discloses
    /// nothing about whether runs exist.
    /// </summary>
    private const string NeutralWorkspace = "strategy";

    private static string MapWorkspace(StrategyRunSummary? latest)
        => latest?.Promotion?.State switch
        {
            StrategyRunPromotionState.LiveManaged => "accounting",
            StrategyRunPromotionState.CandidateForLive => "trading",
            StrategyRunPromotionState.CandidateForPaper => "strategy",
            _ => latest?.Mode == StrategyRunMode.Live ? "trading" : "strategy"
        };
}
