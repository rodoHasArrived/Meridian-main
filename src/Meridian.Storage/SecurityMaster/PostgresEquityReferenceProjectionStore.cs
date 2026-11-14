using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Reads equity reference projections from PostgreSQL by security, exchange, and issuer.
/// </summary>
public sealed class PostgresEquityReferenceProjectionStore : IEquityReferenceProjectionStore
{
    internal const string AssetClass = "Equity";

    internal static readonly RegistryProjectionQuery ByExchange = new(nameof(ByExchange),
        """
        where exchange_code = @exchange_code
        order by display_name
        """);

    internal static readonly RegistryProjectionQuery ByIssuer = new(nameof(ByIssuer),
        """
        where lower(issuer_name) = lower(@issuer_name)
        order by display_name
        """);

    private readonly RegistryReferenceProjectionStore<EquityProjectionRow> _store;

    public PostgresEquityReferenceProjectionStore(SecurityMasterOptions options)
    {
        _store = CreateStore(options);
    }

    internal static RegistryReferenceProjectionStore<EquityProjectionRow> CreateStore(SecurityMasterOptions options)
        => new(options, AssetClass, MapRow, [ByExchange, ByIssuer]);

    public Task<EquityProjectionRow?> GetEquityAsync(Guid securityId, CancellationToken ct = default)
        => _store.GetBySecurityIdAsync(securityId, ct);

    public Task<IReadOnlyList<EquityProjectionRow>> GetByExchangeAsync(string exchangeCode, CancellationToken ct = default)
        => _store.ListAsync(ByExchange, [new NpgsqlParameter("exchange_code", exchangeCode.Trim().ToUpperInvariant())], ct);

    public Task<IReadOnlyList<EquityProjectionRow>> GetByIssuerAsync(string issuerName, CancellationToken ct = default)
        => _store.ListAsync(ByIssuer, [new NpgsqlParameter("issuer_name", issuerName.Trim())], ct);

    internal static EquityProjectionRow MapRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.String("display_name"),
            row.String("currency"),
            row.NullableString("share_class"),
            row.NullableString("voting_rights_cat"),
            row.NullableString("classification"),
            row.NullableString("exchange_code"),
            row.NullableString("country_of_risk"),
            row.NullableString("issuer_name"),
            row.String("primary_identifier_value"),
            row.Int64("version"));
}
