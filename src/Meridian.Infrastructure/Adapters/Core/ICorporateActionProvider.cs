using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;

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
    string SourceProvider);
