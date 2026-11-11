using FluentAssertions;
using Meridian.Ledger;

namespace Meridian.Tests.Ledger;

public sealed class LedgerReportPackPeriodBoundaryTests
{
    [Fact]
    public void Build_ExcludesAJournalStampedExactlyAtPeriodStart_FromPeriodDetail()
    {
        // The pack's period statements treat a journal at the period start as opening balance;
        // the GL detail and its control totals must use the same window.
        var periodStart = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var asOf = new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero);
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var capital = new LedgerAccount("Capital", LedgerAccountType.Equity);
        var fees = new LedgerAccount("Fee income", LedgerAccountType.Revenue);
        var ledger = new Meridian.Ledger.Ledger();
        Post(ledger, periodStart, "Opening contribution", cash, capital, 1_000m);
        Post(ledger, periodStart.AddDays(10), "June fee", cash, fees, 50m);

        var pack = LedgerReportPackBuilder.Build(
            ledger,
            new LedgerReportPackRequest(
                "report-1", "fund-1", "2026-06", periodStart, asOf, asOf, "USD", "tests", asOf));

        var glDetail = pack.Artifacts.Single(artifact => artifact.Name == "gl-detail.csv").Content;
        glDetail.Should().Contain("June fee").And.NotContain("Opening contribution");
        var manifest = pack.Artifacts.Single(artifact => artifact.Name == "manifest.csv").Content;
        manifest.Should().Contain("gl-detail-journal-entry-count,1");
        manifest.Should().Contain("gl-detail-total-debits,50");
    }

    private static void Post(
        Meridian.Ledger.Ledger ledger,
        DateTimeOffset timestamp,
        string description,
        LedgerAccount debit,
        LedgerAccount credit,
        decimal amount)
    {
        var journalEntryId = Guid.NewGuid();
        ledger.Post(new JournalEntry(
            journalEntryId,
            timestamp,
            description,
            [
                new LedgerEntry(Guid.NewGuid(), journalEntryId, timestamp, debit, amount, 0m, description),
                new LedgerEntry(Guid.NewGuid(), journalEntryId, timestamp, credit, 0m, amount, description)
            ]));
    }
}
