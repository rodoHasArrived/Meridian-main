using Meridian.Contracts.CertificatesOfDeposit;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Instruments.CertificatesOfDeposit;

public sealed class CertificateOfDepositProjectionService : ICertificateOfDepositReferenceService
{
    private readonly ISecurityMasterStore _securityMasterStore;
    private readonly ICertificateOfDepositReferenceProjectionStore _projectionStore;

    public CertificateOfDepositProjectionService(
        ISecurityMasterStore securityMasterStore,
        ICertificateOfDepositReferenceProjectionStore projectionStore)
    {
        _securityMasterStore = securityMasterStore;
        _projectionStore = projectionStore;
    }

    public async Task<CertificateOfDepositReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
    {
        var security = await _securityMasterStore.GetProjectionAsync(securityId, ct).ConfigureAwait(false);
        if (security is null || !string.Equals(security.AssetClass, "CertificateOfDeposit", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var row = await _projectionStore.GetCertificateOfDepositAsync(securityId, ct).ConfigureAwait(false);
        return row is null ? null : MapRow(row);
    }

    public async Task<IReadOnlyList<CertificateOfDepositReferenceDto>> GetByIssuerAsync(string issuerName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(issuerName))
        {
            return Array.Empty<CertificateOfDepositReferenceDto>();
        }

        var rows = await _projectionStore.GetByIssuerAsync(issuerName.Trim(), ct).ConfigureAwait(false);
        return rows.Select(MapRow).ToArray();
    }

    public async Task<IReadOnlyList<CertificateOfDepositReferenceDto>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default)
    {
        var rows = await _projectionStore.GetMaturingBeforeAsync(beforeDate, ct).ConfigureAwait(false);
        return rows.Select(MapRow).ToArray();
    }

    private static CertificateOfDepositReferenceDto MapRow(CertificateOfDepositProjectionRow row)
        => new(
            row.SecurityId,
            row.DisplayName,
            row.Currency,
            row.IssuerName,
            row.Maturity,
            row.CouponRate,
            row.CallableDate,
            row.DayCount,
            row.PrimaryIdentifierValue,
            row.Version);
}

public sealed class NullCertificateOfDepositReferenceService : ICertificateOfDepositReferenceService
{
    public Task<CertificateOfDepositReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult<CertificateOfDepositReferenceDto?>(null);

    public Task<IReadOnlyList<CertificateOfDepositReferenceDto>> GetByIssuerAsync(string issuerName, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CertificateOfDepositReferenceDto>>(Array.Empty<CertificateOfDepositReferenceDto>());

    public Task<IReadOnlyList<CertificateOfDepositReferenceDto>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CertificateOfDepositReferenceDto>>(Array.Empty<CertificateOfDepositReferenceDto>());
}
