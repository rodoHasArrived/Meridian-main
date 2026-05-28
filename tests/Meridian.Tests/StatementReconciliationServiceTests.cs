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
