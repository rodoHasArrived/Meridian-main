using Meridian.Application.Reconciliation;
using Meridian.Infrastructure.Reconciliation;

namespace Meridian.Tests.Reconciliation;

public sealed class StatementImportAndMatchingTests
{
    [Fact]
    public async Task Import_is_idempotent_by_checksum()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "statement.csv");
        await File.WriteAllTextAsync(path, "account,symbol,quantity,price,cashAmount,activityType,tradeDate\nA1,SPY,10,500,5000,BUY,2026-01-02\n");
        var service = new CsvBrokerStatementService(new JsonCanonicalStatementStore(root));
        var req = new BrokerStatementImportRequest("samplebroker", path, new DateOnly(2026, 1, 31));

        await service.ImportAsync(req);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(req));
    }

    [Fact]
    public async Task Validation_fails_for_bad_header()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-test-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "bad.csv");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(path, "foo,bar\n1,2\n");
        var service = new CsvBrokerStatementService(new JsonCanonicalStatementStore(root));
        var result = await service.ValidateAsync(new BrokerStatementImportRequest("samplebroker", path, new DateOnly(2026, 1, 31)));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Matcher_returns_confidence_and_rationale()
    {
        var matcher = new StatementMatchingService();
        var outcomes = matcher.MatchRows([
            new("i1",1,"A1","SPY",1,500,500,"BUY",new DateOnly(2026,1,1),"x"),
            new("i1",2,"A1","LONGSYMBOL",0,0,0,"DIV",new DateOnly(2026,1,1),"y")
        ]);
        Assert.Equal(2, outcomes.Count);
        Assert.Contains(outcomes, o => o.OutcomeType == "unmatched" && o.Confidence < 0.5m);
    }
}
