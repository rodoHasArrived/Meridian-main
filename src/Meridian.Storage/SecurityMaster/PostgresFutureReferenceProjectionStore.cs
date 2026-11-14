using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Reads futures reference projections from PostgreSQL by security and root symbol.
/// </summary>
public sealed class PostgresFutureReferenceProjectionStore : IFutureReferenceProjectionStore
{
    internal const string AssetClass = "Future";

    internal static readonly RegistryProjectionQuery ByRootSymbol = new(nameof(ByRootSymbol),
        """
        where root_symbol = @root_symbol
        order by expiry_date
        """);

    private readonly RegistryReferenceProjectionStore<FutureProjectionRow> _store;

    public PostgresFutureReferenceProjectionStore(SecurityMasterOptions options)
    {
        _store = CreateStore(options);
    }

    internal static RegistryReferenceProjectionStore<FutureProjectionRow> CreateStore(SecurityMasterOptions options)
        => new(options, AssetClass, MapRow, [ByRootSymbol]);

    public Task<FutureProjectionRow?> GetFutureAsync(Guid securityId, CancellationToken ct = default)
        => _store.GetBySecurityIdAsync(securityId, ct);

    public Task<IReadOnlyList<FutureProjectionRow>> GetByRootSymbolAsync(string rootSymbol, CancellationToken ct = default)
        => _store.ListAsync(ByRootSymbol, [new NpgsqlParameter("root_symbol", rootSymbol.Trim().ToUpperInvariant())], ct);

    internal static FutureProjectionRow MapRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.String("display_name"),
            row.String("currency"),
            row.String("root_symbol"),
            row.String("contract_month"),
            row.Date("expiry_date"),
            row.Decimal("multiplier"),
            row.NullableString("settlement_type"),
            row.Boolean("is_roll_target"),
            row.NullableInt32("roll_window_days"),
            row.NullableDate("last_trading_dt"),
            row.NullableDate("first_notice_dt"),
            row.NullableDate("delivery_month_dt"),
            row.NullableString("delivery_location_code"),
            row.String("lifecycle_stat"),
            row.String("primary_identifier_value"),
            row.Int64("version"));
}
