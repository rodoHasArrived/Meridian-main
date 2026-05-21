using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresOptionReferenceProjectionStore : IOptionReferenceProjectionStore
{
    private readonly SecurityMasterOptions _options;

    public PostgresOptionReferenceProjectionStore(SecurityMasterOptions options)
    {
        _options = options;
    }

    public async Task<OptionContractProjectionRow?> GetContractAsync(string contractSymbol, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select contract_symbol,
                   security_id,
                   option_chain_id,
                   underlying_symbol,
                   underlying_security_id,
                   put_call,
                   strike,
                   expiry_date,
                   multiplier,
                   is_adjusted,
                   last_trading_dt,
                   lifecycle_stat,
                   updated_at,
                   version
            from {Qualified("option_contract_projection")}
            where contract_symbol = @contract_symbol;
            """;
        command.Parameters.AddWithValue("contract_symbol", contractSymbol);
        return await ReadSingleAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OptionContractProjectionRow>> GetSeriesContractsAsync(string optionChainId, DateOnly expiryDate, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select contract_symbol,
                   security_id,
                   option_chain_id,
                   underlying_symbol,
                   underlying_security_id,
                   put_call,
                   strike,
                   expiry_date,
                   multiplier,
                   is_adjusted,
                   last_trading_dt,
                   lifecycle_stat,
                   updated_at,
                   version
            from {Qualified("option_contract_projection")}
            where option_chain_id = @option_chain_id
              and expiry_date = @expiry_date
            order by strike, put_call;
            """;
        command.Parameters.AddWithValue("option_chain_id", optionChainId);
        command.Parameters.AddWithValue("expiry_date", expiryDate.ToDateTime(TimeOnly.MinValue));
        return await ReadManyAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OptionContractProjectionRow>> GetUnderlyingContractsAsync(string underlyingSymbol, DateOnly expiryDate, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select contract_symbol,
                   security_id,
                   option_chain_id,
                   underlying_symbol,
                   underlying_security_id,
                   put_call,
                   strike,
                   expiry_date,
                   multiplier,
                   is_adjusted,
                   last_trading_dt,
                   lifecycle_stat,
                   updated_at,
                   version
            from {Qualified("option_contract_projection")}
            where underlying_symbol = @underlying_symbol
              and expiry_date = @expiry_date
            order by option_chain_id, strike, put_call;
            """;
        command.Parameters.AddWithValue("underlying_symbol", underlyingSymbol.ToUpperInvariant());
        command.Parameters.AddWithValue("expiry_date", expiryDate.ToDateTime(TimeOnly.MinValue));
        return await ReadManyAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DateOnly>> GetExpiryLadderAsync(string underlyingSymbol, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select distinct expiry_date
            from {Qualified("option_contract_projection")}
            where underlying_symbol = @underlying_symbol
            order by expiry_date;
            """;
        command.Parameters.AddWithValue("underlying_symbol", underlyingSymbol.ToUpperInvariant());

        var results = new List<DateOnly>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(DateOnly.FromDateTime(reader.GetDateTime(0)));
        }

        return results;
    }

    public async Task UpsertChainSnapshotAsync(OptionChainSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var optionChainId = BuildOptionChainId(snapshot.UnderlyingSymbol, snapshot.Expiration);
        var lifecycleStat = snapshot.Expiration < DateOnly.FromDateTime(DateTime.UtcNow) ? "Expired" : "Active";

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        foreach (var quote in snapshot.Calls)
        {
            await UpsertQuoteAsync(connection, transaction, optionChainId, snapshot, quote, OptionRight.Call, lifecycleStat, ct).ConfigureAwait(false);
        }

        foreach (var quote in snapshot.Puts)
        {
            await UpsertQuoteAsync(connection, transaction, optionChainId, snapshot, quote, OptionRight.Put, lifecycleStat, ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private async Task UpsertQuoteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string optionChainId,
        OptionChainSnapshot snapshot,
        OptionQuote quote,
        OptionRight right,
        string lifecycleStat,
        CancellationToken ct)
    {
        var contractSymbol = NormalizeContractSymbol(quote.Symbol, quote.Contract, right);

        await using (var contractCommand = connection.CreateCommand())
        {
            contractCommand.Transaction = transaction;
            contractCommand.CommandText =
                $"""
                insert into {Qualified("option_contract_projection")} (
                    contract_symbol, security_id, option_chain_id, underlying_symbol, underlying_security_id,
                    put_call, strike, expiry_date, multiplier, is_adjusted, last_trading_dt, lifecycle_stat, updated_at, version)
                values (
                    @contract_symbol, null, @option_chain_id, @underlying_symbol, null,
                    @put_call, @strike, @expiry_date, @multiplier, false, @last_trading_dt, @lifecycle_stat, @updated_at, 1)
                on conflict (contract_symbol) do update set
                    option_chain_id = excluded.option_chain_id,
                    underlying_symbol = excluded.underlying_symbol,
                    put_call = excluded.put_call,
                    strike = excluded.strike,
                    expiry_date = excluded.expiry_date,
                    multiplier = excluded.multiplier,
                    last_trading_dt = excluded.last_trading_dt,
                    lifecycle_stat = excluded.lifecycle_stat,
                    updated_at = excluded.updated_at,
                    version = {Qualified("option_contract_projection")}.version + 1;
                """;
            contractCommand.Parameters.AddWithValue("contract_symbol", contractSymbol);
            contractCommand.Parameters.AddWithValue("option_chain_id", optionChainId);
            contractCommand.Parameters.AddWithValue("underlying_symbol", snapshot.UnderlyingSymbol.Trim().ToUpperInvariant());
            contractCommand.Parameters.AddWithValue("put_call", right.ToString());
            contractCommand.Parameters.AddWithValue("strike", quote.Contract.Strike);
            contractCommand.Parameters.AddWithValue("expiry_date", quote.Contract.Expiration.ToDateTime(TimeOnly.MinValue));
            contractCommand.Parameters.AddWithValue("multiplier", (decimal)quote.Contract.Multiplier);
            contractCommand.Parameters.AddWithValue("last_trading_dt", snapshot.Expiration.ToDateTime(TimeOnly.MinValue));
            contractCommand.Parameters.AddWithValue("lifecycle_stat", lifecycleStat);
            contractCommand.Parameters.AddWithValue("updated_at", snapshot.Timestamp.UtcDateTime);
            await contractCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var seriesCommand = connection.CreateCommand())
        {
            seriesCommand.Transaction = transaction;
            seriesCommand.CommandText =
                $"""
                insert into {Qualified("option_series_projection")} (
                    option_chain_id, contract_symbol, underlying_symbol, expiry_date, strike, put_call)
                values (
                    @option_chain_id, @contract_symbol, @underlying_symbol, @expiry_date, @strike, @put_call)
                on conflict (option_chain_id, contract_symbol) do update set
                    underlying_symbol = excluded.underlying_symbol,
                    expiry_date = excluded.expiry_date,
                    strike = excluded.strike,
                    put_call = excluded.put_call;
                """;
            seriesCommand.Parameters.AddWithValue("option_chain_id", optionChainId);
            seriesCommand.Parameters.AddWithValue("contract_symbol", contractSymbol);
            seriesCommand.Parameters.AddWithValue("underlying_symbol", snapshot.UnderlyingSymbol.Trim().ToUpperInvariant());
            seriesCommand.Parameters.AddWithValue("expiry_date", quote.Contract.Expiration.ToDateTime(TimeOnly.MinValue));
            seriesCommand.Parameters.AddWithValue("strike", quote.Contract.Strike);
            seriesCommand.Parameters.AddWithValue("put_call", right.ToString());
            await seriesCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var lifecycleCommand = connection.CreateCommand())
        {
            lifecycleCommand.Transaction = transaction;
            lifecycleCommand.CommandText =
                $"""
                insert into {Qualified("option_lifecycle_projection")} (
                    contract_symbol, lifecycle_stat, listed_at, last_trading_dt, expiry_date, version)
                values (
                    @contract_symbol, @lifecycle_stat, @listed_at, @last_trading_dt, @expiry_date, 1)
                on conflict (contract_symbol) do update set
                    lifecycle_stat = excluded.lifecycle_stat,
                    last_trading_dt = excluded.last_trading_dt,
                    expiry_date = excluded.expiry_date,
                    version = {Qualified("option_lifecycle_projection")}.version + 1;
                """;
            lifecycleCommand.Parameters.AddWithValue("contract_symbol", contractSymbol);
            lifecycleCommand.Parameters.AddWithValue("lifecycle_stat", lifecycleStat);
            lifecycleCommand.Parameters.AddWithValue("listed_at", snapshot.Timestamp.UtcDateTime);
            lifecycleCommand.Parameters.AddWithValue("last_trading_dt", snapshot.Expiration.ToDateTime(TimeOnly.MinValue));
            lifecycleCommand.Parameters.AddWithValue("expiry_date", quote.Contract.Expiration.ToDateTime(TimeOnly.MinValue));
            await lifecycleCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using var aliasCommand = connection.CreateCommand();
        aliasCommand.Transaction = transaction;
        aliasCommand.CommandText =
            $"""
            insert into {Qualified("option_alias_projection")} (
                contract_symbol, alias_kind, alias_value, provider, normalized_alias)
            values (
                @contract_symbol, 'ProviderSymbol', @alias_value, '', @normalized_alias)
            on conflict (contract_symbol, alias_kind, alias_value, provider) do nothing;
            """;
        aliasCommand.Parameters.AddWithValue("contract_symbol", contractSymbol);
        aliasCommand.Parameters.AddWithValue("alias_value", quote.Symbol);
        aliasCommand.Parameters.AddWithValue("normalized_alias", quote.Symbol.Trim().ToUpperInvariant());
        await aliasCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<OptionContractProjectionRow?> ReadSingleAsync(NpgsqlCommand command, CancellationToken ct)
    {
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return MapRow(reader);
    }

    private async Task<IReadOnlyList<OptionContractProjectionRow>> ReadManyAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var results = new List<OptionContractProjectionRow>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(MapRow(reader));
        }

        return results;
    }

    private static OptionContractProjectionRow MapRow(NpgsqlDataReader reader)
        => new(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.GetString(5),
            reader.GetDecimal(6),
            DateOnly.FromDateTime(reader.GetDateTime(7)),
            reader.GetDecimal(8),
            reader.GetBoolean(9),
            reader.IsDBNull(10) ? null : DateOnly.FromDateTime(reader.GetDateTime(10)),
            reader.GetString(11),
            new DateTimeOffset(reader.GetDateTime(12), TimeSpan.Zero),
            reader.GetInt64(13));

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("SecurityMasterOptions.ConnectionString is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private string Qualified(string table) => $"{_options.Schema}.{table}";

    private static string BuildOptionChainId(string underlyingSymbol, DateOnly expiryDate)
        => $"{underlyingSymbol.Trim().ToUpperInvariant()}-{expiryDate:yyyyMMdd}";

    private static string NormalizeContractSymbol(string symbol, OptionContractSpec spec, OptionRight right)
    {
        if (!string.IsNullOrWhiteSpace(spec.OccSymbol))
        {
            return spec.OccSymbol.Trim().ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(symbol))
        {
            return symbol.Trim().ToUpperInvariant();
        }

        var rightLabel = right == OptionRight.Call ? "C" : "P";
        return $"{spec.UnderlyingSymbol.Trim().ToUpperInvariant()}-{spec.Expiration:yyyyMMdd}-{rightLabel}-{spec.Strike:0.####}";
    }
}
