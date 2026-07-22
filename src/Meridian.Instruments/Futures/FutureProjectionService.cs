using Meridian.Contracts.Futures;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Instruments.Futures;

public sealed class FutureProjectionService
    : InstrumentProjectionServiceBase<FutureProjectionRow, FutureReferenceDto>, IFutureReferenceService
{
    private readonly IFutureReferenceProjectionStore _projectionStore;

    public FutureProjectionService(
        ISecurityMasterStore securityMasterStore,
        IFutureReferenceProjectionStore projectionStore)
        : base(securityMasterStore)
    {
        _projectionStore = projectionStore;
    }

    protected override string AssetClass => "Future";

    protected override Task<FutureProjectionRow?> FetchRowAsync(Guid securityId, CancellationToken ct)
        => _projectionStore.GetFutureAsync(securityId, ct);

    public Task<IReadOnlyList<FutureReferenceDto>> GetByRootSymbolAsync(string rootSymbol, CancellationToken ct = default)
        => QueryByTermAsync(rootSymbol, _projectionStore.GetByRootSymbolAsync, ct, toUpperInvariant: true);

    public async Task<IReadOnlyList<FutureReferenceDto>> GetExpiryLadderAsync(string rootSymbol, CancellationToken ct = default)
    {
        var all = await GetByRootSymbolAsync(rootSymbol, ct).ConfigureAwait(false);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return all
            .Where(r => r.LifecycleStat is not FutureLifecycleStat.Expired and not FutureLifecycleStat.Retired)
            .Where(r => r.ExpiryDate >= today)
            .OrderBy(r => r.ExpiryDate)
            .ToArray();
    }

    public async Task<FutureReferenceDto?> GetFrontMonthAsync(string rootSymbol, CancellationToken ct = default)
    {
        var ladder = await GetExpiryLadderAsync(rootSymbol, ct).ConfigureAwait(false);
        return ladder.FirstOrDefault(r => r.IsRollTarget)
            ?? ladder.FirstOrDefault(r => r.LifecycleStat == FutureLifecycleStat.Active);
    }

    protected override FutureReferenceDto MapRow(FutureProjectionRow row)
    {
        Enum.TryParse<FutureLifecycleStat>(row.LifecycleStat, ignoreCase: true, out var stat);
        return new FutureReferenceDto(
            row.SecurityId,
            row.DisplayName,
            row.Currency,
            row.RootSymbol,
            row.ContractMonth,
            row.ExpiryDate,
            row.Multiplier,
            row.SettlementType,
            row.IsRollTarget,
            row.RollWindowDays,
            row.LastTradingDate,
            row.FirstNoticeDate,
            row.DeliveryMonthDate,
            row.DeliveryLocationCode,
            stat,
            row.PrimaryIdentifierValue,
            row.Version);
    }
}

public sealed class NullFutureReferenceService : IFutureReferenceService
{
    public Task<FutureReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult<FutureReferenceDto?>(null);

    public Task<IReadOnlyList<FutureReferenceDto>> GetByRootSymbolAsync(string rootSymbol, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<FutureReferenceDto>>(Array.Empty<FutureReferenceDto>());

    public Task<IReadOnlyList<FutureReferenceDto>> GetExpiryLadderAsync(string rootSymbol, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<FutureReferenceDto>>(Array.Empty<FutureReferenceDto>());

    public Task<FutureReferenceDto?> GetFrontMonthAsync(string rootSymbol, CancellationToken ct = default)
        => Task.FromResult<FutureReferenceDto?>(null);
}
