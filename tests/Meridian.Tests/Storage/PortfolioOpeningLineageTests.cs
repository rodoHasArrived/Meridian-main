using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed class PortfolioOpeningLineageTests
{
    [Fact]
    public void MixedOpening_RequiresExactApprovedActiveLineageForEveryInstrument()
    {
        var at = new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
        var book = Guid.NewGuid();
        var account = Guid.NewGuid();
        var id = Guid.NewGuid();
        var securityA = Guid.NewGuid();
        var securityB = Guid.NewGuid();
        var lineA = new LedgerEntry(Guid.NewGuid(), id, at, LedgerAccounts.Securities("AAPL", account.ToString("D")),
            100m, 0m, "Opening", new(InstrumentId: securityA));
        var lineB = new LedgerEntry(Guid.NewGuid(), id, at, LedgerAccounts.Securities("MSFT", account.ToString("D")),
            150m, 0m, "Opening", new(InstrumentId: securityB));
        var capital = new LedgerEntry(Guid.NewGuid(), id, at, LedgerAccounts.CapitalAccountFor(account.ToString("D")), 0m, 250m, "Opening");
        var tags = new Dictionary<string, string> { ["securityMaster.lineageMode"] = "per-line" };
        Add(tags, lineA, securityA);
        Add(tags, lineB, securityB);
        var period = new LedgerAccountingPeriod(Guid.NewGuid(), book, 2026, 11, "November", new(2026, 11, 1),
            new(2026, 11, 30), "Open", at.AddDays(-1), null, 1);
        LedgerJournalEntryWrite Write(IReadOnlyDictionary<string, string> evidence)
            => new(new JournalEntry(id, at, "Opening", [lineA, lineB, capital],
                new JournalEntryMetadata(EffectiveDate: new(2026, 11, 12),
                    IdempotencyKey: $"portfolio-opening:{book:D}:{account:D}", Tags: evidence)), account, period.PeriodId);
        LedgerPeriodPostingGuard.Validate(Write(tags), period);
        tags.Remove($"securityMaster.line.{lineB.EntryId:D}.provenance");
        var missing = () => LedgerPeriodPostingGuard.Validate(Write(tags), period);
        missing.Should().Throw<LedgerValidationException>().WithMessage("*Each opening instrument line*");
        Add(tags, lineB, securityA);
        var crossMapped = () => LedgerPeriodPostingGuard.Validate(Write(tags), period);
        crossMapped.Should().Throw<LedgerValidationException>().WithMessage("*Each opening instrument line*");
    }

    private static void Add(Dictionary<string, string> tags, LedgerEntry line, Guid security)
    {
        var prefix = $"securityMaster.line.{line.EntryId:D}.";
        var provenance = $"security-master:{security:N};server-resolved:true;approved:true;status:Active;version:1;currency:USD";
        tags[prefix + "securityId"] = security.ToString("D");
        tags[prefix + "provenance"] = provenance;
        tags[prefix + "lineage"] = $"{line.Account.Symbol}:{security:N}:ledger-map:manual-journal:{line.Account.Symbol}:{security:N}:Assets:sm-approval:security-master-active:{security:N}:security-status:Active:{provenance}";
    }
}
