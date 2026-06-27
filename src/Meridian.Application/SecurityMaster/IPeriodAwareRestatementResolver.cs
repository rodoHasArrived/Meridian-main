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
/// Resolves the report packs that consumed a changed line in a locked period covering an upstream
/// edit, as restatement candidates. The default implementation returns none (report-usage projection
/// integration is a later slice); an empty result for a hard-closed book is treated as a manual
/// locate-affected-packs task rather than "no restatement needed".
/// </summary>
public interface IRestatementCandidateResolver
{
    Task<IReadOnlyList<RestatementCandidateDto>> ResolveAsync(
        Guid securityId,
        Guid ledgerBookId,
        DateOnly effectiveDate,
        IReadOnlyList<string> changedFields,
        CancellationToken ct = default);
}

/// <summary>Default no-op candidate resolver — surfaces no packs until the report-usage projection is wired.</summary>
public sealed class NullRestatementCandidateResolver : IRestatementCandidateResolver
{
    private static readonly IReadOnlyList<RestatementCandidateDto> Empty = [];

    public Task<IReadOnlyList<RestatementCandidateDto>> ResolveAsync(
        Guid securityId,
        Guid ledgerBookId,
        DateOnly effectiveDate,
        IReadOnlyList<string> changedFields,
        CancellationToken ct = default)
        => Task.FromResult(Empty);
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
                    // report packs that consumed the line need restating.
                    candidates.AddRange(await ResolveCandidatesAsync(publishedEvent, ledgerBookId, effectiveDate, ct).ConfigureAwait(false));
                    break;

                case LedgerPeriodStatusDto.HardClosed:
                default:
                    // Hard-closed or any indeterminate/default-deny state: no posting, mandatory proposal.
                    restatementRequired = true;
                    candidates.AddRange(await ResolveCandidatesAsync(publishedEvent, ledgerBookId, effectiveDate, ct).ConfigureAwait(false));
                    _logger.LogInformation(
                        "Security Master revision {RevisionId} affects hard-closed ledger book {LedgerBookId} at {EffectiveDate}; proposing restatement (no posting).",
                        publishedEvent.RevisionId, ledgerBookId, effectiveDate);
                    break;
            }
        }

        // Any located candidate (even from a soft-closed book) is a published pack that must be restated.
        return new SecurityMasterRestatementDecision(restatementRequired || candidates.Count > 0, candidates);
    }

    private Task<IReadOnlyList<RestatementCandidateDto>> ResolveCandidatesAsync(
        SecurityMasterRevisionPublishedEvent publishedEvent, Guid ledgerBookId, DateOnly effectiveDate, CancellationToken ct)
        => _candidateResolver.ResolveAsync(
            publishedEvent.SecurityId, ledgerBookId, effectiveDate, publishedEvent.ChangedFields, ct);
}
