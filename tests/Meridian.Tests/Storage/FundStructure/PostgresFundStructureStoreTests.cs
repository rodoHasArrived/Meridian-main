using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Storage.FundStructure;
using Meridian.Tests.Storage.FundAccounts;
using Xunit;

namespace Meridian.Tests.Storage.FundStructure;

[Trait("Category", "Integration")]
public sealed class PostgresFundStructureStoreTests : IClassFixture<FundAccountDatabaseFixture>
{
    private readonly FundAccountDatabaseFixture _fixture;

    public PostgresFundStructureStoreTests(FundAccountDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [FundAccountDatabaseFact]
    public async Task IsEmptyAsync_NonOrganizationEntityExists_ReturnsFalse()
    {
        var options = CreateOptions("fs_partial_store");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await new FundStructureMigrationRunner(options).EnsureMigratedAsync(cts.Token);
            var store = new PostgresFundStructureStore(options);
            var existingFund = MakeFund();

            await store.UpsertFundAsync(existingFund, cts.Token);

            (await store.IsEmptyAsync(cts.Token)).Should().BeFalse(
                "all fund-structure tables, not only organizations, define whether the store is empty");
            (await store.GetFundAsync(existingFund.FundId, cts.Token)).Should().NotBeNull();
        }
        finally
        {
            await FundAccountDatabaseFixture.DropSchemaAsync(options.ConnectionString, options.Schema);
        }
    }

    [FundAccountDatabaseFact]
    public async Task UpsertLinkedAccountIdAsync_DisconnectedLinkedAccountIdentity_RoundTrips()
    {
        var options = CreateOptions("fs_linked_account");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await new FundStructureMigrationRunner(options).EnsureMigratedAsync(cts.Token);
            var store = new PostgresFundStructureStore(options);
            var linkedAccountId = Guid.NewGuid();

            await store.UpsertLinkedAccountIdAsync(linkedAccountId, cts.Token);
            (await store.GetAllLinkedAccountIdsAsync(cts.Token))
                .Should().ContainSingle().Which.Should().Be(linkedAccountId);
            (await store.IsEmptyAsync(cts.Token)).Should().BeFalse(
                "a retained account node identity is fund-structure state even without an active link");
        }
        finally
        {
            await FundAccountDatabaseFixture.DropSchemaAsync(options.ConnectionString, options.Schema);
        }
    }

    private FundStructureStoreOptions CreateOptions(string prefix) => new()
    {
        ConnectionString = _fixture.Options.ConnectionString,
        Schema = $"{prefix}_{Guid.NewGuid():N}"
    };

    private static FundSummaryDto MakeFund() => new(
        FundId: Guid.NewGuid(),
        BusinessId: null,
        Code: $"FUND-{Guid.NewGuid():N}",
        Name: "Existing fund",
        BaseCurrency: "USD",
        IsActive: true,
        EffectiveFrom: DateTimeOffset.UtcNow,
        EffectiveTo: null,
        SleeveIds: [],
        VehicleIds: [],
        EntityIds: [],
        InvestmentPortfolioIds: [],
        AccountIds: []);
}
