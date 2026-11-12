using System.Text.Json;
using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;

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
}
