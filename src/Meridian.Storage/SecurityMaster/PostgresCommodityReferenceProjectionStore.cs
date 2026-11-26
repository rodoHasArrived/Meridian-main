using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Reads commodity reference projections from PostgreSQL by security, type, and exchange.
/// </summary>
public sealed class PostgresCommodityReferenceProjectionStore : ICommodityReferenceProjectionStore
{
    internal const string AssetClass = "Commodity";

    internal static readonly RegistryProjectionQuery ByCommodityType = new(nameof(ByCommodityType),
        """
        where lower(commodity_type) = lower(@commodity_type)
        order by display_name
        """);

    internal static readonly RegistryProjectionQuery ByExchange = new(nameof(ByExchange),
        """
        where exchange_code = @exchange_code
        order by display_name
        """);

    private readonly RegistryReferenceProjectionStore<CommodityProjectionRow> _store;

    public PostgresCommodityReferenceProjectionStore(SecurityMasterOptions options)
    {
        _store = CreateStore(options);
    }

    internal static RegistryReferenceProjectionStore<CommodityProjectionRow> CreateStore(SecurityMasterOptions options)
        => new(options, AssetClass, MapRow, [ByCommodityType, ByExchange]);

    public Task<CommodityProjectionRow?> GetCommodityAsync(Guid securityId, CancellationToken ct = default)
        => _store.GetBySecurityIdAsync(securityId, ct);

    public Task<IReadOnlyList<CommodityProjectionRow>> GetByCommodityTypeAsync(string commodityType, CancellationToken ct = default)
        => _store.ListAsync(ByCommodityType, [new NpgsqlParameter("commodity_type", commodityType)], ct);

    public Task<IReadOnlyList<CommodityProjectionRow>> GetByExchangeAsync(string exchangeCode, CancellationToken ct = default)
        => _store.ListAsync(ByExchange, [new NpgsqlParameter("exchange_code", exchangeCode.Trim().ToUpperInvariant())], ct);

    internal static CommodityProjectionRow MapRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.String("display_name"),
            row.String("currency"),
            row.String("commodity_type"),
            row.NullableString("denomination"),
            row.NullableDecimal("contract_size"),
            row.NullableString("exchange_code"),
            row.NullableString("delivery_country"),
            row.String("primary_identifier_value"),
            row.Int64("version"));
}
