using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Tests.AssetOperations;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task FactorPaydown_PostedProjectionRecoversAfterCommitAndPositionAdvanceUsingExactReceipt()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true, factorPool: true);
        var command = await FactorPaydownCommandAsync(fixture, 0.90m, FirstPaydown);
        var instruction = command.FactorPaydown!;
        var dimensions = new LedgerDimensionSetDto(FundId: "amort-fund", InstrumentId: instruction.Scope.SecurityId,
            BookId: fixture.BookId.ToString("D"))
        { PositionId = instruction.Scope.BookPositionId };
        var request = FactorPaydownTestData.ProjectRequest(instruction, dimensions, carryingPath: AmortAccount.Name,
            cashPath: "Cash", lossPath: "Realized Loss");
        var mutation = new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.FactorPaydown,
            AssetAccountId: AmortAccount.Name, FactorPaydown: instruction);
        var (versions, approval) = SuccessorPublicationVersions(command, request, mutation);
        command = WithSuccessorPublicationEvidence(command, request, approval);
        var options = new AssetOperationsOptions
        { ConnectionString = fixture.Options.ConnectionString, Schema = "factor_paydown_pub_" + Guid.NewGuid().ToString("N") };
        await new AssetOperationsMigrationRunner(options).EnsureMigratedAsync();
        try
        {
            var publishers = new IAssetAccountingEventProjectionStore[]
            {
                new InMemoryAssetOperationsProjectionStore(fixture.Restart()),
                new PostgresAssetOperationsProjectionStore(options, fixture.Restart())
            };
            var source = await fixture.Positions.GetSecurityAsync(fixture.Security.SecurityId);
            var position = source.BookPositions.Single();
            foreach (var publisher in publishers)
            {
                await SeedPublicationPositionAsync((IInstrumentPositionProjectionStore)publisher, source.InstrumentRoles.Single(), position);
                foreach (var version in versions)
                    await publisher.AppendAsync(version, version.SpineVersion - 1, position.Version);
            }
            var committed = await fixture.Store.AppendAssetPostingAsync(command);
            var impact = PublicationImpact(committed);
            var posted = versions[^1] with
            {
                SpineVersion = 5,
                Stages = [.. versions[^1].Stages, new(AssetAccountingLifecycleStageDto.Posted,
                    committed.Journal.CreatedAt, "independent-controller", [approval], committed.Journal.Entry.JournalEntryId.ToString("D"))],
                PostedJournalImpact = impact,
                TaxLotMutationBatchId = committed.MutationBatchId
            };
            AssetAccountingEventSpineValidator.Validate(posted).Should().BeEmpty();
            foreach (var publisher in publishers)
            {
                await ((IInstrumentPositionProjectionStore)publisher).UpsertAsync(source.InstrumentRoles.Single(),
                    position with { Version = position.Version + 1 }, null, position.Version,
                    new("controller", "evidence://factor-paydown/new-position", "Later position revision", DateTimeOffset.UtcNow));
                var missing = () => publisher.AppendAsync(posted with { TaxLotMutationBatchId = Guid.NewGuid() }, 4, position.Version);
                await missing.Should().ThrowAsync<InvalidOperationException>();
                (await publisher.GetLatestAsync(posted.EventId, posted.EventVersion))!.Projection.SpineVersion.Should().Be(4);
            }
            publishers[1] = new PostgresAssetOperationsProjectionStore(options, fixture.Restart());
            foreach (var publisher in publishers)
            {
                (await publisher.AppendAsync(posted, 4, position.Version)).WasReplay.Should().BeFalse();
                var replay = await publisher.AppendAsync(posted, 4, position.Version);
                replay.WasReplay.Should().BeTrue();
                replay.Projection.PostedJournalImpact.Should().BeEquivalentTo(impact);
                replay.Projection.FactorPaydown.Should().BeEquivalentTo(instruction);
            }
            (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().ContainSingle();
        }
        finally
        {
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync();
            await using var cleanup = new NpgsqlCommand($"drop schema {options.Schema} cascade", connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
