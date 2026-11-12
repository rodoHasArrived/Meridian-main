using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

public sealed partial class CorporateActionAccountingPostgresRoundTripTests
{
    // Called only after the real native-post round trip committed its ledger/spine/case result.
    private static async Task AssertCashDividendHistoryImmutableAsync(SecurityMasterOptions options, CancellationToken ct)
    {
        string[] tables =
        [
            "cash_dividend_attestation_declaration_history",
            "cash_dividend_attestation_reservations",
            "cash_dividend_attestation_event_aliases",
            "cash_dividend_attestations",
            "cash_dividend_attestation_postings"
        ];
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(ct);
        var original = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            var rows = await ReadRowsAsync(table);
            using var json = JsonDocument.Parse(rows);
            json.RootElement.GetArrayLength().Should().BeGreaterThan(0, "each guard must reject an actual retained native-post record");
            original.Add(table, rows);
        }

        foreach (var table in tables)
        {
            var qualified = QualifiedTable(table);
            foreach (var sql in new[]
            {
                $"update {qualified} set tenant_id = tenant_id",
                $"delete from {qualified}",
                // CASCADE bypasses the FK-only refusal, so SQLSTATE 55000 must come from the
                // append-only trigger rather than a referenced-table constraint (0A000).
                $"truncate table {qualified} cascade"
            })
            {
                await using var mutation = connection.CreateCommand();
                mutation.CommandText = sql;
                var error = await Assert.ThrowsAsync<PostgresException>(() => mutation.ExecuteNonQueryAsync(ct));
                error.SqlState.Should().Be("55000");
                error.MessageText.Should().StartWith("Cash-dividend attestation");
            }
        }

        foreach (var table in tables)
            (await ReadRowsAsync(table)).Should().Be(original[table],
                "failed update, delete and cascading truncate must preserve every original native-post row");

        string QualifiedTable(string table) => "\"" + options.Schema.Replace("\"", "\"\"", StringComparison.Ordinal) + "\".\"" + table + "\"";

        async Task<string> ReadRowsAsync(string table)
        {
            await using var read = connection.CreateCommand();
            read.CommandText = $"select coalesce(jsonb_agg(to_jsonb(r) order by to_jsonb(r)::text), '[]'::jsonb)::text from {QualifiedTable(table)} r";
            return (string)(await read.ExecuteScalarAsync(ct))!;
        }
    }
}
