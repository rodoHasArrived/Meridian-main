using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.FinancialOperations.Intercompany;

public sealed class IntercompanyCounterpartReversalBuilderTests
{
    [Fact]
    public void ReversalOfReviewedManualReplacement_PreservesOriginalFxWithoutCurrentRate()
    {
        var fixture = Fixture(foreign: true);
        var draft = Build(fixture);

        Assert.Equal(fixture.Prior.LatestLink!.TargetJournalId, draft.ReversalOfJournalEntryId);
        Assert.NotEqual(fixture.Prior.TargetJournalId, draft.ReversalOfJournalEntryId);
        Assert.Equal(ManualJournalEntryTypeDto.Reversal, draft.EntryType);
        Assert.Null(draft.IntercompanyFx);
        Assert.All(draft.Lines, line =>
        {
            Assert.Equal(125m, line.Amount);
            Assert.Equal("EUR", line.TransactionCurrency!.Currency);
            Assert.Equal(100m, line.TransactionCurrency.Amount);
            Assert.Equal(1.25m, line.TransactionCurrency.FxRateToFunctional);
        });
        Assert.Equal(AccountingTemplateLineSideDto.Debit, draft.Lines[0].Side);
        Assert.Equal(AccountingTemplateLineSideDto.Credit, draft.Lines[1].Side);
    }

    [Fact]
    public void LinkedJournalWithUnrelatedActivity_RequiresManualCorrection()
    {
        var fixture = Fixture();
        var entry = fixture.Original.Entry;
        var timestamp = entry.Timestamp;
        fixture = fixture with
        {
            Original = fixture.Original with
            {
                Entry = new(entry.JournalEntryId, timestamp, entry.Description,
            [.. entry.Lines,
                new(Guid.NewGuid(), entry.JournalEntryId, timestamp, new("Expenses:Tax", LedgerAccountType.Expense), 200m, 0m, entry.Description),
                new(Guid.NewGuid(), entry.JournalEntryId, timestamp, new("Assets:Cash", LedgerAccountType.Asset), 0m, 200m, entry.Description)], entry.Metadata)
            }
        };

        Assert.Throws<InvalidOperationException>(() => Build(fixture));
    }

    [Fact]
    public void OrdinaryContraWithUnprovedEconomicScope_CannotBeCopiedByAccountName()
    {
        var fixture = Fixture();
        var entry = fixture.Original.Entry;
        var contra = entry.Lines[1];
        fixture = fixture with
        {
            Original = fixture.Original with
            {
                Entry = new(entry.JournalEntryId, entry.Timestamp, entry.Description,
            [entry.Lines[0], new(contra.EntryId, entry.JournalEntryId, entry.Timestamp, contra.Account, contra.Debit, contra.Credit,
                contra.Description, contra.Dimensions! with { CostCenterId = "unrelated-business" })], entry.Metadata)
            }
        };

        Assert.Throws<InvalidOperationException>(() => Build(fixture));
    }

    [Theory]
    [InlineData("transaction")]
    [InlineData("revision")]
    public void EqualAmountOtherEvent_CannotAuthorizeThisReversal(string difference)
    {
        var fixture = Fixture();
        fixture = fixture with
        {
            Prior = fixture.Prior with
            {
                Source = fixture.Prior.Source with
                {
                    TransactionId = difference == "transaction" ? "same-amount-other-invoice" : fixture.Prior.Source.TransactionId,
                    AccountingArrangementRevisionId = difference == "revision" ? Guid.NewGuid() : fixture.Prior.Source.AccountingArrangementRevisionId
                }
            }
        };

        Assert.Throws<InvalidOperationException>(() => Build(fixture));
    }

    [Fact]
    public void HistoricalAgreementDate_RequiresCompleteSameBookSourceInverse()
    {
        var fixture = Fixture();
        var sourceOriginal = SourceRecord(fixture.Prior.Source);
        var sourceInverse = InverseRecord(sourceOriginal, fixture.Proposal.Source.SourceJournalId, fixture.Proposal.Source.EffectiveDate);

        Assert.Equal(IntercompanyProposalTestData.Date,
            IntercompanyCounterpartReversalBuilder.VerifySource(fixture.Proposal.Source, sourceOriginal, sourceInverse));
        var entry = sourceInverse.Entry;
        var first = entry.Lines[0];
        var changed = new JournalEntry(entry.JournalEntryId, entry.Timestamp, entry.Description,
            [new(first.EntryId, entry.JournalEntryId, entry.Timestamp,
                new(first.Account.Name, first.Account.AccountType, FinancialAccountId: "other-financial-account"),
                first.Debit, first.Credit, first.Description, first.Dimensions), entry.Lines[1]], entry.Metadata);
        Assert.Throws<InvalidOperationException>(() => IntercompanyCounterpartReversalBuilder.VerifySource(fixture.Proposal.Source,
            sourceOriginal, sourceInverse with { Entry = changed }));
        Assert.Throws<InvalidOperationException>(() => IntercompanyCounterpartReversalBuilder.VerifySource(fixture.Proposal.Source,
            sourceOriginal, sourceInverse with { SourceJournalEntryId = Guid.NewGuid() }));
    }

    private static ManualJournalEntryDraftDto Build(ReversalFixture fixture)
        => IntercompanyCounterpartReversalBuilder.Build(fixture.Proposal, fixture.Prior, fixture.Agreement, fixture.Original,
            IntercompanyProposalTestData.TargetBook(), IntercompanyProposalTestData.Period() with { StartDate = new(2026, 7, 1), EndDate = new(2026, 7, 31) },
            IntercompanyProposalTestData.TargetBindings(), IntercompanyProposalTestData.Chart(), IntercompanyProposalTestData.Now.AddDays(2));

    internal static ReversalFixture Fixture(bool foreign = false)
    {
        var prior = IntercompanyProposalTestData.Proposal();
        if (foreign)
            prior = prior with
            {
                Source = prior.Source with
                { SourceFunctionalCurrency = "EUR", Lines = prior.Source.Lines.Select(line => line with { Currency = "EUR" }).ToArray() }
            };
        var originalId = Guid.NewGuid();
        prior = prior with
        {
            Status = IntercompanyProposalStatusDto.Drafted,
            TargetBookId = IntercompanyProposalTestData.BookB,
            TargetJournalId = prior.Source.ProposalId,
            LatestLink = new("tenant-a", Guid.NewGuid(), prior.Source.ProposalId, IntercompanyProposalTestData.BookB,
                originalId, "reviewer", "Posted manual replacement after rejecting generated draft", IntercompanyProposalTestData.Now)
        };
        var proposal = prior with
        {
            Source = prior.Source with
            {
                ProposalId = Guid.NewGuid(),
                SourceJournalId = Guid.NewGuid(),
                ReversesJournalId = prior.Source.SourceJournalId,
                EffectiveDate = new(2026, 7, 2),
                Lines = prior.Source.Lines.Select(line => line with
                {
                    Debit = line.Credit,
                    Credit = line.Debit,
                    TransactionDebit = line.TransactionCredit,
                    TransactionCredit = line.TransactionDebit
                }).ToArray()
            },
            TargetBookId = null,
            TargetJournalId = null,
            LatestLink = null,
            Status = IntercompanyProposalStatusDto.Pending
        };
        var mainDimensions = new LedgerLineDimensionSet(FundId: "target-fund", EntityId: IntercompanyProposalTestData.EntityB.ToString("D"),
            CounterpartyId: IntercompanyProposalTestData.EntityA.ToString("D"), BookId: IntercompanyProposalTestData.BookB.ToString("D"))
        {
            IntercompanyAgreementId = prior.Source.IntercompanyAgreementId,
            IntercompanyAgreementVersion = prior.Source.IntercompanyAgreementVersion,
            ChartAccountNodeId = "b-payable",
            AccountingArrangementId = prior.Source.AccountingArrangementId.ToString("D"),
            AccountingArrangementRevisionId = prior.Source.AccountingArrangementRevisionId.ToString("D"),
            IntercompanyTransactionId = prior.Source.TransactionId,
            AccountBindingRevisionId = Guid.NewGuid().ToString("D")
        };
        var contraDimensions = mainDimensions with
        {
            IntercompanyAgreementId = prior.Source.IntercompanyAgreementId,
            IntercompanyAgreementVersion = prior.Source.IntercompanyAgreementVersion,
            ChartAccountNodeId = "b-cash",
            AccountingArrangementId = null,
            AccountingArrangementRevisionId = null,
            IntercompanyTransactionId = null,
            AccountBindingRevisionId = null
        };
        var amount = foreign ? 125m : 100m;
        var timestamp = IntercompanyProposalTestData.Now;
        var entry = new JournalEntry(originalId, timestamp, "Reviewed manual counterpart", [
            new(Guid.NewGuid(), originalId, timestamp, new("Liabilities:Due to A", LedgerAccountType.Liability), 0m, amount, "Reviewed manual counterpart", mainDimensions,
                foreign ? new("EUR", "USD", 0m, 100m, 1.25m) : null),
            new(Guid.NewGuid(), originalId, timestamp, new("Assets:Cash", LedgerAccountType.Asset), amount, 0m, "Reviewed manual counterpart", contraDimensions,
                foreign ? new("EUR", "USD", 100m, 0m, 1.25m) : null)], new JournalEntryMetadata(EffectiveDate: IntercompanyProposalTestData.Date));
        return new(proposal, prior, IntercompanyProposalTestData.Agreement(currency: foreign ? "EUR" : "USD", effectiveTo: IntercompanyProposalTestData.Now.AddHours(14)),
            new(entry, Guid.NewGuid(), Guid.NewGuid(), null, null, 1, timestamp));
    }

    internal static LedgerJournalEntryRecord SourceRecord(IntercompanyProposalSourceDto source)
    {
        var timestamp = new DateTimeOffset(source.EffectiveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var entry = new JournalEntry(source.SourceJournalId, timestamp, "Source event", source.Lines.Select(line => new LedgerEntry(line.LineId,
            source.SourceJournalId, timestamp, new(line.AccountName, Enum.Parse<LedgerAccountType>(line.AccountType)), line.Debit, line.Credit, "Source event",
            new LedgerLineDimensionSet(EntityId: source.SourceEntityId.ToString("D"), CounterpartyId: source.CounterpartyEntityId.ToString("D"))
            {
                IntercompanyAgreementId = source.IntercompanyAgreementId,
                IntercompanyAgreementVersion = source.IntercompanyAgreementVersion,
                ChartAccountNodeId = line.ChartAccountNodeId,
                AccountingArrangementId = source.AccountingArrangementId.ToString("D"),
                AccountingArrangementRevisionId = source.AccountingArrangementRevisionId.ToString("D"),
                IntercompanyTransactionId = source.TransactionId
            })).ToArray(),
            new JournalEntryMetadata(EffectiveDate: source.EffectiveDate));
        return new(entry, Guid.NewGuid(), Guid.NewGuid(), null, null, 1, timestamp);
    }

    internal static LedgerJournalEntryRecord InverseRecord(LedgerJournalEntryRecord original, Guid journal, DateOnly date)
    {
        var timestamp = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var inverse = new JournalEntry(journal, timestamp, "Source reversal", original.Entry.Lines.Select(line => new LedgerEntry(Guid.NewGuid(), journal,
            timestamp, line.Account, line.Credit, line.Debit, "Source reversal", line.Dimensions,
            line.Currency is { } c ? new(c.TransactionCurrency, c.FunctionalCurrency, c.TransactionCredit, c.TransactionDebit, c.FxRateToFunctional) : null)).ToArray(),
            original.Entry.Metadata with { EffectiveDate = date });
        return new(inverse, Guid.NewGuid(), Guid.NewGuid(), null, null, 2, timestamp, SourceJournalEntryId: original.Entry.JournalEntryId,
            PostingKind: LedgerPostingKindDto.Adjustment);
    }

    internal sealed record ReversalFixture(IntercompanyProposalDto Proposal, IntercompanyProposalDto Prior,
        ResolvedIntercompanyAccountingArrangementDto Agreement, LedgerJournalEntryRecord Original);
}
