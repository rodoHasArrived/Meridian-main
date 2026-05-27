using Meridian.Contracts.Treasury;
using Npgsql;

namespace Meridian.Storage.MoneyMarket;

/// <summary>
/// PostgreSQL implementation of <see cref="IMoneyMarketFundAuxStore"/> using raw Npgsql.
/// </summary>
public sealed class PostgresMoneyMarketFundStore : IMoneyMarketFundAuxStore
{
    private readonly MoneyMarketStoreOptions _options;

    public PostgresMoneyMarketFundStore(MoneyMarketStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    // ── Fund records ─────────────────────────────────────────────────────────

    public async Task UpsertFundAsync(
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
        CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_options.Schema}.mmf_funds
                (security_id, display_name, currency, fund_family, is_sweep_eligible,
                 weighted_average_maturity, has_liquidity_fee, is_active,
                 effective_from, effective_to, version)
            VALUES
                (@id, @name, @currency, @family, @sweep, @wam, @fee, @active, @eff_from, @eff_to, @version)
            ON CONFLICT (security_id) DO UPDATE SET
                display_name                = EXCLUDED.display_name,
                currency                    = EXCLUDED.currency,
                fund_family                 = EXCLUDED.fund_family,
                is_sweep_eligible           = EXCLUDED.is_sweep_eligible,
                weighted_average_maturity   = EXCLUDED.weighted_average_maturity,
                has_liquidity_fee           = EXCLUDED.has_liquidity_fee,
                is_active                   = EXCLUDED.is_active,
                effective_from              = EXCLUDED.effective_from,
                effective_to                = EXCLUDED.effective_to,
                version                     = EXCLUDED.version;
            """;

        cmd.Parameters.AddWithValue("id",       securityId);
        cmd.Parameters.AddWithValue("name",     displayName);
        cmd.Parameters.AddWithValue("currency", currency);
        cmd.Parameters.AddWithValue("family",   (object?)fundFamily ?? DBNull.Value);
        cmd.Parameters.AddWithValue("sweep",    isSweepEligible);
        cmd.Parameters.AddWithValue("wam",      (object?)weightedAverageMaturityDays ?? DBNull.Value);
        cmd.Parameters.AddWithValue("fee",      hasLiquidityFee);
        cmd.Parameters.AddWithValue("active",   isActive);
        cmd.Parameters.AddWithValue("eff_from", effectiveFrom);
        cmd.Parameters.AddWithValue("eff_to",   (object?)effectiveTo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("version",  version);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<(Guid SecurityId, string DisplayName, string Currency, string? FundFamily,
          bool IsSweepEligible, int? WeightedAverageMaturityDays, bool HasLiquidityFee,
          bool IsActive, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, long Version)?> GetFundAsync(
        Guid securityId, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT security_id, display_name, currency, fund_family, is_sweep_eligible,
                   weighted_average_maturity, has_liquidity_fee, is_active,
                   effective_from, effective_to, version
            FROM {_options.Schema}.mmf_funds
            WHERE security_id = @id;
            """;
        cmd.Parameters.AddWithValue("id", securityId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadFundRow(reader);
    }

    public async Task<IReadOnlyList<(Guid SecurityId, string DisplayName, string Currency, string? FundFamily,
                        bool IsSweepEligible, int? WeightedAverageMaturityDays, bool HasLiquidityFee,
                        bool IsActive, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, long Version)>> GetAllFundsAsync(
        CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT security_id, display_name, currency, fund_family, is_sweep_eligible,
                   weighted_average_maturity, has_liquidity_fee, is_active,
                   effective_from, effective_to, version
            FROM {_options.Schema}.mmf_funds
            ORDER BY display_name;
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<(Guid, string, string, string?, bool, int?, bool, bool, DateTimeOffset, DateTimeOffset?, long)>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadFundRow(reader));
        return results;
    }

    // ── Liquidity overrides ──────────────────────────────────────────────────

    public async Task UpsertLiquidityOverrideAsync(Guid securityId, MmfLiquidityState state, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_options.Schema}.mmf_liquidity_overrides (security_id, liquidity_state)
            VALUES (@id, @state)
            ON CONFLICT (security_id) DO UPDATE SET
                liquidity_state = EXCLUDED.liquidity_state,
                set_at = now();
            """;
        cmd.Parameters.AddWithValue("id",    securityId);
        cmd.Parameters.AddWithValue("state", (short)state);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, MmfLiquidityState>> GetAllLiquidityOverridesAsync(CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT security_id, liquidity_state FROM {_options.Schema}.mmf_liquidity_overrides;";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new Dictionary<Guid, MmfLiquidityState>();
        while (await reader.ReadAsync(ct))
            result[reader.GetGuid(0)] = (MmfLiquidityState)reader.GetInt16(1);
        return result;
    }

    // ── Rebuild checkpoints ──────────────────────────────────────────────────

    public async Task UpsertRebuildCheckpointAsync(MmfRebuildCheckpointDto checkpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_options.Schema}.mmf_rebuild_checkpoints
                (security_id, aggregate_version, checkpointed_at, rebuild_source)
            VALUES (@id, @ver, @at, @src)
            ON CONFLICT (security_id) DO UPDATE SET
                aggregate_version   = EXCLUDED.aggregate_version,
                checkpointed_at     = EXCLUDED.checkpointed_at,
                rebuild_source      = EXCLUDED.rebuild_source;
            """;
        cmd.Parameters.AddWithValue("id",  checkpoint.SecurityId);
        cmd.Parameters.AddWithValue("ver", checkpoint.AggregateVersion);
        cmd.Parameters.AddWithValue("at",  checkpoint.CheckpointedAt);
        cmd.Parameters.AddWithValue("src", checkpoint.RebuildSource);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<MmfRebuildCheckpointDto>> GetRebuildCheckpointsAsync(CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT security_id, aggregate_version, checkpointed_at, rebuild_source
            FROM {_options.Schema}.mmf_rebuild_checkpoints
            ORDER BY checkpointed_at DESC;
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<MmfRebuildCheckpointDto>();
        while (await reader.ReadAsync(ct))
            results.Add(new MmfRebuildCheckpointDto(
                SecurityId:       reader.GetGuid(0),
                AggregateVersion: reader.GetInt64(1),
                CheckpointedAt:   reader.GetFieldValue<DateTimeOffset>(2),
                RebuildSource:    reader.GetString(3)));
        return results;
    }

    // ── Utility ──────────────────────────────────────────────────────────────

    public async Task<bool> IsEmptyAsync(CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT NOT EXISTS (SELECT 1 FROM {_options.Schema}.mmf_funds LIMIT 1);";
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is true;
    }

    // ── Mapping helpers ──────────────────────────────────────────────────────

    private static (Guid, string, string, string?, bool, int?, bool, bool, DateTimeOffset, DateTimeOffset?, long) ReadFundRow(NpgsqlDataReader r)
        => (r.GetGuid(0),
            r.GetString(1),
            r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3),
            r.GetBoolean(4),
            r.IsDBNull(5) ? (int?)null : r.GetInt32(5),
            r.GetBoolean(6),
            r.GetBoolean(7),
            r.GetFieldValue<DateTimeOffset>(8),
            r.IsDBNull(9) ? (DateTimeOffset?)null : r.GetFieldValue<DateTimeOffset>(9),
            r.GetInt64(10));
}
