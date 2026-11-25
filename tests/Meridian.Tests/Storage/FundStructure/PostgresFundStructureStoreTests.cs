using FluentAssertions;
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
}
