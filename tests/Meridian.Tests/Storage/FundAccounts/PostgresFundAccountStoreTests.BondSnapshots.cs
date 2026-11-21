using Meridian.Contracts.FundStructure;

namespace Meridian.Tests.Storage.FundAccounts;

public sealed partial class PostgresFundAccountStoreTests
{
    [FundAccountDatabaseFact]
    public async Task BondSnapshot_RetainsExactEconomicsCostAndImmutableRetry()
    {
        var store = CreateStore();
        var account = MakeAccount();
        await store.UpsertAccountAsync(account);
        var date = new DateOnly(2026, 11, 12);
        var batchId = Guid.NewGuid();
        var economics = new BondHoldingEconomicsDto(100_000m, 80_000m, .8m, 1.05m,
            BondHoldingQuoteBasis.PercentOfCurrentFace, 99.123456789m, false, -23.123456789m,
            SourceEvidenceId: "synthetic-evidence", OriginalSourceJson: "{\"original\":\"synthetic\"}", InvoiceBasis: BondHoldingInvoiceBasis.IndexedPrincipal);
        var marketValue = BondHoldingNormalization.Calculate(economics, "USD").Dirty;
        var line = new CustodianPositionLineDto(Guid.NewGuid(), batchId, account.AccountId, date, "SYNTHETIC", "SecurityId",
            100_000m, marketValue, "USD", "Synthetic indexed amortizer", "Bond", false, 97_123.123456789m, Guid.NewGuid(), economics);
        var request = new IngestCustodianStatementRequest(batchId, account.AccountId, date, "Synthetic", "synthetic-v1", null,
            [line], "synthetic-reviewer", Authority: new(1, "synthetic-reviewed", new string('a', 64)));
        var batch = new CustodianStatementBatchDto(batchId, account.AccountId, date, request.CustodianName, request.SourceFormat,
            1, DateTimeOffset.UtcNow, request.LoadedBy, request.Authority, BondHoldingNormalization.ContentHash(request));

        await store.InsertCustodianStatementBatchAsync(batch, [line]);
        await store.InsertCustodianStatementBatchAsync(batch, [line]);
        var retained = await store.GetCustodianSnapshotAsync(account.AccountId, date);
        Assert.Equal(line, Assert.Single(retained!.Lines));
        var changed = line with { CostBasis = line.CostBasis + 1m };
        var changedRequest = request with { Lines = [changed] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.InsertCustodianStatementBatchAsync(
            batch with { ContentSha256 = BondHoldingNormalization.ContentHash(changedRequest) }, [changed]));
        Assert.Equal(line, Assert.Single((await store.GetCustodianSnapshotAsync(account.AccountId, date))!.Lines));
    }

    [FundAccountDatabaseFact]
    public async Task BondSnapshot_EmptyFullRevisionSupersedesWithoutDeletingHistory()
    {
        var store = CreateStore();
        var account = MakeAccount();
        await store.UpsertAccountAsync(account);
        var service = new Meridian.PortfolioRecords.FundAccounts.PostgresFundAccountService(store);
        var date = new DateOnly(2026, 11, 12);
        var firstId = Guid.NewGuid();
        var firstLine = new CustodianPositionLineDto(Guid.NewGuid(), firstId, account.AccountId, date,
            "synthetic", "SecurityId", 100m, 100m, "USD", "Synthetic", "Bond", false, 98m, Guid.NewGuid(),
            new(100m, 100m, 1m, 1m, BondHoldingQuoteBasis.PercentOfCurrentFace, 100m, false, 0m, InvoiceBasis: BondHoldingInvoiceBasis.Nominal));
        var first = await service.IngestCustodianStatementAsync(new(firstId, account.AccountId, date, "Synthetic", "synthetic-v1",
            null, [firstLine], "synthetic", Authority: new(1, "source1", new string('a', 64))));
        var next = await service.IngestCustodianStatementAsync(new(Guid.NewGuid(), account.AccountId, date, "Synthetic", "synthetic-v1",
            null, [], "synthetic", Authority: new(2, "source2", new string('b', 64), SupersedesBatchId: firstId)));
        Assert.Equal(next.BatchId, (await service.GetCustodianSnapshotAsync(account.AccountId, date))!.Batch.BatchId);
        Assert.Empty((await service.GetCustodianSnapshotAsync(account.AccountId, date))!.Lines);
        Assert.Equal(first.BatchId, (await service.GetCustodianSnapshotAsync(account.AccountId, date, first.IngestedAt))!.Batch.BatchId);
        Assert.Single(await store.GetCustodianPositionsAsync(account.AccountId, date));
    }
}
