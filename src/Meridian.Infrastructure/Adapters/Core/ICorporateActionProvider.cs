using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Provider-agnostic interface for fetching corporate action history (dividends and stock
/// splits) from a data source and merging it into the Security Master event store.
/// </summary>
/// <remarks>
/// <para>
/// Implementations should be decorated with <see cref="DataSourceAttribute"/> so they
/// are discoverable via the attribute-based provider registry (ADR-005).  The Security
/// Master aggregates results from all registered implementations with last-writer-wins
/// conflict resolution keyed by <c>(securityId, ex_date, corporateActionType)</c>.
/// </para>
/// <para>
/// Implementations must degrade gracefully (log and return empty) when the upstream
/// API is unavailable or the ticker is not recognised by the provider.
/// </para>
/// <para>
/// An empty list from <see cref="FetchAsync"/> therefore cannot tell "no actions" from "fetch
/// failed". Providers that can tell the two apart override
/// <see cref="FetchWithOutcomeAsync"/>, which is the member ingest calls.
/// </para>
/// </remarks>
[ImplementsAdr("ADR-001", "Provider-agnostic corporate action abstraction follows IHistoricalDataProvider pattern")]
[ImplementsAdr("ADR-005", "Attribute-based discovery via DataSourceAttribute")]
public interface ICorporateActionProvider
{
    /// <summary>
    /// Returns the provider identifier string used to tag corporate action events when
    /// multiple sources contribute data for the same security.
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Declares whether this adapter release is certified to supply acceptance-grade event
    /// identity and retained raw evidence. The safe default is review-only.
    /// </summary>
    CorporateActionProviderReleaseStatusDto ReleaseStatus =>
        CorporateActionProviderReleaseStatusDto.ReviewOnly;

    /// <summary>
    /// Fetches all available corporate actions (dividends, splits, mergers, etc.) for
    /// <paramref name="ticker"/> and <paramref name="securityId"/> from the upstream
    /// data source.
    /// </summary>
    /// <param name="ticker">The canonical ticker string (e.g., "AAPL").</param>
    /// <param name="securityId">The Security Master identifier for the security.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Zero or more <see cref="CorporateActionCommand"/> objects ready to be dispatched
    /// to the Security Master event store.  The caller is responsible for deduplication.
    /// </returns>
    Task<IReadOnlyList<CorporateActionCommand>> FetchAsync(
        string ticker,
        Guid securityId,
        CancellationToken ct = default);

    /// <summary>
    /// Fetches corporate actions and states whether the provider's history was read in full.
    /// Ingest calls this member so that a fetch that failed or was cut short is not mistaken
    /// for a provider that has no actions.
    /// </summary>
    /// <remarks>
    /// The default adapts <see cref="FetchAsync"/>: whatever it returns is treated as a complete
    /// result and whatever it throws propagates, which is the behavior providers had before this
    /// member existed. A provider that can tell a failed or truncated fetch from an empty history
    /// overrides it.
    /// </remarks>
    async Task<CorporateActionFetchResult> FetchWithOutcomeAsync(
        string ticker,
        Guid securityId,
        CancellationToken ct = default)
        => CorporateActionFetchResult.Complete(
            await FetchAsync(ticker, securityId, ct).ConfigureAwait(false));
}

/// <summary>How much of a provider's corporate-action history a fetch read.</summary>
public enum CorporateActionFetchStatus
{
    /// <summary>The provider answered in full; an empty result means it has no actions.</summary>
    Complete = 0,

    /// <summary>The fetch began but stopped before the provider's history was read in full.</summary>
    Incomplete = 1,

    /// <summary>The provider could not be read at all (for example no credentials, or a refused first request).</summary>
    Unavailable = 2,
}

/// <summary>
/// Outcome of <see cref="ICorporateActionProvider.FetchWithOutcomeAsync"/>.
/// <see cref="Observations"/> is populated only for a <see cref="CorporateActionFetchStatus.Complete"/>
/// fetch: part of a history is withheld rather than handed on as if it were all of it, and
/// <see cref="WithheldObservationCount"/> records how many observations were read and withheld.
/// <see cref="Reason"/> is a fixed provider-authored description; it never carries credentials
/// or provider response bodies.
/// </summary>
public sealed class CorporateActionFetchResult
{
    private CorporateActionFetchResult(
        CorporateActionFetchStatus status,
        IReadOnlyList<CorporateActionCommand> observations,
        string? reason,
        int withheldObservationCount)
    {
        Status = status;
        Observations = observations;
        Reason = reason;
        WithheldObservationCount = withheldObservationCount;
    }

    public CorporateActionFetchStatus Status { get; }

    public IReadOnlyList<CorporateActionCommand> Observations { get; }

    public string? Reason { get; }

    public int WithheldObservationCount { get; }

    public bool IsComplete => Status == CorporateActionFetchStatus.Complete;

    public static CorporateActionFetchResult Complete(IReadOnlyList<CorporateActionCommand> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        return new(CorporateActionFetchStatus.Complete, observations, null, 0);
    }

    public static CorporateActionFetchResult Incomplete(string reason, int withheldObservationCount = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentOutOfRangeException.ThrowIfNegative(withheldObservationCount);
        return new(CorporateActionFetchStatus.Incomplete, [], reason, withheldObservationCount);
    }

    public static CorporateActionFetchResult Unavailable(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(CorporateActionFetchStatus.Unavailable, [], reason, 0);
    }
}

/// <summary>
/// Command model produced by an <see cref="ICorporateActionProvider"/> implementation.
/// Contains the fields needed to raise a <c>CorporateActionDeclared</c> domain event.
/// </summary>
public sealed record CorporateActionCommand(
    Guid SecurityId,
    string ActionType,
    DateOnly ExDate,
    DateOnly? RecordDate,
    DateOnly? PayableDate,
    decimal? Amount,
    string? Currency,
    decimal? SplitFromFactor,
    decimal? SplitToFactor,
    string? Description,
    string SourceProvider,
    string? SourceEventId = null,
    string? SourceEventVersion = null,
    DateTimeOffset? ObservedAtUtc = null,
    string? EvidenceHash = null,
    string? EvidenceReference = null,
    CorporateActionProviderReleaseStatusDto ReleaseStatus = CorporateActionProviderReleaseStatusDto.ReviewOnly);
