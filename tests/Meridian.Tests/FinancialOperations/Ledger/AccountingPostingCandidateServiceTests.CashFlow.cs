using FluentAssertions;
using Meridian.Ledger;
using Meridian.Tests.Ledger;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed partial class AccountingPostingCandidateServiceTests
{
    [Theory]
    [InlineData(101.5)]
    [InlineData(98.5)]
    public async Task FactorPaydownCandidate_ReportsActualPrincipalWithoutCashGainOrLoss_AndReverses(double price)
    {
        var harness = await CreateAuthoritativeFactorHarnessAsync(
            [BuildFaceLotOfRecord(AuthoritativeFactorHarness.LedgerBookId, AuthoritativeFactorHarness.SecurityId,
                AuthoritativeFactorHarness.PositionId, 950_000m, .95m, 100m,
                pricePercentOfPar: (decimal)price)], .95m, .90m);
        var result = await harness.BuildWriteAsync();
        result.Candidate.HasBlockingIssues.Should().BeFalse();
        var entry = result.Write!.Entry;
        var ledger = new Meridian.Ledger.Ledger();

        ledger.Post(entry);

        CanonicalCashFlowTestData.AssertReportAndReversal(ledger, entry, 50_000m);
    }
}
