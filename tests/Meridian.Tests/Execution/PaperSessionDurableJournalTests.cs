using Meridian.Contracts.Integrity;
using Meridian.Core.IO;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class PaperSessionDurableJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"paper-journal-{Guid.NewGuid():N}");

    [Fact]
    public async Task FillCostsAndFinancingKeepTheirJournalIdsAndCashAfterRestart()
    {
        var store = OpenStore();
        Guid[] journalIds;
        string sessionId;
        DateTimeOffset afterFinancing;
        await using (var service = OpenService(store))
        {
            var summary = await service.CreateSessionAsync(new("strategy", null, 1_000m));
            sessionId = summary.SessionId;
            var fillTime = summary.CreatedAt.AddMinutes(1);
            var fill = Fill(fillTime);
            var fillId = PaperSessionFillRecord.ComputeCanonicalFillId(fill);
            await service.RecordFillAsync(sessionId, fillId, fill, 0, null,
                strategyId: "strategy", runId: "paper-run");
            await service.RecordFillAsync(sessionId, fillId, fill, 0, null,
                strategyId: "strategy", runId: "paper-run");

            // A fee is part of the retained economics, but does not change the existing fill identity.
            await Assert.ThrowsAsync<InvalidDataException>(() => service.RecordFillAsync(sessionId, fillId,
                fill with { Fees = 1.5m }, 0, null, strategyId: "strategy", runId: "paper-run"));
            var feeEvidence = Assert.IsType<PaperSessionCutoffEvidence>(await service.GetCutoffEvidenceAsync(sessionId, fillTime));
            Assert.Equal(947.25m, feeEvidence.JournalCash);
            Assert.Equal(feeEvidence.JournalCash, feeEvidence.PortfolioCash);
            Assert.Empty(feeEvidence.Breaks);

            var chargeDate = DateOnly.FromDateTime(fillTime.UtcDateTime).AddDays(1);
            var charge = new PaperFinancingCharge(sessionId, PaperTradingPortfolio.DefaultAccountId,
                chargeDate, 4m, "test-assessment", "Paper margin charge", "test-operator");
            Assert.True(await service.RecordFinancingAsync(charge));
            Assert.False(await service.RecordFinancingAsync(charge));
            await Assert.ThrowsAsync<InvalidDataException>(() => service.RecordFinancingAsync(charge with { Amount = 5m }));
            afterFinancing = charge.OccurredAt;

            var ledger = Assert.IsAssignableFrom<IReadOnlyLedger>(service.GetLedger(sessionId));
            journalIds = ledger.Journal.Select(static entry => entry.JournalEntryId).ToArray();
            Assert.All(ledger.Journal, entry =>
            {
                Assert.Equal(sessionId, entry.Metadata.LedgerBook);
                Assert.Equal(PaperTradingPortfolio.DefaultAccountId, entry.Metadata.FinancialAccountId);
                Assert.All(entry.Lines, line =>
                {
                    Assert.Equal("USD", line.Currency?.FunctionalCurrency);
                    Assert.NotNull(line.ChartAccount);
                });
            });
            Assert.Contains(ledger.Journal, entry => entry.Metadata.FillId == fillId
                && entry.Metadata.Tags?["runId"] == "paper-run");
            var receipts = await store.LoadJournalBatchesAsync(sessionId);
            Assert.Equal(3, receipts.Count);
            Assert.Equal(charge, Assert.Single(receipts, receipt => receipt.Batch.Purpose == PaperSessionJournalPurpose.Financing).Batch.Financing);
        }

        await using var restarted = OpenService(OpenStore());
        await restarted.InitialiseAsync();
        Assert.Equal(journalIds, restarted.GetLedger(sessionId)!.Journal.Select(static entry => entry.JournalEntryId).ToArray());
        var cutoff = Assert.IsType<PaperSessionCutoffEvidence>(await restarted.GetCutoffEvidenceAsync(sessionId, afterFinancing));
        Assert.Equal(943.25m, cutoff.JournalCash);
        Assert.Equal(cutoff.JournalCash, cutoff.PortfolioCash);
        Assert.Empty(cutoff.Breaks);
    }

    [Fact]
    public async Task RestartCompletesAnUnacknowledgedFillClaimExactlyOnce()
    {
        var store = OpenStore();
        var sessionId = string.Empty;
        PaperSessionFillRecord fill;
        await using (var initial = OpenService(store))
        {
            var session = await initial.CreateSessionAsync(new("recovery", null, 1_000m));
            sessionId = session.SessionId;
            fill = PaperSessionFillRecord.CreateCanonical(Fill(session.CreatedAt.AddSeconds(1)));
            var claim = await store.TryAppendFillAsync(sessionId, fill);
            Assert.Equal(PaperSessionFillAppendStatus.Added, claim.Status);
            Assert.False(Assert.Single(await store.LoadFillRecordsAsync(sessionId)).IsApplied);
            Assert.Single(await store.LoadJournalBatchesAsync(sessionId));
            Assert.Equal(1_000m, initial.GetSession(sessionId)!.Portfolio!.Cash);
        }

        await using var restarted = OpenService(OpenStore());
        await restarted.InitialiseAsync();
        await restarted.RecordFillAsync(sessionId, fill.FillId, fill.Fill);
        Assert.True(Assert.Single(await store.LoadFillRecordsAsync(sessionId)).IsApplied);
        var receipts = await store.LoadJournalBatchesAsync(sessionId);
        Assert.Equal(2, receipts.Count);
        var fillBatch = Assert.Single(receipts, receipt => receipt.Batch.Purpose == PaperSessionJournalPurpose.Fill).Batch;
        Assert.All(fillBatch.Entries, entry => Assert.Equal(fill.FillId, entry.Metadata.FillId));
        Assert.Equal(947.25m, restarted.GetSession(sessionId)!.Portfolio!.Cash);
    }

    [Fact]
    public async Task ConflictingBatchCannotReplaceTheRetainedReceipt()
    {
        var store = OpenStore();
        await using var service = OpenService(store);
        var session = await service.CreateSessionAsync(new("opening", null, 1_000m));
        var receipt = Assert.Single(await store.LoadJournalBatchesAsync(session.SessionId));
        Assert.Equal(receipt.ContentHash, (await OpenStore().AppendJournalBatchAsync(receipt.Batch)).ContentHash);

        var conflict = receipt.Batch with { SourceHash = Sha256Digest.ComputeUtf8("different opening source") };
        Assert.Equal(receipt.Batch.BatchId, conflict.BatchId);
        Task<PaperSessionJournalReceipt> competingWrite;
        await using (await ExclusiveFileLock.AcquireAsync(Path.Combine(_root, ".journal-receipts.lock"), TimeSpan.FromSeconds(5)))
        {
            competingWrite = OpenStore().AppendJournalBatchAsync(conflict);
            Assert.False(competingWrite.IsCompleted);
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => competingWrite);
        var after = Assert.Single(await store.LoadJournalBatchesAsync(session.SessionId));
        Assert.Equal(receipt.ContentHash, after.ContentHash);
        Assert.Equal(receipt.Batch.SourceHash, after.Batch.SourceHash);
    }

    [Fact]
    public async Task NativeOrdersReuseTheirDefaultSessionAndRefuseASelectedSessionFromAnotherAccount()
    {
        var accountId = Guid.NewGuid();
        var otherAccountId = Guid.NewGuid();
        var request = new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m,
            FundAccountId = accountId
        };
        var grant = new ExecutionAccountGrant(accountId, "operator", ExecutionAccountGrantSources.HttpPrincipal, DateTimeOffset.UtcNow);
        var authority = OrderSubmissionAuthority.ForOperatorTicket("operator", grant, "trace");
        string defaultId;

        await using (var service = OpenService(OpenStore()))
        {
            var other = await service.CreateSessionAsync(new("scoped", null) { FinancialAccountId = otherAccountId.ToString("D") });
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReserveNativeOrderAsync(request, authority,
                other.SessionId, "cross-account", CancellationToken.None));
            defaultId = await service.ReserveNativeOrderAsync(request, authority, null, "first", CancellationToken.None);
            Assert.Equal(defaultId, await service.ReserveNativeOrderAsync(request, authority, null, "second", CancellationToken.None));
            Assert.Equal(accountId.ToString("D"), service.GetSessionAccountId(defaultId));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CloseSessionAsync(defaultId));
        }

        await using var restarted = OpenService(OpenStore());
        Assert.Equal(defaultId, await restarted.ReserveNativeOrderAsync(request, authority, null, "third", CancellationToken.None));
    }

    [Fact]
    public async Task NativeAccountCurrencyIsVerifiedBeforeRetainingOpeningCapital()
    {
        var store = OpenStore();
        var accountId = Guid.NewGuid();
        await using var service = new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance, store,
            resolveAccountCurrency: static (_, _) => Task.FromResult<string?>("EUR"));

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateSessionAsync(
            new("unsupported-currency", null) { FinancialAccountId = accountId.ToString("D") }));
        Assert.Empty(await store.LoadAllSessionsAsync());
        Assert.Empty(service.GetSessions());
    }

    [Fact]
    public async Task StrategySessionRetryReusesOpeningJournalAfterInterruptedMetadataWrite()
    {
        var store = OpenStore();
        const string runId = "reviewed-paper-run";
        var startedAt = new DateTimeOffset(2026, 1, 15, 14, 30, 0, TimeSpan.Zero);
        var accountId = Guid.NewGuid();
        var request = new CreatePaperSessionDto("reviewed-strategy", null, 1_000m, ["AAPL"])
        {
            FinancialAccountId = accountId.ToString("D")
        };
        var sessionId = PaperSessionPersistenceService.GetStrategyRunSessionId(runId);
        await using (var first = OpenService(store))
            await first.EnsureStrategyRunSessionAsync(runId, startedAt, request);

        var original = Assert.Single(await store.LoadJournalBatchesAsync(sessionId));
        File.Delete(Path.Combine(_root, sessionId, "session.json"));

        await using var restarted = OpenService(OpenStore());
        var recovered = await restarted.EnsureStrategyRunSessionAsync(runId, startedAt, request);
        Assert.Equal(sessionId, recovered.SessionId);
        Assert.Equal(accountId, recovered.FundAccountId);
        Assert.Equal(startedAt, recovered.CreatedAt);
        Assert.Equal(original.ContentHash, Assert.Single(await store.LoadJournalBatchesAsync(sessionId)).ContentHash);
    }

    private JsonlFilePaperSessionStore OpenStore() => new(_root, NullLogger<JsonlFilePaperSessionStore>.Instance);

    private static PaperSessionPersistenceService OpenService(IPaperSessionStore store) =>
        new(NullLogger<PaperSessionPersistenceService>.Instance, store,
            resolveAccountCurrency: static (_, _) => Task.FromResult<string?>("USD"));

    private static ExecutionReport Fill(DateTimeOffset time) => new()
    {
        OrderId = "paper-test-order",
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        ReportType = ExecutionReportType.Fill,
        OrderStatus = OrderStatus.Filled,
        OrderQuantity = 2m,
        FilledQuantity = 2m,
        FillPrice = 25m,
        Commission = 2m,
        Fees = 0.5m,
        SlippageCost = 0.25m,
        Timestamp = time
    };

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
