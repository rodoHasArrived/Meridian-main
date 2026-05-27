using Meridian.Contracts.Treasury;

namespace Meridian.Storage.MoneyMarket;

/// <summary>
/// Persistence contract for the Money Market Fund auxiliary store.
/// Stores fund records, per-fund liquidity overrides, and rebuild checkpoints.
/// </summary>
public interface IMoneyMarketFundAuxStore
{
    // ── Fund records ─────────────────────────────────────────────────────────

    Task UpsertFundAsync(
        Guid securityId,
        string displayName,
        string currency,
        string? fundFamily,
        bool isSweepEligible,
        int? weightedAverageMaturityDays,
        bool hasLiquidityFee,
        bool isActive,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        long version,
        CancellationToken ct = default);

    Task<(Guid SecurityId, string DisplayName, string Currency, string? FundFamily,
          bool IsSweepEligible, int? WeightedAverageMaturityDays, bool HasLiquidityFee,
          bool IsActive, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, long Version)?> GetFundAsync(
        Guid securityId,
        CancellationToken ct = default);

    Task<IReadOnlyList<(Guid SecurityId, string DisplayName, string Currency, string? FundFamily,
                        bool IsSweepEligible, int? WeightedAverageMaturityDays, bool HasLiquidityFee,
                        bool IsActive, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, long Version)>> GetAllFundsAsync(
        CancellationToken ct = default);

    // ── Liquidity overrides ──────────────────────────────────────────────────

    Task UpsertLiquidityOverrideAsync(Guid securityId, MmfLiquidityState state, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, MmfLiquidityState>> GetAllLiquidityOverridesAsync(CancellationToken ct = default);

    // ── Rebuild checkpoints ──────────────────────────────────────────────────

    Task UpsertRebuildCheckpointAsync(MmfRebuildCheckpointDto checkpoint, CancellationToken ct = default);
    Task<IReadOnlyList<MmfRebuildCheckpointDto>> GetRebuildCheckpointsAsync(CancellationToken ct = default);

    // ── Utility ──────────────────────────────────────────────────────────────

    Task<bool> IsEmptyAsync(CancellationToken ct = default);
}
