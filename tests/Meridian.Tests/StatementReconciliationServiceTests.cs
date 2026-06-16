using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;

namespace Meridian.Tests;

public sealed class StatementReconciliationServiceTests
{
    [Fact]
    public void Fingerprint_IsDeterministic()
    {
        var a = DeterministicFingerprint.Compute("abc");
        var b = DeterministicFingerprint.Compute("abc");
        Assert.Equal(a, b);
    }

    [Fact]
    public void MatchRows_ProducesMatchAndCaseBranches()
    {
        var svc = new StatementReconciliationService();
        var rows = new[]
        {
            new NormalizedStatementRow("1", StatementRowKind.Position, "AAPL", 10, 0, DateTimeOffset.UtcNow, "USD", "f1", new Dictionary<string,string>()),
            new NormalizedStatementRow("2", StatementRowKind.CashBalance, string.Empty, 0, 100, DateTimeOffset.UtcNow, "USD", "f2", new Dictionary<string,string>())
        };

        var result = svc.MatchRows(rows);
        Assert.Single(result.Matches);
        Assert.Single(result.Cases);
        var reconciliationCase = result.Cases[0];
        Assert.Equal("case:2", reconciliationCase.CaseId);
        Assert.Equal("fund-ops", reconciliationCase.Owner);
        Assert.Equal("NeedsInvestigation", reconciliationCase.Disposition);
        Assert.Equal(0, reconciliationCase.AgingDays);
        Assert.NotNull(reconciliationCase.DueAtUtc);
        var attachment = Assert.Single(reconciliationCase.Attachments);
        Assert.Equal("ExternalStatementRow", attachment.EvidenceKind);
        Assert.Equal("f2", attachment.ContentHash);
        Assert.NotNull(reconciliationCase.BreakExplanation);
        Assert.Contains("cash", reconciliationCase.BreakExplanation.ProbableCause, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Meridian ledger", reconciliationCase.BreakExplanation.SourceSystems);
        Assert.Contains("cash ledger", reconciliationCase.BreakExplanation.LedgerImpact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Compare the statement cash balance", reconciliationCase.BreakExplanation.SuggestedNextAction);
    }

    [Fact]
    public async Task ImportAsync_Returns_Typed_Normalized_Broker_Collections_With_Source_Trace()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(filePath,
            [
                "account,symbol,quantity,price,cashAmount,activityType,tradeDate,accountId,externalAccountId,securityId,unresolvedIdentifier,currency,marketValue,settlementDate,amount,feesCommission,externalReference",
                "EXT-A1,SPY,10,500,0,position,2026-05-29,ACC-1,EXT-A1,SEC-SPY,,USD,5000,2026-06-01,5000,0,POS-1",
                "EXT-A1,,0,0,125.25,cash,2026-05-29,ACC-1,EXT-A1,,,USD,0,2026-05-29,125.25,0,CASH-1",
                "EXT-A1,QQQ,2,400,0,buy,2026-05-30,ACC-1,EXT-A1,,QQQ,USD,800,2026-06-02,800,1.25,TXN-1"
            ]);

            var result = await svc.ImportAsync("broker", filePath, CancellationToken.None);

            Assert.Equal(3, result.RowCount);
            Assert.Single(result.Positions);
            Assert.Single(result.CashBalances);
            Assert.Single(result.Transactions);
            Assert.Equal("ACC-1", result.Positions[0].AccountId);
            Assert.Equal("EXT-A1", result.Positions[0].ExternalAccountId);
            Assert.Equal("SEC-SPY", result.Positions[0].SecurityId);
            Assert.Equal(5000m, result.Positions[0].MarketValue);
            Assert.Equal(new DateOnly(2026, 6, 1), result.Positions[0].SettlementDate);
            Assert.Equal(result.ImportId, result.Positions[0].StatementRunId);
            Assert.False(string.IsNullOrWhiteSpace(result.Positions[0].SourceRowHash));
            Assert.Equal(125.25m, result.CashBalances[0].Amount);
            Assert.Equal(result.ImportId, result.CashBalances[0].StatementRunId);
            Assert.False(string.IsNullOrWhiteSpace(result.CashBalances[0].SourceRowHash));
            Assert.Equal("buy", result.Transactions[0].TransactionType);
            Assert.Equal(1.25m, result.Transactions[0].FeesCommission);
            Assert.Equal("TXN-1", result.Transactions[0].ExternalReference);
            Assert.Equal(result.ImportId, result.Transactions[0].StatementRunId);
            Assert.False(string.IsNullOrWhiteSpace(result.Transactions[0].SourceRowHash));
            Assert.Equal(result.ImportId, result.Securities[0].StatementRunId);
            Assert.False(string.IsNullOrWhiteSpace(result.Securities[0].SourceRowHash));
            Assert.All(result.SourceRows, row =>
            {
                Assert.Equal(result.ImportId, row.StatementRunId);
                Assert.False(string.IsNullOrWhiteSpace(row.SourceRowHash));
                Assert.True(row.RawSnapshot.ContainsKey("rawLine"));
            });
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ImportAsync_Observes_Cancellation_Before_Normalization()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(filePath,
            [
                "account,symbol,quantity,price,cashAmount,activityType,tradeDate",
                "A1,SPY,10,500,0,position,2026-05-29"
            ]);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() => svc.ImportAsync("broker", filePath, cts.Token));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ValidateAsync_ThrowsWhenLocalFileMissing()
    {
        var svc = new StatementReconciliationService();

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            svc.ValidateAsync("local", "/tmp/does-not-exist.csv", CancellationToken.None));
    }

    [Fact]
    public async Task ImportAsync_CountsRowsForLocalFile()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(filePath, ["header", "row1", "row2"]);
            var result = await svc.ImportAsync("local", filePath, CancellationToken.None);
            Assert.Equal(2, result.RowCount);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void StatementMappingProfileRegistry_Exposes_Canonical_And_Sample_Broker_Profiles()
    {
        var registry = StatementMappingProfileRegistry.Defaults;

        var canonical = registry.Resolve(StatementMappingProfileRegistry.CanonicalCsvV1ProfileId);
        var sampleBroker = registry.Resolve(StatementMappingProfileRegistry.SampleBrokerCsvV1ProfileId);

        Assert.Equal("account", canonical.FindField(StatementCanonicalField.Account)?.SourceColumn);
        Assert.Equal("symbol", canonical.FindField(StatementCanonicalField.SecurityIdentifier)?.SourceColumn);
        Assert.Equal("cashAmount", canonical.FindField(StatementCanonicalField.CashAmount)?.SourceColumn);
        Assert.Equal("BrokerAccount", sampleBroker.FindField(StatementCanonicalField.Account)?.SourceColumn);
        Assert.Equal("Ticker", sampleBroker.FindField(StatementCanonicalField.SecurityIdentifier)?.SourceColumn);
        Assert.Equal("Commission", sampleBroker.FindField(StatementCanonicalField.FeesCommission)?.SourceColumn);
        Assert.Equal("trade", sampleBroker.MapActivityType("BUY"));
        Assert.Equal("dividend", sampleBroker.MapActivityType("DIV"));
    }

    [Fact]
    public async Task ReconcileAsync_Uses_Selected_SampleBrokerMappingProfile()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(filePath,
            [
                "BrokerAccount,Ticker,Units,ExecutionPrice,NetCash,TxnCode,TradeDate,SettleDate,CCY,Commission,BrokerTransactionId",
                "BRK-1,MSFT,12,410,0,POS,2026-05-27,2026-05-29,EUR,0,EXT-1",
                "BRK-1,MSFT,0,0,18.25,DIV,2026-05-28,2026-05-30,EUR,0,EXT-2"
            ]);

            var validation = await svc.ValidateAsync("broker", filePath, StatementMappingProfileRegistry.SampleBrokerCsvV1ProfileId, CancellationToken.None);
            var result = await svc.ReconcileAsync("broker", filePath, StatementMappingProfileRegistry.SampleBrokerCsvV1ProfileId, CancellationToken.None);
            var intake = await svc.CreateExternalStatementCasesAsync("broker", filePath, StatementMappingProfileRegistry.SampleBrokerCsvV1ProfileId, CancellationToken.None);

            Assert.Contains(StatementMappingProfileRegistry.SampleBrokerCsvV1ProfileId, validation);
            Assert.Equal(1, result.MatchCount);
            Assert.Equal(1, result.UnresolvedCount);
            Assert.Equal(2, intake.RowCount);
            Assert.Single(intake.Cases);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ValidateAsync_Throws_When_SelectedMappingProfile_RequiredColumnIsMissing()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(filePath,
            [
                "BrokerAccount,Units,ExecutionPrice,NetCash,TxnCode,TradeDate",
                "BRK-1,12,410,0,POS,2026-05-27"
            ]);

            var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
                svc.ValidateAsync("broker", filePath, StatementMappingProfileRegistry.SampleBrokerCsvV1ProfileId, CancellationToken.None));

            Assert.Contains("sample-broker-csv-v1", ex.Message);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

}
