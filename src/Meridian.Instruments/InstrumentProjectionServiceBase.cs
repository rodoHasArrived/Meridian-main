using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Instruments;

/// <summary>
/// Shared shape for instrument reference projection services: verify the Security Master
/// asset class, fetch the instrument projection row, and map it to the contract DTO.
/// Derived services keep their instrument-specific query methods and delegate the common
/// gate/fetch/map flow to this base.
/// </summary>
/// <typeparam name="TRow">The projection-store row type for the instrument.</typeparam>
/// <typeparam name="TDto">The reference DTO exposed by the instrument's contract.</typeparam>
public abstract class InstrumentProjectionServiceBase<TRow, TDto>
    where TRow : class
    where TDto : class
{
    protected InstrumentProjectionServiceBase(ISecurityMasterStore securityMasterStore)
    {
        SecurityMasterStore = securityMasterStore;
    }

    /// <summary>Security Master store used for asset-class verification.</summary>
    protected ISecurityMasterStore SecurityMasterStore { get; }

    /// <summary>Security Master asset class this service projects (e.g. "Equity").</summary>
    protected abstract string AssetClass { get; }

    /// <summary>Fetches the instrument projection row for the security.</summary>
    protected abstract Task<TRow?> FetchRowAsync(Guid securityId, CancellationToken ct);

    /// <summary>Maps a projection row to the instrument's reference DTO.</summary>
    protected abstract TDto MapRow(TRow row);

    /// <summary>
    /// Fetches the reference DTO for <paramref name="securityId"/>, returning null when the
    /// security is unknown, carries a different asset class, or has no projection row.
    /// </summary>
    public async Task<TDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
    {
        var security = await GetVerifiedSecurityAsync(securityId, ct).ConfigureAwait(false);
        if (security is null)
        {
            return null;
        }

        var row = await FetchRowAsync(securityId, ct).ConfigureAwait(false);
        return row is null ? null : MapRow(row);
    }

    /// <summary>
    /// Loads the Security Master projection and returns it only when it exists and matches
    /// <see cref="AssetClass"/> (case-insensitive); otherwise null.
    /// </summary>
    protected async Task<SecurityProjectionRecord?> GetVerifiedSecurityAsync(Guid securityId, CancellationToken ct)
    {
        var security = await SecurityMasterStore.GetProjectionAsync(securityId, ct).ConfigureAwait(false);
        return security is null || !string.Equals(security.AssetClass, AssetClass, StringComparison.OrdinalIgnoreCase)
            ? null
            : security;
    }

    /// <summary>Maps a batch of projection rows to DTOs.</summary>
    protected IReadOnlyList<TDto> MapRows(IReadOnlyList<TRow> rows)
        => rows.Select(MapRow).ToArray();

    /// <summary>
    /// Runs a text-keyed projection query with the shared guard-and-normalize convention:
    /// blank keys return an empty list, otherwise the key is trimmed (and optionally
    /// upper-cased) before querying, and the rows are mapped to DTOs.
    /// </summary>
    protected async Task<IReadOnlyList<TDto>> QueryByTermAsync(
        string term,
        Func<string, CancellationToken, Task<IReadOnlyList<TRow>>> query,
        CancellationToken ct,
        bool toUpperInvariant = false)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return Array.Empty<TDto>();
        }

        var normalized = toUpperInvariant ? term.Trim().ToUpperInvariant() : term.Trim();
        var rows = await query(normalized, ct).ConfigureAwait(false);
        return MapRows(rows);
    }
}
