using Meridian.Contracts.MoneyMarketFunds;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Instruments.MoneyMarketFunds;

public sealed class MoneyMarketFundProjectionService
    : InstrumentProjectionServiceBase<MoneyMarketFundProjectionRow, MoneyMarketFundReferenceDto>,
      IMoneyMarketFundReferenceService
{
    private readonly IMoneyMarketFundReferenceProjectionStore _projectionStore;

    public MoneyMarketFundProjectionService(
        ISecurityMasterStore securityMasterStore,
        IMoneyMarketFundReferenceProjectionStore projectionStore)
        : base(securityMasterStore)
    {
        _projectionStore = projectionStore;
    }

    protected override string AssetClass => "MoneyMarketFund";

    protected override Task<MoneyMarketFundProjectionRow?> FetchRowAsync(Guid securityId, CancellationToken ct)
        => _projectionStore.GetMoneyMarketFundAsync(securityId, ct);

    public Task<IReadOnlyList<MoneyMarketFundReferenceDto>> GetByFundFamilyAsync(string fundFamily, CancellationToken ct = default)
        => QueryByTermAsync(fundFamily, _projectionStore.GetByFundFamilyAsync, ct);

    public async Task<IReadOnlyList<MoneyMarketFundReferenceDto>> GetBySweepEligibilityAsync(bool sweepEligible, CancellationToken ct = default)
    {
        var rows = await _projectionStore.GetBySweepEligibilityAsync(sweepEligible, ct).ConfigureAwait(false);
        return MapRows(rows);
    }

    protected override MoneyMarketFundReferenceDto MapRow(MoneyMarketFundProjectionRow row)
        => new(
            row.SecurityId,
            row.DisplayName,
            row.Currency,
            row.FundFamily,
            row.SweepEligible,
            row.WeightedAverageMaturityDays,
            row.LiquidityFeeEligible,
            row.PrimaryIdentifierValue,
            row.Version);
}

public sealed class NullMoneyMarketFundReferenceService : IMoneyMarketFundReferenceService
{
    public Task<MoneyMarketFundReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult<MoneyMarketFundReferenceDto?>(null);

    public Task<IReadOnlyList<MoneyMarketFundReferenceDto>> GetByFundFamilyAsync(string fundFamily, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MoneyMarketFundReferenceDto>>(Array.Empty<MoneyMarketFundReferenceDto>());

    public Task<IReadOnlyList<MoneyMarketFundReferenceDto>> GetBySweepEligibilityAsync(bool sweepEligible, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MoneyMarketFundReferenceDto>>(Array.Empty<MoneyMarketFundReferenceDto>());
}
