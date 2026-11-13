using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Reads swap reference projections from PostgreSQL by security, type, and maturity.
/// </summary>
public sealed class PostgresSwapReferenceProjectionStore : ISwapReferenceProjectionStore
{
    internal const string AssetClass = "Swap";

    internal static readonly RegistryProjectionQuery BySwapType = new(nameof(BySwapType),
        """
        where lower(swap_type) = lower(@swap_type)
        order by maturity_date
        """);

    internal static readonly RegistryProjectionQuery MaturingBefore = new(nameof(MaturingBefore),
        """
        where maturity_date < @before_date
          and lifecycle_stat not in ('Matured', 'Terminated')
        order by maturity_date
        """);

    private readonly RegistryReferenceProjectionStore<SwapProjectionRow> _store;

    public PostgresSwapReferenceProjectionStore(SecurityMasterOptions options)
    {
        _store = CreateStore(options);
    }

    internal static RegistryReferenceProjectionStore<SwapProjectionRow> CreateStore(SecurityMasterOptions options)
        => new(options, AssetClass, MapRow, [BySwapType, MaturingBefore]);

    public Task<SwapProjectionRow?> GetSwapAsync(Guid securityId, CancellationToken ct = default)
        => _store.GetBySecurityIdAsync(securityId, ct);

    public Task<IReadOnlyList<SwapProjectionRow>> GetBySwapTypeAsync(string swapType, CancellationToken ct = default)
        => _store.ListAsync(BySwapType, [new NpgsqlParameter("swap_type", swapType)], ct);

    public Task<IReadOnlyList<SwapProjectionRow>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default)
        => _store.ListAsync(MaturingBefore, [new NpgsqlParameter("before_date", beforeDate.ToDateTime(TimeOnly.MinValue))], ct);

    internal static SwapProjectionRow MapRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.String("display_name"),
            row.String("currency"),
            row.NullableString("swap_type"),
            row.Date("effective_date"),
            row.Date("maturity_date"),
            row.String("lifecycle_stat"),
            row.String("primary_identifier_value"),
            row.Int64("version"));
}
