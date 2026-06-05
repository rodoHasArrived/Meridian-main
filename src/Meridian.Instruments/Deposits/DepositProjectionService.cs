using Meridian.Contracts.Deposits;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Instruments.Deposits;

public sealed class DepositProjectionService : IDepositReferenceService
{
    private readonly ISecurityMasterStore _securityMasterStore;
    private readonly IDepositReferenceProjectionStore _projectionStore;

    public DepositProjectionService(
        ISecurityMasterStore securityMasterStore,
        IDepositReferenceProjectionStore projectionStore)
    {
        _securityMasterStore = securityMasterStore;
        _projectionStore = projectionStore;
    }

    public async Task<DepositReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
    {
        var security = await _securityMasterStore.GetProjectionAsync(securityId, ct).ConfigureAwait(false);
        if (security is null || !string.Equals(security.AssetClass, "Deposit", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var row = await _projectionStore.GetDepositAsync(securityId, ct).ConfigureAwait(false);
        return row is null ? null : MapRow(row);
    }

    public async Task<IReadOnlyList<DepositReferenceDto>> GetByInstitutionAsync(string institutionName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(institutionName))
        {
            return Array.Empty<DepositReferenceDto>();
        }

        var rows = await _projectionStore.GetByInstitutionAsync(institutionName.Trim(), ct).ConfigureAwait(false);
        return rows.Select(MapRow).ToArray();
    }

    public async Task<IReadOnlyList<DepositReferenceDto>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default)
    {
        var rows = await _projectionStore.GetMaturingBeforeAsync(beforeDate, ct).ConfigureAwait(false);
        return rows.Select(MapRow).ToArray();
    }

    private static DepositReferenceDto MapRow(DepositProjectionRow row)
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
