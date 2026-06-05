using Meridian.Contracts.Equity;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Instruments.Equity;

public sealed class EquityProjectionService : IEquityReferenceService
{
    private readonly ISecurityMasterStore _securityMasterStore;
    private readonly IEquityReferenceProjectionStore _projectionStore;

    public EquityProjectionService(
        ISecurityMasterStore securityMasterStore,
        IEquityReferenceProjectionStore projectionStore)
    {
        _securityMasterStore = securityMasterStore;
        _projectionStore = projectionStore;
    }

    public async Task<EquityReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
    {
        var security = await _securityMasterStore.GetProjectionAsync(securityId, ct).ConfigureAwait(false);
        if (security is null || !string.Equals(security.AssetClass, "Equity", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var row = await _projectionStore.GetEquityAsync(securityId, ct).ConfigureAwait(false);
        return row is null ? null : MapRow(row);
    }

    public async Task<IReadOnlyList<EquityReferenceDto>> GetByExchangeAsync(string exchangeCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(exchangeCode))
        {
            return Array.Empty<EquityReferenceDto>();
        }

        var rows = await _projectionStore.GetByExchangeAsync(exchangeCode.Trim(), ct).ConfigureAwait(false);
        return rows.Select(MapRow).ToArray();
    }

    public async Task<IReadOnlyList<EquityReferenceDto>> GetByIssuerAsync(string issuerName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(issuerName))
        {
            return Array.Empty<EquityReferenceDto>();
        }

        var rows = await _projectionStore.GetByIssuerAsync(issuerName.Trim(), ct).ConfigureAwait(false);
        return rows.Select(MapRow).ToArray();
    }

    private static EquityReferenceDto MapRow(EquityProjectionRow row)
        => new(
            row.SecurityId,
            row.DisplayName,
            row.Currency,
            row.ShareClass,
            row.VotingRightsCat,
            row.Classification,
            row.ExchangeCode,
            row.CountryOfRisk,
            row.IssuerName,
            row.PrimaryIdentifierValue,
            row.Version);
}

public sealed class NullEquityReferenceService : IEquityReferenceService
{
    public Task<EquityReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult<EquityReferenceDto?>(null);

    public Task<IReadOnlyList<EquityReferenceDto>> GetByExchangeAsync(string exchangeCode, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<EquityReferenceDto>>(Array.Empty<EquityReferenceDto>());

    public Task<IReadOnlyList<EquityReferenceDto>> GetByIssuerAsync(string issuerName, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<EquityReferenceDto>>(Array.Empty<EquityReferenceDto>());
}
