using Meridian.Contracts.Equity;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Instruments.Equity;

public sealed class EquityProjectionService
    : InstrumentProjectionServiceBase<EquityProjectionRow, EquityReferenceDto>, IEquityReferenceService
{
    private readonly IEquityReferenceProjectionStore _projectionStore;

    public EquityProjectionService(
        ISecurityMasterStore securityMasterStore,
        IEquityReferenceProjectionStore projectionStore)
        : base(securityMasterStore)
    {
        _projectionStore = projectionStore;
    }

    protected override string AssetClass => "Equity";

    protected override Task<EquityProjectionRow?> FetchRowAsync(Guid securityId, CancellationToken ct)
        => _projectionStore.GetEquityAsync(securityId, ct);

    public Task<IReadOnlyList<EquityReferenceDto>> GetByExchangeAsync(string exchangeCode, CancellationToken ct = default)
        => QueryByTermAsync(exchangeCode, _projectionStore.GetByExchangeAsync, ct);

    public Task<IReadOnlyList<EquityReferenceDto>> GetByIssuerAsync(string issuerName, CancellationToken ct = default)
        => QueryByTermAsync(issuerName, _projectionStore.GetByIssuerAsync, ct);

    protected override EquityReferenceDto MapRow(EquityProjectionRow row)
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
