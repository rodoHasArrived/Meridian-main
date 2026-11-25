using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Reads money market fund reference projections from PostgreSQL by security, family, and sweep eligibility.
/// </summary>
public sealed class PostgresMoneyMarketFundReferenceProjectionStore : IMoneyMarketFundReferenceProjectionStore
{
    internal const string AssetClass = "MoneyMarketFund";

    internal static readonly RegistryProjectionQuery ByFundFamily = new(nameof(ByFundFamily),
        """
        where lower(fund_family) = lower(@fund_family)
        order by display_name
        """);

    internal static readonly RegistryProjectionQuery BySweepEligibility = new(nameof(BySweepEligibility),
        """
        where sweep_eligible = @sweep_eligible
        order by display_name
        """);

    private readonly RegistryReferenceProjectionStore<MoneyMarketFundProjectionRow> _store;

    public PostgresMoneyMarketFundReferenceProjectionStore(SecurityMasterOptions options)
    {
        _store = CreateStore(options);
    }

    internal static RegistryReferenceProjectionStore<MoneyMarketFundProjectionRow> CreateStore(SecurityMasterOptions options)
        => new(options, AssetClass, MapRow, [ByFundFamily, BySweepEligibility]);

    public Task<MoneyMarketFundProjectionRow?> GetMoneyMarketFundAsync(Guid securityId, CancellationToken ct = default)
        => _store.GetBySecurityIdAsync(securityId, ct);

    public Task<IReadOnlyList<MoneyMarketFundProjectionRow>> GetByFundFamilyAsync(string fundFamily, CancellationToken ct = default)
        => _store.ListAsync(ByFundFamily, [new NpgsqlParameter("fund_family", fundFamily)], ct);

    public Task<IReadOnlyList<MoneyMarketFundProjectionRow>> GetBySweepEligibilityAsync(bool sweepEligible, CancellationToken ct = default)
        => _store.ListAsync(BySweepEligibility, [new NpgsqlParameter("sweep_eligible", sweepEligible)], ct);

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
