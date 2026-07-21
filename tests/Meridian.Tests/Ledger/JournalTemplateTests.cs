using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Tests reusable journal templates: parameterized amount resolution, balance enforcement,
/// materialization into postable journals, and registry semantics.
/// </summary>
public sealed class JournalTemplateTests
{
    private static readonly DateTimeOffset At = new(2026, 06, 30, 12, 0, 0, TimeSpan.Zero);

    private static JournalTemplate ManagementFeeTemplate() => new(
        "mgmt-fee-accrual",
        "Management Fee Accrual",
        "Accrue the monthly management fee.",
        [
            new JournalTemplateLine(LedgerAccounts.ManagementFeeExpenseFor("FUND-A"), JournalTemplateSide.Debit, "fee"),
            new JournalTemplateLine(LedgerAccounts.ManagementFeePayableFor("FUND-A"), JournalTemplateSide.Credit, "fee"),
        ]);

    [Fact]
    public void Instantiate_ResolvesParametersIntoBalancedJournal()
    {
        var template = ManagementFeeTemplate();

        var instance = template.Instantiate(new JournalTemplateInstantiation(
            At,
            new Dictionary<string, decimal> { ["fee"] = 12_500m }));

        instance.IsBalanced.Should().BeTrue();
        instance.TotalDebits.Should().Be(12_500m);
        instance.TotalCredits.Should().Be(12_500m);
        instance.Lines.Should().HaveCount(2);

        var journal = instance.ToJournalEntry();
        journal.IsBalanced.Should().BeTrue();
        journal.Lines.Should().HaveCount(2);
    }

    [Fact]
    public void Instantiate_MissingRequiredParameter_Throws()
    {
        var template = ManagementFeeTemplate();
        var act = () => template.Instantiate(new JournalTemplateInstantiation(At, new Dictionary<string, decimal>()));
        act.Should().Throw<LedgerValidationException>();
    }

    [Fact]
    public void Instantiate_AppliesFactorAndFixedAmount()
    {
        // A split where 60% of the base amount is expensed and 40% deferred, credit the payable in full.
        var template = new JournalTemplate(
            "split-fee",
            "Split Fee",
            "Split a fee across expense and deferral.",
            [
                new JournalTemplateLine(LedgerAccounts.ManagementFeeExpenseFor("F"), JournalTemplateSide.Debit, "fee", Factor: 0.6m),
                new JournalTemplateLine(new LedgerAccount("Deferred Fee", LedgerAccountType.Asset), JournalTemplateSide.Debit, "fee", Factor: 0.4m),
                new JournalTemplateLine(LedgerAccounts.ManagementFeePayableFor("F"), JournalTemplateSide.Credit, "fee"),
            ]);

        var instance = template.Instantiate(new JournalTemplateInstantiation(
            At,
            new Dictionary<string, decimal> { ["fee"] = 1_000m }));

        instance.IsBalanced.Should().BeTrue();
        instance.TotalDebits.Should().Be(1_000m);
    }

    [Fact]
    public void Instantiate_UnbalancedTemplate_Throws()
    {
        var template = new JournalTemplate(
            "bad",
            "Unbalanced",
            "Debits do not equal credits.",
            [
                new JournalTemplateLine(LedgerAccounts.Cash, JournalTemplateSide.Debit, FixedAmount: 100m),
                new JournalTemplateLine(LedgerAccounts.RealizedGain, JournalTemplateSide.Credit, FixedAmount: 90m),
            ]);

        var act = () => template.Instantiate(new JournalTemplateInstantiation(At, new Dictionary<string, decimal>()));
        act.Should().Throw<LedgerValidationException>();
    }

    [Fact]
    public void TemplateBook_RegisterAndInstantiate_RoundTrips()
    {
        var book = new JournalTemplateBook();
        book.Register(ManagementFeeTemplate());

        book.Templates.Should().ContainSingle();
        var instance = book.Instantiate("mgmt-fee-accrual", new JournalTemplateInstantiation(
            At,
            new Dictionary<string, decimal> { ["fee"] = 500m }));

        instance.TotalDebits.Should().Be(500m);
    }

    [Fact]
    public void TemplateBook_Get_UnknownTemplate_Throws()
    {
        var book = new JournalTemplateBook();
        var act = () => book.Get("nope");
        act.Should().Throw<KeyNotFoundException>();
    }
}
