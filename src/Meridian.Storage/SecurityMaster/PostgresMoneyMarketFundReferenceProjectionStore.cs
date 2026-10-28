using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Reads money market fund reference projections from PostgreSQL by security, family, and sweep eligibility.
/// </summary>
public sealed class PostgresMoneyMarketFundReferenceProjectionStore : IMoneyMarketFundReferenceProjectionStore
{
    internal const string AssetClass = "MoneyMarketFund";

    private readonly PostgresReferenceProjectionReader _reader;

    public PostgresMoneyMarketFundReferenceProjectionStore(SecurityMasterOptions options)
    {
        _reader = new PostgresReferenceProjectionReader(options);
    }

    public Task<MoneyMarketFundProjectionRow?> GetMoneyMarketFundAsync(Guid securityId, CancellationToken ct = default)
        => _reader.QuerySingleAsync(
            _reader.SelectSql(AssetClass, "where security_id = @security_id"),
            [new NpgsqlParameter("security_id", securityId)],
            MapRow,
            ct);

    public Task<IReadOnlyList<MoneyMarketFundProjectionRow>> GetByFundFamilyAsync(string fundFamily, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectSql(AssetClass,
                """
                where lower(fund_family) = lower(@fund_family)
                order by display_name
                """),
            [new NpgsqlParameter("fund_family", fundFamily)],
            MapRow,
            ct);

    public Task<IReadOnlyList<MoneyMarketFundProjectionRow>> GetBySweepEligibilityAsync(bool sweepEligible, CancellationToken ct = default)
        => _reader.QueryAsync(
            _reader.SelectSql(AssetClass,
                """
                where sweep_eligible = @sweep_eligible
                order by display_name
                """),
            [new NpgsqlParameter("sweep_eligible", sweepEligible)],
            MapRow,
            ct);

    internal static MoneyMarketFundProjectionRow MapRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.String("display_name"),
            row.String("currency"),
            row.NullableString("fund_family"),
            row.Boolean("sweep_eligible"),
            row.NullableInt32("weighted_average_maturity_days"),
            row.Boolean("liquidity_fee_eligible"),
            row.String("primary_identifier_value"),
            row.Int64("version"));
}
