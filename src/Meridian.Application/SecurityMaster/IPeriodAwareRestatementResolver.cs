using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Phase 3 period-aware propagation: decides, for a published Security Master revision, whether any
/// affected ledger book is in a closed period and therefore requires a governed restatement proposal
/// rather than silent mutation. The authoritative lock state is the ledger accounting-period status
/// (<see cref="ILedgerPeriodLockReader"/>) — the same authority <c>LedgerPeriodPostingGuard</c> enforces.
///
/// <para>Routing per book (D3 matrix): <c>Open</c> → immediate propagation (lower-order publish
/// handlers already applied it), no restatement; <c>SoftClosed</c> → the downstream effect posts as a
/// governed adjustment (a later slice), and only already-<i>published</i> report packs become
/// restatement candidates; <c>HardClosed</c> or any indeterminate/default-deny state → no posting and
/// a mandatory restatement proposal. When a hard-closed book has exposure but no candidate can be
/// located, the decision still reports <see cref="SecurityMasterRestatementDecision.RestatementRequired"/>
/// (a manual "locate affected packs" task) — a closed period is never silently completed.</para>
/// </summary>
public interface IPeriodAwareRestatementResolver
{
    Task<SecurityMasterRestatementDecision> ResolveAsync(
        SecurityMasterRevisionPublishedEvent publishedEvent, CancellationToken ct = default);
}

/// <summary>
/// The restatement outcome a publish reports to the operator: whether a closed-period edit requires
/// restatement, and the report packs proposed for it (the operator approves each via the existing
/// governed report-pack restatement path; the workbench only proposes).
/// </summary>
public sealed record SecurityMasterRestatementDecision(
    bool RestatementRequired,
    IReadOnlyList<RestatementCandidateDto> Candidates);

/// <summary>
/// The outcome of locating report packs that consumed a changed line in a locked period: the actionable
/// restatement <see cref="Candidates"/>, plus <see cref="HasNonActionableMatches"/> — true when at least
/// one published pack referenced the security but could not be turned into an actionable candidate (for
/// example an already-restated pack the workflow cannot re-restate). The caller uses the flag to require
/// a manual follow-up for a soft-closed book even when no actionable candidate was produced, so a real
/// match is never silently dropped. <see cref="RestatementCandidateResult.IsAuthoritative"/> is false
/// when the configured reporting authority cannot answer the lookup at all; that indeterminate state
/// also requires manual follow-up and must not be interpreted as an authoritative empty result.
/// </summary>
public sealed record RestatementCandidateResult(
    IReadOnlyList<RestatementCandidateDto> Candidates,
    bool HasNonActionableMatches,
    bool IsAuthoritative = true)
{
    public static RestatementCandidateResult Empty { get; } = new([], false);
}

/// <summary>
/// Resolves the report packs that consumed a changed line in a locked period covering an upstream
/// edit, as restatement candidates. <c>fundProfileId</c> scopes the search to the impacted
/// fund's published packs. An empty result for a hard-closed book is treated by the caller as a manual
/// locate-affected-packs task rather than "no restatement needed", so a precise resolver that cannot
/// tie a published pack to the security never silently completes a closed period.
/// </summary>
public interface IRestatementCandidateResolver
{
    Task<RestatementCandidateResult> ResolveAsync(
        Guid securityId,
        Guid ledgerBookId,
        DateOnly effectiveDate,
        IReadOnlyList<string> changedFields,
        string? fundProfileId,
        CancellationToken ct = default);
}

/// <summary>
/// No-op candidate resolver retained as the fallback for hosts without a report-pack backend. The
/// report-pack-backed resolver (<c>ReportPackRestatementCandidateResolver</c> in the workstation layer)
/// is the registered default; this surfaces no packs, leaving the caller's default-deny safety net to
/// flag a hard-closed book for manual locate.
/// </summary>
public sealed class NullRestatementCandidateResolver : IRestatementCandidateResolver
{
    public Task<RestatementCandidateResult> ResolveAsync(
        Guid securityId,
        Guid ledgerBookId,
        DateOnly effectiveDate,
        IReadOnlyList<string> changedFields,
        string? fundProfileId,
        CancellationToken ct = default)
        => Task.FromResult(RestatementCandidateResult.Empty);
}

/// <summary>
/// Fail-closed bridge for hosts whose canonical reporting authority cannot yet answer the
/// security-to-released-run candidate query. It never fabricates a legacy report-pack candidate;
/// instead it marks the lookup indeterminate so soft-closed and hard-closed edits require explicit
/// manual restatement review.
/// </summary>
public sealed class IndeterminateRestatementCandidateResolver : IRestatementCandidateResolver
{
    public Task<RestatementCandidateResult> ResolveAsync(
        Guid securityId,
        Guid ledgerBookId,
        DateOnly effectiveDate,
        IReadOnlyList<string> changedFields,
        string? fundProfileId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new RestatementCandidateResult(
            Candidates: [],
            HasNonActionableMatches: false,
            IsAuthoritative: false));
    }
}

/// <summary>
/// Default <see cref="IPeriodAwareRestatementResolver"/> over the authoritative
/// <see cref="ILedgerPeriodLockReader"/>. Pure routing — it never posts; it only proposes.
/// </summary>
public sealed class PeriodAwareRestatementResolver : IPeriodAwareRestatementResolver
{
    private readonly ILedgerPeriodLockReader _lockReader;
    private readonly IRestatementCandidateResolver _candidateResolver;
    private readonly ILogger<PeriodAwareRestatementResolver> _logger;

    public PeriodAwareRestatementResolver(
        ILedgerPeriodLockReader lockReader,
        IRestatementCandidateResolver candidateResolver,
        ILogger<PeriodAwareRestatementResolver> logger)
    {
        _lockReader = lockReader ?? throw new ArgumentNullException(nameof(lockReader));
        _candidateResolver = candidateResolver ?? throw new ArgumentNullException(nameof(candidateResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SecurityMasterRestatementDecision> ResolveAsync(
        SecurityMasterRevisionPublishedEvent publishedEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(publishedEvent);

        if (publishedEvent.AffectedLedgerBookIds.Count == 0)
        {
            return new SecurityMasterRestatementDecision(RestatementRequired: false, Candidates: []);
        }

        var effectiveDate = DateOnly.FromDateTime(publishedEvent.EffectiveFrom.UtcDateTime);
        var candidates = new List<RestatementCandidateDto>();
        var restatementRequired = false;

        foreach (var ledgerBookId in publishedEvent.AffectedLedgerBookIds)
        {
            var status = await _lockReader.GetPeriodStatusAsync(ledgerBookId, effectiveDate, ct).ConfigureAwait(false);
            switch (status)
            {
                case LedgerPeriodStatusDto.Open:
                    // Immediate propagation handled by lower-order publish handlers; nothing to restate.
                    break;

                case LedgerPeriodStatusDto.SoftClosed:
                    // The effect posts as a governed adjustment (later slice). Only already-published
                    // report packs that consumed the line need restating. A pack that matched but is not
                    // an actionable candidate (e.g. already Restated, which the workflow cannot re-restate)
                    // still needs manual follow-up; unlike a hard-closed book the soft-closed arm does not
                    // default-deny, so without this the decision would be silently empty for a real match.
                    var softClosed = await ResolveCandidatesAsync(publishedEvent, ledgerBookId, effectiveDate, ct).ConfigureAwait(false);
                    candidates.AddRange(softClosed.Candidates);
                    if (softClosed.HasNonActionableMatches || !softClosed.IsAuthoritative)
                    {
                        restatementRequired = true;
                        _logger.LogWarning(
                            "Security Master revision {RevisionId} affects soft-closed ledger book {LedgerBookId} at {EffectiveDate}, but the canonical reporting candidate lookup is indeterminate or has a non-actionable match; flagging manual restatement follow-up.",
                            publishedEvent.RevisionId,
                            ledgerBookId,
                            effectiveDate);
                    }
                    break;

                case LedgerPeriodStatusDto.HardClosed:
                default:
                    // Hard-closed or any indeterminate/default-deny state: no posting, mandatory proposal.
                    restatementRequired = true;
                    var hardClosed = await ResolveCandidatesAsync(publishedEvent, ledgerBookId, effectiveDate, ct).ConfigureAwait(false);
                    candidates.AddRange(hardClosed.Candidates);
                    _logger.LogInformation(
                        "Security Master revision {RevisionId} affects hard-closed ledger book {LedgerBookId} at {EffectiveDate}; proposing restatement (no posting).",
                        publishedEvent.RevisionId, ledgerBookId, effectiveDate);
                    break;
            }
        }

        // The same fund-scoped published pack can surface from more than one affected ledger book;
        // collapse to one proposal per report so the operator never sees a duplicate restatement
        // candidate. Any located candidate (even from a soft-closed book) is a published pack that must
        // be restated.
        var distinctCandidates = candidates
            .GroupBy(static candidate => candidate.ReportId)
            .Select(static group => group.First())
            .ToArray();

        return new SecurityMasterRestatementDecision(
            restatementRequired || distinctCandidates.Length > 0, distinctCandidates);
    }

    private Task<RestatementCandidateResult> ResolveCandidatesAsync(
        SecurityMasterRevisionPublishedEvent publishedEvent, Guid ledgerBookId, DateOnly effectiveDate, CancellationToken ct)
        => _candidateResolver.ResolveAsync(
            publishedEvent.SecurityId, ledgerBookId, effectiveDate, publishedEvent.ChangedFields,
            publishedEvent.DownstreamImpact.FundProfileId, ct);
}
