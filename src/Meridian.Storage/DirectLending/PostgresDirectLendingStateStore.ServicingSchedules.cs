using Meridian.Contracts.DirectLending;
using Npgsql;

namespace Meridian.Storage.DirectLending;

/// <summary>
/// Normalized projections for the opt-in servicing schedules (migration 011): applied rate resets
/// and PIK capitalizations. Both are replaced from the servicing state on every save, matching the
/// accrual-entry projection, so they never drift from <c>loan_state.servicing_json</c>.
/// </summary>
public sealed partial class PostgresDirectLendingStateStore
{
    private async Task ReplaceRateResetHistoryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid loanId,
        IReadOnlyList<RateResetDto>? history,
        CancellationToken ct)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = $"delete from {Qualified("loan_rate_reset_projection")} where loan_id = @loan_id;";
            delete.Parameters.AddWithValue("loan_id", loanId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        if (history is null)
        {
            return;
        }

        foreach (var reset in history)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified("loan_rate_reset_projection")} (
                    loan_id,
                    effective_date,
                    reset_kind,
                    index_name,
                    fixing_observation_date,
                    observed_rate,
                    spread_bps,
                    all_in_rate,
                    source_ref,
                    fixing_id,
                    fixing_revision)
                values (
                    @loan_id,
                    @effective_date,
                    @reset_kind,
                    @index_name,
                    @fixing_observation_date,
                    @observed_rate,
                    @spread_bps,
                    @all_in_rate,
                    @source_ref,
                    @fixing_id,
                    @fixing_revision);
                """;
            insert.Parameters.AddWithValue("loan_id", loanId);
            insert.Parameters.AddWithValue("effective_date", reset.EffectiveDate);
            insert.Parameters.AddWithValue("reset_kind", (reset.ResetKind ?? RateResetKind.Manual).ToString());
            insert.Parameters.AddWithValue("index_name", reset.IndexName);
            insert.Parameters.Add(new NpgsqlParameter("fixing_observation_date", NpgsqlTypes.NpgsqlDbType.Date)
            {
                Value = (object?)reset.FixingObservationDate ?? DBNull.Value
            });
            insert.Parameters.AddWithValue("observed_rate", reset.ObservedRate);
            insert.Parameters.AddWithValue("spread_bps", reset.SpreadBps);
            insert.Parameters.AddWithValue("all_in_rate", reset.AllInRate);
            insert.Parameters.AddWithValue("source_ref", (object?)reset.SourceRef ?? DBNull.Value);
            insert.Parameters.Add(new NpgsqlParameter("fixing_id", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)reset.FixingId ?? DBNull.Value });
            insert.Parameters.Add(new NpgsqlParameter("fixing_revision", NpgsqlTypes.NpgsqlDbType.Integer) { Value = (object?)reset.FixingRevision ?? DBNull.Value });
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task ReplacePikCapitalizationsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid loanId,
        IReadOnlyList<PikCapitalizationDto>? capitalizations,
        CancellationToken ct)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = $"delete from {Qualified("loan_pik_capitalization_projection")} where loan_id = @loan_id;";
            delete.Parameters.AddWithValue("loan_id", loanId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        if (capitalizations is null)
        {
            return;
        }

        foreach (var capitalization in capitalizations)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified("loan_pik_capitalization_projection")} (
                    loan_id,
                    capitalization_date,
                    period_start,
                    period_end,
                    amount,
                    recorded_at)
                values (
                    @loan_id,
                    @capitalization_date,
                    @period_start,
                    @period_end,
                    @amount,
                    @recorded_at);
                """;
            insert.Parameters.AddWithValue("loan_id", loanId);
            insert.Parameters.AddWithValue("capitalization_date", capitalization.CapitalizationDate);
            insert.Parameters.AddWithValue("period_start", capitalization.PeriodStart);
            insert.Parameters.AddWithValue("period_end", capitalization.PeriodEnd);
            insert.Parameters.AddWithValue("amount", capitalization.Amount);
            insert.Parameters.AddWithValue("recorded_at", capitalization.RecordedAt.UtcDateTime);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<RateResetDto>> LoadRateResetHistoryCoreAsync(
        NpgsqlConnection connection,
        Guid loanId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select effective_date,
                   reset_kind,
                   index_name,
                   fixing_observation_date,
                   observed_rate,
                   spread_bps,
                   all_in_rate,
                   source_ref,
                   fixing_id,
                   fixing_revision
            from {Qualified("loan_rate_reset_projection")}
            where loan_id = @loan_id
            order by effective_date;
            """;
        command.Parameters.AddWithValue("loan_id", loanId);

        var results = new List<RateResetDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new RateResetDto(
                DateOnly.FromDateTime(reader.GetDateTime(0)),
                reader.GetString(2),
                reader.GetDecimal(4),
                reader.GetDecimal(5),
                reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetString(7))
            {
                ResetKind = ParseEnum<RateResetKind>(reader.GetString(1)),
                FixingObservationDate = reader.IsDBNull(3) ? null : DateOnly.FromDateTime(reader.GetDateTime(3)),
                FixingId = reader.IsDBNull(8) ? null : reader.GetGuid(8),
                FixingRevision = reader.IsDBNull(9) ? null : reader.GetInt32(9)
            });
        }

        return results;
    }

    private async Task<IReadOnlyList<PikCapitalizationDto>> LoadPikCapitalizationsCoreAsync(
        NpgsqlConnection connection,
        Guid loanId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select capitalization_date,
                   period_start,
                   period_end,
                   amount,
                   recorded_at
            from {Qualified("loan_pik_capitalization_projection")}
            where loan_id = @loan_id
            order by capitalization_date;
            """;
        command.Parameters.AddWithValue("loan_id", loanId);

        var results = new List<PikCapitalizationDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new PikCapitalizationDto(
                DateOnly.FromDateTime(reader.GetDateTime(0)),
                DateOnly.FromDateTime(reader.GetDateTime(1)),
                DateOnly.FromDateTime(reader.GetDateTime(2)),
                reader.GetDecimal(3),
                new DateTimeOffset(reader.GetDateTime(4), TimeSpan.Zero)));
        }

        return results;
    }
}
