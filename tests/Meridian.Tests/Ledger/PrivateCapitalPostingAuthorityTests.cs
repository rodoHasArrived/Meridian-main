using FluentAssertions;
using Meridian.Contracts.Banking;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class PrivateCapitalPostingAuthorityTests
{
    private static readonly Guid EntityId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid BankId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateOnly Received = new(2026, 3, 20);
    private static BankTransactionDto Source() => new(BankId, EntityId, "CapitalCallFunding", Received,
        Received, Received, 100m, "USD", "retained-reference", DateTimeOffset.UnixEpoch, false);
    private static PrivateCapitalPostingAuthorityDto Authority(BankTransactionDto source)
        => new(new("tenant", "company", EntityId.ToString("D"), "fund", Guid.NewGuid(), "USD"),
            "call", "commitment", 1, "plan", "Funding", 100m, Received,
            "receipt", BankId.ToString("D"), PrivateCapitalPostingAuthority.BankTransactionHash(source));

    [Fact]
    public void SameRetainedBankEvidence_Matches_AndVoidedOrChangedEvidenceRefuses()
    {
        var source = Source();
        var authority = Authority(source);
        PrivateCapitalPostingAuthority.MatchesBankTransaction(authority, source).Should().BeTrue();
        PrivateCapitalPostingAuthority.MatchesBankTransaction(authority, source with { IsVoided = true }).Should().BeFalse();
        PrivateCapitalPostingAuthority.MatchesBankTransaction(authority, source with { ExternalRef = "changed" }).Should().BeFalse();
        PrivateCapitalPostingAuthority.MatchesBankTransaction(authority, source with { Amount = 101m }).Should().BeFalse();
        PrivateCapitalPostingAuthority.Deserialize(PrivateCapitalPostingAuthority.Serialize(authority)).Should().Be(authority);
    }

    [Theory]
    [InlineData("Drawdown")]
    [InlineData("InterestPayment")]
    [InlineData("Seed")]
    [InlineData("Credit")]
    [InlineData("Reversal")]
    public void PositiveUnrelatedBankTransactions_CannotFundCapitalCalls(string transactionType)
    {
        var source = Source() with { TransactionType = transactionType };
        PrivateCapitalPostingAuthority.MatchesBankTransaction(Authority(source), source).Should().BeFalse();
    }
}
