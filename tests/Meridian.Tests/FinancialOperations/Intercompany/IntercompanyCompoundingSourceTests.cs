using Meridian.Application.FundStructure;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Data = Meridian.Tests.FinancialOperations.Intercompany.IntercompanyProposalTestData;

namespace Meridian.Tests.FinancialOperations.Intercompany;

/// <summary>
/// Compounding facts from real posted journals: June's posted accrual (4,166.67, dated 30 June) becomes July's base;
/// once it is reversed, July has nothing posted to compound on and an uncovered June, so it is refused.
/// </summary>
public sealed class IntercompanyCompoundingSourceTests
{
    private static readonly Guid Interest = Guid.Parse("a1111111-1111-1111-1111-111111111111");
    private static readonly DateOnly June = new(2026, 6, 1);
    private static readonly DateOnly July = new(2026, 7, 1);
    private static readonly DateOnly August = new(2026, 8, 1);

    [Fact]
    public void JulyCompoundsOnJunesPostedAccrual()
    {
        var june = Accrual(new(2026, 6, 30), "loan-interest:20260601-20260701", 4166.67m);
        var facts = Build([june]);

        Assert.Equal([new IntercompanyAccrualWindowDto(June, July)], facts.Evidence!.PostedWindows);
        // (1,000,000 + 4,166.67) x 5% x 31/360 = 4,305.5556 + 17.9398 = 4,323.4954... -> 4,323.50
        var preview = Preview(facts);
        Assert.Equal(4323.50m, preview.Amount);
        Assert.Equal(4166.67m, Assert.Single(preview.Slices).CompoundedInterest);
    }

    [Fact]
    public void AReversedAccrualNeitherCoversJuneNorCompounds()
    {
        var june = Accrual(new(2026, 6, 30), "loan-interest:20260601-20260701", 4166.67m);
        var facts = Build([june, Inverse(june)]);

        Assert.Empty(facts.Evidence!.PostedWindows);
        var preview = Preview(facts);
        Assert.Null(preview.Amount);
        Assert.Contains("do not cover 2026-06-01", Assert.Single(preview.Diagnostics));
    }

    private static (IntercompanyAccrualCompoundingEvidenceDto? Evidence, IReadOnlyList<IntercompanyAccrualPrincipalSegmentDto> Balance, string? Refusal)
        Build(IReadOnlyList<LedgerJournalEntryRecord> journals)
        => IntercompanyAccrualSourceBuilder.BuildCompounding(journals, Interest, Data.BookA, new HashSet<string> { "a-int-recv" },
            new HashSet<string> { "a-int-pay" }, "USD", "USD", June, August, June);

    private static IntercompanyAccrualPreviewDto Preview((IntercompanyAccrualCompoundingEvidenceDto? Evidence,
        IReadOnlyList<IntercompanyAccrualPrincipalSegmentDto> Balance, string? Refusal) facts)
    {
        var draft = new IntercompanyAgreementDraftDto("IC-COMP", "Synthetic compounding loan", Data.EntityA, "Lender", Data.EntityB, "Borrower",
            IntercompanyAgreementTypeDto.Loan, "USD", June, new(2027, 6, 1),
            new()
            {
                StructuredTerms = new(new(June, new(2027, 6, 1), IntercompanyFrequencyDto.Monthly, "WEEKENDS", BusinessDayConvention.Following),
                    IntercompanyRateBasisDto.Fixed, 0.05m, DayCount: IntercompanyDayCountDto.Actual360, Compounding: IntercompanyCompoundingDto.Monthly)
            });
        var legal = new IntercompanyAgreementRevisionDto(Guid.NewGuid(), 1, null, null, IntercompanyAgreementActionDto.Activate, draft, Guid.NewGuid(),
            "hash", ["preparer"], "submitter", "reviewer", new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), "Synthetic approved terms");
        return new IntercompanyAccrualPreviewService(new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]))
            .Preview(new(July, August, [new(July, August, Interest, Guid.NewGuid(), legal)], [new(July, August, 1_000_000m, "principal")])
            {
                AccruedInterest = facts.Balance,
                PostedAccrualWindows = facts.Evidence?.PostedWindows ?? [],
                FirstPrincipalDate = facts.Evidence?.FirstPrincipalDate
            });
    }

    private static LedgerJournalEntryRecord Accrual(DateOnly date, string transaction, decimal amount)
    {
        var id = Guid.NewGuid();
        var timestamp = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var dimensions = new LedgerLineDimensionSet(EntityId: Data.EntityA.ToString("D"), CounterpartyId: Data.EntityB.ToString("D"))
        { ChartAccountNodeId = "a-int-recv", AccountingArrangementId = Interest.ToString("D"), IntercompanyTransactionId = transaction };
        var entry = new JournalEntry(id, timestamp, "Synthetic accrual",
        [
            new(Guid.NewGuid(), id, timestamp, new("Assets:Interest receivable", LedgerAccountType.Asset), amount, 0m, "Synthetic accrual", dimensions),
            new(Guid.NewGuid(), id, timestamp, new("Income:Interest", LedgerAccountType.Revenue), 0m, amount, "Synthetic accrual",
                dimensions with { ChartAccountNodeId = "a-int-income" })
        ], new JournalEntryMetadata(EffectiveDate: date, Tags: new Dictionary<string, string> { [IntercompanyAccrualEvidenceGuard.DigestTag] = new string('e', 64) }));
        return new(entry, Guid.NewGuid(), Guid.NewGuid(), null, null, 1, timestamp);
    }

    private static LedgerJournalEntryRecord Inverse(LedgerJournalEntryRecord original)
    {
        var id = Guid.NewGuid();
        var entry = new JournalEntry(id, original.Entry.Timestamp, "Reversal", original.Entry.Lines.Select(line => new LedgerEntry(
            Guid.NewGuid(), id, line.Timestamp, line.Account, line.Credit, line.Debit, "Reversal", line.Dimensions, null)).ToArray(),
            original.Entry.Metadata);
        return original with { Entry = entry, GlobalSequence = original.GlobalSequence + 1, SourceJournalEntryId = original.Entry.JournalEntryId };
    }
}
