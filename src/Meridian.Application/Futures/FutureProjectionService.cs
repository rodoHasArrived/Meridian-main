using Meridian.Contracts.Futures;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.Futures;

public sealed class FutureProjectionService : IFutureReferenceService
{
    private readonly ISecurityMasterStore _securityMasterStore;
    private readonly IFutureReferenceProjectionStore _projectionStore;

    public FutureProjectionService(
        ISecurityMasterStore securityMasterStore,
        IFutureReferenceProjectionStore projectionStore)
    {
        _securityMasterStore = securityMasterStore;
        _projectionStore = projectionStore;
    }

    public async Task<FutureReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
    {
        var security = await _securityMasterStore.GetProjectionAsync(securityId, ct).ConfigureAwait(false);
        if (security is null || !string.Equals(security.AssetClass, "Future", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var row = await _projectionStore.GetFutureAsync(securityId, ct).ConfigureAwait(false);
        return row is null ? null : MapRow(row);
    }

    public async Task<IReadOnlyList<FutureReferenceDto>> GetByRootSymbolAsync(string rootSymbol, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rootSymbol))
        {
            return Array.Empty<FutureReferenceDto>();
        }

        var rows = await _projectionStore.GetByRootSymbolAsync(rootSymbol.Trim().ToUpperInvariant(), ct).ConfigureAwait(false);
        return rows.Select(MapRow).ToArray();
    }

    public async Task<IReadOnlyList<FutureReferenceDto>> GetExpiryLadderAsync(string rootSymbol, CancellationToken ct = default)
    {
        var all = await GetByRootSymbolAsync(rootSymbol, ct).ConfigureAwait(false);
        return all
            .Where(r => r.LifecycleStat != FutureLifecycleStat.Retired)
            .OrderBy(r => r.ExpiryDate)
            .ToArray();
    }

    public async Task<FutureReferenceDto?> GetFrontMonthAsync(string rootSymbol, CancellationToken ct = default)
    {
        var ladder = await GetExpiryLadderAsync(rootSymbol, ct).ConfigureAwait(false);
        return ladder.FirstOrDefault(r => r.IsRollTarget)
            ?? ladder.FirstOrDefault(r => r.LifecycleStat == FutureLifecycleStat.Active);
    }

    private static FutureReferenceDto MapRow(FutureProjectionRow row)
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
