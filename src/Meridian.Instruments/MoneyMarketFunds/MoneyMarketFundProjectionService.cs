using Meridian.Contracts.MoneyMarketFunds;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Instruments.MoneyMarketFunds;

public sealed class MoneyMarketFundProjectionService : IMoneyMarketFundReferenceService
{
    private readonly ISecurityMasterStore _securityMasterStore;
    private readonly IMoneyMarketFundReferenceProjectionStore _projectionStore;

    public MoneyMarketFundProjectionService(
        ISecurityMasterStore securityMasterStore,
        IMoneyMarketFundReferenceProjectionStore projectionStore)
    {
        _securityMasterStore = securityMasterStore;
        _projectionStore = projectionStore;
    }

    public async Task<MoneyMarketFundReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
    {
        var security = await _securityMasterStore.GetProjectionAsync(securityId, ct).ConfigureAwait(false);
        if (security is null || !string.Equals(security.AssetClass, "MoneyMarketFund", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var row = await _projectionStore.GetMoneyMarketFundAsync(securityId, ct).ConfigureAwait(false);
        return row is null ? null : MapRow(row);
    }

    public async Task<IReadOnlyList<MoneyMarketFundReferenceDto>> GetByFundFamilyAsync(string fundFamily, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fundFamily))
        {
            return Array.Empty<MoneyMarketFundReferenceDto>();
        }

        var rows = await _projectionStore.GetByFundFamilyAsync(fundFamily.Trim(), ct).ConfigureAwait(false);
        return rows.Select(MapRow).ToArray();
    }

    public async Task<IReadOnlyList<MoneyMarketFundReferenceDto>> GetBySweepEligibilityAsync(bool sweepEligible, CancellationToken ct = default)
    {
        var rows = await _projectionStore.GetBySweepEligibilityAsync(sweepEligible, ct).ConfigureAwait(false);
        return rows.Select(MapRow).ToArray();
    }

    private static MoneyMarketFundReferenceDto MapRow(MoneyMarketFundProjectionRow row)
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
