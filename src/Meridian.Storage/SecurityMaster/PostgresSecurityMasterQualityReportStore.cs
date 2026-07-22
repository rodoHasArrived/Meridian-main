using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresSecurityMasterQualityReportStore : ISecurityMasterQualityReportStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    private readonly SecurityMasterOptions _options;

    public PostgresSecurityMasterQualityReportStore(SecurityMasterOptions options)
    {
        _options = options;
    }

    public async Task<SecurityMasterQualityReportDto?> GetLatestAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select report_json
            from {Qualified("security_master_quality_reports")}
            order by run_at desc
            limit 1;
            """;

        var raw = (string?)await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return raw is null
            ? null
            : JsonSerializer.Deserialize<SecurityMasterQualityReportDto>(raw, JsonOptions);
    }

    public async Task SaveAsync(SecurityMasterQualityReportDto report, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(report, JsonOptions);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into {Qualified("security_master_quality_reports")} (run_at, report_json)
            values (@run_at, @report_json::jsonb);
            """;
        command.Parameters.AddWithValue("run_at", report.RunAt.UtcDateTime);
        command.Parameters.AddWithValue("report_json", json);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            throw new InvalidOperationException("SecurityMasterOptions.ConnectionString is not configured.");

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private string Qualified(string table) => $"{_options.Schema}.{table}";
}
