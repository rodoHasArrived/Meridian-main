using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Tests the fixed-asset depreciation projector: balanced expense/accumulated-depreciation lines
/// scoped per asset, plus the account-type and amount guards it shares with the fixed-income
/// amortization projector.
/// </summary>
public sealed class FixedAssetDepreciationProjectorTests
{
    private static readonly Guid AssetId = new("11111111-1111-1111-1111-111111111111");

    private static LedgerAccount CostAccount => LedgerAccounts.FixedAssetCostFor(AssetId.ToString("D"));

    [Fact]
    public void Project_PostsDepreciationExpenseAndAccumulatedDepreciation()
    {
        var input = new DepreciationInput(AssetId, CostAccount, 500m);

        var projection = FixedAssetDepreciationProjector.Project(input);

        projection.IsBalanced.Should().BeTrue();
        projection.TotalDebits.Should().Be(500m);
        projection.Lines.Should().HaveCount(2);

        var expenseLine = projection.Lines.Single(line => line.account.AccountType == LedgerAccountType.Expense);
        expenseLine.account.Name.Should().Be("Depreciation Expense");
        expenseLine.debit.Should().Be(500m);
        expenseLine.credit.Should().Be(0m);

        var accumulatedLine = projection.Lines.Single(line => line.account.Name == "Accumulated Depreciation");
        accumulatedLine.account.AccountType.Should().Be(LedgerAccountType.Asset);
        accumulatedLine.debit.Should().Be(0m);
        accumulatedLine.credit.Should().Be(500m);
    }

    [Fact]
    public void Project_ScopesAccountsByAssetId()
    {
        var projection = FixedAssetDepreciationProjector.Project(new DepreciationInput(AssetId, CostAccount, 42m));

        projection.AssetId.Should().Be(AssetId);
        projection.Lines.Should().Contain(line => line.account == LedgerAccounts.DepreciationExpenseFor(AssetId.ToString("D")));
        projection.Lines.Should().Contain(line => line.account == LedgerAccounts.AccumulatedDepreciationFor(AssetId.ToString("D")));
    }

    [Fact]
    public void Project_NonAssetCostAccount_Throws()
    {
        var expenseAccount = LedgerAccounts.DepreciationExpenseFor(AssetId.ToString("D"));
        var input = new DepreciationInput(AssetId, expenseAccount, 100m);

        var act = () => FixedAssetDepreciationProjector.Project(input);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Project_ZeroAmount_Throws()
    {
        var act = () => FixedAssetDepreciationProjector.Project(new DepreciationInput(AssetId, CostAccount, 0m));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Project_NegativeAmount_Throws()
    {
        var act = () => FixedAssetDepreciationProjector.Project(new DepreciationInput(AssetId, CostAccount, -1m));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Project_EmptyAssetId_Throws()
    {
        var act = () => FixedAssetDepreciationProjector.Project(new DepreciationInput(Guid.Empty, CostAccount, 100m));

        act.Should().Throw<ArgumentException>();
    }
}
