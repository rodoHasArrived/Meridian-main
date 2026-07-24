using Meridian.Contracts.CertificatesOfDeposit;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Instruments.CertificatesOfDeposit;

public sealed class CertificateOfDepositProjectionService
    : InstrumentProjectionServiceBase<CertificateOfDepositProjectionRow, CertificateOfDepositReferenceDto>,
      ICertificateOfDepositReferenceService
{
    private readonly ICertificateOfDepositReferenceProjectionStore _projectionStore;

    public CertificateOfDepositProjectionService(
        ISecurityMasterStore securityMasterStore,
        ICertificateOfDepositReferenceProjectionStore projectionStore)
        : base(securityMasterStore)
    {
        _projectionStore = projectionStore;
    }

    protected override string AssetClass => "CertificateOfDeposit";

    protected override Task<CertificateOfDepositProjectionRow?> FetchRowAsync(Guid securityId, CancellationToken ct)
        => _projectionStore.GetCertificateOfDepositAsync(securityId, ct);

    public Task<IReadOnlyList<CertificateOfDepositReferenceDto>> GetByIssuerAsync(string issuerName, CancellationToken ct = default)
        => QueryByTermAsync(issuerName, _projectionStore.GetByIssuerAsync, ct);

    public async Task<IReadOnlyList<CertificateOfDepositReferenceDto>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default)
    {
        var rows = await _projectionStore.GetMaturingBeforeAsync(beforeDate, ct).ConfigureAwait(false);
        return MapRows(rows);
    }

    protected override CertificateOfDepositReferenceDto MapRow(CertificateOfDepositProjectionRow row)
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
