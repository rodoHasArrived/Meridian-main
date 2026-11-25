using System.Text.Json;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class PaperSessionAccountingProvenanceTests
{
    private const string Owner = "broker-a";

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public async Task SessionRestart_RetainsOpeningAuthorityAndOwner_WithoutInferringHistoricalBasis(
        int evidenceVersion, bool shortPosition)
    {
        var root = TempRoot();
        var store = new JsonlFilePaperSessionStore(root, NullLogger<JsonlFilePaperSessionStore>.Instance);
        var opening = Fill(shortPosition ? OrderSide.Sell : OrderSide.Buy, "opening", 100m);
        string sessionId;
        try
        {
            await using (var service = new PaperSessionPersistenceService(
                             NullLogger<PaperSessionPersistenceService>.Instance, store))
            {
                sessionId = (await service.CreateSessionAsync(new CreatePaperSessionDto("provenance", null, 10_000m))).SessionId;
                if (evidenceVersion == 0)
                    await service.RecordFillAsync(sessionId, opening);
                else
                    await service.RecordFillAsync(sessionId, PaperSessionFillRecord.ComputeCanonicalFillId(opening), opening,
                        accountingEvidenceVersion: 1, financialAccountId: Owner);
                AssertClosingAuthority(service.GetActivePortfolio(sessionId)!, evidenceVersion, shortPosition);
            }

            await using var restarted = new PaperSessionPersistenceService(
                NullLogger<PaperSessionPersistenceService>.Instance, store);
            await restarted.InitialiseAsync();
            AssertClosingAuthority(restarted.GetActivePortfolio(sessionId)!, evidenceVersion, shortPosition);
            var retained = Assert.Single(await store.LoadFillRecordsAsync(sessionId));
            Assert.Equal(evidenceVersion, retained.AccountingEvidenceVersion);
            Assert.Equal(evidenceVersion == 1 ? Owner : null, retained.FinancialAccountId);
            Assert.Equal(PaperSessionFillRecord.ComputeCanonicalHash(opening), retained.CanonicalHash);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SameReportHash_CannotUpgradeUnknownOpeningOrReplaceItsRetainedOwner()
    {
        var root = TempRoot();
        var store = new JsonlFilePaperSessionStore(root, NullLogger<JsonlFilePaperSessionStore>.Instance);
        var legacy = PaperSessionFillRecord.CreateCanonical(Fill(OrderSide.Sell, "claim", 100m));
        var current = legacy with { AccountingEvidenceVersion = 1, FinancialAccountId = Owner };
        try
        {
            Assert.Equal(PaperSessionFillAppendStatus.Added, (await store.TryAppendFillAsync("legacy", legacy)).Status);
            Assert.Equal(PaperSessionFillAppendStatus.Conflict, (await store.TryAppendFillAsync("legacy", current)).Status);
            Assert.Equal(PaperSessionFillAppendStatus.Added, (await store.TryAppendFillAsync("current", current)).Status);
            Assert.Equal(PaperSessionFillAppendStatus.Conflict,
                (await store.TryAppendFillAsync("current", current with { FinancialAccountId = "broker-b" })).Status);
            Assert.Equal(PaperSessionFillAppendStatus.ExistingSame,
                (await store.TryAppendFillAsync("current", current with { FinancialAccountId = "BROKER-A" })).Status);
            Assert.Equal(current, Assert.Single(await store.LoadFillRecordsAsync("current")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnvelopeProvenance_PreservesReportIdentityHashAndLegacySerializedShape()
    {
        var legacy = PaperSessionFillRecord.CreateCanonical(Fill(OrderSide.Sell, "hash", 100m));
        var current = legacy with { AccountingEvidenceVersion = 1, FinancialAccountId = Owner };
        var legacyJson = JsonSerializer.Serialize(legacy, ExecutionJsonContext.Default.PaperSessionFillRecord);
        using var legacyDocument = JsonDocument.Parse(legacyJson);
        Assert.False(legacyDocument.RootElement.TryGetProperty("accountingEvidenceVersion", out _));
        Assert.False(legacyDocument.RootElement.TryGetProperty("financialAccountId", out _));
        var restored = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(current, ExecutionJsonContext.Default.PaperSessionFillRecord),
            ExecutionJsonContext.Default.PaperSessionFillRecord)!.Validate();
        Assert.Equal(current, restored);
        Assert.Equal(legacy.FillId, restored.FillId);
        Assert.Equal(legacy.CanonicalHash, restored.CanonicalHash);
    }

    private static void AssertClosingAuthority(PaperTradingPortfolio portfolio, int version, bool shortPosition)
    {
        var closing = portfolio.ApplyFillWithAccountingEvidence(
            Fill(shortPosition ? OrderSide.Buy : OrderSide.Sell, "closing", 110m), ownerAccountId: version == 1 ? Owner : null);
        Assert.Equal(shortPosition ? -100m : 100m, closing.RealizedPnl);
        if (version == 0)
            Assert.Null(closing.Evidence);
        else
        {
            var evidence = Assert.IsType<TradeFillAccountingEvidence>(closing.Evidence);
            Assert.Equal(Owner, evidence.FinancialAccountId);
            Assert.Equal(shortPosition ? SecurityTradePostingKind.CoverShort : SecurityTradePostingKind.CloseLong,
                evidence.Closing!.Kind);
            Assert.Equal(1_000m, evidence.Closing.CarryingValue);
        }
    }

    private static ExecutionReport Fill(OrderSide side, string id, decimal price) => new()
    {
        OrderId = id,
        ClientOrderId = id,
        Symbol = "AAPL",
        Side = side,
        ReportType = ExecutionReportType.Fill,
        OrderStatus = OrderStatus.Filled,
        OrderQuantity = 10m,
        FilledQuantity = 10m,
        FillPrice = price,
        Timestamp = new DateTimeOffset(2026, 10, 1, 14, 30, 0, TimeSpan.Zero)
    };

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-session-provenance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
