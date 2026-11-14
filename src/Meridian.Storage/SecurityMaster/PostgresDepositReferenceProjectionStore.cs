using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Reads deposit reference projections from PostgreSQL by security, institution, and maturity.
/// </summary>
public sealed class PostgresDepositReferenceProjectionStore : IDepositReferenceProjectionStore
{
    internal const string AssetClass = "Deposit";

    internal static readonly RegistryProjectionQuery ByInstitution = new(nameof(ByInstitution),
        """
        where lower(institution_name) = lower(@institution_name)
        order by maturity_date
        """);

    internal static readonly RegistryProjectionQuery MaturingBefore = new(nameof(MaturingBefore),
        """
        where maturity_date is not null
          and maturity_date < @before_date
        order by maturity_date
        """);

    private readonly RegistryReferenceProjectionStore<DepositProjectionRow> _store;

    public PostgresDepositReferenceProjectionStore(SecurityMasterOptions options)
    {
        _store = CreateStore(options);
    }

    internal static RegistryReferenceProjectionStore<DepositProjectionRow> CreateStore(SecurityMasterOptions options)
        => new(options, AssetClass, MapRow, [ByInstitution, MaturingBefore]);

    public Task<DepositProjectionRow?> GetDepositAsync(Guid securityId, CancellationToken ct = default)
        => _store.GetBySecurityIdAsync(securityId, ct);

    public Task<IReadOnlyList<DepositProjectionRow>> GetByInstitutionAsync(string institutionName, CancellationToken ct = default)
        => _store.ListAsync(ByInstitution, [new NpgsqlParameter("institution_name", institutionName)], ct);

    public Task<IReadOnlyList<DepositProjectionRow>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default)
        => _store.ListAsync(MaturingBefore, [new NpgsqlParameter("before_date", beforeDate.ToDateTime(TimeOnly.MinValue))], ct);

    internal static DepositProjectionRow MapRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.String("display_name"),
            row.String("currency"),
            row.String("deposit_type"),
            row.String("institution_name"),
            row.NullableDate("maturity_date"),
            row.NullableDecimal("interest_rate"),
            row.NullableString("day_count"),
            row.Boolean("is_callable"),
            row.String("primary_identifier_value"),
            row.Int64("version"));
}
