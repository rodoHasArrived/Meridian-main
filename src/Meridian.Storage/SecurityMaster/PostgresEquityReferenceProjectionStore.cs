using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresEquityReferenceProjectionStore : IEquityReferenceProjectionStore
{
    internal const string AssetClass = "Equity";

    private readonly PostgresReferenceProjectionReader _reader;

    public PostgresEquityReferenceProjectionStore(SecurityMasterOptions options)
    {
        _reader = new PostgresReferenceProjectionReader(options);
    }

    public Task<EquityProjectionRow?> GetEquityAsync(Guid securityId, CancellationToken ct = default)
        => _reader.QuerySingleAsync(
            _reader.SelectSql(AssetClass, "where security_id = @security_id"),
            [new NpgsqlParameter("security_id", securityId)],
            MapRow,
            ct);

    public Task<IReadOnlyList<EquityProjectionRow>> GetByExchangeAsync(string exchangeCode, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectSql(AssetClass,
                """
                where exchange_code = @exchange_code
                order by display_name
                """),
            [new NpgsqlParameter("exchange_code", exchangeCode.Trim().ToUpperInvariant())],
            MapRow,
            ct);

    public Task<IReadOnlyList<EquityProjectionRow>> GetByIssuerAsync(string issuerName, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectSql(AssetClass,
                """
                where lower(issuer_name) = lower(@issuer_name)
                order by display_name
                """),
            [new NpgsqlParameter("issuer_name", issuerName.Trim())],
            MapRow,
            ct);

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
