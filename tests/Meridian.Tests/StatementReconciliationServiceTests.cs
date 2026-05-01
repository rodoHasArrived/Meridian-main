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
        Assert.Equal("2", result.Cases[0].RowId);
    }
}
