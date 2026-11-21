using FluentAssertions;
using Meridian.Execution.Events;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Execution;

public sealed partial class PaperFinancingPostingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task FinancingAndFillProvenance_ReplayTogetherWithoutMovingOpeningCapital(int evidenceVersion)
    {
        var root = TempRoot();
        var target = new RetainedTarget();
        var store = new JsonlFilePaperSessionStore(Path.Combine(root, "sessions"), NullLogger<JsonlFilePaperSessionStore>.Instance);
        var sessionId = $"financing-provenance-{evidenceVersion}";
        var createdAt = Charge().OccurredAt.AddDays(-1);
        await store.SaveSessionMetadataAsync(new(sessionId, "strategy-1", "Paper", 1_000m, createdAt,
            null, true, [], AccountingContext: Scope, FinancialAccountId: PaperTradingPortfolio.DefaultAccountId));
        var opening = new ExecutionReport
        {
            OrderId = "opening-with-financing",
            Symbol = "AAPL",
            ReportType = ExecutionReportType.Fill,
            Side = OrderSide.Buy,
            OrderStatus = Meridian.Execution.Sdk.OrderStatus.Filled,
            OrderQuantity = 1m,
            FilledQuantity = 1m,
            FillPrice = 100m,
            Timestamp = Charge().OccurredAt.AddHours(1)
        };

        void AssertProjection(PaperSessionPersistenceService sessions)
        {
            sessions.GetSession(sessionId)!.Portfolio!.Cash.Should().Be(887.50m);
            var ledger = sessions.GetLedger(sessionId)!;
            ledger.GetBalance(LedgerAccounts.MarginInterestExpense).Should().Be(12.50m);
            ledger.Journal.Single(entry => entry.Description == "Initial capital — paper trading session")
                .Timestamp.Should().Be(createdAt);
            var portfolio = sessions.GetActivePortfolio(sessionId)!;
            portfolio.CheckLedgerIntegrity().Should().BeEmpty();
            var closing = portfolio.ApplyFillWithAccountingEvidence(opening with
            {
                OrderId = "closing-after-financing",
                Side = OrderSide.Sell,
                FillPrice = 110m,
                Timestamp = opening.Timestamp.AddHours(1)
            }, ownerAccountId: evidenceVersion == 1 ? "broker-a" : null);
            closing.RealizedPnl.Should().Be(10m);
            if (evidenceVersion == 0)
                closing.Evidence.Should().BeNull("unknown historical opening authority cannot be upgraded by replay");
            else
            {
                closing.Evidence.Should().NotBeNull();
                closing.Evidence!.FinancialAccountId.Should().Be("broker-a");
                closing.Evidence.Closing!.CarryingValue.Should().Be(100m);
            }
        }

        await using (var financing = CreatePostingService(root, Scope, target))
        await using (var sessions = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance,
            store, financing: financing))
        {
            await sessions.RecordFinancingAsync(Charge(sessionId));
            await sessions.RecordFillAsync(sessionId, PaperSessionFillRecord.ComputeCanonicalFillId(opening), opening,
                evidenceVersion, evidenceVersion == 1 ? "broker-a" : null);
            AssertProjection(sessions);
            (await sessions.ReplaySessionAsync(sessionId))!.Cash.Should().Be(887.50m);
        }

        await using var recoveredFinancing = CreatePostingService(root, Scope, target);
        await using var recoveredSessions = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance,
            store, financing: recoveredFinancing);
        await recoveredSessions.InitialiseAsync();
        AssertProjection(recoveredSessions);
        (await recoveredSessions.ReplaySessionAsync(sessionId))!.Cash.Should().Be(887.50m);
        var retained = (await store.LoadFillRecordsAsync(sessionId)).Should().ContainSingle().Which;
        retained.AccountingEvidenceVersion.Should().Be(evidenceVersion);
        retained.FinancialAccountId.Should().Be(evidenceVersion == 1 ? "broker-a" : null);
        retained.CanonicalHash.Should().Be(PaperSessionFillRecord.ComputeCanonicalHash(opening));
        (await recoveredSessions.CloseSessionAsync(sessionId)).Should().BeTrue();
        target.AppendCount.Should().Be(1);
        AssertNative(target.Records.Values.Single().Entry, "USD");
    }

    [Fact]
    public void FinancingProjection_UsesTheRetainedDefaultAccountInAMultiAccountBook()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(new AccountDefinition[]
        {
            new(PaperTradingPortfolio.DefaultAccountId, "Default account", AccountKind.Brokerage, 1_000m),
            new("second", "Second account", AccountKind.Brokerage, 500m)
        }, ledger);

        portfolio.ApplyFinancingCharge(Charge()).Should().BeTrue();

        portfolio.CheckLedgerIntegrity().Should().BeEmpty();
        ledger.GetBalance(LedgerAccounts.CashFor(PaperTradingPortfolio.DefaultAccountId)).Should().Be(987.50m);
        ledger.GetBalance(LedgerAccounts.CashFor("second")).Should().Be(500m);
        ledger.GetBalance(LedgerAccounts.Cash).Should().Be(0m);
        ledger.Journal.Single(entry => entry.Metadata.ActivityType == "margin-interest").Lines
            .Should().OnlyContain(line => line.Account.FinancialAccountId == PaperTradingPortfolio.DefaultAccountId);
        portfolio.ApplyFinancingCharge(Charge()).Should().BeFalse();
    }
}
