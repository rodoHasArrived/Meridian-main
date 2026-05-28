using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresCertificateOfDepositReferenceProjectionStore : ICertificateOfDepositReferenceProjectionStore
{
    private readonly SecurityMasterOptions _options;

    public PostgresCertificateOfDepositReferenceProjectionStore(SecurityMasterOptions options)
    {
        _options = options;
    }

    public async Task<CertificateOfDepositProjectionRow?> GetCertificateOfDepositAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, display_name, currency, issuer_name, maturity_date,
                   coupon_rate, callable_date, day_count, primary_identifier_value, version
            from {Qualified("certificate_of_deposit_projection")}
            where security_id = @security_id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        return await ReadSingleAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CertificateOfDepositProjectionRow>> GetByIssuerAsync(string issuerName, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, display_name, currency, issuer_name, maturity_date,
                   coupon_rate, callable_date, day_count, primary_identifier_value, version
            from {Qualified("certificate_of_deposit_projection")}
            where lower(issuer_name) = lower(@issuer_name)
            order by maturity_date;
            """;
        command.Parameters.AddWithValue("issuer_name", issuerName);
        return await ReadManyAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CertificateOfDepositProjectionRow>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, display_name, currency, issuer_name, maturity_date,
                   coupon_rate, callable_date, day_count, primary_identifier_value, version
            from {Qualified("certificate_of_deposit_projection")}
            where maturity_date < @before_date
            order by maturity_date;
            """;
        command.Parameters.AddWithValue("before_date", beforeDate.ToDateTime(TimeOnly.MinValue));
        return await ReadManyAsync(command, ct).ConfigureAwait(false);
    }

    private async Task<CertificateOfDepositProjectionRow?> ReadSingleAsync(NpgsqlCommand command, CancellationToken ct)
    {
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return MapRow(reader);
    }

    private async Task<IReadOnlyList<CertificateOfDepositProjectionRow>> ReadManyAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var results = new List<CertificateOfDepositProjectionRow>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(MapRow(reader));
        }

        return results;
    }

    private static CertificateOfDepositProjectionRow MapRow(NpgsqlDataReader reader)
        => new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            DateOnly.FromDateTime(reader.GetDateTime(4)),
            reader.IsDBNull(5) ? null : reader.GetDecimal(5),
            reader.IsDBNull(6) ? null : DateOnly.FromDateTime(reader.GetDateTime(6)),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetString(8),
            reader.GetInt64(9));

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
}
