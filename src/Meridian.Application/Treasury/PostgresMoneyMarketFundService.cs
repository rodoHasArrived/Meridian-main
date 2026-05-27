using Meridian.Contracts.Treasury;
using Meridian.Storage.MoneyMarket;

namespace Meridian.Application.Treasury;

/// <summary>
/// PostgreSQL-backed implementation of <see cref="IMoneyMarketFundService"/>
/// and <see cref="IMmfLiquidityService"/>.
/// Fund records, liquidity overrides, and rebuild checkpoints are persisted
/// in the <see cref="IMoneyMarketFundAuxStore"/>.
/// </summary>
public sealed class PostgresMoneyMarketFundService : IMoneyMarketFundService, IMmfLiquidityService
{
    // WAM threshold (days) below which a fund is considered Liquid.
    private const int LiquidWamThresholdDays = 60;

    private readonly IMoneyMarketFundAuxStore _store;

    public PostgresMoneyMarketFundService(IMoneyMarketFundAuxStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    // ── IMoneyMarketFundService ──────────────────────────────────────────────

    public async Task<MmfDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default)
    {
        var row = await _store.GetFundAsync(securityId, ct);
        return row is null ? null : ToDto(row.Value);
    }

    public async Task<IReadOnlyList<MmfDetailDto>> SearchAsync(MmfSearchQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var allFunds = await _store.GetAllFundsAsync(ct);
        var overrides = await _store.GetAllLiquidityOverridesAsync(ct);

        IEnumerable<(Guid SecurityId, string DisplayName, string Currency, string? FundFamily,
                     bool IsSweepEligible, int? WeightedAverageMaturityDays, bool HasLiquidityFee,
                     bool IsActive, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, long Version)> results = allFunds;

        if (query.ActiveOnly)
            results = results.Where(static f => f.IsActive);

        if (query.FundFamily is not null)
        {
            var normalized = NormalizeFamily(query.FundFamily);
            results = results.Where(f =>
                string.Equals(f.FundFamily, normalized, StringComparison.Ordinal));
        }

        if (query.IsSweepEligible.HasValue)
            results = results.Where(f => f.IsSweepEligible == query.IsSweepEligible.Value);

        if (query.HasLiquidityFee.HasValue)
            results = results.Where(f => f.HasLiquidityFee == query.HasLiquidityFee.Value);

        if (query.MaxWamDays.HasValue)
            results = results.Where(f =>
                f.WeightedAverageMaturityDays.HasValue &&
                f.WeightedAverageMaturityDays.Value <= query.MaxWamDays.Value);

        if (query.LiquidityState.HasValue)
            results = results.Where(f =>
                ComputeLiquidityState(f, overrides) == query.LiquidityState.Value);

        return results
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(ToDto)
            .ToArray();
    }

    public async Task<MmfLiquidityDto?> GetLiquidityAsync(Guid securityId, CancellationToken ct = default)
    {
        var row = await _store.GetFundAsync(securityId, ct);
        if (row is null) return null;

        var overrides = await _store.GetAllLiquidityOverridesAsync(ct);
        return new MmfLiquidityDto(
            row.Value.SecurityId,
            ComputeLiquidityState(row.Value, overrides),
            row.Value.WeightedAverageMaturityDays,
            DateTimeOffset.UtcNow);
    }

    public async Task<MmfSweepProfileDto?> GetSweepProfileAsync(Guid securityId, CancellationToken ct = default)
    {
        var row = await _store.GetFundAsync(securityId, ct);
        if (row is null) return null;

        return new MmfSweepProfileDto(
            row.Value.SecurityId,
            row.Value.IsSweepEligible,
            row.Value.HasLiquidityFee,
            row.Value.FundFamily,
            DateTimeOffset.UtcNow);
    }

    public async Task<MmfFundFamilyDto?> GetFundFamilyAsync(string normalizedFamilyName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedFamilyName);

        var family = NormalizeFamily(normalizedFamilyName)!;
        var allFunds = await _store.GetAllFundsAsync(ct);
        var members = allFunds
            .Where(f => string.Equals(f.FundFamily, family, StringComparison.Ordinal))
            .Select(static f => f.SecurityId)
            .ToList();

        if (members.Count == 0) return null;

        return new MmfFundFamilyDto(family, members, members.Count, DateTimeOffset.UtcNow);
    }

    public Task<IReadOnlyList<MmfRebuildCheckpointDto>> GetRebuildCheckpointsAsync(CancellationToken ct = default)
        => _store.GetRebuildCheckpointsAsync(ct);

    public async Task RebuildProjectionsAsync(Guid securityId, CancellationToken ct = default)
    {
        var row = await _store.GetFundAsync(securityId, ct);
        if (row is null) return;

        await _store.UpsertRebuildCheckpointAsync(new MmfRebuildCheckpointDto(
            securityId,
            row.Value.Version,
            DateTimeOffset.UtcNow,
            "postgres"), ct);
    }

    // ── IMmfLiquidityService ─────────────────────────────────────────────────

    public Task<MmfLiquidityDto?> GetLiquidityStateAsync(Guid securityId, CancellationToken ct = default)
        => GetLiquidityAsync(securityId, ct);

    public Task<MmfFundFamilyDto?> GetFamilyProjectionAsync(string normalizedFamilyName, CancellationToken ct = default)
        => GetFundFamilyAsync(normalizedFamilyName, ct);

    public async Task<IReadOnlyList<MmfDetailDto>> GetByFamilyAsync(string normalizedFamilyName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedFamilyName);

        var family = NormalizeFamily(normalizedFamilyName)!;
        var allFunds = await _store.GetAllFundsAsync(ct);
        return allFunds
            .Where(f => string.Equals(f.FundFamily, family, StringComparison.Ordinal))
            .Select(ToDto)
            .ToArray();
    }

    public async Task<IReadOnlyList<MmfLiquidityDto>> GetAllLiquidFundsAsync(CancellationToken ct = default)
    {
        var allFunds = await _store.GetAllFundsAsync(ct);
        var overrides = await _store.GetAllLiquidityOverridesAsync(ct);

        return allFunds
            .Where(f => f.IsActive && ComputeLiquidityState(f, overrides) == MmfLiquidityState.Liquid)
            .Select(f => new MmfLiquidityDto(
                f.SecurityId,
                MmfLiquidityState.Liquid,
                f.WeightedAverageMaturityDays,
                DateTimeOffset.UtcNow))
            .ToArray();
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static MmfLiquidityState ComputeLiquidityState(
        (Guid SecurityId, string DisplayName, string Currency, string? FundFamily,
         bool IsSweepEligible, int? WeightedAverageMaturityDays, bool HasLiquidityFee,
         bool IsActive, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, long Version) f,
        IReadOnlyDictionary<Guid, MmfLiquidityState> overrides)
    {
        if (!f.IsActive) return MmfLiquidityState.Inactive;

        if (overrides.TryGetValue(f.SecurityId, out var overrideState))
            return overrideState;

        if (f.WeightedAverageMaturityDays.HasValue)
            return f.WeightedAverageMaturityDays.Value <= LiquidWamThresholdDays
                ? MmfLiquidityState.Liquid
                : MmfLiquidityState.Restricted;

        return MmfLiquidityState.Liquid;
    }

    private static MmfDetailDto ToDto(
        (Guid SecurityId, string DisplayName, string Currency, string? FundFamily,
         bool IsSweepEligible, int? WeightedAverageMaturityDays, bool HasLiquidityFee,
         bool IsActive, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, long Version) f)
        => new(f.SecurityId, f.DisplayName, f.Currency, f.FundFamily,
               f.IsSweepEligible, f.WeightedAverageMaturityDays, f.HasLiquidityFee,
               f.IsActive ? "Active" : "Inactive",
               f.Version, f.EffectiveFrom, f.EffectiveTo);

    private static string? NormalizeFamily(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : name.Trim().ToUpperInvariant();
}
