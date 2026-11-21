using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;

namespace Meridian.Tests.Ui;

public sealed partial class ProviderLedgerReconciliationServiceTests
{
    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_UsesCorrectedCustodianPopulationWithoutAddingRetainedHistory()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(root, includeSecurityLookup: true);
            var date = (await fixture.AccountService.GetLatestBalanceSnapshotAsync(fixture.AccountId))!.AsOfDate;
            var original = Statement(1, 90m);
            var first = await fixture.AccountService.IngestCustodianStatementAsync(original);
            var corrected = Statement(2, 100m) with
            { Authority = new(2, "synthetic-reviewed-correction", new string('b', 64), SupersedesBatchId: first.BatchId) };
            await fixture.AccountService.IngestCustodianStatementAsync(corrected);
            await fixture.AccountService.IngestCustodianStatementAsync(original);

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Matched);
            detail.ShadowBookComparison!.Lines.Should().Contain(line =>
                line.Dimension == "position-quantity:AAPL" && line.InternalAmount == 100m && line.ProviderAmount == 100m
                && line.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            detail.ShadowBookComparison.Lines.Should().Contain(line =>
                line.Dimension == "position-market-value:AAPL" && line.InternalAmount == 18_750m && line.ProviderAmount == 18_750m
                && line.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            detail.ShadowBookComparison.Lines.Should().Contain(line =>
                line.Dimension == "position-cost-basis:AAPL" && line.InternalAmount == 15_000m && line.ProviderAmount == 15_000m
                && line.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);

            IngestCustodianStatementRequest Statement(int revision, decimal quantity)
            {
                var batch = Guid.NewGuid();
                var line = new CustodianPositionLineDto(Guid.NewGuid(), batch, fixture.AccountId, date, "AAPL", "Symbol",
                    quantity, quantity * 187.5m, "USD", "Apple Inc.", "Equity", false, quantity * 150m);
                return new(batch, fixture.AccountId, date, "synthetic-custodian", "synthetic", null, [line], "tests",
                    Authority: new(revision, "synthetic-reviewed-statement", new string('a', 64)));
            }
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }
}
