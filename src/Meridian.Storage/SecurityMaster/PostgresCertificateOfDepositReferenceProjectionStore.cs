using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Reads certificate of deposit reference projections from PostgreSQL by security, issuer, and maturity.
/// </summary>
public sealed class PostgresCertificateOfDepositReferenceProjectionStore : ICertificateOfDepositReferenceProjectionStore
{
    internal const string AssetClass = "CertificateOfDeposit";

    internal static readonly RegistryProjectionQuery ByIssuer = new(nameof(ByIssuer),
        """
        where lower(issuer_name) = lower(@issuer_name)
        order by maturity_date
        """);

    internal static readonly RegistryProjectionQuery MaturingBefore = new(nameof(MaturingBefore),
        """
        where maturity_date < @before_date
        order by maturity_date
        """);

    private readonly RegistryReferenceProjectionStore<CertificateOfDepositProjectionRow> _store;

    public PostgresCertificateOfDepositReferenceProjectionStore(SecurityMasterOptions options)
    {
        _store = CreateStore(options);
    }

    internal static RegistryReferenceProjectionStore<CertificateOfDepositProjectionRow> CreateStore(SecurityMasterOptions options)
        => new(options, AssetClass, MapRow, [ByIssuer, MaturingBefore]);

    public Task<CertificateOfDepositProjectionRow?> GetCertificateOfDepositAsync(Guid securityId, CancellationToken ct = default)
        => _store.GetBySecurityIdAsync(securityId, ct);

    public Task<IReadOnlyList<CertificateOfDepositProjectionRow>> GetByIssuerAsync(string issuerName, CancellationToken ct = default)
        => _store.ListAsync(ByIssuer, [new NpgsqlParameter("issuer_name", issuerName)], ct);

    public Task<IReadOnlyList<CertificateOfDepositProjectionRow>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default)
        => _store.ListAsync(MaturingBefore, [new NpgsqlParameter("before_date", beforeDate.ToDateTime(TimeOnly.MinValue))], ct);

    internal static CertificateOfDepositProjectionRow MapRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.String("display_name"),
            row.String("currency"),
            row.String("issuer_name"),
            row.Date("maturity_date"),
            row.NullableDecimal("coupon_rate"),
            row.NullableDate("callable_date"),
            row.NullableString("day_count"),
            row.String("primary_identifier_value"),
            row.Int64("version"));
}
