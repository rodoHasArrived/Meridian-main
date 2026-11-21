using FluentAssertions;
using Meridian.Execution.Events;
using Meridian.Execution.Models;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Execution;

public sealed class CanonicalPaperPositionAttributionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("different-run")]
    public void LegacyOrConflictingRunLabelsCannotGainTrustedOwnershipOnRestart(string? trustedRun)
    {
        var history = new History { RetainedTrustedRun = trustedRun, UseExplicitTrustedRun = true };
        history.Acquire("claimed-run", 5m);
        var result = history.Recover();
        result.RunId.Should().BeNull();
        var position = history.RestorePortfolio(result).CapturePositionAccountingEvidence().Positions.Single();
        position.StrategyRunId.Should().BeNull();
        position.Quantity.Should().BeNull();
        position.MonetaryBasis.Should().BeNull();
        position.CoverageFailure.Should().Contain("run ownership");
    }

    [Fact]
    public void SameRunPartialReliefRetainsExactCurrentCycleAttribution()
    {
        var history = new History();
        var lot = history.Acquire("run-a", 10m);
        history.Dispose("run-a", lot, 3m);
        var result = history.Recover();
        result.RunId.Should().Be("run-a");
        result.FillIds.Should().HaveCount(2, "retained relief is part of the current basis history");
    }

    [Fact]
    public void RestartAfterOtherRunPartialReliefDoesNotRegainAcquisitionRunCertainty()
    {
        var history = new History();
        var lot = history.Acquire("run-a", 10m);
        history.Dispose("run-b", lot, 2m);
        var result = history.Recover();
        result.RunId.Should().BeNull();
        var snapshot = history.RestorePortfolio(result).CapturePositionAccountingEvidence();
        snapshot.Positions.Single().StrategyRunId.Should().BeNull();
        snapshot.Positions.Single().Quantity.Should().BeNull();
        snapshot.Positions.Single().MonetaryBasis.Should().BeNull();
        snapshot.Positions.Single().CoverageFailure.Should().Contain("run ownership");
        snapshot.Accounts.Single().Positions.Single().ExactQuantity.Should().Be(8m);
    }

    [Fact]
    public void AClosedFirstLotDoesNotEraseMixedOwnershipWhileThePositionRemainsOpen()
    {
        var history = new History();
        var first = history.Acquire("run-a", 5m);
        history.Acquire("run-b", 5m);
        history.Dispose("run-a", first, 5m);
        history.Recover().RunId.Should().BeNull("the first lot closing is not a flat position boundary");
    }

    [Fact]
    public void ProvenFlatBoundaryStartsAnIndependentLaterRunCycle()
    {
        var history = new History();
        var first = history.Acquire("old-run", 5m);
        history.Dispose("other-run", first, 5m);
        history.Acquire("new-run", 3m);
        var result = history.Recover();
        result.RunId.Should().Be("new-run");
        result.FillIds.Should().ContainSingle();
        history.RestorePortfolio(result).CapturePositionAccountingEvidence().Positions.Single().Quantity.Should().Be(3m);
    }

    [Theory]
    [InlineData("missing-receipt")]
    [InlineData("missing-mutations")]
    [InlineData("duplicate-sequence")]
    public void IncompleteQuantityHistoryCannotAttestRunOwnership(string defect)
    {
        var history = new History();
        var first = history.Acquire("run-a", 5m);
        history.Dispose("run-a", first, 1m);
        if (defect == "missing-receipt") history.Receipts.Remove(history.Receipts.Keys.First());
        if (defect == "missing-mutations")
        {
            var key = history.Receipts.Keys.First();
            history.Receipts[key] = history.Receipts[key] with { Mutations = [] };
        }
        if (defect == "duplicate-sequence") history.Journals[1] = history.Journals[1] with { GlobalSequence = history.Journals[0].GlobalSequence };
        history.Recover().RunId.Should().BeNull();
    }

    private sealed class History
    {
        private const string AccountId = "native-account";
        private readonly Guid _bookId = Guid.NewGuid();
        private readonly Guid _securityId = Guid.NewGuid();
        private readonly Guid _positionId = Guid.NewGuid();
        private readonly Guid _periodId = Guid.NewGuid();
        private readonly LedgerAccount _account = LedgerAccounts.Securities("TEST", AccountId);
        private readonly Dictionary<Guid, LedgerTaxLotRecord> _lots = [];
        public List<LedgerJournalEntryRecord> Journals { get; } = [];
        public Dictionary<Guid, AtomicTaxLotJournalResult> Receipts { get; } = [];
        public bool UseExplicitTrustedRun { get; init; }
        public string? RetainedTrustedRun { get; init; }

        public Guid Acquire(string runId, decimal quantity)
        {
            var lotId = Guid.NewGuid();
            Apply(runId, lotId, quantity);
            return lotId;
        }
        public void Dispose(string runId, Guid lotId, decimal quantity) => Apply(runId, lotId, -quantity);

        private void Apply(string runId, Guid lotId, decimal delta)
        {
            var batchId = Guid.NewGuid();
            var journalId = Guid.NewGuid();
            var sourceId = Guid.NewGuid();
            var fillId = Guid.NewGuid();
            var time = DateTimeOffset.UtcNow.AddMinutes(-1).AddSeconds(Journals.Count);
            _lots.TryGetValue(lotId, out var before);
            var afterQuantity = (before?.OpenQuantity ?? 0m) + delta;
            var after = new LedgerTaxLotRecord(lotId, _bookId, _account, lotId.ToString("D"), DateOnly.FromDateTime(time.UtcDateTime),
                before?.OriginalQuantity ?? delta, afterQuantity, 100m, "USD", before?.CreatedAt ?? time, time,
                SourceJournalEntryId: before?.SourceJournalEntryId ?? journalId, Version: (before?.Version ?? 0) + 1,
                OriginatingMutationBatchId: before?.OriginatingMutationBatchId ?? batchId, LastMutationBatchId: batchId,
                SecurityId: _securityId, BookPositionId: _positionId);
            var description = "Synthetic retained canonical trade";
            var cash = LedgerAccounts.CashFor(AccountId);
            var amount = Math.Abs(delta) * 100m;
            var lines = delta > 0m
                ? new[] { new LedgerEntry(Guid.NewGuid(), journalId, time, _account, amount, 0m, description), new LedgerEntry(Guid.NewGuid(), journalId, time, cash, 0m, amount, description) }
                : [new LedgerEntry(Guid.NewGuid(), journalId, time, _account, 0m, amount, description), new LedgerEntry(Guid.NewGuid(), journalId, time, cash, amount, 0m, description)];
            var tags = new Dictionary<string, string>
            { ["sourceEventType"] = "execution.trade-fill", [LedgerQuery.StrategyRunIdTag] = runId };
            var trustedRun = UseExplicitTrustedRun ? RetainedTrustedRun : runId;
            if (trustedRun is not null) tags[LedgerQuery.AccountingRunIdTag] = trustedRun;
            var journal = new JournalEntry(journalId, time, description, lines,
                new JournalEntryMetadata(ActivityType: "execution.trade-fill", Symbol: "TEST", SecurityId: _securityId,
                    FillId: fillId, FinancialAccountId: AccountId, Tags: tags));
            var record = new LedgerJournalEntryRecord(journal, Guid.NewGuid(), _periodId, null, fillId, Journals.Count + 1, time);
            var kind = delta > 0m ? AtomicTaxLotMutationKind.Acquisition : AtomicTaxLotMutationKind.Disposal;
            var mutation = new LedgerTaxLotMutationRecord(Guid.NewGuid(), batchId, kind, lotId, after.LotId, 0,
                before?.OpenQuantity ?? 0m, delta, afterQuantity, 100m, amount, before?.Version ?? 0, after.Version,
                "synthetic-evidence", journalId, sourceId, [], time, before, after, SecurityId: _securityId, BookPositionId: _positionId);
            Receipts.Add(batchId, new(batchId, kind, "synthetic", false, record, [after], [mutation], []));
            Journals.Add(record);
            _lots[lotId] = after;
        }

        public CanonicalPaperRunAttribution Recover() => CanonicalPaperPositionAttribution.Recover(
            new(Journals, []), _account, _securityId, _positionId, _lots.Values.Where(static lot => lot.OpenQuantity > 0m).ToArray(), Receipts);

        public PaperTradingPortfolio RestorePortfolio(CanonicalPaperRunAttribution attribution)
        {
            var portfolio = new PaperTradingPortfolio([new AccountDefinition(AccountId, "Native account", AccountKind.Brokerage, 0m)]);
            portfolio.RequireCanonicalRecovery(null);
            var open = _lots.Values.Where(static lot => lot.OpenQuantity > 0m).ToArray();
            portfolio.RestoreCanonicalState(AccountId, 1000m, 0m,
                [new("TEST", open.Select(static lot => new CanonicalPaperLotRecovery(lot.LotId, lot.OpenQuantity, lot.UnitCost,
                    lot.OpenQuantity * lot.UnitCost, lot.CreatedAt, lot.TaxLotRecordId)).ToArray(), 100m, DateTimeOffset.UtcNow, "RetainedExecutionFill")
                { AccountingSecurityId = _securityId, AccountingRunId = attribution.RunId, AccountingFillIds = attribution.FillIds }], "synthetic/v2");
            portfolio.BindPositionAccountingEvidenceScope(AccountId, "USD");
            return portfolio;
        }
    }
}
