using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Reconciliation;

namespace Meridian.Tests.Reconciliation;

public sealed class StatementMatchingToleranceTests
{
    [Fact]
    public void MatchRows_ProducesDeterministicMismatchCodes()
    {
        var rows = new[]
        {
            new CanonicalStatementRow("i",1,"acct","",0m,0m,0m,"position",new DateOnly(2026,1,1),"a"),
            new CanonicalStatementRow("i",2,"acct","SPY",0m,0m,5m,"cash",new DateOnly(2026,1,1),"b"),
            new CanonicalStatementRow("i",3,"acct","SPY",1m,100m,0m,"trade",new DateOnly(2026,1,1),"c"),
        };

        var outcomes = new StatementMatchingService().MatchRows(rows);

        Assert.Equal("POS_SYMBOL_MISSING", outcomes[0].OutcomeType);
        Assert.Equal("CASH_TOLERANCE_BREACH", outcomes[1].OutcomeType);
        Assert.Equal("TXN_TOLERANCE_BREACH", outcomes[2].OutcomeType);
    }

    [Fact]
    public void MatchRows_WhenToleranceAllowsMatch_EmbedsProfileVersionAndRuleIdInExplanation()
    {
        var tolerance = StatementMatchingTolerance.Default with
        {
            ToleranceProfileId = "ops-cash-profile",
            ToleranceProfileVersion = 4,
            CashToleranceRuleId = "cash-absolute-ops-v4",
            BasisPointCashTolerance = 10m
        };
        var rows = new[]
        {
            new CanonicalStatementRow("i", 1, "acct", "SPY", 0m, 0m, 0.005m, "cash", new DateOnly(2026, 1, 1), "cash-row")
        };

        var outcomes = new StatementMatchingService().MatchRows(rows, tolerance);

        Assert.Collection(outcomes, outcome =>
        {
            Assert.Equal("matched", outcome.OutcomeType);
            Assert.Equal("ops-cash-profile", outcome.ToleranceProfileId);
            Assert.Equal(4, outcome.ToleranceProfileVersion);
            Assert.Equal("cash-absolute-ops-v4", outcome.ToleranceRuleId);
            Assert.Contains("cash-absolute-ops-v4", outcome.Rationale, StringComparison.Ordinal);
        });
    }

}
