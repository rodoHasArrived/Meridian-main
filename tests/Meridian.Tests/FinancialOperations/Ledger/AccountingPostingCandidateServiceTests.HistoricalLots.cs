using FluentAssertions;
using Meridian.Ledger;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed partial class AccountingPostingCandidateServiceTests
{
    [Fact]
    public async Task BuildCandidateAsync_FactorPaydownWithoutHistoricalQuantityEvidenceBlocksDraft()
    {
        var harness = await CreateAuthoritativeFactorHarnessAsync();
        harness.TaxLots.HistoryReadFailure = new LedgerValidationException("Missing retained disposal date.");
        var result = await harness.Service.BuildCandidateAsync(harness.Request);
        result.HasBlockingIssues.Should().BeTrue();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.instrument-lot-history-unavailable" && issue.BlocksCandidate);
    }
}
