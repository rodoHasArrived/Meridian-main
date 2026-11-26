using System.Text.Json;
using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.Ledger;

namespace Meridian.Tests.Application.DirectLending;

public sealed class DirectLendingJournalLinesTests
{
    [Theory]
    [InlineData("accrual", """{"interestAmount":-1}""", "interestAmount")]
    [InlineData("accrual", """{"interestAmount":100,"prepaidInterestConsumed":-1}""", "prepaidInterestConsumed")]
    [InlineData("mixed", """{"amount":-1}""", "amount")]
    [InlineData("mixed", """{"amount":100,"resolution":{"excessToPrincipal":0,"breakdown":{"toInterest":-10,"toPrincipal":110}}}""", "toInterest")]
    [InlineData("mixed", """{"amount":100,"resolution":{"excessToPrincipal":-10,"breakdown":{"toPrincipal":110}}}""", "excessToPrincipal")]
    [InlineData("mixed", """{"amount":100,"resolution":{"excessToPrincipal":100,"prepaymentPenaltyCharged":-1}}""", "prepaymentPenaltyCharged")]
    [InlineData("apply", """{"appliedAmount":100,"breakdown":{"toInterest":-10,"toPrincipal":110}}""", "toInterest")]
    [InlineData("refund", """{"amount":-1}""", "amount")]
    public void Projection_RejectsNegativeAmountsBeforeFilteringOrNetting(
        string projection,
        string payloadJson,
        string member)
    {
        using var payload = JsonDocument.Parse(payloadJson);

        var act = () => projection switch
        {
            "accrual" => DirectLendingJournalLines.ForAccruedInterest(payload.RootElement),
            "mixed" => DirectLendingJournalLines.ForMixedPayment(payload.RootElement),
            "apply" => DirectLendingJournalLines.ForUnappliedCashApplied(payload.RootElement),
            "refund" => DirectLendingJournalLines.ForUnappliedCashRefunded(payload.RootElement),
            _ => throw new ArgumentException("Unknown projection.", nameof(projection))
        };

        act.Should().Throw<DirectLendingCommandException>()
            .Where(ex => ex.Error.Code == DirectLendingErrorCode.Validation)
            .WithMessage($"*'{member}' cannot be negative*");
    }
    [Fact]
    public void ForEvent_RetainsExplicitRolesAndSharedClassificationsForSplitPayment()
    {
        using var payload = JsonDocument.Parse("""
            {"amount":100,"resolution":{"breakdown":{"toInterest":20,"toPrincipal":50},
             "excessToPrincipal":0,"excessToPrepaidInterest":10,"unappliedAmount":20}}
            """);

        var lines = DirectLendingJournalLines.ForEvent(
            "loan.mixed-payment-applied", payload.RootElement, Guid.NewGuid().ToString("D"));

        lines.Select(line => (line.AccountRole, line.Account, line.AccountType, line.Debit, line.Credit))
            .Should().Equal(
                ((LedgerAccountRole?)LedgerAccountRole.Cash, "Cash", LedgerAccountType.Asset, 100m, 0m),
                ((LedgerAccountRole?)LedgerAccountRole.LoanAccruedInterestReceivable, "AccruedInterestReceivable", LedgerAccountType.Asset, 0m, 20m),
                ((LedgerAccountRole?)LedgerAccountRole.LoanPrincipal, "LoanPrincipal", LedgerAccountType.Asset, 0m, 50m),
                ((LedgerAccountRole?)LedgerAccountRole.LoanPrepaidInterestLiability, "PrepaidInterestLiability", LedgerAccountType.Liability, 0m, 10m),
                ((LedgerAccountRole?)LedgerAccountRole.LoanUnappliedCashLiability, "UnappliedCashLiability", LedgerAccountType.Liability, 0m, 20m));
        lines.Sum(line => line.Debit).Should().Be(lines.Sum(line => line.Credit));
    }

    [Fact]
    public void CompatibilityAccrualProjection_UsesDefinitionsWithoutPostingScope()
    {
        using var payload = JsonDocument.Parse("""{"interestAmount":100,"prepaidInterestConsumed":20}""");

        var lines = DirectLendingJournalLines.ForAccruedInterest(payload.RootElement);

        lines.Select(line => (line.Account, line.AccountType, line.Debit, line.Credit)).Should().Equal(
            ("PrepaidInterestLiability", LedgerAccountType.Liability, 20m, 0m),
            ("AccruedInterestReceivable", LedgerAccountType.Asset, 80m, 0m),
            ("InterestIncome", LedgerAccountType.Revenue, 0m, 100m));
        lines.Should().OnlyContain(line => line.AccountRole != null);
    }

    [Fact]
    public void CompatibilityProjection_RejectsPresentNonNumericAmount()
    {
        using var payload = JsonDocument.Parse("""{"interestAmount":"100"}""");

        var act = () => DirectLendingJournalLines.ForAccruedInterest(payload.RootElement);

        act.Should().Throw<DirectLendingCommandException>()
            .Where(ex => ex.Error.Code == DirectLendingErrorCode.Validation)
            .WithMessage("*'interestAmount' must be a JSON number*");
    }

}
