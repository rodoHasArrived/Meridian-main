using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.TestSupport;
using Data = Meridian.Tests.FinancialOperations.Intercompany.IntercompanyProposalTestData;

namespace Meridian.Tests.FinancialOperations.Intercompany;

/// <summary>Synthetic June 2026 management fee: entity A is the declared service provider, entity B the recipient.</summary>
public sealed class IntercompanyFeeAccrualSourceBuilderTests
{
    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 7, 1);

    [Fact]
    public void APostedProviderFee_MirrorsThroughTheExistingProposalBuilder_AsTheRecipientsExpenseAgainstPayable()
    {
        var posting = Fee();
        var source = IntercompanyFeeAccrualSourceBuilder.BuildDraft(new(Data.BookA, posting.ArrangementId, From, To), "tenant-a", "company-a",
            "preparer", SourceBook(), Data.Period() with { LedgerBookId = Data.BookA }, posting, Computation(posting),
            Bindings("a", Data.BookA), Chart("a"), null, Data.Now).Draft!;
        var proposal = new IntercompanyProposalDto(new IntercompanyProposalSourceDto(Guid.NewGuid(), "tenant-a", "company-a",
            source.JournalEntryId, Data.BookA, Data.EntityA, Data.EntityB, posting.ArrangementId, posting.RevisionId,
            source.Lines[0].Dimensions!.IntercompanyTransactionId!, source.AccountingDate, "USD",
            source.Lines.Select(line => new IntercompanyProposalSourceLineDto(Guid.NewGuid(), line.AccountPath,
                line.Side == AccountingTemplateLineSideDto.Debit ? "Asset" : "Revenue", line.Dimensions!.ChartAccountNodeId,
                line.Side == AccountingTemplateLineSideDto.Debit ? line.Amount : 0m,
                line.Side == AccountingTemplateLineSideDto.Credit ? line.Amount : 0m, "USD",
                line.Side == AccountingTemplateLineSideDto.Debit ? line.Amount : 0m,
                line.Side == AccountingTemplateLineSideDto.Credit ? line.Amount : 0m)).ToArray())
        {
            IntercompanyAgreementId = posting.CanonicalAgreementId,
            IntercompanyAgreementVersion = posting.CanonicalAgreementVersion,
            IntercompanyAgreementPayloadHash = posting.CanonicalPayloadHash
        }, "retained-source-digest", IntercompanyProposalStatusDto.Pending, 0, Data.Now);

        var counterpart = IntercompanyProposalDraftBuilder.Build(proposal, posting, Data.TargetBook(), Data.Period(),
            Bindings("a", Data.BookA), Bindings("b", Data.BookB), Chart("b"), null, Data.Now);

        Assert.Equal([(AccountingTemplateLineSideDto.Debit, "a-fee-recv", 30_000m), (AccountingTemplateLineSideDto.Credit, "a-fee-revenue", 30_000m)],
            source.Lines.Select(line => (line.Side, line.Dimensions!.ChartAccountNodeId!, line.Amount)));
        Assert.Equal([(AccountingTemplateLineSideDto.Credit, "b-fee-pay", 30_000m), (AccountingTemplateLineSideDto.Debit, "b-fee-expense", 30_000m)],
            counterpart.Lines.Select(line => (line.Side, line.Dimensions!.ChartAccountNodeId!, line.Amount)));
        Assert.Equal("management-fee:20260601-20260701", counterpart.Lines[0].Dimensions!.IntercompanyTransactionId);
        Assert.Null(counterpart.IntercompanyAccrualEvidenceJson);
    }

    [Fact]
    public void OverlapCountsOnlyUnreversedFeesOfTheProfile_AndIgnoresInterestWindows()
    {
        var posting = Fee();
        var fee = Journal(posting.ArrangementId, IntercompanyFeeAccrualEvidenceGuard.TransactionId(From, To));
        var interest = Journal(posting.ArrangementId, IntercompanyAccrualEvidenceGuard.TransactionId(From, To));
        var other = Journal(Guid.NewGuid(), IntercompanyFeeAccrualEvidenceGuard.TransactionId(From, To));
        var reversal = Inverse(fee);

        Assert.Equal("management-fee:20260601-20260701", IntercompanyFeeAccrualSourceBuilder.FindPostedOverlap([fee], posting.ArrangementId, new(2026, 6, 15), new(2026, 7, 15)));
        Assert.Null(IntercompanyFeeAccrualSourceBuilder.FindPostedOverlap([interest, other], posting.ArrangementId, From, To));
        Assert.Null(IntercompanyFeeAccrualSourceBuilder.FindPostedOverlap([fee, reversal], posting.ArrangementId, From, To));
        // Originals and their corrections both count, so a recomputation after a reversal gets a fresh identity.
        Assert.Equal(2, IntercompanyFeeAccrualSourceBuilder.PostedGeneration([fee, reversal], posting.ArrangementId, From, To));
    }

    [Fact]
    public void TheProfileMustCarryTheRevenueOrExpenseNodeForTheSourceSide()
    {
        var posting = Fee(mappings: [new(Data.EntityA, Data.BookA, "a-fee-recv", "a-fee-pay"), new(Data.EntityB, Data.BookB, "b-fee-recv", "b-fee-pay")]);

        var error = Assert.Throws<InvalidOperationException>(() => IntercompanyFeeAccrualSourceBuilder.BuildDraft(
            new(Data.BookA, posting.ArrangementId, From, To), "tenant-a", "company-a", "preparer", SourceBook(),
            Data.Period() with { LedgerBookId = Data.BookA }, posting, Computation(posting), Bindings("a", Data.BookA), Chart("a"), null, Data.Now));

        Assert.Contains("fee-revenue node", error.Message);
    }

    private static ResolvedIntercompanyAccountingArrangementDto Fee(IReadOnlyList<IntercompanyPartyMappingDto>? mappings = null)
    {
        var profile = IntercompanyArrangementTestData.Create(Data.AccountingArrangementId, Guid.Parse("e1111111-1111-1111-1111-111111111111"),
            IntercompanyAccountingTemplateKindDto.ManagementFee, Data.EntityA, Data.EntityB, "USD", mappings ??
            [
                new(Data.EntityA, Data.BookA, "a-fee-recv", "a-fee-pay", null, "a-fee-revenue", "a-fee-expense"),
                new(Data.EntityB, Data.BookB, "b-fee-recv", "b-fee-pay", null, "b-fee-revenue", "b-fee-expense")
            ], Data.RevisionId, Data.Now.AddDays(-30));
        return profile with
        {
            LegalRevision = profile.LegalRevision with
            {
                Terms = profile.LegalRevision.Terms with { PartyOneRole = "Service provider", PartyTwoRole = "Recipient" }
            }
        };
    }

    private static IntercompanyFeeAccrualPreviewDto Computation(ResolvedIntercompanyAccountingArrangementDto posting)
        => new(IntercompanyAccrualRules.StatusReady, IntercompanyFeeAccrualRules.RuleVersion, posting.CanonicalAgreementId, "USD", From, To,
            30_000m, 30_000m, "kernel-test", [new("WEEKENDS", new string('c', 64))],
            [new(1, From, To, posting.RevisionId, posting.CanonicalAgreementVersion, posting.CanonicalPayloadHash, 1, From, To,
                IntercompanyFixedFeeBasisDto.PerPeriod, 30_000m, null, 1m, 30_000m)], ["Calculated proposal input only."]);

    private static LedgerBookRecord SourceBook() => new(Data.BookA, "source-fund", Data.EntityA, FundStructureNodeKindDto.Entity,
        "Source", "USD", Data.Now, Data.Now);

    private static IReadOnlyList<ChartOfAccountsNodeDto> Chart(string prefix) =>
    [
        new($"{prefix}-fee-recv", "Assets:Fee receivable", "Assets:Fee receivable", "Asset", IntercompanyRole: IntercompanyAccountRoleDto.Receivable),
        new($"{prefix}-fee-pay", "Liabilities:Fee payable", "Liabilities:Fee payable", "Liability", IntercompanyRole: IntercompanyAccountRoleDto.Payable),
        new($"{prefix}-fee-revenue", "Income:Management fees", "Income:Management fees", "Revenue", IntercompanyRole: IntercompanyAccountRoleDto.Revenue),
        new($"{prefix}-fee-expense", "Expenses:Management fees", "Expenses:Management fees", "Expense", IntercompanyRole: IntercompanyAccountRoleDto.Expense)
    ];

    private static IReadOnlyList<IntercompanyAccountBindingDto> Bindings(string prefix, Guid book) => Chart(prefix)
        .Select(account => new IntercompanyAccountBindingDto(Guid.NewGuid(), Guid.NewGuid(), 1, book, account.NodeId,
            account.IntercompanyRole!.Value, account.Path, account.AccountName, account.AccountType, new(2026, 1, 1), null,
            [], "tenant-a", "company-a", "reviewer", Data.Now, "Synthetic fixture")).ToArray();

    private static LedgerJournalEntryRecord Journal(Guid arrangement, string transaction)
    {
        var id = Guid.NewGuid();
        var timestamp = new DateTimeOffset(Data.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var dimensions = new LedgerLineDimensionSet(EntityId: Data.EntityA.ToString("D"), CounterpartyId: Data.EntityB.ToString("D"))
        { ChartAccountNodeId = "a-fee-recv", AccountingArrangementId = arrangement.ToString("D"), IntercompanyTransactionId = transaction };
        var entry = new JournalEntry(id, timestamp, "Synthetic fee",
        [
            new(Guid.NewGuid(), id, timestamp, new("Assets:Fee receivable", LedgerAccountType.Asset), 30_000m, 0m, "Synthetic fee", dimensions),
            new(Guid.NewGuid(), id, timestamp, new("Income:Management fees", LedgerAccountType.Revenue), 0m, 30_000m, "Synthetic fee",
                dimensions with { ChartAccountNodeId = "a-fee-revenue" })
        ], new JournalEntryMetadata(EffectiveDate: Data.Date, Tags: new Dictionary<string, string> { [IntercompanyAccrualEvidenceGuard.DigestTag] = new string('e', 64) }));
        return new(entry, Guid.NewGuid(), Guid.NewGuid(), null, null, 1, timestamp);
    }

    /// <summary>The posted reversal the correction workflow produces: same scope, sides swapped, source-linked.</summary>
    private static LedgerJournalEntryRecord Inverse(LedgerJournalEntryRecord original)
    {
        var id = Guid.NewGuid();
        var entry = new JournalEntry(id, original.Entry.Timestamp, "Reversal", original.Entry.Lines.Select(line => new LedgerEntry(
            Guid.NewGuid(), id, line.Timestamp, line.Account, line.Credit, line.Debit, "Reversal", line.Dimensions, null)).ToArray(),
            original.Entry.Metadata);
        return original with { Entry = entry, GlobalSequence = original.GlobalSequence + 1, SourceJournalEntryId = original.Entry.JournalEntryId };
    }
}
