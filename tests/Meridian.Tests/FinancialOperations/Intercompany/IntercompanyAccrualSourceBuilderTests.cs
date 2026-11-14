using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Entities.FundStructure;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.TestSupport;
using Data = Meridian.Tests.FinancialOperations.Intercompany.IntercompanyProposalTestData;

namespace Meridian.Tests.FinancialOperations.Intercompany;

/// <summary>Synthetic June 2026 loan: entity A lends to entity B in USD unless a test says otherwise.</summary>
public sealed class IntercompanyAccrualSourceBuilderTests
{
    private static readonly Guid PrincipalArrangement = Guid.Parse("d1111111-1111-1111-1111-111111111111");
    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 7, 1);
    private static readonly HashSet<string> Receivables = ["a-receivable"];
    private static readonly HashSet<string> Payables = ["a-payable"];

    [Fact]
    public void Principal_IsReadFromPostedLoanJournals_WithMovementsEffectiveOnTheirOwnDate()
    {
        var drawdown = PrincipalJournal(new(2026, 5, 15), "a-receivable", debit: 1_000_000m);
        var repayment = PrincipalJournal(new(2026, 6, 11), "a-receivable", credit: 600_000m);
        var afterWindow = PrincipalJournal(new(2026, 7, 1), "a-receivable", debit: 50m);
        var otherProfile = PrincipalJournal(new(2026, 6, 5), "a-receivable", debit: 999m, arrangement: Guid.NewGuid());

        var result = IntercompanyAccrualSourceBuilder.BuildPrincipal([repayment, afterWindow, otherProfile, drawdown],
            PrincipalArrangement, Data.BookA, Receivables, Payables, "USD", "USD", From, To);

        Assert.Null(result.Refusal);
        Assert.Equal([(From, new DateOnly(2026, 6, 11), 1_000_000m), (new DateOnly(2026, 6, 11), To, 400_000m)],
            result.Segments.Select(segment => (segment.From, segment.To, segment.Principal)));
        Assert.All(result.Segments, segment => Assert.StartsWith($"ledger-principal:{Data.BookA:D}:", segment.EvidenceReference));
        var evidence = result.Evidence!;
        Assert.Equal((IntercompanyAccountRoleDto.Receivable, 1_000_000m, 1), (evidence.Role, evidence.OpeningPrincipal, evidence.OpeningJournalCount));
        var movement = Assert.Single(evidence.Movements);
        Assert.Equal((repayment.Entry.JournalEntryId, -600_000m), (movement.JournalEntryId, movement.SignedAmount));
    }

    [Fact]
    public void BorrowerBook_ReadsItsPayablePrincipalAsAPositiveBalance()
    {
        var result = IntercompanyAccrualSourceBuilder.BuildPrincipal([PrincipalJournal(From, "a-payable", credit: 250_000m)],
            PrincipalArrangement, Data.BookA, Receivables, Payables, "USD", "USD", From, To);

        Assert.Equal(IntercompanyAccountRoleDto.Payable, result.Evidence!.Role);
        Assert.Equal((From, To, 250_000m), (result.Segments[0].From, result.Segments[0].To, result.Segments[0].Principal));
    }

    [Theory]
    [InlineData("none", "No posted principal")]
    [InlineData("both", "ambiguous")]
    [InlineData("negative", "negative")]
    [InlineData("currency", "agreement-currency")]
    public void Principal_ThatCannotBeProved_IsRefused(string variant, string expected)
    {
        LedgerJournalEntryRecord[] records = variant switch
        {
            "none" => [],
            "both" => [PrincipalJournal(From, "a-receivable", debit: 10m), PrincipalJournal(From, "a-payable", credit: 10m)],
            "negative" => [PrincipalJournal(From, "a-receivable", debit: 10m), PrincipalJournal(new(2026, 6, 10), "a-receivable", credit: 25m)],
            _ => [PrincipalJournal(From, "a-receivable", debit: 10m, transactionCurrency: "EUR")]
        };

        var result = IntercompanyAccrualSourceBuilder.BuildPrincipal(records, PrincipalArrangement, Data.BookA,
            Receivables, Payables, "USD", "USD", From, To);

        Assert.Null(result.Evidence);
        Assert.Empty(result.Segments);
        Assert.Contains(expected, result.Refusal);
    }

    [Fact]
    public void Authority_SplitsTheWindowAtTheSupersedingProfileRevision_AndRefusesUncoveredDays()
    {
        var first = Interest(Data.RevisionId, new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)).Arrangement;
        var secondId = Guid.NewGuid();
        var second = first with { RevisionId = secondId, RevisionNumber = 2, EffectiveFrom = new(2026, 6, 16, 0, 0, 0, TimeSpan.Zero), SupersedesRevisionId = first.RevisionId };
        var history = new IntercompanyAccountingArrangementHistoryDto([Approval(first), Approval(second)]);

        var segments = IntercompanyAccrualSourceBuilder.ResolveAuthority(history, From, To);

        Assert.Equal([(From, new DateOnly(2026, 6, 16), first.RevisionId), (new DateOnly(2026, 6, 16), To, secondId)], segments);
        var error = Assert.Throws<InvalidOperationException>(() =>
            IntercompanyAccrualSourceBuilder.ResolveAuthority(history, new(2026, 5, 30), To));
        Assert.Contains("2026-05-30", error.Message);
    }

    [Fact]
    public void PostedOverlap_BlocksReaccrual_UntilASourceLinkedExactInverseReversesIt()
    {
        var interest = Data.AccountingArrangementId;
        var window = IntercompanyAccrualEvidenceGuard.TransactionId(From, To);
        var posted = AccrualJournal(interest, "a-int-recv", window, debit: 4166.67m);
        var inverse = Inverse(posted);

        Assert.Equal("loan-interest:20260601-20260701", IntercompanyAccrualSourceBuilder.FindPostedOverlap([posted], interest, new(2026, 6, 15), new(2026, 7, 15)));
        Assert.Null(IntercompanyAccrualSourceBuilder.FindPostedOverlap([posted], interest, To, new(2026, 8, 1)));
        Assert.Null(IntercompanyAccrualSourceBuilder.FindPostedOverlap([posted, inverse], interest, From, To));
        Assert.Null(IntercompanyAccrualSourceBuilder.FindPostedOverlap([posted], Guid.NewGuid(), From, To));
    }

    [Fact]
    public void PostedOverlap_IsNotClearedBySettlementOrByAnUnlinkedOffsettingEntry()
    {
        var interest = Data.AccountingArrangementId;
        var window = IntercompanyAccrualEvidenceGuard.TransactionId(From, To);
        var posted = AccrualJournal(interest, "a-int-recv", window, debit: 4166.67m);
        // Cash settles the receivable under the same transaction identity: the receivable nets to zero while
        // the interest income stays posted, so the window is still accrued.
        var settlement = AccrualJournal(interest, "a-int-recv", window, credit: 4166.67m, accrualTag: false);
        var unlinked = Inverse(posted) with { SourceJournalEntryId = null };
        // A posted accrual on a node retired by a later profile revision still covers its days.
        var oldNode = AccrualJournal(interest, "a-int-recv-retired", window, debit: 4166.67m);

        Assert.NotNull(IntercompanyAccrualSourceBuilder.FindPostedOverlap([posted, settlement], interest, From, To));
        Assert.NotNull(IntercompanyAccrualSourceBuilder.FindPostedOverlap([posted, unlinked], interest, From, To));
        Assert.NotNull(IntercompanyAccrualSourceBuilder.FindPostedOverlap([oldNode], interest, From, To));
    }

    [Fact]
    public void RecomputationAfterReversal_GetsANewRetryStableIdentity_AndLeavesTheOriginalAlone()
    {
        var interest = Data.AccountingArrangementId;
        var posted = AccrualJournal(interest, "a-int-recv", IntercompanyAccrualEvidenceGuard.TransactionId(From, To), debit: 4166.67m);
        LedgerJournalEntryRecord[] reversed = [posted, Inverse(posted)];

        var generation = IntercompanyAccrualSourceBuilder.PostedGeneration(reversed, interest, From, To);
        var original = BuildDraft();
        var replacement = BuildDraft(generation: generation);

        Assert.Equal(0, IntercompanyAccrualSourceBuilder.PostedGeneration([], interest, From, To));
        Assert.Equal(0, IntercompanyAccrualSourceBuilder.PostedGeneration(reversed, interest, From, new(2026, 6, 16)));
        Assert.Equal(2, generation);
        Assert.NotEqual(original.Draft!.JournalEntryId, replacement.Draft!.JournalEntryId);
        Assert.Equal(replacement.Draft.JournalEntryId, BuildDraft(generation: generation).Draft!.JournalEntryId);
        Assert.Equal(replacement.Draft.JournalEntryId, replacement.Evidence!.DraftId);
        Assert.Null(IntercompanyAccrualEvidenceGuard.Validate(replacement.Draft));
    }

    [Fact]
    public void LenderDraft_IsAnUnpostedBalancedDraft_PinningAgreementVersionProfileRevisionAndEvidence()
    {
        var proposal = BuildDraft();
        var draft = proposal.Draft!;

        Assert.True(proposal.IsReady);
        Assert.Equal(ManualJournalEntryStatusDto.Draft, draft.Status);
        Assert.Null(draft.ApprovedBy);
        Assert.Null(draft.PostedAtUtc);
        Assert.Equal(Data.Date, draft.AccountingDate);
        Assert.Equal("preparer", draft.PreparedBy);
        Assert.Equal([(AccountingTemplateLineSideDto.Debit, "a-int-recv", 4166.67m), (AccountingTemplateLineSideDto.Credit, "a-int-income", 4166.67m)],
            draft.Lines.Select(line => (line.Side, line.Dimensions!.ChartAccountNodeId!, line.Amount)));
        var posting = Interest();
        Assert.All(draft.Lines, line =>
        {
            Assert.Equal(posting.CanonicalAgreementId, line.Dimensions!.IntercompanyAgreementId);
            Assert.Equal(posting.CanonicalAgreementVersion, line.Dimensions.IntercompanyAgreementVersion);
            Assert.Equal(posting.ArrangementId.ToString("D"), line.Dimensions.AccountingArrangementId);
            Assert.Equal(posting.RevisionId.ToString("D"), line.Dimensions.AccountingArrangementRevisionId);
            Assert.Equal("loan-interest:20260601-20260701", line.Dimensions.IntercompanyTransactionId);
            Assert.Equal(Data.EntityB.ToString("D"), line.Dimensions.CounterpartyId);
        });
        var evidence = proposal.Evidence!;
        Assert.Equal((posting.RevisionId, posting.CanonicalAgreementVersion, posting.CanonicalPayloadHash, draft.JournalEntryId),
            (evidence.PostingArrangementRevisionId, evidence.PostingAgreementVersion, evidence.PostingAgreementPayloadHash, evidence.DraftId));
        Assert.Equal(PrincipalArrangement, evidence.Principal.PrincipalArrangementId);
        Assert.Equal(proposal.EvidenceDigest, draft.IntercompanyAccrualEvidenceDigest);
        Assert.Contains($"intercompany-accrual-evidence:sha256:{proposal.EvidenceDigest}", draft.EvidenceLinks);
        Assert.Null(IntercompanyAccrualEvidenceGuard.Validate(draft));
    }

    [Fact]
    public void Draft_IsDeterministic_SoPreviewAndCreateAgreeAndOneWindowHasOneIdentity()
    {
        var first = BuildDraft();
        var second = BuildDraft(now: Data.Now.AddHours(3));
        var otherWindow = IntercompanyAccrualSourceBuilder.DraftId("tenant-a", Request() with { AccrualTo = new(2026, 6, 16) });

        Assert.Equal(first.EvidenceDigest, second.EvidenceDigest);
        Assert.Equal(first.Draft!.JournalEntryId, second.Draft!.JournalEntryId);
        Assert.NotEqual(first.Draft.JournalEntryId, otherWindow);
    }

    [Fact]
    public void BorrowerDraft_DebitsInterestExpenseAndCreditsInterestPayable()
    {
        var draft = BuildDraft(book: BorrowerBook(), period: Data.Period(), role: IntercompanyAccountRoleDto.Payable,
            chart: Chart("b"), bindings: Bindings("b", Data.BookB)).Draft!;

        Assert.Equal([(AccountingTemplateLineSideDto.Credit, "b-int-pay"), (AccountingTemplateLineSideDto.Debit, "b-int-expense")],
            draft.Lines.Select(line => (line.Side, line.Dimensions!.ChartAccountNodeId!)));
        Assert.Equal(Data.EntityA.ToString("D"), draft.Lines[0].Dimensions!.CounterpartyId);
        Assert.Null(IntercompanyAccrualEvidenceGuard.Validate(draft));
    }

    [Fact]
    public void EditedLinesOrReplacedProvenance_FailTheRetainedEvidenceGuard()
    {
        var draft = BuildDraft().Draft!;
        var edited = draft with { Lines = draft.Lines.Select(line => line with { Amount = 9999m }).ToArray() };
        var redated = draft with { AccountingDate = draft.AccountingDate.AddDays(-1) };
        var extra = draft with { Lines = [.. draft.Lines, draft.Lines[0]] };
        var replaced = draft with { IntercompanyAccrualEvidenceJson = draft.IntercompanyAccrualEvidenceJson!.Replace("4166.67", "9999.00") };
        var stripped = draft with { IntercompanyAccrualEvidenceJson = null };
        var correction = draft with { JournalEntryId = Guid.NewGuid(), Lines = edited.Lines };
        static AccountingTemplateLineSideDto Flip(AccountingTemplateLineSideDto side) => side == AccountingTemplateLineSideDto.Debit
            ? AccountingTemplateLineSideDto.Credit : AccountingTemplateLineSideDto.Debit;
        // Still balanced, same amounts and pins: only the direction says the lender owes instead of earns.
        var swapped = draft with { Lines = draft.Lines.Select(line => line with { Side = Flip(line.Side) }).ToArray() };
        var movedAccount = draft with { Lines = [draft.Lines[0], draft.Lines[1] with { Dimensions = draft.Lines[1].Dimensions! with { ChartAccountNodeId = "a-int-expense" } }] };

        Assert.Contains("retained amount", IntercompanyAccrualEvidenceGuard.Validate(edited));
        Assert.Contains("book, date", IntercompanyAccrualEvidenceGuard.Validate(redated));
        Assert.Contains("exactly the computed", IntercompanyAccrualEvidenceGuard.Validate(extra));
        Assert.Contains("retained server computation", IntercompanyAccrualEvidenceGuard.Validate(replaced));
        Assert.Contains("retained server computation", IntercompanyAccrualEvidenceGuard.Validate(stripped));
        Assert.True(IntercompanyAccrualEvidenceGuard.IsAccrualDraft(stripped));
        Assert.Contains("lending direction", IntercompanyAccrualEvidenceGuard.Validate(swapped));
        Assert.Contains("lending direction", IntercompanyAccrualEvidenceGuard.Validate(movedAccount));
        Assert.True(IntercompanyAccrualEvidenceGuard.IsOriginalAccrualDraft(draft));
        Assert.False(IntercompanyAccrualEvidenceGuard.IsOriginalAccrualDraft(correction));
        // A reversal or rebook is proved by the correction workflow against the immutable original.
        Assert.Null(IntercompanyAccrualEvidenceGuard.Validate(correction));
    }

    [Fact]
    public void CrossCurrencyLoan_RequiresGovernedConversion_AndRetainsOriginalAmounts()
    {
        var withoutFx = Assert.Throws<InvalidOperationException>(() => BuildDraft(currency: "EUR"));
        var fx = new ConsolidationFxAmountDto(4583.34m, "USD", 1.1m, Guid.NewGuid(), false, Guid.NewGuid(), 3, "policy", "r1", new string('f', 64));

        var proposal = BuildDraft(currency: "EUR", fx: fx);

        Assert.Contains("approved posting-date conversion", withoutFx.Message);
        Assert.All(proposal.Draft!.Lines, line =>
        {
            Assert.Equal((4583.34m, "USD"), (line.Amount, line.Currency));
            Assert.Equal(new ManualJournalLineCurrencyDto("EUR", 4166.67m, 1.1m), line.TransactionCurrency);
        });
        Assert.Equal(fx, proposal.Evidence!.Fx);
        Assert.Equal(fx, proposal.Draft.IntercompanyFx);
        Assert.Null(IntercompanyAccrualEvidenceGuard.Validate(proposal.Draft));
    }

    [Theory]
    [InlineData("template")]
    [InlineData("revision")]
    [InlineData("period")]
    [InlineData("income-node")]
    [InlineData("binding")]
    public void IncompleteOrMismatchedAuthority_CannotProduceADraft(string variant)
    {
        var posting = variant switch
        {
            "template" => Interest(template: IntercompanyAccountingTemplateKindDto.Loan),
            "revision" => Interest(Guid.NewGuid()),
            "income-node" => Interest(mappings: InterestMappings().Select(item => item with { RevenueAccountNodeId = null }).ToArray()),
            _ => Interest()
        };
        var period = variant == "period" ? SourcePeriod() with { Status = "Closed" } : SourcePeriod();
        var bindings = variant == "binding" ? [] : Bindings("a", Data.BookA);

        Assert.Throws<InvalidOperationException>(() => IntercompanyAccrualSourceBuilder.BuildDraft(Request(), "tenant-a", "company-a",
            "preparer", SourceBook(), period, posting, Computation(Interest()), Principal(), bindings, Chart("a"), null, Data.Now));
    }

    [Fact]
    public void PostedSource_MirrorsThroughTheExistingProposalBuilder_AsABalancedCounterpartDraft()
    {
        var source = BuildDraft().Draft!;
        var posting = Interest();
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

        var counterpart = IntercompanyProposalDraftBuilder.Build(proposal, posting, BorrowerBook(), Data.Period(),
            Bindings("a", Data.BookA), Bindings("b", Data.BookB), Chart("b"), null, Data.Now);
        var principalOnly = Assert.Throws<InvalidOperationException>(() => IntercompanyProposalDraftBuilder.Build(proposal,
            Interest(template: IntercompanyAccountingTemplateKindDto.Loan), BorrowerBook(), Data.Period(),
            Bindings("a", Data.BookA), Bindings("b", Data.BookB), Chart("b"), null, Data.Now));

        Assert.Equal(ManualJournalEntryStatusDto.Draft, counterpart.Status);
        Assert.Equal([(AccountingTemplateLineSideDto.Credit, "b-int-pay", 4166.67m), (AccountingTemplateLineSideDto.Debit, "b-int-expense", 4166.67m)],
            counterpart.Lines.Select(line => (line.Side, line.Dimensions!.ChartAccountNodeId!, line.Amount)));
        Assert.Equal(proposal.Source.ProposalId, counterpart.IntercompanyProposalId);
        Assert.Null(counterpart.IntercompanyAccrualEvidenceJson);
        Assert.Contains("LoanInterest", principalOnly.Message);
    }

    [Fact]
    public void LoanInterestProfile_RequiresALoanAgreement_AndIncomeAgainstExpenseNodes()
    {
        var valid = Interest();
        IntercompanyAccountingArrangementPolicy.Validate(valid.Arrangement, valid.LegalRevision);

        var noLegs = valid.Arrangement with { PartyMappings = valid.PartyMappings.Select(item => item with { RevenueAccountNodeId = null, ExpenseAccountNodeId = null }).ToArray() };
        var sameSide = valid.Arrangement with { PartyMappings = valid.PartyMappings.Select(item => item with { ExpenseAccountNodeId = null }).ToArray() };
        var fee = valid.LegalRevision with { Terms = valid.LegalRevision.Terms with { AgreementType = IntercompanyAgreementTypeDto.ManagementFee } };

        Assert.Contains("interest-income", Assert.Throws<ArgumentException>(() => IntercompanyAccountingArrangementPolicy.Validate(noLegs, valid.LegalRevision)).Message);
        Assert.Throws<ArgumentException>(() => IntercompanyAccountingArrangementPolicy.Validate(sameSide, valid.LegalRevision));
        Assert.Contains("incompatible", Assert.Throws<ArgumentException>(() => IntercompanyAccountingArrangementPolicy.Validate(valid.Arrangement, fee)).Message);
    }

    private static IntercompanyAccrualRequestDto Request() => new(Data.BookA, Data.AccountingArrangementId, PrincipalArrangement, From, To);

    private static IntercompanyAccrualProposalDto BuildDraft(LedgerBookRecord? book = null, LedgerAccountingPeriod? period = null,
        IntercompanyAccountRoleDto role = IntercompanyAccountRoleDto.Receivable, IReadOnlyList<ChartOfAccountsNodeDto>? chart = null,
        IReadOnlyList<IntercompanyAccountBindingDto>? bindings = null, string currency = "USD", ConsolidationFxAmountDto? fx = null,
        DateTimeOffset? now = null, int generation = 0)
    {
        var posting = Interest(currency: currency);
        var source = book ?? SourceBook();
        return IntercompanyAccrualSourceBuilder.BuildDraft(Request() with { SourceBookId = source.LedgerBookId }, "tenant-a", "company-a",
            "preparer", source, period ?? SourcePeriod(), posting, Computation(posting), Principal(role),
            bindings ?? Bindings("a", Data.BookA), chart ?? Chart("a"), fx, now ?? Data.Now, generation);
    }

    private static IntercompanyPartyMappingDto[] InterestMappings() =>
    [
        new(Data.EntityA, Data.BookA, "a-int-recv", "a-int-pay", null, "a-int-income", "a-int-expense"),
        new(Data.EntityB, Data.BookB, "b-int-recv", "b-int-pay", null, "b-int-income", "b-int-expense")
    ];

    private static ResolvedIntercompanyAccountingArrangementDto Interest(Guid? revision = null, DateTimeOffset? effectiveFrom = null,
        IntercompanyAccountingTemplateKindDto template = IntercompanyAccountingTemplateKindDto.LoanInterest,
        IReadOnlyList<IntercompanyPartyMappingDto>? mappings = null, string currency = "USD")
        => IntercompanyArrangementTestData.Create(Data.AccountingArrangementId, Guid.Parse("e1111111-1111-1111-1111-111111111111"),
            template, Data.EntityA, Data.EntityB, currency, mappings ?? InterestMappings(), revision ?? Data.RevisionId,
            effectiveFrom ?? Data.Now.AddDays(-30));

    private static IntercompanyAccrualPreviewDto Computation(ResolvedIntercompanyAccountingArrangementDto posting)
        => new(IntercompanyAccrualRules.StatusReady, IntercompanyAccrualRules.RuleVersion, posting.CanonicalAgreementId,
            posting.Currency, From, To, 4166.67m, 4166.6666666666666666666666667m, "kernel-test", [new("WEEKENDS", new string('c', 64))],
            [new(1, From, To, posting.RevisionId, posting.CanonicalAgreementVersion, posting.CanonicalPayloadHash, 1,
                1_000_000m, 0.05m, "ACT/360", 0.0833333333333333333333333333m, 4166.6666666666666666666666667m, "principal-evidence")],
            ["Calculated proposal input only."]);

    private static IntercompanyAccrualPrincipalEvidenceDto Principal(IntercompanyAccountRoleDto role = IntercompanyAccountRoleDto.Receivable)
        => new(PrincipalArrangement, role, 1_000_000m, 1, new string('d', 64), []);

    private static LedgerBookRecord SourceBook() => new(Data.BookA, "source-fund", Data.EntityA, FundStructureNodeKindDto.Entity,
        "Source", "USD", Data.Now, Data.Now);
    private static LedgerBookRecord BorrowerBook() => Data.TargetBook();
    private static LedgerAccountingPeriod SourcePeriod() => Data.Period() with { LedgerBookId = Data.BookA };

    private static IReadOnlyList<ChartOfAccountsNodeDto> Chart(string prefix) =>
    [
        new($"{prefix}-int-recv", "Assets:Interest receivable", "Assets:Interest receivable", "Asset", IntercompanyRole: IntercompanyAccountRoleDto.Receivable),
        new($"{prefix}-int-pay", "Liabilities:Interest payable", "Liabilities:Interest payable", "Liability", IntercompanyRole: IntercompanyAccountRoleDto.Payable),
        new($"{prefix}-int-income", "Income:Interest", "Income:Interest", "Revenue", IntercompanyRole: IntercompanyAccountRoleDto.Revenue),
        new($"{prefix}-int-expense", "Expenses:Interest", "Expenses:Interest", "Expense", IntercompanyRole: IntercompanyAccountRoleDto.Expense)
    ];

    private static IReadOnlyList<IntercompanyAccountBindingDto> Bindings(string prefix, Guid book) => Chart(prefix)
        .Select(account => new IntercompanyAccountBindingDto(Guid.NewGuid(), Guid.NewGuid(), 1, book, account.NodeId,
            account.IntercompanyRole!.Value, account.Path, account.AccountName, account.AccountType, new(2026, 1, 1), null,
            [], "tenant-a", "company-a", "reviewer", Data.Now, "Synthetic fixture")).ToArray();

    private static IntercompanyAccountingArrangementEventDto Approval(IntercompanyAccountingArrangementDto revision)
        => new(Guid.NewGuid(), "digest", IntercompanyAccountingArrangementActionDto.Approve, "mapping-reviewer",
            revision.EffectiveFrom, null, revision);

    private static LedgerJournalEntryRecord PrincipalJournal(DateOnly date, string node, decimal debit = 0m, decimal credit = 0m,
        Guid? arrangement = null, string? transactionCurrency = null)
        => Journal(date, arrangement ?? PrincipalArrangement, node, "drawdown", debit, credit, transactionCurrency);

    private static LedgerJournalEntryRecord AccrualJournal(Guid arrangement, string node, string transaction, decimal debit = 0m,
        decimal credit = 0m, bool accrualTag = true)
        => Journal(Data.Date, arrangement, node, transaction, debit, credit, null, accrualTag);

    /// <summary>The posted reversal the correction workflow produces: same scope and provenance, sides swapped, source-linked.</summary>
    private static LedgerJournalEntryRecord Inverse(LedgerJournalEntryRecord original)
    {
        var id = Guid.NewGuid();
        var entry = new JournalEntry(id, original.Entry.Timestamp, "Reversal", original.Entry.Lines.Select(line => new LedgerEntry(
            Guid.NewGuid(), id, line.Timestamp, line.Account, line.Credit, line.Debit, "Reversal", line.Dimensions, null)).ToArray(),
            original.Entry.Metadata);
        return original with { Entry = entry, GlobalSequence = original.GlobalSequence + 1, SourceJournalEntryId = original.Entry.JournalEntryId };
    }

    private static LedgerJournalEntryRecord Journal(DateOnly date, Guid arrangement, string node, string transaction,
        decimal debit, decimal credit, string? transactionCurrency, bool accrualTag = false)
    {
        var id = Guid.NewGuid();
        var timestamp = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var dimensions = new LedgerLineDimensionSet(EntityId: Data.EntityA.ToString("D"), CounterpartyId: Data.EntityB.ToString("D"))
        {
            ChartAccountNodeId = node,
            AccountingArrangementId = arrangement.ToString("D"),
            IntercompanyTransactionId = transaction
        };
        var amount = debit + credit;
        var entry = new JournalEntry(id, timestamp, "Synthetic journal",
        [
            new(Guid.NewGuid(), id, timestamp, new("Intercompany", LedgerAccountType.Asset), debit, credit, "Synthetic journal", dimensions,
                transactionCurrency is null ? null : new(transactionCurrency, "USD", debit, credit, 1m)),
            new(Guid.NewGuid(), id, timestamp, new("Assets:Cash", LedgerAccountType.Asset), credit > 0m ? amount : 0m,
                debit > 0m ? amount : 0m, "Synthetic journal")
        ], new JournalEntryMetadata(EffectiveDate: date, Tags: accrualTag
            ? new Dictionary<string, string> { [IntercompanyAccrualEvidenceGuard.DigestTag] = new string('e', 64) } : null));
        return new(entry, Guid.NewGuid(), Guid.NewGuid(), null, null, 1, timestamp);
    }
}
