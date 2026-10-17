using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Integration")]
public sealed class SecurityMasterPostgresSearchPaginationTests(SecurityMasterDatabaseFixture fixture)
    : IClassFixture<SecurityMasterDatabaseFixture>
{
    [SecurityMasterDatabaseFact]
    public async Task SearchAsync_TiedNamesAndRanks_PagesEveryActiveSecurityExactlyOnce()
    {
        const int pageSize = 500;
        var securityIds = Enumerable.Range(1, pageSize + 3)
            .Select(index => Guid.Parse($"11111111-1111-1111-1111-{index:x12}"))
            .ToArray();
        var inactiveId = Guid.Parse("11111111-1111-1111-1111-000000000000");
        await SeedTiedSecuritiesAsync(securityIds, inactiveId);
        var store = new PostgresSecurityMasterStore(fixture.Options);

        // Empty scans drive the corporate-action fetcher. Short substring queries and full-text
        // queries use the other search branches; every row also has the same full-text rank.
        foreach (var query in new[] { string.Empty, "x", "Shared" })
        {
            var first = await store.SearchAsync(new SecuritySearchRequest(query, pageSize, ActiveOnly: true));
            var second = await store.SearchAsync(new SecuritySearchRequest(query, pageSize, pageSize, ActiveOnly: true));
            var exhausted = await store.SearchAsync(new SecuritySearchRequest(query, pageSize, pageSize * 2, ActiveOnly: true));

            first.Select(security => security.SecurityId).Should().Equal(securityIds.Take(pageSize));
            second.Select(security => security.SecurityId).Should().Equal(securityIds.Skip(pageSize));
            first.Concat(second).Select(security => security.SecurityId).Should().OnlyHaveUniqueItems();
            exhausted.Should().BeEmpty();
            first.Concat(second).Should().OnlyContain(security => security.Status == SecurityStatusDto.Active);
        }

        var includingInactive = await store.SearchAsync(new SecuritySearchRequest("Shared", pageSize + 4));
        includingInactive.Select(security => security.SecurityId).Should().Equal(new[] { inactiveId }.Concat(securityIds));
    }

    private async Task SeedTiedSecuritiesAsync(Guid[] securityIds, Guid inactiveId)
    {
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // Seed the migrated query table in reverse ID order so physical insertion order cannot
        // accidentally satisfy the public paging contract. The fixture owns an isolated schema.
        command.CommandText = $$"""
            insert into {{fixture.Options.Schema}}.securities (
                security_id, asset_class, status, display_name, currency,
                primary_identifier_kind, primary_identifier_value, normalized_primary_identifier_value,
                common_terms, asset_specific_terms, provenance, version, effective_from)
            select security_id, 'Equity',
                   case when security_id = @inactive_id then 'Inactive' else 'Active' end,
                   'x Shared Securities', 'USD', 'Ticker', security_id::text, upper(security_id::text),
                   '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, 1, '2026-01-01'::timestamptz
            from unnest(@security_ids) as securities(security_id)
            order by securities.security_id desc;
            """;
        command.Parameters.AddWithValue("inactive_id", inactiveId);
        command.Parameters.AddWithValue("security_ids", securityIds.Append(inactiveId).ToArray());
        (await command.ExecuteNonQueryAsync()).Should().Be(securityIds.Length + 1);
    }
}
