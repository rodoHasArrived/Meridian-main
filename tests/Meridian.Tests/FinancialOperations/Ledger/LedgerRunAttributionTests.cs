using Meridian.Ledger;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed class LedgerRunAttributionTests
{
    [Theory]
    [InlineData(null, "run-a", false)]
    [InlineData("run-a", null, false)]
    [InlineData("run-a", "run-b", false)]
    [InlineData("run-a", "RUN-A", false)]
    [InlineData("", "", false)]
    [InlineData(" run-a ", " run-a ", false)]
    [InlineData("run-a", "run-a", true)]
    public void TrustedOwnershipRequiresRetainedMarkerAndExactRunLabel(string? admittedRun, string? label, bool expected)
    {
        var tags = new Dictionary<string, string>();
        if (label is not null) tags[LedgerQuery.StrategyRunIdTag] = label;
        if (admittedRun is not null) tags[LedgerQuery.AccountingRunIdTag] = admittedRun;
        Assert.Equal(expected, LedgerRunAttribution.TryGetTrustedRunId(new(Tags: tags), out var runId));
        Assert.Equal(expected ? admittedRun : null, runId);
    }
}
