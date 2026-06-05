using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation;

namespace Meridian.Tests.Reconciliation;

public sealed class BrokerCustodianMatchingPipelineTests
{
    [Fact]
    public void Match_WhenRecordsAlignExactly_ShouldAutoMatchWithExactTier()
    {
        var now = new DateTimeOffset(2026, 5, 28, 10, 0, 0, TimeSpan.Zero);
        var broker = CreateRecord("broker-1", CanonicalReconciliationSourceKind.Broker, quantity: 100m, price: 10m, marketValue: 1_000m, cash: 0m);
        var custodian = CreateRecord("custodian-1", CanonicalReconciliationSourceKind.Custodian, quantity: 100m, price: 10m, marketValue: 1_000m, cash: 0m);

        var decisions = new BrokerCustodianMatchingPipeline().Match("run-1", [broker], [custodian], now, "ops@example.test");

        decisions.Should().ContainSingle();
        decisions[0].Status.Should().Be(CanonicalReconciliationDecisionStatus.AutoMatched);
        decisions[0].Tier.Should().Be(ReconciliationMatchTier.Exact);
        decisions[0].Confidence.Should().Be(1.00m);
        decisions[0].RuleId.Should().Be("exact-position-cash-transaction-v1");
    }

    [Fact]
    public void Match_WhenRecordsAlignButCurrencyDiffers_ShouldLeaveBothRowsUnmatched()
    {
        var now = new DateTimeOffset(2026, 5, 28, 10, 0, 0, TimeSpan.Zero);
        var broker = CreateRecord("broker-1", CanonicalReconciliationSourceKind.Broker, quantity: 100m, price: 10m, marketValue: 1_000m, cash: 0m, currency: "USD");
        var custodian = CreateRecord("custodian-1", CanonicalReconciliationSourceKind.Custodian, quantity: 100m, price: 10m, marketValue: 1_000m, cash: 0m, currency: "EUR");

        var decisions = new BrokerCustodianMatchingPipeline().Match("run-1", [broker], [custodian], now, "ops@example.test");

        decisions.Should().HaveCount(2);
        decisions.Should().OnlyContain(decision => decision.Status == CanonicalReconciliationDecisionStatus.UnresolvedBreak);
        decisions.Should().OnlyContain(decision => decision.RightRecordId == null);
    }

    [Fact]
    public void Match_WhenOnlyHeuristicRuleFits_ShouldProposeDecisionForOperatorReview()
    {
        var now = new DateTimeOffset(2026, 5, 28, 10, 0, 0, TimeSpan.Zero);
        var broker = CreateRecord("broker-1", CanonicalReconciliationSourceKind.Broker, quantity: 100m, price: 10m, marketValue: 1_000m, cash: 0m, reference: "BRK-1");
        var custodian = CreateRecord("custodian-1", CanonicalReconciliationSourceKind.Custodian, quantity: 100.5m, price: 10.2m, marketValue: 1_020m, cash: 0m, reference: "CST-1");

        var decisions = new BrokerCustodianMatchingPipeline().Match("run-1", [broker], [custodian], now, "ops@example.test");

        decisions.Should().ContainSingle();
        decisions[0].Status.Should().Be(CanonicalReconciliationDecisionStatus.Proposed);
        decisions[0].Tier.Should().Be(ReconciliationMatchTier.Heuristic);
        decisions[0].Confidence.Should().BeGreaterThanOrEqualTo(0.70m);
    }

    [Fact]
    public async Task FileReconciliationDecisionJournal_ShouldAppendImmutableDecisionsAndResolutionHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-recon-journal-" + Guid.NewGuid().ToString("N"));
        try
        {
            var now = new DateTimeOffset(2026, 5, 28, 10, 0, 0, TimeSpan.Zero);
            var decision = new BrokerCustodianMatchingPipeline().Match(
                "run-1",
                [CreateRecord("broker-1", CanonicalReconciliationSourceKind.Broker, quantity: 100m, price: 10m, marketValue: 1_000m, cash: 0m)],
                [],
                now,
                "ops@example.test").Single();
            var history = new ReconciliationResolutionHistoryEntry(
                "history-1",
                decision.DecisionId,
                decision.Status.ToString(),
                CanonicalReconciliationDecisionStatus.ManuallyResolved.ToString(),
                "ops@example.test",
                "Matched to custodian correction ticket.",
                now.AddMinutes(5),
                decision.EvidenceReferences);
            var journal = new FileReconciliationDecisionJournal(root);

            await journal.AppendDecisionAsync(decision);
            await journal.AppendHistoryAsync(history);

            (await journal.ListDecisionsAsync("run-1")).Should().ContainSingle().Which.Status.Should().Be(CanonicalReconciliationDecisionStatus.UnresolvedBreak);
            (await journal.ListHistoryAsync(decision.DecisionId)).Should().ContainSingle().Which.ToStatus.Should().Be("ManuallyResolved");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FileReconciliationDecisionJournal_WhenInterruptedTempFileExists_ShouldKeepCommittedJsonlReadable()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-recon-journal-" + Guid.NewGuid().ToString("N"));
        try
        {
            var now = new DateTimeOffset(2026, 5, 28, 10, 0, 0, TimeSpan.Zero);
            var journal = new FileReconciliationDecisionJournal(root);
            var firstDecision = new BrokerCustodianMatchingPipeline().Match(
                "run-1",
                [CreateRecord("broker-1", CanonicalReconciliationSourceKind.Broker, quantity: 100m, price: 10m, marketValue: 1_000m, cash: 0m)],
                [],
                now,
                "ops@example.test").Single();
            var secondDecision = new BrokerCustodianMatchingPipeline().Match(
                "run-1",
                [CreateRecord("broker-2", CanonicalReconciliationSourceKind.Broker, quantity: 50m, price: 20m, marketValue: 1_000m, cash: 0m)],
                [],
                now,
                "ops@example.test").Single();

            await journal.AppendDecisionAsync(firstDecision);
            var decisionPath = Path.Combine(root, "reconciliation", "decision-journal", "runs", "run-1", "decisions.jsonl");
            await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(decisionPath)!, ".decisions.jsonl.interrupted.tmp"), "partial-json");

            await journal.AppendDecisionAsync(secondDecision);

            var decisions = await journal.ListDecisionsAsync("run-1");
            decisions.Select(static decision => decision.LeftRecordId).Should().Equal("broker-1", "broker-2");
            File.ReadLines(decisionPath).Should().HaveCount(2);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static CanonicalReconciliationRecord CreateRecord(
        string recordId,
        CanonicalReconciliationSourceKind sourceKind,
        decimal quantity,
        decimal price,
        decimal marketValue,
        decimal cash,
        string reference = "EXT-1",
        string currency = "USD")
    {
        var descriptor = new BrokerCustodianFeedDescriptor(
            FeedId: $"feed-{sourceKind}",
            SourceKind: sourceKind,
            SourceSystem: sourceKind.ToString(),
            AccountId: "fund-account-1",
            ExternalAccountId: "external-account-1",
            BusinessDate: new DateOnly(2026, 5, 28),
            BaseCurrency: currency,
            CapturedAtUtc: new DateTimeOffset(2026, 5, 28, 9, 0, 0, TimeSpan.Zero),
            ContentHash: "HASH",
            AdapterId: "test-adapter",
            AdapterVersion: "1");

        return new CanonicalReconciliationRecord(
            RecordId: recordId,
            Feed: descriptor,
            RecordKind: CanonicalReconciliationRecordKind.Position,
            InstrumentId: "SEC-AAPL",
            Symbol: "AAPL",
            Quantity: quantity,
            Price: price,
            MarketValue: marketValue,
            CashAmount: cash,
            Currency: currency,
            TradeDate: new DateOnly(2026, 5, 28),
            SettlementDate: new DateOnly(2026, 5, 29),
            ActivityType: "Position",
            ExternalReference: reference,
            Evidence: new Dictionary<string, string> { ["sourceRow"] = recordId });
    }
}
