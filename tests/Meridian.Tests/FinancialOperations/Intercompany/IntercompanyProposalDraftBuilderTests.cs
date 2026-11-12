using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.FinancialOperations.Intercompany;

public sealed class IntercompanyProposalDraftBuilderTests
{
    [Theory]
    [InlineData(true, true, AccountingTemplateLineSideDto.Credit, "b-payable")]
    [InlineData(true, false, AccountingTemplateLineSideDto.Debit, "b-payable")]
    [InlineData(false, false, AccountingTemplateLineSideDto.Debit, "b-receivable")]
    [InlineData(false, true, AccountingTemplateLineSideDto.Credit, "b-receivable")]
    public void MonetaryDirection_ProducesBalancedReciprocalDraft(bool receivable, bool sourceDebit,
        AccountingTemplateLineSideDto expectedSide, string expectedNode)
    {
        var source = IntercompanyProposalTestData.Proposal(receivable, sourceDebit);
        var draft = Build(source);

        Assert.Equal(ManualJournalEntryStatusDto.Draft, draft.Status);
        Assert.Equal(expectedSide, draft.Lines[0].Side);
        Assert.Equal(expectedNode, draft.Lines[0].Dimensions!.ChartAccountNodeId);
        Assert.Equal(0m, draft.Lines.Sum(x => x.Side == AccountingTemplateLineSideDto.Debit ? x.Amount : -x.Amount));
        Assert.All(draft.Lines, line => Assert.Equal(100m, line.Amount));
        Assert.Equal(IntercompanyProposalTestData.EntityB.ToString("D"), draft.Lines[0].Dimensions!.EntityId);
        Assert.Equal(IntercompanyProposalTestData.EntityA.ToString("D"), draft.Lines[0].Dimensions!.CounterpartyId);
        Assert.Equal(source.Source.TransactionId, draft.Lines[0].Dimensions!.IntercompanyTransactionId);
    }

    [Fact]
    public void RepeatedEqualAmountEvents_KeepDistinctProposalAndTransactionIdentities()
    {
        var first = IntercompanyProposalTestData.Proposal(transaction: "invoice-100");
        var second = IntercompanyProposalTestData.Proposal(transaction: "invoice-101");
        var firstDraft = Build(first);
        var secondDraft = Build(second);

        Assert.NotEqual(firstDraft.JournalEntryId, secondDraft.JournalEntryId);
        Assert.NotEqual(firstDraft.TreasuryContext!.IdempotencyKey, secondDraft.TreasuryContext!.IdempotencyKey);
        Assert.Equal(firstDraft.Lines[0].Amount, secondDraft.Lines[0].Amount);
        Assert.NotEqual(firstDraft.Lines[0].Dimensions!.IntercompanyTransactionId, secondDraft.Lines[0].Dimensions!.IntercompanyTransactionId);
    }

    [Fact]
    public void SourceReversal_FlipsSignsAndKeepsOriginalAmountAndSourceIdentity()
    {
        var original = IntercompanyProposalTestData.Proposal();
        var reversal = IntercompanyProposalTestData.Proposal(sourceDebit: false);
        reversal = reversal with { Source = reversal.Source with { ReversesJournalId = original.Source.SourceJournalId } };
        var originalDraft = Build(original);
        var reversalDraft = Build(reversal);

        Assert.NotEqual(originalDraft.Lines[0].Side, reversalDraft.Lines[0].Side);
        Assert.Equal(originalDraft.Lines[0].Amount, reversalDraft.Lines[0].Amount);
        Assert.Contains($"journal:{reversal.Source.SourceJournalId:D}", reversalDraft.EvidenceLinks);
    }

    [Fact]
    public void ForeignCurrencyWithoutApprovedConversion_CannotInventFunctionalAmounts()
    {
        var source = IntercompanyProposalTestData.Proposal();
        var agreement = IntercompanyProposalTestData.Agreement() with { Currency = "EUR" };
        source = source with { Source = source.Source with { Lines = source.Source.Lines.Select(line => line with { Currency = "EUR" }).ToArray() } };
        Assert.Throws<InvalidOperationException>(() => Build(source, agreement));
    }

    [Fact]
    public void UnknownMirrorRuleVersion_RequiresSupportedInterpretation()
    {
        var source = IntercompanyProposalTestData.Proposal();
        source = source with { Source = source.Source with { RuleVersion = "intercompany-mirror-v999" } };
        Assert.Throws<InvalidOperationException>(() => Build(source));
    }

    [Fact]
    public void SharedUnattributedContraAcrossEvents_RequiresExplicitAllocation()
    {
        var source = IntercompanyProposalTestData.Proposal();
        source = source with { Source = source.Source with { RequiresSourceAllocation = true } };

        var error = Assert.Throws<InvalidOperationException>(() => Build(source));

        Assert.Contains("multiple intercompany event groups", error.Message);
    }

    [Fact]
    public void UnrelatedBalancedActivity_CannotBeSilentlyExcludedFromSourceTemplate()
    {
        var source = IntercompanyProposalTestData.Proposal();
        source = source with { Source = source.Source with { Lines = [.. source.Source.Lines,
            new(Guid.NewGuid(), "Expenses:Unrelated", "Expense", "unrelated-expense", 25m, 0m, "USD", 25m, 0m),
            new(Guid.NewGuid(), "Assets:Unrelated", "Asset", "unrelated-asset", 0m, 25m, "USD", 0m, 25m)] } };

        var error = Assert.Throws<InvalidOperationException>(() => Build(source));

        Assert.Contains("Every retained source leg", error.Message);
    }

    [Fact]
    public void MixedGrossOperatingRoles_CannotCollapseIntoOneNetCounterpartExpense()
    {
        var source = IntercompanyProposalTestData.Proposal();
        source = source with { Source = source.Source with { Lines = [source.Source.Lines[0],
            new(Guid.NewGuid(), "Expenses:Recharge", "Expense", "a-expense", 100m, 0m, "USD", 100m, 0m),
            new(Guid.NewGuid(), "Income:Recharge", "Revenue", "a-revenue", 0m, 200m, "USD", 0m, 200m)] } };
        var agreement = IntercompanyProposalTestData.Agreement() with
        {
            Kind = IntercompanyAgreementKindDto.CostRecharge,
            PartyMappings = IntercompanyProposalTestData.Agreement().PartyMappings.Select(mapping => mapping.EntityId == IntercompanyProposalTestData.EntityA
                ? mapping with { RevenueAccountNodeId = "a-revenue", ExpenseAccountNodeId = "a-expense" } : mapping).ToArray()
        };

        var error = Assert.Throws<InvalidOperationException>(() => Build(source, agreement));

        Assert.Contains("Split mixed revenue and expense", error.Message);
    }

    [Fact]
    public void ExpenseSharing_CanRechargeAnExpenseWithoutInventingSourceRevenue()
    {
        var source = IntercompanyProposalTestData.Proposal();
        source = source with { Source = source.Source with { Lines = [source.Source.Lines[0],
            new(Guid.NewGuid(), "Expenses:Shared", "Expense", "a-expense", 0m, 100m, "USD", 0m, 100m)] } };
        var agreement = IntercompanyProposalTestData.Agreement() with
        {
            Kind = IntercompanyAgreementKindDto.ExpenseShare,
            PartyMappings = IntercompanyProposalTestData.Agreement().PartyMappings.Select(mapping => mapping with
                { ExpenseAccountNodeId = mapping.EntityId == IntercompanyProposalTestData.EntityA ? "a-expense" : "b-expense" }).ToArray()
        };
        var expense = new ChartOfAccountsNodeDto("b-expense", "Expenses:Shared", "Expenses:Shared", "Expense",
            IntercompanyRole: IntercompanyAccountRoleDto.Expense);
        var binding = new IntercompanyAccountBindingDto(Guid.NewGuid(), Guid.NewGuid(), 1, IntercompanyProposalTestData.BookB,
            expense.NodeId, IntercompanyAccountRoleDto.Expense, expense.Path, expense.AccountName, expense.AccountType,
            new(2026, 1, 1), null, [], "tenant-a", "company-a", "reviewer", IntercompanyProposalTestData.Now, "Synthetic fixture");

        var draft = IntercompanyProposalDraftBuilder.Build(source, agreement, IntercompanyProposalTestData.TargetBook(),
            IntercompanyProposalTestData.Period(), [], [.. IntercompanyProposalTestData.TargetBindings(), binding],
            [.. IntercompanyProposalTestData.Chart(), expense], null, IntercompanyProposalTestData.Now);

        Assert.Equal("b-expense", draft.Lines[1].Dimensions!.ChartAccountNodeId);
        Assert.Equal(AccountingTemplateLineSideDto.Debit, draft.Lines[1].Side);
        Assert.Equal(100m, draft.Lines[1].Amount);
    }

    private static ManualJournalEntryDraftDto Build(IntercompanyProposalDto source, IntercompanyAgreementDto? agreement = null)
        => IntercompanyProposalDraftBuilder.Build(source, agreement ?? IntercompanyProposalTestData.Agreement(),
            IntercompanyProposalTestData.TargetBook(), IntercompanyProposalTestData.Period(), [],
            IntercompanyProposalTestData.TargetBindings(), IntercompanyProposalTestData.Chart(), null, IntercompanyProposalTestData.Now);
}

internal static class IntercompanyProposalTestData
{
    public static readonly Guid EntityA = Guid.Parse("a1111111-1111-1111-1111-111111111111");
    public static readonly Guid EntityB = Guid.Parse("b1111111-1111-1111-1111-111111111111");
    public static readonly Guid BookA = Guid.Parse("a2222222-2222-2222-2222-222222222222");
    public static readonly Guid BookB = Guid.Parse("b2222222-2222-2222-2222-222222222222");
    public static readonly Guid AgreementId = Guid.Parse("c1111111-1111-1111-1111-111111111111");
    public static readonly Guid RevisionId = Guid.Parse("c2222222-2222-2222-2222-222222222222");
    public static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);
    public static readonly DateOnly Date = new(2026, 6, 30);
    public static LedgerBookRecord TargetBook() => new(BookB, "target-fund", EntityB, FundStructureNodeKindDto.Entity,
        "Target", "USD", Now, Now);
    public static LedgerAccountingPeriod Period() => new(Guid.NewGuid(), BookB, 2026, 6, "June",
        new(2026, 6, 1), Date, "Open", Now, null, 1);
    public static IntercompanyAgreementDto Agreement() => new(AgreementId, Guid.NewGuid(), "loan-a-b", IntercompanyAgreementKindDto.Loan,
        EntityA, EntityB, "Lender", "Borrower", "USD", "Monthly", Now.AddDays(-30), null, RevisionId, 1, 3,
        IntercompanyAgreementStatusDto.Approved,
        [new(EntityA, BookA, "a-receivable", "a-payable", "a-cash"), new(EntityB, BookB, "b-receivable", "b-payable", "b-cash")],
        null, "preparer", Now.AddDays(-1), "reviewer", Now, "reviewer", Now);
    public static IntercompanyProposalDto Proposal(bool receivable = true, bool sourceDebit = true, string transaction = "invoice-100")
    {
        var source = new IntercompanyProposalSourceDto(Guid.NewGuid(), "tenant-a", "company-a", Guid.NewGuid(), BookA,
            EntityA, EntityB, AgreementId, RevisionId, transaction, Date, "USD",
            [new(Guid.NewGuid(), receivable ? "Assets:Due from B" : "Liabilities:Due to B", receivable ? "Asset" : "Liability",
                receivable ? "a-receivable" : "a-payable", sourceDebit ? 100m : 0m, sourceDebit ? 0m : 100m,
                "USD", sourceDebit ? 100m : 0m, sourceDebit ? 0m : 100m),
             new(Guid.NewGuid(), "Assets:Cash", "Asset", "a-cash", sourceDebit ? 0m : 100m, sourceDebit ? 100m : 0m,
                "USD", sourceDebit ? 0m : 100m, sourceDebit ? 100m : 0m)]);
        return new(source, "retained-source-digest", IntercompanyProposalStatusDto.Pending, 0, Now);
    }
    public static IReadOnlyList<ChartOfAccountsNodeDto> Chart() => [
        new("b-receivable", "Assets:Due from A", "Assets:Due from A", "Asset", IntercompanyRole: IntercompanyAccountRoleDto.Receivable),
        new("b-payable", "Liabilities:Due to A", "Liabilities:Due to A", "Liability", IntercompanyRole: IntercompanyAccountRoleDto.Payable),
        new("b-cash", "Assets:Cash", "Assets:Cash", "Asset")];
    public static IReadOnlyList<IntercompanyAccountBindingDto> TargetBindings() => Chart().Where(x => x.IntercompanyRole.HasValue)
        .Select(account => new IntercompanyAccountBindingDto(Guid.NewGuid(), Guid.NewGuid(), 1, BookB, account.NodeId,
            account.IntercompanyRole!.Value, account.Path, account.AccountName, account.AccountType, new(2026, 1, 1), null,
            [], "tenant-a", "company-a", "reviewer", Now, "Synthetic fixture")).ToArray();
}
