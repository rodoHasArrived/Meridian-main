using Meridian.Application.Reconciliation;
using Meridian.Domain.Reconciliation;

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
        Assert.Equal("case:2", result.Cases[0].CaseId);
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
            Assert.Equal(3, result.RowCount);
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
