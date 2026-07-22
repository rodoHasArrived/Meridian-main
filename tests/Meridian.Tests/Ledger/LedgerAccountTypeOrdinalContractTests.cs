using FluentAssertions;
using Meridian.FSharp.Ledger;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Cross-language contract guard: the F# posting kernel (<c>Posting.fs</c>) cannot reference
/// <c>Meridian.Ledger</c>, so it pins the <see cref="LedgerAccountType"/> ordinals by value.
/// Reordering the C# enum would silently flip debit/credit normal-balance math. These tests
/// turn that silent corruption into a loud CI failure — update Posting.fs and the enum together.
/// </summary>
public sealed class LedgerAccountTypeOrdinalContractTests
{
    [Fact]
    public void LedgerAccountType_Ordinals_MatchFSharpPostingKernel()
    {
        ((int)LedgerAccountType.Asset).Should().Be(Posting.AssetOrdinal);
        ((int)LedgerAccountType.Liability).Should().Be(Posting.LiabilityOrdinal);
        ((int)LedgerAccountType.Equity).Should().Be(Posting.EquityOrdinal);
        ((int)LedgerAccountType.Revenue).Should().Be(Posting.RevenueOrdinal);
        ((int)LedgerAccountType.Expense).Should().Be(Posting.ExpenseOrdinal);
    }

    [Theory]
    [InlineData(LedgerAccountType.Asset, true)]
    [InlineData(LedgerAccountType.Liability, false)]
    [InlineData(LedgerAccountType.Equity, false)]
    [InlineData(LedgerAccountType.Revenue, false)]
    [InlineData(LedgerAccountType.Expense, true)]
    public void CalculateNetBalance_AppliesCorrectNormalBalance(LedgerAccountType accountType, bool debitNormal)
    {
        const decimal debits = 100m;
        const decimal credits = 40m;

        var net = global::Meridian.Ledger.Ledger.CalculateNetBalance(accountType, debits, credits);

        var expected = debitNormal ? debits - credits : credits - debits;
        net.Should().Be(expected, "{0} accounts are {1}-normal", accountType, debitNormal ? "debit" : "credit");
    }

    [Fact]
    public void LedgerAccountType_HasExactlyFiveMembers()
    {
        // A sixth member would fall into the F# kernel's credit-normal default branch.
        // Adding one requires updating Posting.fs and this contract test deliberately.
        Enum.GetValues<LedgerAccountType>().Should().HaveCount(5);
    }
}
