using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Integration")]
public sealed class PostgresSecurityMasterPricingHistoryTests(SecurityMasterDatabaseFixture fixture)
    : IClassFixture<SecurityMasterDatabaseFixture>
{
    [SecurityMasterDatabaseFact]
    public async Task Observations_RetainOutOfOrderHistoryAndRefuseSameTimestampReplacement()
    {
        var securityId = await SeedSecurityAsync();
        var store = new PostgresSecurityMasterPricingStore(fixture.Options);
        var date = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var earlier = new RecordRawPriceRequest(securityId, "source", 98m, date, "operator", SecurityPriceUnit.PercentOfPar);
        await store.RecordRawPriceAsync(earlier with { Price = 99m, PriceAsOf = date.AddDays(1) });
        await store.RecordRawPriceAsync(earlier);
        await store.RecordRawPriceAsync(earlier); // identical replay preserves retained evidence
        (await store.GetRawPricesAsync(securityId, date)).Single().Price.Should().Be(98m);
        (await store.GetRawPricesAsync(securityId, date.AddDays(1))).Single().Price.Should().Be(99m);
        await store.Invoking(s => s.RecordRawPriceAsync(earlier with { Price = 97m }))
            .Should().ThrowAsync<InvalidOperationException>();
        (await store.GetRawPricesAsync(securityId, date)).Single().Price.Should().Be(98m);
    }

    [SecurityMasterDatabaseFact]
    public async Task KnowledgeCutoff_PreservesObservationAndHierarchySelectionAfterLateArrival()
    {
        var securityId = await SeedSecurityAsync();
        var store = new PostgresSecurityMasterPricingStore(fixture.Options);
        var date = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        await store.RecordRawPriceAsync(new(securityId, "source", 98m, date, "operator", SecurityPriceUnit.PercentOfPar));
        var first = new SecurityPricingHierarchyDto(securityId, null, [new(1, "source", "Source", 3)], date, "operator");
        await store.UpsertHierarchyAsync(first);
        var knownAt = await DatabaseNowAsync();
        // A newer effective observation and hierarchy arrive after the original evaluation.
        await store.RecordRawPriceAsync(new(securityId, "source", 99m, date.AddDays(1), "operator", SecurityPriceUnit.PercentOfPar));
        await store.UpsertHierarchyAsync(first with { Entries = [new(1, "other", "Other", 3)], AsOf = date.AddDays(1) });
        (await store.GetRawPricesAsync(securityId, date.AddDays(2), knownAt: knownAt)).Single().Price.Should().Be(98m);
        (await store.GetHierarchyAsOfAsync(securityId, null, date.AddDays(2), knownAt: knownAt))!
            .Entries.Single().SourceId.Should().Be("source");
        (await store.GetRawPricesAsync(securityId, date.AddDays(2))).Single().Price.Should().Be(99m);
        (await store.GetHierarchyAsOfAsync(securityId, null, date.AddDays(2)))!.Entries.Single().SourceId.Should().Be("other");
    }

    [SecurityMasterDatabaseFact]
    public async Task HierarchyVersions_KeepAccountScopeAndRefuseReplacement()
    {
        var securityId = await SeedSecurityAsync();
        var store = new PostgresSecurityMasterPricingStore(fixture.Options);
        var date = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var first = new SecurityPricingHierarchyDto(securityId, "account-a", [new(1, "source", "Source", 3)], date, "operator");
        await store.UpsertHierarchyAsync(first);
        await store.UpsertHierarchyAsync(first with { AccountId = "account-b", Entries = [new(1, "other", "Other", 3)] });
        await store.Invoking(s => s.UpsertHierarchyAsync(first with { Entries = [new(1, "changed", "Changed", 3)] }))
            .Should().ThrowAsync<InvalidOperationException>();
        (await store.GetHierarchyAsOfAsync(securityId, "account-a", date))!.Entries.Single().SourceId.Should().Be("source");
        (await store.GetHierarchyAsOfAsync(securityId, "account-b", date))!.Entries.Single().SourceId.Should().Be("other");
        (await store.GetHierarchyAsOfAsync(securityId, null, date)).Should().BeNull();
    }

    private async Task<DateTimeOffset> DatabaseNowAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select clock_timestamp()";
        return new DateTimeOffset((DateTime)(await command.ExecuteScalarAsync())!);
    }

    private async Task<Guid> SeedSecurityAsync()
    {
        var id = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            insert into {fixture.Options.Schema}.securities (
                security_id, asset_class, status, display_name, currency, primary_identifier_kind,
                primary_identifier_value, normalized_primary_identifier_value, common_terms,
                asset_specific_terms, provenance, version, effective_from)
            values (@id, 'Bond', 'Active', 'Price history test', 'USD', 'InternalCode', @identifier,
                @identifier, jsonb_build_object(), jsonb_build_object(), jsonb_build_object(), 1, now());
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("identifier", id.ToString("N"));
        await command.ExecuteNonQueryAsync();
        return id;
    }
}
