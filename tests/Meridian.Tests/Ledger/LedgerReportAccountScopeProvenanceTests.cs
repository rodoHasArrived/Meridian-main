using FluentAssertions;
using Meridian.Ledger;

namespace Meridian.Tests.Ledger;

public sealed class LedgerReportAccountScopeProvenanceTests
{
    [Fact]
    public void AggregatedCapitalRow_RetainsActualInvestorAccountsWithinAsOfAndSelectedScope()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var asOf = new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero);
        var first = Post("investor-a", "entity-selected", 60_000m, asOf.AddDays(-10));
        var second = Post("investor-b", "entity-selected", 40_000m, asOf.AddDays(-5));
        Post("investor-a", "entity-foreign", 999m, asOf.AddDays(-2));
        Post("investor-a", "entity-selected", 999m, asOf.AddDays(1));
        var request = new LedgerReportPackRequest("capital-provenance", "fund-alpha", "2026-05",
            new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), asOf, asOf, "USD", "reviewer", asOf.AddDays(1),
            lineDimensions: new LedgerLineDimensionSet(EntityId: "entity-selected"));

        var combined = LedgerReportPackBuilder.Build(ledger, request);
        combined.Statements.PartnersCapital!.EndingCapital.Should().Be(100_000m);
        var combinedCapital = combined.LineProvenance.Single(row => row.ArtifactName == "trial-balance.csv"
            && row.RowKey == "Investor Capital");
        combinedCapital.Value.Should().Be(100_000m);
        combinedCapital.LedgerJournalEntryIds.Should().BeEquivalentTo(new[] { first, second });

        var selectedInvestor = LedgerReportPackBuilder.Build(ledger, request, financialAccountId: "investor-a");
        selectedInvestor.Statements.PartnersCapital!.EndingCapital.Should().Be(60_000m);
        var selectedCapital = selectedInvestor.LineProvenance.Single(row => row.ArtifactName == "trial-balance.csv"
            && row.RowKey == "Investor Capital");
        selectedCapital.Value.Should().Be(60_000m);
        selectedCapital.LedgerJournalEntryIds.Should().ContainSingle().Which.Should().Be(first);

        Guid Post(string investor, string entity, decimal amount, DateTimeOffset when)
        {
            var dimensions = new LedgerLineDimensionSet(EntityId: entity, InvestorId: investor);
            ledger.PostLines(when, "Synthetic contribution",
            [
                (LedgerAccounts.CashAccount(investor), amount, 0m, dimensions),
                (LedgerAccounts.InvestorCapitalFor(investor), 0m, amount, dimensions)
            ]);
            return ledger.Journal[^1].JournalEntryId;
        }
    }
}
