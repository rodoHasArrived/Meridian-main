using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.FinancialOperations.Consolidation;
using Npgsql;

namespace Meridian.Tests.Storage;

[Trait("Category", "Integration")]
public sealed class ConsolidationFxPostgresTests
{
    [LedgerDatabaseFact]
    public async Task ApprovedRatesAndConfiguration_RoundTripRetainedHashesAndScopedRevisions()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var book = await database.JournalStore.SaveLedgerBookAsync(ConsolidationStorageFixture.Book("fx"), ct);
        var store = new ConsolidationFxPostgresStore(database.Options);
        var rates = ConsolidationFxTests.Rates(ConsolidationFxTests.Quote(ConsolidationFxRateKindDto.Closing, 1.2m, new(2026, 6, 30)));
        await store.RetainRateSetAsync(rates, ct);
        await store.RetainRateSetAsync(rates, ct);
        var retained = await store.GetRateSetAsync("tenant", "company", rates.RateSetId, 1, ct);
        Assert.NotNull(retained?.ContentHash);
        Assert.Null(await store.GetRateSetAsync("foreign", "company", rates.RateSetId, 1, ct));
        var config = new ConsolidationFxBookConfigurationDto(book.LedgerBookId, 1, "tenant", "company",
            new(2026, 1, 1), null, rates.RateSetId, 1, ConsolidationFxTests.Policy(), "owner", rates.ApprovedAt);
        await store.RetainConfigurationAsync(config, ct);
        var storedConfig = await store.GetConfigurationAsync(book.LedgerBookId, new(2026, 6, 30), ct);
        Assert.NotNull(storedConfig?.ContentHash);
        Assert.Equal(config.Policy, storedConfig!.Policy);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RetainRateSetAsync(rates with { SourceEvidence = "changed" }, ct));
        await store.RetainRateSetAsync(rates with { Revision = 2, PreviousHash = retained!.ContentHash }, ct);
        Assert.Equal(2, (await store.ListRateSetsAsync("tenant", "company", ct)).Count);
        Assert.Equal(1, (await store.GetConfigurationAsync(book.LedgerBookId, new(2026, 6, 30), ct))!.RateSetRevision);
    }

    [LedgerDatabaseFact]
    public async Task DatabaseRejectsUpdateAndDeleteOfApprovedRateEvidence()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var store = new ConsolidationFxPostgresStore(database.Options);
        var rates = ConsolidationFxTests.Rates(ConsolidationFxTests.Quote(ConsolidationFxRateKindDto.Closing, 1.2m, new(2026, 6, 30)));
        await store.RetainRateSetAsync(rates, ct);
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync(ct);
        var schema = new NpgsqlCommandBuilder().QuoteIdentifier(database.Options.SchemaName);
        foreach (var sql in new[] { $"update {schema}.consolidation_fx_rate_sets set document = document", $"delete from {schema}.consolidation_fx_rate_sets" })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(ct));
            Assert.Equal("P0001", exception.SqlState);
        }
        Assert.Single(await store.ListRateSetsAsync("tenant", "company", ct));
    }
}
