using System.Security.Cryptography;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Storage.FundAccounts;
using Meridian.Storage.FundStructure;
using Meridian.Tests.Storage.FundAccounts;
using Xunit;

namespace Meridian.Tests.Storage.FundStructure;

[Trait("Category", "Integration")]
[Collection(nameof(FundAccountDatabaseCollection))]
public sealed class PostgresFundStructureStoreTests
{
    private readonly FundAccountDatabaseFixture _fixture;

    public PostgresFundStructureStoreTests(FundAccountDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [FundAccountDatabaseFact]
    public async Task ImportLegacySnapshotIfEmptyAsync_LateInvalidEntity_RollsBackDataAndReceipt()
    {
        var options = CreateOptions("fs_legacy_rollback");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await new FundStructureMigrationRunner(options).EnsureMigratedAsync(cts.Token);
            var store = new PostgresFundStructureStore(options);
            var first = MakeOrganization();
            var invalid = MakeOrganization() with { Name = null! };
            var sourceHash = CreateSourceHash();

            var exception = await Assert.ThrowsAnyAsync<Exception>(
                () => store.ImportLegacySnapshotIfEmptyAsync(
                    CreateRequest(sourceHash, first, invalid),
                    cts.Token));
            exception.Should().NotBeOfType<OperationCanceledException>();
            (await store.IsEmptyAsync(cts.Token)).Should().BeTrue(
                "the valid first entity and invalid second entity share one explicit transaction");

            (await store.ImportLegacySnapshotIfEmptyAsync(
                CreateRequest(sourceHash, first),
                cts.Token)).Should().Be(
                FundStructureLegacyImportResult.Imported,
                "a rolled-back import must not leave a durable receipt");
            (await store.GetOrganizationAsync(first.OrganizationId, cts.Token)).Should().NotBeNull();
        }
        finally
        {
            await FundAccountDatabaseFixture.DropSchemaAsync(options.ConnectionString, options.Schema);
        }
    }

    [FundAccountDatabaseFact]
    public async Task ImportLegacySnapshotIfEmptyAsync_PreCanceled_LeavesDataAndReceiptUncommitted()
    {
        var options = CreateOptions("fs_legacy_cancel");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await new FundStructureMigrationRunner(options).EnsureMigratedAsync(timeout.Token);
            var store = new PostgresFundStructureStore(options);
            var organization = MakeOrganization();
            var sourceHash = CreateSourceHash();
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();

            var act = () => store.ImportLegacySnapshotIfEmptyAsync(
                CreateRequest(sourceHash, organization),
                canceled.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            (await store.IsEmptyAsync(timeout.Token)).Should().BeTrue();
            (await store.ImportLegacySnapshotIfEmptyAsync(
                CreateRequest(sourceHash, organization),
                timeout.Token)).Should().Be(FundStructureLegacyImportResult.Imported);
        }
        finally
        {
            await FundAccountDatabaseFixture.DropSchemaAsync(options.ConnectionString, options.Schema);
        }
    }

    [FundAccountDatabaseFact]
    public async Task ImportLegacySnapshotIfEmptyAsync_CommittedReceipt_MakesReplayNonMutating()
    {
        var options = CreateOptions("fs_legacy_replay");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await new FundStructureMigrationRunner(options).EnsureMigratedAsync(cts.Token);
            var store = new PostgresFundStructureStore(options);
            var imported = MakeOrganization();
            var ignoredReplayEntity = MakeOrganization();
            var sourceHash = CreateSourceHash();

            (await store.ImportLegacySnapshotIfEmptyAsync(
                CreateRequest(sourceHash, imported),
                cts.Token)).Should().Be(FundStructureLegacyImportResult.Imported);

            (await store.ImportLegacySnapshotIfEmptyAsync(
                CreateRequest(sourceHash, ignoredReplayEntity),
                cts.Token)).Should().Be(FundStructureLegacyImportResult.AlreadyImported);
            (await store.GetOrganizationAsync(imported.OrganizationId, cts.Token)).Should().NotBeNull();
            (await store.GetOrganizationAsync(ignoredReplayEntity.OrganizationId, cts.Token)).Should().BeNull(
                "receipt recovery must not replay a different payload under the same source hash");
        }
        finally
        {
            await FundAccountDatabaseFixture.DropSchemaAsync(options.ConnectionString, options.Schema);
        }
    }

    [FundAccountDatabaseFact]
    public async Task SharedSchema_FundAccountAndFundStructureReceiptsRemainIndependent()
    {
        var schema = $"legacy_shared_{Guid.NewGuid():N}";
        var accountOptions = new FundAccountStoreOptions
        {
            ConnectionString = _fixture.Options.ConnectionString,
            Schema = schema
        };
        var structureOptions = new FundStructureStoreOptions
        {
            ConnectionString = _fixture.Options.ConnectionString,
            Schema = schema
        };

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await new FundAccountMigrationRunner(accountOptions).EnsureMigratedAsync(cts.Token);
            await new FundStructureMigrationRunner(structureOptions).EnsureMigratedAsync(cts.Token);

            var account = MakeAccount();
            var accountResult = await new PostgresFundAccountStore(accountOptions)
                .ImportLegacySnapshotIfEmptyAsync(
                    new FundAccountLegacyImportRequest(
                        CreateSourceHash(),
                        [
                            new FundAccountLegacyImportAccount(
                                account,
                                [],
                                [],
                                [],
                                [],
                                [],
                                [])
                        ]),
                    cts.Token);
            var structureResult = await new PostgresFundStructureStore(structureOptions)
                .ImportLegacySnapshotIfEmptyAsync(
                    CreateRequest(CreateSourceHash(), MakeOrganization()),
                    cts.Token);

            accountResult.Should().Be(FundAccountLegacyImportResult.Imported);
            structureResult.Should().Be(FundStructureLegacyImportResult.Imported);
        }
        finally
        {
            await FundAccountDatabaseFixture.DropSchemaAsync(accountOptions.ConnectionString, schema);
        }
    }

    [FundAccountDatabaseFact]
    public async Task IsEmptyAsync_NonOrganizationEntityExists_RejectsLegacyImport()
    {
        var options = CreateOptions("fs_partial_store");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await new FundStructureMigrationRunner(options).EnsureMigratedAsync(cts.Token);
            var store = new PostgresFundStructureStore(options);
            var existingFund = MakeFund();
            var legacyOrganization = MakeOrganization();

            await store.UpsertFundAsync(existingFund, cts.Token);

            (await store.IsEmptyAsync(cts.Token)).Should().BeFalse(
                "all fund-structure tables, not only organizations, define whether import is safe");
            (await store.ImportLegacySnapshotIfEmptyAsync(
                CreateRequest(CreateSourceHash(), legacyOrganization),
                cts.Token)).Should().Be(FundStructureLegacyImportResult.StoreNotEmpty);
            (await store.GetOrganizationAsync(legacyOrganization.OrganizationId, cts.Token)).Should().BeNull();
            (await store.GetFundAsync(existingFund.FundId, cts.Token)).Should().NotBeNull();
        }
        finally
        {
            await FundAccountDatabaseFixture.DropSchemaAsync(options.ConnectionString, options.Schema);
        }
    }

    [FundAccountDatabaseFact]
    public async Task ImportLegacySnapshotIfEmptyAsync_DisconnectedLinkedAccountIdentity_RoundTrips()
    {
        var options = CreateOptions("fs_linked_account");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await new FundStructureMigrationRunner(options).EnsureMigratedAsync(cts.Token);
            var store = new PostgresFundStructureStore(options);
            var linkedAccountId = Guid.NewGuid();
            var request = new FundStructureLegacyImportRequest(
                CreateSourceHash(),
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [linkedAccountId]);

            (await store.ImportLegacySnapshotIfEmptyAsync(request, cts.Token))
                .Should().Be(FundStructureLegacyImportResult.Imported);
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

    private static FundStructureLegacyImportRequest CreateRequest(
        string sourceHash,
        params OrganizationSummaryDto[] organizations)
        => new(
            sourceHash,
            organizations,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            []);

    private static OrganizationSummaryDto MakeOrganization() => new(
        OrganizationId: Guid.NewGuid(),
        Code: $"ORG-{Guid.NewGuid():N}",
        Name: "Legacy organization",
        BaseCurrency: "USD",
        IsActive: true,
        EffectiveFrom: DateTimeOffset.UtcNow,
        EffectiveTo: null,
        BusinessIds: []);

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

    private static AccountSummaryDto MakeAccount() => new(
        AccountId: Guid.NewGuid(),
        AccountType: AccountTypeDto.Custody,
        EntityId: null,
        FundId: Guid.NewGuid(),
        SleeveId: null,
        VehicleId: null,
        AccountCode: $"CUST-{Guid.NewGuid():N}",
        DisplayName: "Legacy account",
        BaseCurrency: "USD",
        Institution: "Custodian",
        IsActive: true,
        EffectiveFrom: DateTimeOffset.UtcNow,
        EffectiveTo: null,
        PortfolioId: null,
        LedgerReference: null,
        StrategyId: null,
        RunId: null);

    private static string CreateSourceHash()
        => Convert.ToHexString(SHA256.HashData(Guid.NewGuid().ToByteArray())).ToLowerInvariant();
}
