using Meridian.Contracts.PrivateCapital;
using Meridian.PortfolioRecords.PrivateCapital;
using Meridian.Storage.FundAccounts;

namespace Meridian.Tests.Storage.FundAccounts;

[Trait("Category", "Integration")]
public sealed class PostgresPrivateCapitalRecordStoreTests(FundAccountDatabaseFixture fixture) : IClassFixture<FundAccountDatabaseFixture>
{
    [FundAccountDatabaseFact]
    public async Task ProductionCapture_ReloadAndConcurrentReplayKeepOneSnapshotAndExactSource()
    {
        var scope = new PrivateCapitalScope("private-capital-" + Guid.NewGuid(), "company");
        var store = new PostgresPrivateCapitalRecordStore(fixture.Options);
        var service = new PrivateCapitalRecordService(store, new Authority());
        var evidence = new PrivateCapitalEvidence("ev-012345678901234567890123", "doc", new('a', 64));
        var commitment = await service.CreateAsync(scope, "operator", new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "USD", "Agreement p2", evidence));
        var row = new PrivateCapitalSnapshotInput(commitment.CommitmentId, "2026-09-30", "2026-09-30", "10000000000000.000001", "0", null, "1.25", "100", "GP statement p3");
        var preview = await service.PreviewAsync(scope, "operator", new([row], null, evidence, "Confirmed source values"));
        var command = new AcceptPrivateCapitalImport(preview.BatchId, preview.Fingerprint, Guid.NewGuid());
        var competingService = new PrivateCapitalRecordService(new PostgresPrivateCapitalRecordStore(fixture.Options), new Authority());
        var receipts = await Task.WhenAll(service.AcceptAsync(scope, "operator", command), competingService.AcceptAsync(scope, "operator", command));
        Assert.Equal(receipts[0].SnapshotIds, receipts[1].SnapshotIds);
        var reloaded = await new PrivateCapitalRecordService(new PostgresPrivateCapitalRecordStore(fixture.Options), new Authority()).ReadAsync(scope);
        var snapshot = Assert.Single(reloaded.Snapshots);
        Assert.Equal("10000000000000.000001", snapshot.CommitmentAmount);
        Assert.Equal("0", snapshot.CalledAmount);
        Assert.Null(snapshot.UnfundedAmount);
        Assert.Equal(evidence, snapshot.Evidence);
        Assert.Empty((await store.ReadAsync(scope with { CompanyId = "foreign-company" })));
    }

    [FundAccountDatabaseFact]
    public async Task FailedSqlBatchRollsBackEveryRow_AndMissingScopeIsRefused()
    {
        var scope = new PrivateCapitalScope("private-capital-" + Guid.NewGuid(), "company");
        var store = new PostgresPrivateCapitalRecordStore(fixture.Options);
        var id = Guid.NewGuid();
        await Assert.ThrowsAnyAsync<Exception>(() => store.AppendAsync(scope, _ =>
            [new("test", id, "{}"), new("test", id, "{}") ]));
        Assert.Empty(await store.ReadAsync(scope));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.ReadAsync(new("", "")));
    }

    private sealed class Authority : IPrivateCapitalRecordAuthority
    {
        public Task<PrivateCapitalChoices> ChoicesAsync(PrivateCapitalScope scope, CancellationToken ct) => Task.FromResult(new PrivateCapitalChoices([], []));
        public Task<PrivateCapitalVehicle> ValidateRegisterAsync(PrivateCapitalScope scope, Guid entityId, Guid securityId, string currency, CancellationToken ct)
            => Task.FromResult(new PrivateCapitalVehicle("Vehicle", "Buyout", 2021, 7));
        public Task ValidateEvidenceAsync(PrivateCapitalScope scope, PrivateCapitalEvidence evidence, CancellationToken ct) => Task.CompletedTask;
    }
}
