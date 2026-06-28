using Meridian.Contracts.Workstation;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Resolves the durable accounting ledger books a published Security Master revision could have
/// touched, so the period-aware restatement resolver (Phase 3) can route each book by its accounting
/// period lock status. This is the publish-time feed for
/// <see cref="SecurityMasterRevisionPublishedEvent.AffectedLedgerBookIds"/>; without it the resolver
/// is a no-op end-to-end (an empty affected-book set short-circuits to no restatement).
///
/// <para>Restatement protects <b>durable accounting books</b> — the only ledger surface that carries
/// an accounting-period lock. Those books are keyed by fund profile, so a fund-scoped impact resolves
/// to that fund's books. When the impact reports no ledger exposure, or no durable ledger backend is
/// configured, there is no locked book to protect and the affected set is empty.</para>
/// </summary>
public interface IAffectedLedgerBookResolver
{
    /// <summary>
    /// Resolves the distinct durable ledger book ids implicated by <paramref name="impact"/>.
    /// Returns an empty list when the impact has no ledger exposure or no books can be resolved.
    /// </summary>
    Task<IReadOnlyList<Guid>> ResolveAsync(
        SecurityMasterDownstreamImpactDto impact, CancellationToken ct = default);
}

/// <summary>
/// Default <see cref="IAffectedLedgerBookResolver"/> over <see cref="ILedgerJournalStore"/>. When the
/// impact reports ledger exposure for a known fund profile, every durable ledger book registered for
/// that fund is treated as potentially affected and routed through the period-aware lock check.
/// </summary>
/// <remarks>
/// <para>The <see cref="ILedgerJournalStore"/> is an optional dependency (registered only when a
/// durable ledger backend is configured — see <c>LedgerFeatureRegistration</c>, which binds the store
/// via <c>GetService</c>). With no store, no durable book carries a period lock, so the affected set
/// is empty.</para>
/// <para><b>Granularity:</b> the display-oriented <see cref="SecurityMasterDownstreamImpactDto"/> does
/// not carry per-book identifiers, so resolution is at fund-book granularity — conservative by design,
/// since over-including a fund's books yields an extra restatement <i>proposal</i> (operator-reviewed,
/// never auto-applied) whereas under-including would risk a silent mutation of a closed book. Narrowing
/// to the specific posting books that referenced the security (via journal-line dimensions) and
/// cross-fund expansion for unscoped impacts are deferred to later slices.</para>
/// </remarks>
public sealed class LedgerBookAffectedResolver : IAffectedLedgerBookResolver
{
    private readonly ILogger<LedgerBookAffectedResolver> _logger;
    private readonly ILedgerJournalStore? _journalStore;

    public LedgerBookAffectedResolver(
        ILogger<LedgerBookAffectedResolver> logger,
        ILedgerJournalStore? journalStore = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _journalStore = journalStore;
    }

    public async Task<IReadOnlyList<Guid>> ResolveAsync(
        SecurityMasterDownstreamImpactDto impact, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(impact);

        if (_journalStore is null)
        {
            // No durable ledger backend ⇒ no book carries an accounting-period lock ⇒ nothing to restate.
            return [];
        }

        // No reported ledger exposure ⇒ the edit did not reach a posting book; skip the lookup.
        if (impact.LedgerExposureCount <= 0)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(impact.FundProfileId))
        {
            // Ledger exposure exists but the impact is not scoped to a single fund profile, so the
            // specific books cannot be resolved from the display impact. Cross-fund expansion is a
            // later slice; surface the gap rather than silently resolving an arbitrary scope.
            _logger.LogWarning(
                "Ledger exposure ({LedgerExposureCount}) reported with no fund profile scope; affected ledger books could not be resolved for period-aware routing.",
                impact.LedgerExposureCount);
            return [];
        }

        try
        {
            var books = await _journalStore
                .ListLedgerBooksAsync(fundProfileId: impact.FundProfileId, ct: ct)
                .ConfigureAwait(false);

            return books
                .Select(static b => b.LedgerBookId)
                .Distinct()
                .ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Failed to resolve affected ledger books for fund profile {FundProfileId}; period-aware routing will see no books for this publish.",
                impact.FundProfileId);
            return [];
        }
    }
}
