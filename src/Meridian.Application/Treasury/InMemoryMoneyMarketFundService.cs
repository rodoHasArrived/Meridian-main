using Meridian.Contracts.Treasury;

namespace Meridian.Application.Treasury;

/// <summary>
/// In-memory implementation of <see cref="IMoneyMarketFundService"/> and
/// <see cref="IMmfLiquidityService"/> for unit testing and development scenarios.
/// Fund records are registered directly via <see cref="RegisterAsync"/>.
/// Liquidity state is computed from WAM bands unless overridden per-fund.
/// </summary>
public sealed class InMemoryMoneyMarketFundService : IMoneyMarketFundService, IMmfLiquidityService
{
    // WAM threshold (days) below which a fund is considered Liquid (standard MMF limit).
    private const int LiquidWamThresholdDays = 60;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, StoredMmf> _funds = new();
    private readonly Dictionary<Guid, MmfLiquidityState> _liquidityOverrides = new();
    private readonly List<MmfRebuildCheckpointDto> _checkpoints = new();

    /// <summary>
    /// Seeds the in-memory store with an MMF record.
    /// Calling this with the same <paramref name="securityId"/> twice overwrites the earlier entry.
    /// </summary>
    public Task RegisterAsync(
        Guid securityId,
        string displayName,
        string currency,
        string? fundFamily,
        bool isSweepEligible,
        int? weightedAverageMaturityDays,
        bool hasLiquidityFee,
        bool isActive = true,
        MmfLiquidityState? liquidityStateOverride = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        var record = new StoredMmf(
            securityId,
            displayName.Trim(),
            currency.Trim().ToUpperInvariant(),
            NormalizeFamily(fundFamily),
            isSweepEligible,
            weightedAverageMaturityDays,
            hasLiquidityFee,
            isActive,
            EffectiveFrom: DateTimeOffset.UtcNow,
            EffectiveTo: null,
            Version: 1L);

        lock (_gate)
        {
            _funds[securityId] = record;

            if (liquidityStateOverride.HasValue)
                _liquidityOverrides[securityId] = liquidityStateOverride.Value;
        }

        return Task.CompletedTask;
    }

    // ── IMoneyMarketFundService ──────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<MmfDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(
                _funds.TryGetValue(securityId, out var f) ? ToDto(f) : (MmfDetailDto?)null);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<MmfDetailDto>> SearchAsync(MmfSearchQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        lock (_gate)
        {
            IEnumerable<StoredMmf> results = _funds.Values;

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
                results = results.Where(f => ComputeLiquidityState(f) == query.LiquidityState.Value);

            var list = results
                .Skip(query.Skip)
                .Take(query.Take)
                .Select(ToDto)
                .ToList();

            return Task.FromResult<IReadOnlyList<MmfDetailDto>>(list);
        }
    }

    /// <inheritdoc/>
    public Task<MmfLiquidityDto?> GetLiquidityAsync(Guid securityId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_funds.TryGetValue(securityId, out var f))
                return Task.FromResult<MmfLiquidityDto?>(null);

            return Task.FromResult<MmfLiquidityDto?>(new MmfLiquidityDto(
                f.SecurityId,
                ComputeLiquidityState(f),
                f.WeightedAverageMaturityDays,
                DateTimeOffset.UtcNow));
        }
    }

    /// <inheritdoc/>
    public Task<MmfSweepProfileDto?> GetSweepProfileAsync(Guid securityId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_funds.TryGetValue(securityId, out var f))
                return Task.FromResult<MmfSweepProfileDto?>(null);

            return Task.FromResult<MmfSweepProfileDto?>(new MmfSweepProfileDto(
                f.SecurityId,
                f.IsSweepEligible,
                f.HasLiquidityFee,
                f.FundFamily,
                DateTimeOffset.UtcNow));
        }
    }

    /// <inheritdoc/>
    public Task<MmfFundFamilyDto?> GetFundFamilyAsync(string normalizedFamilyName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedFamilyName);
        return Task.FromResult(BuildFamilyDto(NormalizeFamily(normalizedFamilyName)!));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<MmfRebuildCheckpointDto>> GetRebuildCheckpointsAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<MmfRebuildCheckpointDto>>(_checkpoints.ToList());
        }
    }

    /// <inheritdoc/>
    public Task RebuildProjectionsAsync(Guid securityId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_funds.TryGetValue(securityId, out var f))
                return Task.CompletedTask;

            _checkpoints.RemoveAll(c => c.SecurityId == securityId);
            _checkpoints.Add(new MmfRebuildCheckpointDto(
                securityId,
                f.Version,
                DateTimeOffset.UtcNow,
                "in-memory"));
        }

        return Task.CompletedTask;
    }

    // ── IMmfLiquidityService ─────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<MmfLiquidityDto?> GetLiquidityStateAsync(Guid securityId, CancellationToken ct = default)
        => GetLiquidityAsync(securityId, ct);

    /// <inheritdoc/>
    public Task<MmfFundFamilyDto?> GetFamilyProjectionAsync(string normalizedFamilyName, CancellationToken ct = default)
        => GetFundFamilyAsync(normalizedFamilyName, ct);

    /// <inheritdoc/>
    public Task<IReadOnlyList<MmfDetailDto>> GetByFamilyAsync(string normalizedFamilyName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedFamilyName);

        var family = NormalizeFamily(normalizedFamilyName)!;
        lock (_gate)
        {
            var members = _funds.Values
                .Where(f => string.Equals(f.FundFamily, family, StringComparison.Ordinal))
                .Select(ToDto)
                .ToList();

            return Task.FromResult<IReadOnlyList<MmfDetailDto>>(members);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<MmfLiquidityDto>> GetAllLiquidFundsAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            var liquid = _funds.Values
                .Where(f => f.IsActive && ComputeLiquidityState(f) == MmfLiquidityState.Liquid)
                .Select(f => new MmfLiquidityDto(
                    f.SecurityId,
                    MmfLiquidityState.Liquid,
                    f.WeightedAverageMaturityDays,
                    DateTimeOffset.UtcNow))
                .ToList();

            return Task.FromResult<IReadOnlyList<MmfLiquidityDto>>(liquid);
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private MmfLiquidityState ComputeLiquidityState(StoredMmf f)
    {
        if (!f.IsActive)
            return MmfLiquidityState.Inactive;

        if (_liquidityOverrides.TryGetValue(f.SecurityId, out var overrideState))
            return overrideState;

        // WAM-based classification: WAM ≤ 60 days → Liquid; > 60 days → Restricted.
        // Funds with no WAM data are treated as Liquid (conservative operational default).
        if (f.WeightedAverageMaturityDays.HasValue)
            return f.WeightedAverageMaturityDays.Value <= LiquidWamThresholdDays
                ? MmfLiquidityState.Liquid
                : MmfLiquidityState.Restricted;

        return MmfLiquidityState.Liquid;
    }

    private MmfFundFamilyDto? BuildFamilyDto(string normalizedFamily)
    {
        lock (_gate)
        {
            var members = _funds.Values
                .Where(f => string.Equals(f.FundFamily, normalizedFamily, StringComparison.Ordinal))
                .Select(static f => f.SecurityId)
                .ToList();

            if (members.Count == 0)
                return null;

            return new MmfFundFamilyDto(
                normalizedFamily,
                members,
                members.Count,
                DateTimeOffset.UtcNow);
        }
    }

    private static MmfDetailDto ToDto(StoredMmf f) => new(
        f.SecurityId,
        f.DisplayName,
        f.Currency,
        f.FundFamily,
        f.IsSweepEligible,
        f.WeightedAverageMaturityDays,
        f.HasLiquidityFee,
        f.IsActive ? "Active" : "Inactive",
        f.Version,
        f.EffectiveFrom,
        f.EffectiveTo);

    /// <summary>
    /// Normalises a fund-family name to upper-case trimmed form for consistent grouping.
    /// Returns <c>null</c> for blank or whitespace-only input.
    /// </summary>
    private static string? NormalizeFamily(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : name.Trim().ToUpperInvariant();

    // ── Internal record ──────────────────────────────────────────────────────

    private sealed record StoredMmf(
        Guid SecurityId,
        string DisplayName,
        string Currency,
        string? FundFamily,
        bool IsSweepEligible,
        int? WeightedAverageMaturityDays,
        bool HasLiquidityFee,
        bool IsActive,
        DateTimeOffset EffectiveFrom,
        DateTimeOffset? EffectiveTo,
        long Version);
}
