using Meridian.Contracts.Deposits;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Instruments.Deposits;

public sealed class DepositProjectionService
    : InstrumentProjectionServiceBase<DepositProjectionRow, DepositReferenceDto>, IDepositReferenceService
{
    private readonly IDepositReferenceProjectionStore _projectionStore;

    public DepositProjectionService(
        ISecurityMasterStore securityMasterStore,
        IDepositReferenceProjectionStore projectionStore)
        : base(securityMasterStore)
    {
        _projectionStore = projectionStore;
    }

    protected override string AssetClass => "Deposit";

    protected override Task<DepositProjectionRow?> FetchRowAsync(Guid securityId, CancellationToken ct)
        => _projectionStore.GetDepositAsync(securityId, ct);

    public Task<IReadOnlyList<DepositReferenceDto>> GetByInstitutionAsync(string institutionName, CancellationToken ct = default)
        => QueryByTermAsync(institutionName, _projectionStore.GetByInstitutionAsync, ct);

    public async Task<IReadOnlyList<DepositReferenceDto>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default)
    {
        var rows = await _projectionStore.GetMaturingBeforeAsync(beforeDate, ct).ConfigureAwait(false);
        return MapRows(rows);
    }

    protected override DepositReferenceDto MapRow(DepositProjectionRow row)
        => new(
            row.SecurityId,
            row.DisplayName,
            row.Currency,
            row.DepositType,
            row.InstitutionName,
            row.Maturity,
            row.InterestRate,
            row.DayCount,
            row.IsCallable,
            row.PrimaryIdentifierValue,
            row.Version);
}

public sealed class NullDepositReferenceService : IDepositReferenceService
{
    public Task<DepositReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult<DepositReferenceDto?>(null);

    public Task<IReadOnlyList<DepositReferenceDto>> GetByInstitutionAsync(string institutionName, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DepositReferenceDto>>(Array.Empty<DepositReferenceDto>());

    public Task<IReadOnlyList<DepositReferenceDto>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DepositReferenceDto>>(Array.Empty<DepositReferenceDto>());
}
