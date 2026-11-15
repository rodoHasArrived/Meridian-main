using System.Globalization;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FinancialRecordExplorerReadService
{
    /// <summary>
    /// The most recent run this tenant owns that satisfies <paramref name="predicate"/>, or null.
    /// <para>
    /// The tenant filter is the point: a run's detail is the explorer's entire source -- the trial
    /// balance, the positions, the security references it renders. Selecting the newest qualifying run
    /// globally served one tenant's book to another whenever the other owned the newest run, and the
    /// tenant id reaching only saved-view persistence made that invisible from the call site.
    /// </para>
    /// <para>
    /// Ownership is asked of the registry rather than compared here, so this agrees with every other
    /// fund-scoped read: an unbound fund is accessible (trust-on-first-use, first owner wins), and only
    /// a fund bound to another tenant is refused. A run with no fund profile is not attributable to any
    /// tenant, so it is skipped rather than shown to all of them. With no registry in the composition
    /// there is no tenancy to enforce and every run qualifies, which is the single-company deployment.
    /// </para>
    /// </summary>
    /// <summary>How many runs the ledger explorer's run picker will offer. A picker, not a report.</summary>
    private const int LedgerRunCandidateLimit = 50;

    /// <summary>
    /// The run the caller asked the ledger explorer to answer for, read from the explorer's own
    /// <c>run</c> filter. Null when the caller named none, which means "whichever run is newest".
    /// </summary>
    private static string? ReadRequestedRunId(FinancialRecordExplorerQueryDto? query)
    {
        var requested = query?.Filters?
            .FirstOrDefault(filter =>
                string.Equals(filter?.FilterId, "run", StringComparison.OrdinalIgnoreCase))?
            .Value?
            .Trim();

        return string.IsNullOrEmpty(requested) ? null : requested;
    }

    /// <summary>
    /// One pass over the tenant's run history: the bounded picker list, and the run the caller
    /// named when it is one they may read. <see cref="Requested"/> may also appear in
    /// <see cref="Candidates"/> — it is only absent from the list when it sits past the bound.
    /// </summary>
    private readonly record struct LedgerRunCandidateSet(
        IReadOnlyList<StrategyRunSummary> Candidates,
        StrategyRunSummary? Requested);

    /// <summary>
    /// The runs this caller may pick between, newest first.
    /// <para>
    /// Read from the run summaries alone: a run that retained a ledger names it in
    /// <see cref="StrategyRunSummary.LedgerReference"/>, so the list costs no detail reads. A run
    /// whose ledger turns out to hold no lines still selects — the explorer's empty state says so,
    /// which is honest, where omitting it would silently deny an operator a run they own.
    /// </para>
    /// <para>
    /// A run named explicitly is carried out of this same pass even when it sits past the bound.
    /// Resolving it separately meant reading the tenant's entire run history a second time — and
    /// reloading the promotion lookup with it — so an older deep link grew more expensive the more
    /// run history the tenant retained.
    /// </para>
    /// </summary>
    private async Task<LedgerRunCandidateSet> LoadLedgerRunCandidatesAsync(
        StrategyRunReadService readService,
        FundOwnershipScope fundScope,
        string? requestedRunId,
        CancellationToken ct)
    {
        var readScope = new StrategyRunReadScope(fundScope.TenantId, fundScope.CompanyId);
        var runs = await readService
            .GetRunsAsync(strategyId: null, runType: null, readScope, ct)
            .ConfigureAwait(false);

        var candidates = new List<StrategyRunSummary>();
        StrategyRunSummary? requested = null;
        foreach (var run in runs)
        {
            if (string.IsNullOrWhiteSpace(run.LedgerReference))
            {
                continue;
            }

            var isRequested = requestedRunId is not null
                && string.Equals(run.RunId, requestedRunId, StringComparison.OrdinalIgnoreCase);

            // Past the bound only the run the caller named is still worth an ownership check.
            // Nothing else can enter the picker, and each check is its own read.
            if (candidates.Count >= LedgerRunCandidateLimit && !isRequested)
            {
                continue;
            }

            if (!await IsRunOwnedByScopeAsync(run, fundScope, ct).ConfigureAwait(false))
            {
                continue;
            }

            if (isRequested)
            {
                requested = run;
            }

            if (candidates.Count < LedgerRunCandidateLimit)
            {
                candidates.Add(run);
            }

            if (candidates.Count >= LedgerRunCandidateLimit && (requestedRunId is null || requested is not null))
            {
                break;
            }
        }

        return new LedgerRunCandidateSet(candidates, requested);
    }

    /// <summary>
    /// The run detail the ledger explorer renders: the requested run when it is one this caller may
    /// read and it carries lines, and otherwise the newest run that does.
    /// <para>
    /// The fallback is deliberate rather than an error. A run id can arrive from a bookmarked URL
    /// long after the run was pruned, and the alternative — refusing outright — leaves the screen
    /// with nothing at all where a usable ledger exists. The explorer names the run it resolved in
    /// its scope, source state and proof links, so the substitution is visible.
    /// </para>
    /// </summary>
    private async Task<StrategyRunDetail?> TryLoadLedgerRunDetailAsync(
        StrategyRunReadService readService,
        FundOwnershipScope fundScope,
        IReadOnlyList<StrategyRunSummary> candidates,
        StrategyRunSummary? requestedRun,
        CancellationToken ct)
    {
        var readScope = new StrategyRunReadScope(fundScope.TenantId, fundScope.CompanyId);
        // Resolved by its own ownership check rather than by membership of the candidate list. The
        // list is bounded so the picker stays a picker, and a tenant with more retained runs than
        // that bound would otherwise have a perfectly valid deep link fall through to a newer run
        // -- while the screen's own trial-balance and journal requests still used the run the URL
        // asked for, recombining evidence from two different runs.
        if (requestedRun is not null)
        {
            var detail = await readService.GetRunDetailAsync(requestedRun.RunId, readScope, ct).ConfigureAwait(false);
            if (detail?.Ledger?.TrialBalance.Count > 0)
            {
                return detail;
            }
        }

        foreach (var run in candidates)
        {
            var detail = await readService.GetRunDetailAsync(run.RunId, readScope, ct).ConfigureAwait(false);
            if (detail?.Ledger?.TrialBalance.Count > 0)
            {
                return detail;
            }
        }

        return null;
    }

    /// <summary>
    /// The picker list, guaranteed to contain the run actually being displayed.
    /// <para>
    /// A run resolved from outside the bound is not in the candidate list, so no view carried its
    /// filter and none was marked active. The client then fell back to the first candidate — a
    /// newer run — and used that run's filter for the picker and for any link copied from it, so
    /// an older run's rows were shown under a different run's identity. The requested run takes
    /// the last slot rather than extending the list, keeping the picker bounded.
    /// </para>
    /// </summary>
    private static IReadOnlyList<StrategyRunSummary> IncludeResolvedRun(
        IReadOnlyList<StrategyRunSummary> candidates,
        StrategyRunSummary resolved)
    {
        if (candidates.Any(run => string.Equals(run.RunId, resolved.RunId, StringComparison.OrdinalIgnoreCase)))
        {
            return candidates;
        }

        var bounded = candidates.Take(Math.Max(0, LedgerRunCandidateLimit - 1)).ToList();
        bounded.Add(resolved);
        return bounded;
    }

    /// <summary>
    /// One system view per run the caller may pick, so the client has the actual list of runs
    /// rather than having to infer it from the rows of whichever single run it was handed.
    /// </summary>
    private static IReadOnlyList<FinancialRecordExplorerSavedViewDto> BuildLedgerRunViews(
        IReadOnlyList<StrategyRunSummary> candidates,
        string activeRunId)
        => candidates
            .Select(run => new FinancialRecordExplorerSavedViewDto(
                $"system-{LedgerExplorerId}-run-{run.RunId}",
                DescribeLedgerRun(run),
                $"Simulated trial balance retained by run {run.RunId}.",
                IsSystem: true,
                IsActive: string.Equals(run.RunId, activeRunId, StringComparison.OrdinalIgnoreCase),
                Filters: [new("run", "Run", run.RunId, Tone: FinancialRecordExplorerTone.Info)],
                SearchText: string.Empty,
                ColumnIds: []))
            .ToArray();

    /// <summary>Names a run the way an operator picking between them needs to read it.</summary>
    private static string DescribeLedgerRun(StrategyRunSummary run)
    {
        var name = string.IsNullOrWhiteSpace(run.StrategyName) ? run.StrategyId : run.StrategyName;
        var started = run.StartedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(name)
            ? $"{run.RunId} · {started}"
            : $"{name} · {run.Mode} · {started}";
    }

    private async Task<StrategyRunDetail?> TryLoadLatestRunDetailAsync(
        StrategyRunReadService readService,
        FundOwnershipScope fundScope,
        Func<StrategyRunDetail, bool> predicate,
        CancellationToken ct)
    {
        // Scoped, and unclamped. Two separate reasons for one call.
        //
        // Scoped, because the read service enforces the tenant and company a run was stamped with when
        // it is given one, and that is a stronger claim than fund ownership: a run explicitly retained
        // for another tenant is foreign even when its fund profile has no registry record yet, and the
        // fund-ownership check below cannot see that. Passing the scope also carries through to the
        // detail read, so the row set and the payload agree about who the caller is.
        //
        // Unclamped, because the query-shaped overload clamps any requested limit to 500 rows, so a
        // bounded scan cannot see past the newest 500 runs however wide it asks -- and a tenant whose
        // newest own run sits behind that many others would be told it has no source at all. This
        // overload is the one the accounting workspace already uses over the same runs.
        var readScope = new StrategyRunReadScope(fundScope.TenantId, fundScope.CompanyId);
        var runs = await readService
            .GetRunsAsync(strategyId: null, runType: null, readScope, ct)
            .ConfigureAwait(false);
        foreach (var run in runs)
        {
            if (!await IsRunOwnedByScopeAsync(run, fundScope, ct).ConfigureAwait(false))
            {
                continue;
            }

            var detail = await readService.GetRunDetailAsync(run.RunId, readScope, ct).ConfigureAwait(false);
            if (detail is not null && predicate(detail))
            {
                return detail;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether this run may be used as an explorer source for the calling scope.
    /// <para>
    /// The presence of a registry is what decides the posture. Without one there is no tenancy to
    /// enforce -- the single-company deployment, and the in-process callers -- so every run qualifies.
    /// With one, tenancy is being enforced, and two kinds of run are refused: a fund profile bound to
    /// another tenant, and a run carrying no fund profile at all, which is attributable to nobody and
    /// so must not become everybody's source.
    /// </para>
    /// <para>
    /// An <em>unbound</em> fund still qualifies, because that is what
    /// <see cref="IFundProfileTenancyRegistry"/> means by unbound: ownership is established on first
    /// authoritative use, so a fund nobody has claimed is reachable by the tenant about to claim it.
    /// That is a different thing from a run with no fund at all, which no claim can ever attach to.
    /// </para>
    /// </summary>
    private async Task<bool> IsRunOwnedByScopeAsync(
        StrategyRunSummary run,
        FundOwnershipScope fundScope,
        CancellationToken ct)
    {
        // No registry means no tenancy to enforce -- the single-company deployment and the in-process
        // callers -- so every run qualifies.
        if (_tenancyRegistry is null)
        {
            return true;
        }

        // With a registry present, tenancy is being enforced, and a run carrying no fund profile is
        // attributable to nobody. Admitting it would hand a legacy run's trial balance, positions and
        // security references to every tenant on the host -- and it only takes being the newest
        // qualifying run to become the source for all of them.
        if (string.IsNullOrWhiteSpace(run.FundProfileId))
        {
            return false;
        }

        var ownership = await _tenancyRegistry
            .ResolveAsync(run.FundProfileId.Trim(), ct)
            .ConfigureAwait(false);
        if (ownership is null)
        {
            return true;
        }

        if (!ownership.IsHeldBy(fundScope.TenantId))
        {
            return false;
        }

        // Company is compared only when the caller resolved one. An in-process caller has no company
        // scope to compare, and refusing every run for it would make the legacy overloads useless
        // without closing anything -- the tenant test above has already refused a foreign owner.
        if (string.IsNullOrWhiteSpace(fundScope.CompanyId))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(ownership.CompanyId) &&
               string.Equals(
                   ownership.CompanyId.Trim(),
                   fundScope.CompanyId.Trim(),
                   StringComparison.OrdinalIgnoreCase);
    }

}
